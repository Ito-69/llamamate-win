using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Text;

namespace LlamaMate.App.Services;

public class ServerManager
{
    private readonly ConfigManager _config;
    private readonly LogTailer _logTailer;
    private Process? _serverProcess;
    private CancellationTokenSource? _cts;

    public bool IsRunning => _serverProcess is { HasExited: false };

    public ServerManager(ConfigManager config, LogTailer logTailer)
    {
        _config = config;
        _logTailer = logTailer;
    }

    public async Task Start()
    {
        if (IsRunning) return;

        await Stop();

        var binDir = _config.BinDir();
        var serverExe = Path.Combine(binDir, "llama-server.exe");
        if (!File.Exists(serverExe))
        {
            // fallback: check PATH
            serverExe = "llama-server.exe";
        }

        var cfg = _config.Settings;
        var modelPath = Path.Combine(_config.ModelsDir(), cfg.ActiveModel);

        var args = new StringBuilder();
        if (!string.IsNullOrEmpty(cfg.ActiveModel) && File.Exists(modelPath))
        {
            args.Append($"-m \"{modelPath}\"");
        }
        args.Append($" -c {cfg.ContextSize}");
        args.Append($" --port {cfg.Port}");
        args.Append($" --host {cfg.Host}");
        args.Append($" -ngl {cfg.GpuLayers}");
        args.Append($" -b {cfg.BatchSize}");
        if (cfg.Threads > 0)
            args.Append($" -t {cfg.Threads}");
        if (cfg.FlashAttention)
            args.Append(" -fa");
        if (!string.IsNullOrEmpty(cfg.KvCacheQuant) && cfg.KvCacheQuant != "f16")
            args.Append($" --cache-type-k {cfg.KvCacheQuant}");
        if (!string.IsNullOrEmpty(cfg.ExtraArgs))
            args.Append($" {cfg.ExtraArgs}");

        var logDir = Path.GetDirectoryName(_config.LogPath());
        if (logDir != null) Directory.CreateDirectory(logDir);

        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = binDir
        };

        _serverProcess = new Process { StartInfo = psi };
        _serverProcess.Start();

        _ = Task.Run(() => StreamToLog(_serverProcess.StandardOutput, _cts.Token));
        _ = Task.Run(() => StreamToLog(_serverProcess.StandardError, _cts.Token));

        _logTailer.Start();

        await Task.CompletedTask;
    }

    public async Task Stop()
    {
        if (_serverProcess is { HasExited: false })
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _serverProcess.Kill(entireProcessTree: true);
                await _serverProcess.WaitForExitAsync(cts.Token);
            }
            catch { }
        }

        _serverProcess?.Dispose();
        _serverProcess = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _logTailer.Stop();

        // also try stopping via PowerShell in case it was started by Task Scheduler
        await RunPowerShellAsync("Stop-Server.ps1");

        await Task.CompletedTask;
    }

    public async Task Restart()
    {
        await Stop();
        await Task.Delay(1000);
        await Start();
    }

    public async Task RegisterScheduledTask()
    {
        var psScript = @"
$taskName = 'LlamaMate-Server'
$scriptPath = Join-Path $env:LOCALAPPDATA 'LlamaMate\bin\Start-Server.ps1'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ""-NoProfile -ExecutionPolicy Bypass -File '$scriptPath'""
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
$principal = New-ScheduledTaskPrincipal -UserId '$env:USERNAME' -RunLevel Limited
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force
";
        await RunPowerShellAsync(psScript);
    }

    public async Task UnregisterScheduledTask()
    {
        await RunPowerShellAsync("Unregister-ScheduledTask -TaskName 'LlamaMate-Server' -Confirm:$false -ErrorAction SilentlyContinue");
    }

    public void Cleanup()
    {
        Stop().Wait(TimeSpan.FromSeconds(5));
    }

    private async Task StreamToLog(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                _logTailer.Write(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task<string> RunPowerShellAsync(string script)
    {
        try
        {
            using var ps = PowerShell.Create();
            ps.AddScript(script);
            var output = await Task.Run(() => ps.Invoke());
            return string.Join(Environment.NewLine, output.Select(o => o?.ToString() ?? ""));
        }
        catch
        {
            return "";
        }
    }
}
