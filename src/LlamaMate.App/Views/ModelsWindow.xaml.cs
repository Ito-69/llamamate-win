using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using LlamaMate.App.Services;

namespace LlamaMate.App.Views;

public partial class ModelsWindow : Window
{
    private readonly ConfigManager _config;
    private readonly ModelManager _modelManager;
    private readonly HuggingFaceApi _hfApi;
    private readonly ServerManager _serverManager;

    private readonly List<HfModel> _browseResults = new();
    private readonly List<HfModel> _shortlist = new()
    {
        new HfModel { Id = "Qwen/Qwen2.5-1.5B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "Qwen/Qwen2.5-3B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "Qwen/Qwen2.5-7B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "bartowski/Meta-Llama-3.1-8B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "Qwen/Qwen2.5-14B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "Qwen/Qwen2.5-32B-Instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "google/gemma-2-9b-it-GGUF", FileCount = 1 },
        new HfModel { Id = "microsoft/Phi-3.5-mini-instruct-GGUF", FileCount = 1 },
        new HfModel { Id = "bartowski/Mistral-Nemo-Instruct-2407-GGUF", FileCount = 1 },
        new HfModel { Id = "bartowski/deepseek-coder-6.7b-instruct-GGUF", FileCount = 1 }
    };

    public ModelsWindow(ConfigManager config, ModelManager modelManager, HuggingFaceApi hfApi, ServerManager serverManager)
    {
        InitializeComponent();

        _config = config;
        _modelManager = modelManager;
        _hfApi = hfApi;
        _serverManager = serverManager;

        Loaded += async (_, _) =>
        {
            RefreshActiveModel();
            RefreshInstalled();
            RefreshShortlist();
            await LoadShortlistMetadata();
        };
    }

    private void RefreshAll()
    {
        RefreshActiveModel();
        RefreshInstalled();
        RefreshShortlist();
    }

    private void RefreshShortlist()
    {
        ModelsGrid.ItemsSource = null;
        ModelsGrid.ItemsSource = _shortlist;
    }

    private async Task LoadShortlistMetadata()
    {
        var tasks = _shortlist.Select(async model =>
        {
            var details = await _hfApi.GetModelDetails(model.Id);
            if (details != null)
            {
                model.Downloads = details.Downloads;
                model.Likes = details.Likes;
                model.PipelineTag = details.PipelineTag;
                model.LastModified = details.LastModified;
            }
            try
            {
                var files = await _hfApi.ListModelFiles(model.Id);
                model.FileCount = files.Count;
            }
            catch { model.FileCount = 0; }
        });

        await Task.WhenAll(tasks);
        RefreshShortlist();
    }

    private void RefreshActiveModel()
    {
        var active = _config.Settings.ActiveModel;
        if (string.IsNullOrEmpty(active))
        {
            ActiveModelName.Text = "No model selected";
            ActiveModelSize.Text = "";
            return;
        }

        ActiveModelName.Text = active;
        var size = _modelManager.GetModelSize(active);
        ActiveModelSize.Text = FormatSize(size);
    }

    private void RefreshInstalled()
    {
        var installed = _modelManager.GetInstalledModels();
        var active = _config.Settings.ActiveModel;

        var items = installed.Select(m => new InstalledModelItem
        {
            Name = m,
            Size = _modelManager.GetModelSize(m),
            IsActive = m == active ? "Yes" : ""
        }).ToList();

        InstalledGrid.ItemsSource = items;
    }

    private async void SearchModels(object sender, RoutedEventArgs e)
    {
        var query = SearchQuery.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            MessageBox.Show("Enter a search term.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SearchButton.IsEnabled = false;
        SearchButton.Content = "Searching...";

        try
        {
            var results = await _hfApi.SearchModels(query);
            _browseResults.Clear();
            _browseResults.AddRange(results);

            // Fetch file counts for results
            foreach (var model in _browseResults)
            {
                try
                {
                    var files = await _hfApi.ListModelFiles(model.Id);
                    model.FileCount = files.Count;
                }
                catch { model.FileCount = 0; }
            }

            ModelsGrid.ItemsSource = null;
            ModelsGrid.ItemsSource = _browseResults;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Search failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            SearchButton.Content = "Search";
        }
    }

    private async void ModelsGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ModelsGrid.SelectedItem is not HfModel model) return;

        try
        {
            var files = await _hfApi.ListModelFiles(model.Id);
            if (files.Count == 0)
            {
                MessageBox.Show("No GGUF files found for this model.", "Browse", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var file = files[0];
            var confirm = MessageBox.Show(
                $"Download {file.Path} ({FormatSize(file.Size)})?\nFrom: {model.Id}",
                "Download Model", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            await _modelManager.DownloadModel(model.Id, file.Path);
            RefreshInstalled();
            MessageBox.Show("Download complete.", "Download", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseActiveModel(object sender, RoutedEventArgs e)
    {
        var modelsDir = _config.ModelsDir();
        if (Directory.Exists(modelsDir))
            System.Diagnostics.Process.Start("explorer.exe", modelsDir);
    }

    private void ClearActiveModel(object sender, RoutedEventArgs e)
    {
        _config.Settings.ActiveModel = "";
        _config.Save();
        RefreshActiveModel();
    }

    private void InstalledContext_MakeActive(object sender, RoutedEventArgs e)
    {
        if (InstalledGrid.SelectedItem is InstalledModelItem item)
        {
            _modelManager.SetActive(item.Name);
            RefreshAll();

            if (_serverManager.IsRunning)
            {
                var result = MessageBox.Show(
                    $"Active model changed to:\n{item.Name}\n\nRestart the server now to apply?",
                    "Restart server?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        _ = _serverManager.Restart();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Restart failed: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }

    private void InstalledContext_OpenLocation(object sender, RoutedEventArgs e)
    {
        if (InstalledGrid.SelectedItem is InstalledModelItem item)
        {
            var fullPath = Path.Combine(_config.ModelsDir(), item.Name);
            ShellRunner.OpenFolderAndSelectFile(fullPath);
        }
    }

    private void InstalledContext_Delete(object sender, RoutedEventArgs e)
    {
        if (InstalledGrid.SelectedItem is InstalledModelItem item)
        {
            var confirm = MessageBox.Show(
                $"Delete {item.Name}?", "Delete Model",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            _modelManager.DeleteModel(item.Name);
            RefreshAll();
        }
    }

    private async void DownloadFromUrl(object sender, RoutedEventArgs e)
    {
        var url = InstallUrl.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Enter a model URL.", "Download", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DownloadFromUrlButton.IsEnabled = false;
        DownloadFromUrlButton.Content = "Downloading...";
        InstallStatus.Text = "Downloading...";

        try
        {
            await _modelManager.DownloadFromUrl(url);
            RefreshInstalled();
            InstallStatus.Text = "Download complete.";
            InstallUrl.Text = "";
        }
        catch (Exception ex)
        {
            InstallStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadFromUrlButton.IsEnabled = true;
            DownloadFromUrlButton.Content = "Download";
        }
    }

    private void CancelUrlDownload(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshInstalled(object sender, RoutedEventArgs e)
    {
        RefreshInstalled();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}

public class InstalledModelItem
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string SizeDisplay => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{Size / (1024.0 * 1024):F1} MB",
        _ => $"{Size / (1024.0 * 1024 * 1024):F1} GB"
    };
    public string IsActive { get; set; } = "";
}
