using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using LlamaMate.App.Services;

namespace LlamaMate.App.Views;

public partial class WelcomeWindow : Window
{
    private readonly App _app;

    public WelcomeWindow(App app)
    {
        InitializeComponent();
        _app = app;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _app.ConfigManager.Settings.WelcomeDone = true;
        _app.ConfigManager.Save();
        DialogResult = null;
        Close();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        InstallButton.Content = "Installing\u2026";

        try
        {
            await InstallLlamacpp();
            await DownloadModel();

            SetStatus("Starting server\u2026", 95);
            try
            {
                await _app.ServerManager.Start();
            }
            catch (Exception ex)
            {
                throw new Exception($"Installed, but failed to start server: {ex.Message}");
            }

            StatusText.Text = "Server running! Opening WebUI\u2026";
            ProgressLabel.Text = "Done";
            ProgressBar.Value = 100;

            await Task.Delay(500);
            _app.ConfigManager.Settings.WelcomeDone = true;
            _app.ConfigManager.Save();

            DialogResult = true;

            var cfg = _app.ConfigManager.Settings;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://{cfg.Host}:{cfg.Port}",
                UseShellExecute = true
            });
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            InstallButton.IsEnabled = true;
            InstallButton.Content = "Retry";
            SkipButton.IsEnabled = true;
        }
    }

    private async Task InstallLlamacpp()
    {
        if (!UpdateManager.IsVcRuntimeInstalled())
        {
            SetStatus("Downloading Visual C++ Runtime\u2026", 5);
            var redist = await _app.UpdateManager.DownloadVcRedist();

            SetStatus("Installing Visual C++ Runtime (admin prompt may appear)\u2026", 12);
            var exitCode = await Task.Run(() => UpdateManager.InstallVcRedistSilent(redist));
            try { File.Delete(redist); } catch { }

            if (exitCode != 0 && exitCode != 3010 && !UpdateManager.IsVcRuntimeInstalled())
            {
                throw new Exception(
                    $"VC++ Runtime install failed (code {exitCode}). Install it manually from https://aka.ms/vs/17/release/vc_redist.x64.exe and click Retry.");
            }
        }

        SetStatus("Downloading llama.cpp server\u2026", 20);

        var update = await _app.UpdateManager.CheckForLlamacppUpdate();
        if (update == null)
            throw new Exception("Could not find a llama.cpp release on GitHub.");

        SetStatus($"Downloading {update.TagName}\u2026", 40);
        await _app.UpdateManager.DownloadLlamacppUpdate(update);

        SetStatus("llama.cpp installed", 60);
    }

    private async Task DownloadModel()
    {
        const string repo = "bartowski/Qwen2.5-1.5B-Instruct-GGUF";
        const string file = "Qwen2.5-1.5B-Instruct-Q4_K_M.gguf";

        SetStatus("Downloading test model (Qwen2.5 1.5B, ~1 GB)\u2026", 60);

        await _app.ModelManager.DownloadModel(repo, file);
        _app.ModelManager.SetActive(file);

        SetStatus("Test model ready", 90);
    }

    private void SetStatus(string text, int progress)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = text;
            ProgressBar.Value = progress;
            ProgressLabel.Text = $"{progress}%";
        });
    }
}
