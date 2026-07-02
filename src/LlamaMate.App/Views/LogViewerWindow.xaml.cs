using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LlamaMate.App.Services;

namespace LlamaMate.App.Views;

public partial class LogViewerWindow : Window
{
    private readonly LogTailer _logTailer;
    private readonly DispatcherTimer _refreshTimer;
    private bool _autoScroll = true;

    public LogViewerWindow(LogTailer logTailer)
    {
        InitializeComponent();

        _logTailer = logTailer;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => RefreshLogContent();
        _refreshTimer.Start();

        Loaded += (_, _) => RefreshLogContent();

        // Subscribe to live log events
        _logTailer.OnNewLine += OnNewLogLine;
        _logTailer.OnFileChanged += () =>
        {
            Dispatcher.Invoke(() => RefreshLogContent());
        };

        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _logTailer.OnNewLine -= OnNewLogLine;
        };
    }

    private void OnNewLogLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogContent.AppendText(line + Environment.NewLine);
            if (_autoScroll)
                LogScrollViewer.ScrollToBottom();
        });
    }

    private void RefreshLogContent()
    {
        try
        {
            var content = _logTailer.ReadTail(262144);
            LogContent.Text = content;
            if (_autoScroll)
                LogScrollViewer.ScrollToBottom();
        }
        catch
        {
            LogContent.Text = "Unable to read log file.";
        }
    }

    private void OpenInExplorer(object sender, RoutedEventArgs e)
    {
        var logDir = Path.GetDirectoryName(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LlamaMate", "Logs"));
        if (logDir != null)
            ShellRunner.OpenFolder(logDir);
    }

    private void RefreshLogs(object sender, RoutedEventArgs e)
    {
        RefreshLogContent();
    }

    private void ToggleAutoScroll(object sender, RoutedEventArgs e)
    {
        _autoScroll = !_autoScroll;
        AutoScrollToggle.Content = _autoScroll ? "Auto-Scroll: On" : "Auto-Scroll: Off";
    }

    private void ClearLogs(object sender, RoutedEventArgs e)
    {
        LogContent.Text = "";
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
