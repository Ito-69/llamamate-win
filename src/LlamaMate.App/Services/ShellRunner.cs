using System;
using System.Diagnostics;

namespace LlamaMate.App.Services;

public static class ShellRunner
{
    public static void OpenUrl(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    public static void OpenFile(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    public static void OpenFolder(string folderPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folderPath}\"",
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    public static void OpenFolderAndSelectFile(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    public static (string stdout, string stderr, int exitCode) RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return ("", "Failed to start PowerShell", -1);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (stdout, stderr, process.ExitCode);
    }

    public static void RunDetached(string exePath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
    }
}
