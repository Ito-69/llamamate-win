using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using LlamaMate.App.Services;
using LlamaMate.App.Views;

namespace LlamaMate.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private ModelsWindow? _modelsWindow;
    private SettingsWindow? _settingsWindow;
    private LogViewerWindow? _logViewerWindow;

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

        CreateTrayIcon();
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
        var iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LlamaMate.App.Assets.llama.ico");
        if (iconStream == null)
        {
            iconStream = new MemoryStream(GetFallbackIcoBytes());
        }

        _trayIcon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            ToolTipText = "LlamaMate",
            ContextMenu = BuildTrayMenu()
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) => OpenWebUI();
    }

    private System.Windows.Controls.ContextMenu BuildTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var currentModelItem = new System.Windows.Controls.MenuItem
        {
            Header = GetCurrentModelDisplay(),
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };
        menu.Items.Add(currentModelItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "_Open WebUI", "Ctrl+O", (_, _) => OpenWebUI());
        AddMenuItem(menu, "_Restart Server", "Ctrl+R", async (_, _) => await RestartServer());
        AddMenuItem(menu, "_Stop Server", "", async (_, _) => await StopServer());
        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "_Models\u2026", "Ctrl+M", (_, _) => ShowModelsWindow());
        AddMenuItem(menu, "Server _Settings\u2026", "", (_, _) => ShowSettingsWindow());
        AddMenuItem(menu, "_View Server Logs\u2026", "", (_, _) => ShowLogViewer());
        AddMenuItem(menu, "Check for _App Update\u2026", "", async (_, _) => await CheckAppUpdate());
        AddMenuItem(menu, "Check for llama.cpp _Update\u2026", "", async (_, _) => await CheckLlamacppUpdate());
        menu.Items.Add(new System.Windows.Controls.Separator());

        var launchAtLoginItem = new System.Windows.Controls.MenuItem
        {
            Header = "_Launch at Login",
            IsCheckable = true,
            IsChecked = ConfigManager.Settings.AutoStart
        };
        launchAtLoginItem.Checked += async (_, _) => await ToggleAutoStart(true);
        launchAtLoginItem.Unchecked += async (_, _) => await ToggleAutoStart(false);
        menu.Items.Add(launchAtLoginItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        AddMenuItem(menu, "_Uninstall\u2026", "", async (_, _) => await Uninstall());
        AddMenuItem(menu, "_About LlamaMate", "", (_, _) => ShowAbout());
        AddMenuItem(menu, "_Quit", "Ctrl+Q", (_, _) => Quit());

        return menu;
    }

    private static void AddMenuItem(
        System.Windows.Controls.ContextMenu menu,
        string header,
        string shortcut,
        RoutedEventHandler handler)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = string.IsNullOrEmpty(shortcut) ? header : $"{header}\t{shortcut}",
            InputGestureText = shortcut
        };
        item.Click += handler;
        menu.Items.Add(item);
    }

    private string GetCurrentModelDisplay()
    {
        var model = ConfigManager.Settings.ActiveModel;
        if (string.IsNullOrEmpty(model))
            return "\u26AA No model selected";
        var status = ServerManager.IsRunning ? "\uD83D\uDFE2" : "\u26AA";
        return $"{status} {Path.GetFileNameWithoutExtension(model)}";
    }

    public void UpdateTrayStatus()
    {
        if (_trayIcon == null) return;

        var display = GetCurrentModelDisplay();
        if (_trayIcon.ContextMenu?.Items[0] is System.Windows.Controls.MenuItem item)
        {
            item.Header = display;
        }

        _trayIcon.ToolTipText = $"LlamaMate - {display}";
    }

    private void OpenWebUI()
    {
        var cfg = ConfigManager.Settings;
        var url = $"http://{cfg.Host}:{cfg.Port}";
        ShellRunner.OpenUrl(url);
    }

    private async Task RestartServer()
    {
        await ServerManager.Restart();
        UpdateTrayStatus();
    }

    private async Task StopServer()
    {
        await ServerManager.Stop();
        UpdateTrayStatus();
    }

    private void ShowModelsWindow()
    {
        if (_modelsWindow == null || !_modelsWindow.IsVisible)
        {
            _modelsWindow = new ModelsWindow(ConfigManager, ModelManager, HuggingFaceApi);
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

        if (result == MessageBoxResult.Yes)
        {
            await ServerManager.Stop();
            await ServerManager.UnregisterScheduledTask();
            ShellRunner.RunPowerShell("Uninstall-LlamaMate.ps1 -KeepModels");
            Quit();
        }
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

    private static byte[] GetFallbackIcoBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write((short)32);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(0);
        bw.Write(22);
        bw.Write(0);
        bw.Write(0);
        for (int i = 0; i < 22; i++) bw.Write((byte)0);
        return ms.ToArray();
    }
}
