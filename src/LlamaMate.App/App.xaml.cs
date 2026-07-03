using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using LlamaMate.App.Services;
using LlamaMate.App.Views;

namespace LlamaMate.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private ModelsWindow? _modelsWindow;
    private SettingsWindow? _settingsWindow;
    private LogViewerWindow? _logViewerWindow;
    private bool? _lastRunningState;
    private string? _lastActiveModel;

    public ServerManager ServerManager { get; }
    public ConfigManager ConfigManager { get; }
    public ModelManager ModelManager { get; }
    public LogTailer LogTailer { get; }
    public UpdateManager UpdateManager { get; }
    public HuggingFaceApi HuggingFaceApi { get; }

    public App()
    {
        ConfigManager = new ConfigManager();
        HuggingFaceApi = new HuggingFaceApi();
        ModelManager = new ModelManager(ConfigManager, HuggingFaceApi);
        LogTailer = new LogTailer(ConfigManager);
        UpdateManager = new UpdateManager();
        ServerManager = new ServerManager(ConfigManager, LogTailer);
        ServerManager.StatusChanged += (_, _) =>
        {
            Dispatcher.Invoke(UpdateTrayStatus);
            // Re-check after a short delay, in case IsRunning hasn't propagated yet
            Task.Delay(500).ContinueWith(_ => Dispatcher.Invoke(UpdateTrayStatus));
            Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(UpdateTrayStatus));
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureSingleInstance();

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaMate");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(Path.Combine(appData, "bin"));
        Directory.CreateDirectory(Path.Combine(appData, "lib"));
        Directory.CreateDirectory(Path.Combine(appData, "Logs"));

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaMate");
        Directory.CreateDirectory(configDir);

        ConfigManager.Load();

        System.Windows.Forms.Application.EnableVisualStyles();

        if (!ServerManager.IsServerInstalled && !ConfigManager.Settings.WelcomeDone)
        {
            var welcome = new Views.WelcomeWindow(this);
            var result = welcome.ShowDialog();

            if (result == false)
            {
                Shutdown();
                return;
            }

            ConfigManager.Load();
        }

        CreateTrayIcon();

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => UpdateTrayStatus();
        timer.Start();

        base.OnStartup(e);
    }

    private void EnsureSingleInstance()
    {
        _mutex = new Mutex(true, "LlamaMate-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LlamaMate is already running.", "LlamaMate",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
        }
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new System.Windows.Forms.ContextMenuStrip();
        BuildTrayMenu();

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = CreateIcon(running: ServerManager.IsRunning),
            Text = "LlamaMate",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left)
                OpenWebUI();
        };

        UpdateTrayStatus();
    }

    private static System.Drawing.Icon CreateIcon(bool running)
    {
        var resourceName = running
            ? "LlamaMate.App.Assets.llama-running.png"
            : "LlamaMate.App.Assets.llama-stopped.png";

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                using (var img = System.Drawing.Image.FromStream(stream))
                {
                    var bmp = new System.Drawing.Bitmap(img);
                    var h = bmp.GetHicon();
                    var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
                    bmp.Dispose();
                    DestroyIcon(h);
                    return icon;
                }
            }
        }

        // Fallback: programmatically draw an orange/gray circle
        var fallback = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(fallback))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var brushColor = running
                ? System.Drawing.Color.FromArgb(255, 130, 54)
                : System.Drawing.Color.FromArgb(160, 160, 160);
            using (var brush = new System.Drawing.SolidBrush(brushColor))
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f))
            using (var font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold))
            {
                g.FillEllipse(brush, 4, 4, 24, 24);
                g.DrawEllipse(pen, 4, 4, 24, 24);
                g.DrawString("LM", font, System.Drawing.Brushes.White, 5, 5);
            }
            var h = fallback.GetHicon();
            var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }

    private static System.Drawing.Bitmap RenderIcon(System.Drawing.Bitmap source, bool running, int size)
    {
        var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);

        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Draw the source image scaled to size x size
            g.DrawImage(source, 0, 0, size, size);

            if (!running)
            {
                // Apply grayscale color matrix
                var matrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                {
                    new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                    new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                    new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                    new float[] { 0,       0,       0,       1, 0 },
                    new float[] { 0,       0,       0,       0, 1 }
                });
                var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetColorMatrix(matrix);
                g.DrawImage(bmp, new System.Drawing.Rectangle(0, 0, size, size), 0, 0, size, size, System.Drawing.GraphicsUnit.Pixel, attrs);
            }
        }

        return bmp;
    }

    private static void ApplyGrayscale(System.Drawing.Bitmap bmp)
    {
        var matrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
        {
            new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
            new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
            new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
            new float[] { 0,       0,       0,       1, 0 },
            new float[] { 0,       0,       0,       0, 1 }
        });
        var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(matrix);
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.DrawImage(bmp, rect, 0, 0, bmp.Width, bmp.Height, System.Drawing.GraphicsUnit.Pixel, attrs);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(System.IntPtr hIcon);

    private void BuildTrayMenu()
    {
        _trayMenu!.Items.Clear();

        var installed = ServerManager.IsServerInstalled;
        var running = ServerManager.IsRunning;

        AddMenuItem("", GetCurrentModelDisplay(), null, false);

        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        AddMenuItem("Open WebUI", null, (_, _) => OpenWebUI());

        if (installed)
        {
            if (running)
            {
                AddMenuItem("Stop Server", null, async (_, _) => await StopServer());
                AddMenuItem("Restart Server", "Ctrl+R", async (_, _) => await RestartServer());
            }
            else
            {
                AddMenuItem("Start Server", null, async (_, _) => await StartServer());
            }
        }
        else
        {
            AddMenuItem("Install llama.cpp\u2026", "Ctrl+I", async (_, _) => await InstallLlamacpp());
        }

        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        AddMenuItem("Models\u2026", "Ctrl+M", (_, _) => ShowModelsWindow());
        AddMenuItem("Server Settings\u2026", null, (_, _) => ShowSettingsWindow());
        AddMenuItem("View Server Logs\u2026", null, (_, _) => ShowLogViewer());
        AddMenuItem("Check for App Update\u2026", null, async (_, _) => await CheckAppUpdate());

        if (installed)
            AddMenuItem("Check for llama.cpp Update\u2026", null, async (_, _) => await CheckLlamacppUpdate());

        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var launchItem = new System.Windows.Forms.ToolStripMenuItem("Launch at Login")
        {
            Checked = ConfigManager.Settings.AutoStart,
            CheckOnClick = true
        };
        launchItem.CheckedChanged += async (_, _) => await ToggleAutoStart(launchItem.Checked);
        _trayMenu.Items.Add(launchItem);

        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        AddMenuItem("Uninstall\u2026", null, async (_, _) => await Uninstall());
        AddMenuItem("About LlamaMate", null, (_, _) => ShowAbout());
        AddMenuItem("Quit", "Ctrl+Q", (_, _) => Quit());
    }

    private void AddMenuItem(string text, string? shortcut, EventHandler? handler, bool enabled = true)
    {
        var label = shortcut != null ? $"{text}    {shortcut}" : text;
        var item = new System.Windows.Forms.ToolStripMenuItem(label)
        {
            Enabled = enabled
        };
        if (handler != null)
            item.Click += handler;
        _trayMenu!.Items.Add(item);
    }

    private string GetCurrentModelDisplay()
    {
        var model = ConfigManager.Settings.ActiveModel;
        if (string.IsNullOrEmpty(model))
            return "No model selected";
        return Path.GetFileNameWithoutExtension(model);
    }

    public void UpdateTrayStatus()
    {
        if (_trayIcon == null || _trayMenu == null) return;

        var isRunning = ServerManager.IsRunning;
        var activeModel = ConfigManager.Settings.ActiveModel;

        if (_lastRunningState != isRunning || _lastActiveModel != activeModel)
        {
            _lastRunningState = isRunning;
            _lastActiveModel = activeModel;

            var status = isRunning ? "running" : "stopped";
            _trayIcon.Text = $"LlamaMate - {GetCurrentModelDisplay()} ({status})";

            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateIcon(running: isRunning);
            if (oldIcon != null)
            {
                try { oldIcon.Dispose(); } catch { }
            }

            RefreshTrayMenu();
        }
    }

    private void RefreshTrayMenu()
    {
        if (_trayMenu == null) return;
        BuildTrayMenu();
    }

    private async Task InstallLlamacpp()
    {
        var result = MessageBox.Show(
            "llama-server.exe not found. Download the latest llama.cpp for Windows?",
            "Install llama.cpp", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var update = await UpdateManager.CheckForLlamacppUpdate();
            if (update == null)
            {
                MessageBox.Show("Could not find a llama.cpp release. Check your internet connection.",
                    "Install llama.cpp", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await UpdateManager.DownloadLlamacppUpdate(update);
            MessageBox.Show($"llama.cpp {update.TagName} installed successfully!",
                "Install llama.cpp", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to install llama.cpp:\n{ex.Message}",
                "Install llama.cpp", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshTrayMenu();
    }

    private void OpenWebUI()
    {
        var cfg = ConfigManager.Settings;
        var url = $"http://{cfg.Host}:{cfg.Port}";
        ShellRunner.OpenUrl(url);
    }

    private async Task StartServer()
    {
        try
        {
            await ServerManager.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LlamaMate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshTrayMenu();
    }

    private async Task RestartServer()
    {
        try
        {
            await ServerManager.Restart();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LlamaMate",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshTrayMenu();
    }

    private async Task StopServer()
    {
        await ServerManager.Stop();
        RefreshTrayMenu();
    }

    private void ShowModelsWindow()
    {
        if (_modelsWindow == null || !_modelsWindow.IsVisible)
        {
            _modelsWindow = new ModelsWindow(ConfigManager, ModelManager, HuggingFaceApi, ServerManager);
            _modelsWindow.Closed += (_, _) => { UpdateTrayStatus(); };
        }
        _modelsWindow.Show();
        _modelsWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow(ConfigManager, ServerManager);
            _settingsWindow.Closed += (_, _) => { UpdateTrayStatus(); };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowLogViewer()
    {
        if (_logViewerWindow == null || !_logViewerWindow.IsVisible)
        {
            _logViewerWindow = new LogViewerWindow(LogTailer);
        }
        _logViewerWindow.Show();
        _logViewerWindow.Activate();
    }

    private async Task CheckAppUpdate()
    {
        var update = await UpdateManager.CheckForAppUpdate();
        if (update == null)
        {
            MessageBox.Show("You have the latest version.", "LlamaMate Update",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Version {update.Version} is available. Download and install?",
            "LlamaMate Update", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var path = await UpdateManager.DownloadUpdate(update);
            if (path != null)
            {
                ShellRunner.OpenFile(path);
                Quit();
            }
        }
    }

    private async Task CheckLlamacppUpdate()
    {
        var update = await UpdateManager.CheckForLlamacppUpdate();
        if (update == null)
        {
            MessageBox.Show("You have the latest llama.cpp version.", "llama.cpp Update",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Version {update.TagName} is available. Download and install?",
            "llama.cpp Update", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await UpdateManager.DownloadLlamacppUpdate(update);
            MessageBox.Show("llama.cpp updated. Restart the server to apply.", "llama.cpp Update",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task ToggleAutoStart(bool enable)
    {
        ConfigManager.Settings.AutoStart = enable;
        ConfigManager.Save();
        if (enable)
            await ServerManager.RegisterScheduledTask();
        else
            await ServerManager.UnregisterScheduledTask();
    }

    private async Task Uninstall()
    {
        var result = MessageBox.Show(
            "Uninstall LlamaMate? This will remove the application and server files, but keep your models.",
            "Uninstall LlamaMate", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await ServerManager.Stop();
            await ServerManager.UnregisterScheduledTask();

            var scriptPath = Path.Combine(Path.GetTempPath(), "LlamaMate-Uninstall.ps1");
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("LlamaMate.App.Scripts.Uninstall-LlamaMate.ps1"))
            {
                if (stream == null)
                    throw new Exception("Uninstall script not found in resources.");

                using var fs = File.Create(scriptPath);
                stream.CopyTo(fs);
            }

            var (stdout, stderr, exit) = ShellRunner.RunPowerShell(
                $"& '{scriptPath}' -KeepModels");

            try { File.Delete(scriptPath); } catch { }

            if (exit != 0)
            {
                MessageBox.Show($"Uninstall completed with warnings.\n\n{stderr}",
                    "Uninstall", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstall error: {ex.Message}",
                "Uninstall", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Quit();
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            "LlamaMate for Windows v" + Assembly.GetExecutingAssembly().GetName().Version,
            "About LlamaMate", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Quit()
    {
        _trayIcon?.Dispose();
        ServerManager.Cleanup();
        LogTailer.Stop();
        Shutdown();
    }
}
