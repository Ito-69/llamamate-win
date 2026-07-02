using System;
using System.Windows;
using LlamaMate.App.Services;

namespace LlamaMate.App.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateManager _updateManager;
    private readonly UpdateInfo _updateInfo;

    public UpdateWindow(UpdateManager updateManager, UpdateInfo updateInfo)
    {
        InitializeComponent();

        _updateManager = updateManager;
        _updateInfo = updateInfo;

        VersionInfo.Text = $"Version {updateInfo.Version} is now available (you have {GetCurrentVersion()})";
        ReleaseNotes.Text = updateInfo.Body;
    }

    private async void DownloadAndInstall(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        DownloadButton.Content = "Downloading...";

        try
        {
            var path = await _updateManager.DownloadUpdate(_updateInfo);
            if (path != null)
            {
                ShellRunner.OpenFile(path);
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show("Download failed.", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
                DownloadButton.IsEnabled = true;
                DownloadButton.Content = "Download & Install";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Download failed: {ex.Message}", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
            DownloadButton.IsEnabled = true;
            DownloadButton.Content = "Download & Install";
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string GetCurrentVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v?.ToString() ?? "0.0.0";
    }
}
