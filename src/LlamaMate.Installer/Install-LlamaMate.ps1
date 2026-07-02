<#
.SYNOPSIS
    Install LlamaMate for Windows: llama.cpp server, config, scheduled task, and tray app.
.DESCRIPTION
    Parallels install-llama.sh for macOS. Downloads llama-server binaries,
    installs Python dependencies, registers a scheduled task for auto-start,
    and sets up the LlamaMate tray application.
.PARAMETER Download
    Download and install llama.cpp binaries from GitHub.
.PARAMETER SkipLlamacpp
    Skip downloading llama.cpp (use existing binaries).
.PARAMETER InstallApp
    Install the LlamaMate WPF tray application.
.PARAMETER ModelsDir
    Custom models directory. Default: ~\models
.PARAMETER BinDir
    Custom binary directory. Default: %LOCALAPPDATA%\LlamaMate\bin
.PARAMETER ConfigDir
    Custom config directory. Default: %APPDATA%\LlamaMate
#>

[CmdletBinding()]
param(
    [switch]$Download,
    [switch]$SkipLlamacpp,
    [switch]$InstallApp,
    [string]$ModelsDir = "$env:USERPROFILE\models",
    [string]$BinDir = "$env:LOCALAPPDATA\LlamaMate\bin",
    [string]$ConfigDir = "$env:APPDATA\LlamaMate"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Test-Prerequisites {
    Write-Host "Checking prerequisites..." -ForegroundColor Cyan

    # Windows version
    $os = Get-CimInstance Win32_OperatingSystem
    $version = [Version]$os.Version
    if ($version.Major -lt 10) {
        throw "Windows 10 or later is required (detected: $($os.Caption))"
    }
    Write-Host "  [OK] Windows $($os.Caption)" -ForegroundColor Green

    # PowerShell version
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        throw "PowerShell 5.1 or later is required"
    }
    Write-Host "  [OK] PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Green

    # Internet connectivity
    try {
        $null = Invoke-WebRequest -Uri "https://api.github.com" -UseBasicParsing -TimeoutSec 5
        Write-Host "  [OK] Internet connectivity" -ForegroundColor Green
    }
    catch {
        throw "No internet connection detected"
    }

    # Disk space (~10 GB)
    $drive = Get-PSDrive -Name (Split-Path $ModelsDir -Qualifier).TrimEnd(':')
    if ($drive.Free -lt 10GB) {
        throw "Insufficient disk space. Need ~10 GB free, have $([math]::Round($drive.Free / 1GB, 1)) GB"
    }
    Write-Host "  [OK] Disk space: $([math]::Round($drive.Free / 1GB, 1)) GB free" -ForegroundColor Green
}

