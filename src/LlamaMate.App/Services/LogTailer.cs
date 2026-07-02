using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaMate.App.Services;

public class LogTailer
{
    private readonly ConfigManager _config;
    private FileSystemWatcher? _watcher;
    private long _lastPosition;
    private CancellationTokenSource? _cts;

    public event Action<string>? OnNewLine;
    public event Action? OnFileChanged;

    private string LogPath => _config.LogPath();

    public LogTailer(ConfigManager config)
    {
        _config = config;
    }

    public void Start()
    {
        Stop();

        var logDir = Path.GetDirectoryName(LogPath);
        if (logDir != null) Directory.CreateDirectory(logDir);

        if (!File.Exists(LogPath))
        {
            using var _ = File.Create(LogPath);
        }

        _lastPosition = new FileInfo(LogPath).Length;
        _cts = new CancellationTokenSource();

        _watcher = new FileSystemWatcher
        {
            Path = Path.GetDirectoryName(LogPath) ?? ".",
            Filter = Path.GetFileName(LogPath),
            NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite
        };

        _watcher.Changed += OnLogChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnLogChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void OnLogChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            OnFileChanged?.Invoke();

            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (_lastPosition >= fs.Length) return;

            fs.Seek(_lastPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line != null)
                {
                    OnNewLine?.Invoke(line);
                }
            }

            _lastPosition = fs.Position;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Write(string line)
    {
        try
        {
            var logDir = Path.GetDirectoryName(LogPath);
            if (logDir != null) Directory.CreateDirectory(logDir);

            File.AppendAllText(LogPath, line + Environment.NewLine);
            _lastPosition = new FileInfo(LogPath).Length;
        }
        catch { }
    }

    public string ReadAll()
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            return File.ReadAllText(LogPath, Encoding.UTF8);
        }
        catch
        {
            return "";
        }
    }

    public string ReadTail(int maxBytes = 65536)
    {
        try
        {
            if (!File.Exists(LogPath)) return "";

            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return "";

            var readStart = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(readStart, SeekOrigin.Begin);

            using var reader = new StreamReader(fs, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }
}
