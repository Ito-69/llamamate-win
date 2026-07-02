<#
.SYNOPSIS
    Start the llama.cpp server using LlamaMate configuration.
.DESCRIPTION
    Reads server.conf, locates the model, and launches llama-server.exe.
    Called by the scheduled task at logon or manually from the tray app.
.PARAMETER ConfigPath
    Path to server.conf. Default: %APPDATA%\LlamaMate\server.conf
#>

[CmdletBinding()]
param(
    [string]$ConfigPath = "$env:APPDATA\LlamaMate\server.conf"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ConfigPath)) {
    Write-Error "Configuration not found: $ConfigPath"
    Write-Error "Run Install-LlamaMate.ps1 first."
    exit 1
}

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$binDir = "$env:LOCALAPPDATA\LlamaMate\bin"
$serverExe = Join-Path $binDir "llama-server.exe"

if (-not (Test-Path $serverExe)) {
    $serverExe = "llama-server.exe"
}

$modelsDir = "$env:USERPROFILE\models"
$modelPath = Join-Path $modelsDir $config.ActiveModel

if (-not (Test-Path $modelPath)) {
    Write-Warning "Active model not found: $modelPath"
    Write-Warning "Starting server without a model file"
    $modelArg = ""
}
else {
    $modelArg = "-m `"$modelPath`""
}

$argsList = @()
if ($modelArg) { $argsList += "-m"; $argsList += "`"$modelPath`"" }
$argsList += "-c"; $argsList += [string]$config.ContextSize
$argsList += "--port"; $argsList += [string]$config.Port
$argsList += "--host"; $argsList += $config.Host
$argsList += "-ngl"; $argsList += [string]$config.GpuLayers
$argsList += "-b"; $argsList += [string]$config.BatchSize

if ($config.Threads -gt 0) {
    $argsList += "-t"; $argsList += [string]$config.Threads
}

if ($config.FlashAttention -eq $true) {
    $argsList += "-fa"
}

if ($config.KvCacheQuant -and $config.KvCacheQuant -ne "f16") {
    $argsList += "--cache-type-k"; $argsList += $config.KvCacheQuant
}

$logDir = "$env:LOCALAPPDATA\LlamaMate\Logs"
$null = New-Item -ItemType Directory -Path $logDir -Force
$logFile = Join-Path $logDir "server.log"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $serverExe
$psi.Arguments = $argsList -join " "
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$psi.WorkingDirectory = $binDir

$process = [System.Diagnostics.Process]::Start($psi)

$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()

# Log the start
$startMsg = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Server started (PID: $($process.Id))"
Add-Content -Path $logFile -Value $startMsg

try {
    $process.WaitForExit()
    $stdout = $outputTask.GetAwaiter().GetResult()
    $stderr = $errorTask.GetAwaiter().GetResult()

    if ($stdout) { Add-Content -Path $logFile -Value $stdout }
    if ($stderr) { Add-Content -Path $logFile -Value $stderr }

    $exitMsg = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Server exited (PID: $($process.Id), ExitCode: $($process.ExitCode))"
    Add-Content -Path $logFile -Value $exitMsg
}
catch { }
