<#
.SYNOPSIS
    Uninstall LlamaMate: stop server, remove scheduled task, delete files.
.PARAMETER KeepModels
    If set, preserve the models directory (~\models).
.PARAMETER KeepConfig
    If set, preserve the config directory (%APPDATA%\LlamaMate).
#>

[CmdletBinding()]
param(
    [switch]$KeepModels,
    [switch]$KeepConfig
)

$ErrorActionPreference = "Continue"

$BinDir = "$env:LOCALAPPDATA\LlamaMate\bin"
$AppDir = "$env:LOCALAPPDATA\LlamaMate"
$ConfigDir = "$env:APPDATA\LlamaMate"
$ModelsDir = "$env:USERPROFILE\models"

Write-Host "Uninstalling LlamaMate..." -ForegroundColor Cyan

# 1. Stop server process
Write-Host "  Stopping llama-server..." -ForegroundColor Yellow
try {
    $procs = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        $p.Kill()
        Write-Host "    Killed PID $($p.Id)" -ForegroundColor Gray
    }
}
catch { }

# 2. Unregister scheduled task
Write-Host "  Removing scheduled task..." -ForegroundColor Yellow
try {
    Unregister-ScheduledTask -TaskName "LlamaMate-Server" -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "    Scheduled task removed" -ForegroundColor Gray
}
catch { }

# 3. Remove PATH entry
Write-Host "  Removing from PATH..." -ForegroundColor Yellow
try {
    $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
    if ($userPath) {
        $entries = $userPath -split ";" | Where-Object { $_ -ne $BinDir -and $_ -ne $AppDir\bin }
        $newPath = $entries -join ";"
        [Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
        Write-Host "    PATH entry removed" -ForegroundColor Gray
    }
}
catch { }

# 4. Remove files
Write-Host "  Removing files..." -ForegroundColor Yellow

if (-not $KeepConfig -and (Test-Path $ConfigDir)) {
    try {
        Remove-Item -Path $ConfigDir -Recurse -Force
        Write-Host "    Config removed: $ConfigDir" -ForegroundColor Gray
    }
    catch { Write-Host "    WARN: Could not remove config: $_" -ForegroundColor Yellow }
}

if (Test-Path $BinDir) {
    try {
        Remove-Item -Path $BinDir -Recurse -Force
        Write-Host "    Binaries removed: $BinDir" -ForegroundColor Gray
    }
    catch { Write-Host "    WARN: Could not remove binaries: $_" -ForegroundColor Yellow }
}

if (Test-Path $AppDir) {
    try {
        Remove-Item -Path $AppDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "    App data removed: $AppDir" -ForegroundColor Gray
    }
    catch { }
}

if ($KeepModels) {
    Write-Host "  [KEPT] Models: $ModelsDir" -ForegroundColor Green
}
else {
    $confirm = Read-Host "  Remove models directory ($ModelsDir)? (y/N)"
    if ($confirm -eq "y" -or $confirm -eq "Y") {
        try {
            Remove-Item -Path $ModelsDir -Recurse -Force
            Write-Host "    Models removed: $ModelsDir" -ForegroundColor Gray
        }
        catch { Write-Host "    WARN: Could not remove models: $_" -ForegroundColor Yellow }
    }
}

Write-Host @"

╔══════════════════════════════════╗
║     Uninstall Complete!          ║
╚══════════════════════════════════╝

"@ -ForegroundColor Magenta
