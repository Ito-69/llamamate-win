using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LlamaMate.App.Services;

namespace LlamaMate.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigManager _config;
    private readonly ServerManager _serverManager;
    private bool _dirty;

    public SettingsWindow(ConfigManager config, ServerManager serverManager)
    {
        InitializeComponent();

        _config = config;
        _serverManager = serverManager;

        LoadSettings();
        WireEvents();

        Loaded += (_, _) => UpdateRamEstimate();
    }

    private void WireEvents()
    {
        GpuLayersSlider.ValueChanged += (_, _) =>
        {
            GpuLayersValue.Text = GpuLayersSlider.Value == 0
                ? "0 (CPU only)"
                : $"{GpuLayersSlider.Value} layers";
            _dirty = true;
            UpdateRamEstimate();
        };

        ProfileCombo.SelectionChanged += (_, _) =>
        {
            ApplyProfile();
            _dirty = true;
            UpdateRamEstimate();
        };

        ContextCombo.SelectionChanged += (_, _) => { _dirty = true; UpdateRamEstimate(); };
        BatchSizeText.TextChanged += (_, _) => { _dirty = true; UpdateRamEstimate(); };
        FlashAttentionCheck.Checked += (_, _) => { _dirty = true; };
        FlashAttentionCheck.Unchecked += (_, _) => { _dirty = true; };
        PortText.TextChanged += (_, _) => { _dirty = true; };
        HostText.TextChanged += (_, _) => { _dirty = true; };
        ThreadsText.TextChanged += (_, _) => { _dirty = true; };
    }

    private void LoadSettings()
    {
        var s = _config.Settings;

        GpuLayersSlider.Value = s.GpuLayers;
        ContextCombo.SelectedIndex = s.ContextSize switch
        {
            2048 => 0, 4096 => 1, 8192 => 2, 16384 => 3, 32768 => 4,
            _ => 1
        };
        BatchSizeText.Text = s.BatchSize.ToString();
        FlashAttentionCheck.IsChecked = s.FlashAttention;
        KvCacheCombo.SelectedIndex = s.KvCacheQuant switch
        {
            "f16" => 0, "q8_0" => 1, "q4_0" => 2,
            _ => 0
        };
        PortText.Text = s.Port.ToString();
        HostText.Text = s.Host;
        ThreadsText.Text = s.Threads.ToString();

        ProfileCombo.SelectedIndex = s.Profile switch
        {
            "fast" => 0, "balanced" => 1, "accurate" => 2,
            _ => 1
        };

        _dirty = false;
    }

    private void ApplyProfile()
    {
        switch (ProfileCombo.SelectedIndex)
        {
            case 0: // Fast
                ProfileDescription.Text = "CPU-only, small context, low RAM usage";
                GpuLayersSlider.Value = 0;
                ContextCombo.SelectedIndex = 0; // 2048
                BatchSizeText.Text = "256";
                FlashAttentionCheck.IsChecked = false;
                break;
            case 1: // Balanced
                ProfileDescription.Text = "Mixed GPU/CPU, moderate context";
                GpuLayersSlider.Value = 20;
                ContextCombo.SelectedIndex = 1; // 4096
                BatchSizeText.Text = "512";
                FlashAttentionCheck.IsChecked = false;
                break;
            case 2: // Accurate
                ProfileDescription.Text = "Maximum GPU layers, large context, flash attention";
                GpuLayersSlider.Value = 99;
                ContextCombo.SelectedIndex = 3; // 16384
                BatchSizeText.Text = "1024";
                FlashAttentionCheck.IsChecked = true;
                break;
        }
    }

    private void UpdateRamEstimate()
    {
        try
        {
            var ctxSize = ContextCombo.SelectedItem switch
            {
                ComboBoxItem item when int.TryParse(item.Content?.ToString(), out var v) => v,
                _ => 4096
            };

            var batchSize = int.TryParse(BatchSizeText.Text, out var b) ? b : 512;

            // Rough estimate: model size + ctx * 2 * (n_batch / 512) + overhead
            var totalRam = 0L;
            try
            {
                using var mos = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var mo in mos.Get())
                    totalRam = Convert.ToInt64(mo["TotalPhysicalMemory"]);
            }
            catch { }

            var modelSize = 0L;
            if (!string.IsNullOrEmpty(_config.Settings.ActiveModel))
                modelSize = new FileInfo(
                    Path.Combine(_config.ModelsDir(), _config.Settings.ActiveModel)).Length;

            var ctxMem = ctxSize * (FlashAttentionCheck.IsChecked == true ? 2 : 8); // MB estimate
            var estimated = modelSize + ctxMem * 1024L * 1024L + 512 * 1024 * 1024L;

            var totalRamGb = totalRam / (1024.0 * 1024 * 1024);
            var estimatedGb = estimated / (1024.0 * 1024 * 1024);

            RamEstimate.Text = totalRam > 0
                ? $"RAM Estimate: ~{estimatedGb:F1} GB needed / {totalRamGb:F1} GB available"
                : $"RAM Estimate: ~{estimatedGb:F1} GB needed";
        }
        catch
        {
            RamEstimate.Text = "RAM estimate unavailable";
        }
    }

    private async void ApplyAndRestart(object sender, RoutedEventArgs e)
    {
        var s = _config.Settings;

        s.Profile = ProfileCombo.SelectedIndex switch { 0 => "fast", 1 => "balanced", 2 => "accurate", _ => "balanced" };
        s.GpuLayers = (int)GpuLayersSlider.Value;
        s.ContextSize = ContextCombo.SelectedItem switch
        {
            ComboBoxItem item when int.TryParse(item.Content?.ToString(), out var v) => v,
            _ => 4096
        };
        s.BatchSize = int.TryParse(BatchSizeText.Text, out var b) ? b : 512;
        s.FlashAttention = FlashAttentionCheck.IsChecked == true;
        s.KvCacheQuant = KvCacheCombo.SelectedItem switch
        {
            ComboBoxItem item => item.Content?.ToString() ?? "f16",
            _ => "f16"
        };
        s.Port = int.TryParse(PortText.Text, out var p) ? p : 8080;
        s.Host = HostText.Text;
        s.Threads = int.TryParse(ThreadsText.Text, out var t) ? t : 0;

        _config.Save();
        _dirty = false;

        await _serverManager.Restart();

        MessageBox.Show("Settings applied and server restarted.", "Settings",
            MessageBoxButton.OK, MessageBoxImage.Information);

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty)
        {
            var result = MessageBox.Show("Discard changes?", "Settings",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }
        Close();
    }
}
