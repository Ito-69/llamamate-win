using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LlamaMate.App.Services;

public class ServerSettings
{
    public string ActiveModel { get; set; } = "";
    public int GpuLayers { get; set; } = 0;
    public int ContextSize { get; set; } = 4096;
    public int BatchSize { get; set; } = 512;
    public int Threads { get; set; } = 0;
    public int Port { get; set; } = 8080;
    public string Host { get; set; } = "127.0.0.1";
    public int NGpuLayers { get; set; } = 0;
    public string Profile { get; set; } = "balanced";
    public bool FlashAttention { get; set; } = false;
    public string KvCacheQuant { get; set; } = "f16";
    public string ExtraArgs { get; set; } = "";
    public bool AutoStart { get; set; } = false;
    public bool WelcomeDone { get; set; } = false;
}

public class ConfigManager
{
    private string ConfigPath { get; }

    public ServerSettings Settings { get; private set; } = new();

    public ConfigManager()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LlamaMate");
        ConfigPath = Path.Combine(configDir, "server.conf");
    }

    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Settings = new ServerSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Settings = JsonConvert.DeserializeObject<ServerSettings>(json) ?? new ServerSettings();
        }
        catch
        {
            Settings = new ServerSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir != null) Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
        File.WriteAllText(ConfigPath, json);
    }

    public string ModelsDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "models");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string BinDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaMate", "bin");
    }

    public string LogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LlamaMate", "Logs", "server.log");
    }
}
