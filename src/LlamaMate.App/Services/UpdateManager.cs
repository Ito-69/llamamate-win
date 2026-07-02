using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LlamaMate.App.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string TagName { get; set; } = "";
    public string Body { get; set; } = "";
}

public class LlamacppRelease
{
    public string TagName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
}

public class UpdateManager
{
    private readonly HttpClient _http;

    public UpdateManager()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "LlamaMate/2.3");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<UpdateInfo?> CheckForAppUpdate()
    {
        try
        {
            var url = "https://api.github.com/repos/Ito-69/llamamate-win/releases/latest";
            var response = await _http.GetStringAsync(url);
            var release = JsonConvert.DeserializeObject<JObject>(response);
            if (release == null) return null;

            var tag = release["tag_name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(tag)) return null;

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null) return null;

            var tagVersionStr = tag.TrimStart('v');
            if (!System.Version.TryParse(tagVersionStr, out var tagVersion)) return null;

            if (tagVersion <= currentVersion) return null;

            var assets = release["assets"] as JArray;
            if (assets == null || assets.Count == 0) return null;

            string? downloadUrl = null;
            foreach (var asset in assets)
            {
                var name = asset["name"]?.ToString() ?? "";
                if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset["browser_download_url"]?.ToString();
                    break;
                }
            }

            if (downloadUrl == null) return null;

            return new UpdateInfo
            {
                Version = tag.TrimStart('v'),
                TagName = tag,
                DownloadUrl = downloadUrl,
                ReleaseUrl = release["html_url"]?.ToString() ?? url,
                Body = release["body"]?.ToString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<LlamacppRelease?> CheckForLlamacppUpdate()
    {
        try
        {
            var url = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";
            var response = await _http.GetStringAsync(url);
            var release = JsonConvert.DeserializeObject<JObject>(response);
            if (release == null) return null;

            var tag = release["tag_name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(tag)) return null;

            var assets = release["assets"] as JArray;
            if (assets == null) return null;

            var arch = IsX64() ? "x64" : "arm64";
            var preferred = $"win-cpu-{arch}.zip";
            string? downloadUrl = null;
            foreach (var asset in assets)
            {
                var name = asset["name"]?.ToString() ?? "";
                if (name.EndsWith(preferred, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset["browser_download_url"]?.ToString();
                    break;
                }
            }
            if (downloadUrl == null)
            {
                foreach (var asset in assets)
                {
                    var name = asset["name"]?.ToString() ?? "";
                    if (name.Contains("win-cpu") && name.EndsWith(".zip"))
                    {
                        downloadUrl = asset["browser_download_url"]?.ToString();
                        break;
                    }
                }
            }

            if (downloadUrl == null) return null;

            return new LlamacppRelease
            {
                TagName = tag,
                DownloadUrl = downloadUrl,
                ReleaseUrl = release["html_url"]?.ToString() ?? url
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsX64()
    {
        var arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
        return arch.Contains("64") && !arch.Contains("ARM", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> DownloadUpdate(UpdateInfo update)
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var fileName = $"LlamaMate-{update.Version}.msix";
            var filePath = Path.Combine(tempDir, fileName);

            using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadLlamacppUpdate(LlamacppRelease release)
    {
        var binDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaMate", "bin");

        var tempZip = Path.Combine(Path.GetTempPath(), $"llama-cpp-{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(binDir);

            using var dlClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            dlClient.DefaultRequestHeaders.Add("User-Agent", "LlamaMate/2.3");

            using var response = await dlClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            // Wait a moment in case AV is scanning the zip
            await Task.Delay(500);

            // Extract - delete existing files first to avoid locks
            if (File.Exists(Path.Combine(binDir, "llama-server.exe")))
            {
                try { File.Delete(Path.Combine(binDir, "llama-server.exe")); } catch { }
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, binDir, overwriteFiles: true);
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    public static bool IsVcRuntimeInstalled()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return File.Exists(Path.Combine(system32, "VCRUNTIME140_1.dll"))
            && File.Exists(Path.Combine(system32, "VCRUNTIME140.dll"))
            && File.Exists(Path.Combine(system32, "msvcp140.dll"));
    }

    public async Task<string> DownloadVcRedist()
    {
        var tempExe = Path.Combine(Path.GetTempPath(), $"vc_redist.x64-{Guid.NewGuid():N}.exe");

        using var dlClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        dlClient.DefaultRequestHeaders.Add("User-Agent", "LlamaMate/2.3");

        using var response = await dlClient.GetAsync(
            "https://aka.ms/vs/17/release/vc_redist.x64.exe",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (var fs = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(fs);
        }

        return tempExe;
    }

    public static int InstallVcRedistSilent(string redistExePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = redistExePath,
            Arguments = "/install /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas"
        };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(180_000);
        return p?.ExitCode ?? -1;
    }
}
