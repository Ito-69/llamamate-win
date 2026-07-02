using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace LlamaMate.App.Services;

public class ModelManager
{
    private readonly ConfigManager _config;
    private readonly HuggingFaceApi _hfApi;

    public ModelManager(ConfigManager config, HuggingFaceApi hfApi)
    {
        _config = config;
        _hfApi = hfApi;
    }

    public List<string> GetInstalledModels()
    {
        var modelsDir = _config.ModelsDir();
        if (!Directory.Exists(modelsDir))
            return new List<string>();

        return Directory.GetFiles(modelsDir, "*.gguf")
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToList()!;
    }

    public string? ActiveModel => _config.Settings.ActiveModel;

    public void SetActive(string modelName)
    {
        _config.Settings.ActiveModel = modelName;
        _config.Save();
    }

    public async Task DownloadModel(string repoId, string filename)
    {
        var modelsDir = _config.ModelsDir();
        var destPath = Path.Combine(modelsDir, filename);

        if (File.Exists(destPath))
            return;

        await _hfApi.DownloadFile(repoId, filename, destPath);
    }

    public async Task DownloadFromUrl(string url)
    {
        var uri = new Uri(url);
        var filename = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(filename))
            filename = "model.gguf";

        var modelsDir = _config.ModelsDir();
        var destPath = Path.Combine(modelsDir, filename);

        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }

    public void DeleteModel(string modelName)
    {
        var path = Path.Combine(_config.ModelsDir(), modelName);
        if (File.Exists(path))
        {
            File.Delete(path);
            if (_config.Settings.ActiveModel == modelName)
            {
                _config.Settings.ActiveModel = "";
                _config.Save();
            }
        }
    }

    public long GetModelSize(string modelName)
    {
        var path = Path.Combine(_config.ModelsDir(), modelName);
        if (!File.Exists(path))
            return 0;
        return new FileInfo(path).Length;
    }
}