function Install-LlamaCpp {
    Write-Host "Installing llama.cpp binaries..." -ForegroundColor Cyan

    $tempDir = Join-Path $env:TEMP "llamamate-install"
    $null = New-Item -ItemType Directory -Path $tempDir -Force

    try {
        # Fetch latest release
        $releaseUrl = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest"
        $release = Invoke-RestMethod -Uri $releaseUrl -UseBasicParsing
        $tagName = $release.tag_name
        Write-Host "  Latest release: $tagName" -ForegroundColor Yellow

        # Find the CPU-only Windows build matching the current architecture
        $isX64 = [Environment]::Is64BitOperatingSystem
        $arch = if ($env:PROCESSOR_ARCHITECTURE -match 'ARM') { 'arm64' } else { 'x64' }
        $preferredPattern = "win-cpu-$arch.zip"

        $asset = $release.assets | Where-Object { $_.name -like $preferredPattern } | Select-Object -First 1
        if (-not $asset) {
            $asset = $release.assets | Where-Object { $_.name -like "*win-cpu*" -and $_.name -like "*.zip" } | Select-Object -First 1
        }
        if (-not $asset) {
            # Fallback: any Windows zip
            $asset = $release.assets | Where-Object { $_.name -like "*win*" -and $_.name -like "*.zip" } | Select-Object -First 1
        }
        if (-not $asset) {
            throw "No Windows build found in latest release"
        }

        $zipPath = Join-Path $tempDir $asset.name
        Write-Host "  Downloading $($asset.name)..." -ForegroundColor Yellow

        # Download with progress
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing

        Write-Host "  Extracting..." -ForegroundColor Yellow

        # Ensure bin directory exists
        $null = New-Item -ItemType Directory -Path $BinDir -Force

        # Extract to temp then copy files
        $extractDir = Join-Path $tempDir "extracted"
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force

        # Copy llama-server.exe and dependencies
        Get-ChildItem -Path $extractDir -Recurse -Include "llama-server.exe", "llama.dll", "llama.h" |
            Copy-Item -Destination $BinDir -Force

        if ($asset.name -like "*cuda*") {
            Get-ChildItem -Path $extractDir -Recurse -Include "cublas64_*.dll", "cudart64_*.dll", "cublasLt64_*.dll" |
                Copy-Item -Destination $BinDir -Force
        }

        Write-Host "    Extracted to: $BinDir" -ForegroundColor Gray

        # Verify
        $serverExe = Join-Path $BinDir "llama-server.exe"
        if (-not (Test-Path $serverExe)) {
            throw "llama-server.exe not found after extraction"
        }

        Write-Host "  [OK] llama-server installed at: $serverExe" -ForegroundColor Green
    }
    finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-PythonDeps {
    Write-Host "Installing Python dependencies..." -ForegroundColor Cyan
    try {
        $pip = Get-Command "pip" -ErrorAction SilentlyContinue
        if (-not $pip) {
            $pip = Get-Command "pip3" -ErrorAction SilentlyContinue
        }
        if ($pip) {
            & $pip.Source install --user huggingface_hub 2>&1 | Out-Null
            Write-Host "  [OK] huggingface_hub installed" -ForegroundColor Green
        }
        else {
            Write-Host "  [SKIP] Python/pip not found. huggingface_hub not installed." -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "  [WARN] Failed to install Python deps: $_" -ForegroundColor Yellow
    }
}

function Initialize-Config {
    Write-Host "Initializing configuration..." -ForegroundColor Cyan

    $null = New-Item -ItemType Directory -Path $ConfigDir -Force
    $configFile = Join-Path $ConfigDir "server.conf"

    if (-not (Test-Path $configFile)) {
        $config = @{
            ActiveModel    = ""
            GpuLayers      = 0
            ContextSize    = 4096
            BatchSize      = 512
            Threads        = 0
            Port           = 8080
            Host           = "127.0.0.1"
            Profile        = "balanced"
            FlashAttention = $false
            KvCacheQuant   = "f16"
            ExtraArgs      = ""
            AutoStart      = $false
        } | ConvertTo-Json
        Set-Content -Path $configFile -Value $config -Encoding UTF8
        Write-Host "  [OK] Config written: $configFile" -ForegroundColor Green
    }
    else {
        Write-Host "  [SKIP] Config already exists: $configFile" -ForegroundColor Yellow
    }
}

function Write-StartScript {
    Write-Host "Writing start script..." -ForegroundColor Cyan

    $null = New-Item -ItemType Directory -Path $BinDir -Force
    $scriptPath = Join-Path $BinDir "Start-Server.ps1"

    $scriptContent = @'
param(
    [string]$ConfigPath = "$env:APPDATA\LlamaMate\server.conf"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ConfigPath)) {
    Write-Error "Config not found: $ConfigPath"
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

# Check model
if (-not (Test-Path $modelPath)) {
    Write-Warning "Model not found: $modelPath"
    Write-Warning "Starting server without a model file"
    $modelArg = ""
}
else {
    $modelArg = "-m `"$modelPath`""
}

# RAM check
try {
    $totalRam = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
    $modelSize = (Get-Item $modelPath -ErrorAction SilentlyContinue).Length
    $ctxMem = $config.ContextSize * 2 * 1024 * 1024  # rough ctx memory estimate
    $estimatedNeed = $modelSize + $ctxMem + 512MB
    if ($estimatedNeed -gt 0.9 * $totalRam) {
        Write-Warning "Estimated RAM usage ($([math]::Round($estimatedNeed/1GB,1)) GB) exceeds 90% of total RAM ($([math]::Round($totalRam/1GB,1)) GB)"
    }
}
catch { }

$argsList = @(
    $modelArg
    "-c", "$($config.ContextSize)"
    "--port", "$($config.Port)"
    "--host", "$($config.Host)"
    "-ngl", "$($config.GpuLayers)"
    "-b", "$($config.BatchSize)"
)

if ($config.Threads -gt 0) {
    $argsList += "-t"
    $argsList += "$($config.Threads)"
}

if ($config.FlashAttention -eq $true) {
    $argsList += "-fa"
}

if ($config.KvCacheQuant -and $config.KvCacheQuant -ne "f16") {
    $argsList += "--cache-type-k"
    $argsList += $config.KvCacheQuant
}

if ($config.ExtraArgs) {
    $argsList += $config.ExtraArgs
}

$logDir = "$env:LOCALAPPDATA\LlamaMate\Logs"
$null = New-Item -ItemType Directory -Path $logDir -Force
$logFile = Join-Path $logDir "server.log"

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $serverExe
$startInfo.Arguments = $argsList -join " "
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$startInfo.WorkingDirectory = $binDir

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$process.Start()

# Redirect output to log file
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()

Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
'@

    Set-Content -Path $scriptPath -Value $scriptContent -Encoding UTF8
    Write-Host "  [OK] Start script: $scriptPath" -ForegroundColor Green
}

function Register-ScheduledTask {
    Write-Host "Registering scheduled task for auto-start..." -ForegroundColor Cyan

    $taskName = "LlamaMate-Server"
    $scriptPath = Join-Path $BinDir "Start-Server.ps1"

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERNAME" -RunLevel Limited

    try {
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force
        Write-Host "  [OK] Scheduled task '$taskName' registered" -ForegroundColor Green
    }
    catch {
        Write-Host "  [WARN] Failed to register scheduled task: $_" -ForegroundColor Yellow
    }
}

function Add-ToUserPath {
    param(
        [string]$PathToAdd
    )

    Write-Host "Adding to user PATH..." -ForegroundColor Cyan

    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($userPath -split ";" -contains $PathToAdd) {
        Write-Host "  [SKIP] Already in PATH: $PathToAdd" -ForegroundColor Yellow
        return
    }

    $newPath = if ($userPath) { "$userPath;$PathToAdd" } else { $PathToAdd }
    [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")

    # Also update current session
    $env:PATH = "$env:PATH;$PathToAdd"

    Write-Host "  [OK] Added to PATH: $PathToAdd" -ForegroundColor Green
}

# ===== Main =====

Write-Host @"

╔══════════════════════════════════╗
║     LlamaMate for Windows        ║
║     Installation Script          ║
╚══════════════════════════════════╝

"@ -ForegroundColor Magenta

Test-Prerequisites

# Create directories
$null = New-Item -ItemType Directory -Path $ModelsDir -Force
$null = New-Item -ItemType Directory -Path $BinDir -Force
$null = New-Item -ItemType Directory -Path $ConfigDir -Force

# Install llama.cpp
if (-not $SkipLlamacpp) {
    Install-LlamaCpp
}
else {
    Write-Host "Skipping llama.cpp installation (--SkipLlamacpp)" -ForegroundColor Yellow
}

# Python deps
Install-PythonDeps

# Config
Initialize-Config

# Start script
Write-StartScript

# Scheduled task
Register-ScheduledTask

# PATH
Add-ToUserPath -PathToAdd $BinDir

Write-Host @"

╔══════════════════════════════════╗
║     Installation Complete!       ║
║                                  ║
║  llama-server: $BinDir           ║
║  Config:       $ConfigDir        ║
║  Models:       $ModelsDir        ║
║                                  ║
║  Start server: Start-LlamaServer ║
║  Tray app:     LlamaMate.exe     ║
╚══════════════════════════════════╝

"@ -ForegroundColor Magenta
