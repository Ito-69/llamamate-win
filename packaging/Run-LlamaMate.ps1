<#
.SYNOPSIS
    Unblock and start LlamaMate on first run.
.DESCRIPTION
    Removes the Zone.Identifier (Mark-of-the-Web) that Windows SmartScreen
    applies to downloaded files, then launches LlamaMate.exe.
#>

$ErrorActionPreference = "Stop"
$exe = Join-Path $PSScriptRoot "LlamaMate-1.0.0.exe"

if (-not (Test-Path $exe)) {
    Write-Host "LlamaMate-1.0.0.exe not found in $($PSScriptRoot)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Unblock the file (removes Mark-of-the-Web)
try {
    Unblock-File -Path $exe -ErrorAction SilentlyContinue
    Write-Host "LlamaMate unblocked. Starting..." -ForegroundColor Green
} catch {
    Write-Host "(Could not unblock, trying anyway: $_)" -ForegroundColor Yellow
}

Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot
