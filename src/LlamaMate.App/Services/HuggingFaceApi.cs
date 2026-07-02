using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LlamaMate.App.Services;

public class HuggingFaceApi
{
    private readonly HttpClient _http;

    public HuggingFaceApi()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "LlamaMate/2.3");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<HfModel>> SearchModels(string query, int limit = 30)
    {
        var url = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(query)}" +
                  $"&sort=downloads&direction=-1&limit={limit}" +
                  "&filter=gguf";

        var response = await _http.GetStringAsync(url);
        var models = JsonConvert.DeserializeObject<List<JObject>>(response);
        if (models == null) return new List<HfModel>();

        var result = new List<HfModel>();
        foreach (var m in models)
        {
            var modelId = m["modelId"]?.ToString() ?? m["id"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(modelId)) continue;

            result.Add(new HfModel
            {
                Id = modelId,
                Downloads = m["downloads"]?.Value<long>() ?? 0,
                Likes = m["likes"]?.Value<int>() ?? 0,
                PipelineTag = m["pipeline_tag"]?.ToString() ?? "",
                LastModified = m["lastModified"]?.ToString() ?? ""
            });
        }

        return result;
    }

    public async Task<List<HfFile>> ListModelFiles(string modelId)
    {
        var url = $"https://huggingface.co/api/models/{modelId}/tree/main";
        var response = await _http.GetStringAsync(url);
        var entries = JsonConvert.DeserializeObject<List<JObject>>(response);
        if (entries == null) return new List<HfFile>();

        var result = new List<HfFile>();
        foreach (var e in entries)
        {
            var path = e["path"]?.ToString() ?? "";
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new HfFile
            {
                Path = path,
                Size = e["size"]?.Value<long>() ?? 0
            });
        }

        return result;
    }

    public async Task DownloadFile(string repoId, string filename, string destPath)
    {
        var url = $"https://huggingface.co/{repoId}/resolve/main/{filename}";

        // Use a longer timeout for downloads
        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        client.DefaultRequestHeaders.Add("User-Agent", "LlamaMate/2.3");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var dir = Path.GetDirectoryName(destPath);
        if (dir != null) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }
}

public class HfModel
{
    public string Id { get; set; } = "";
    public long Downloads { get; set; }
    public int Likes { get; set; }
    public string PipelineTag { get; set; } = "";
    public string LastModified { get; set; } = "";
    public int FileCount { get; set; }
}

public class HfFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
}
