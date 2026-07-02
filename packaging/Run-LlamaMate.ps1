<#
.SYNOPSIS
    Unblock and start LlamaMate on first run.
.DESCRIPTION
    Removes the Zone.Identifier (Mark-of-the-Web) that Windows SmartScreen
    applies to downloaded files, then launches LlamaMate.exe.
#>

$ErrorActionPreference = "Stop"

# Find LlamaMate-*.exe in the same folder
$exe = Get-ChildItem -Path $PSScriptRoot -Filter "LlamaMate-*.exe" -ErrorAction SilentlyContinue |
       Where-Object { $_.Name -notlike "*.sha256" } |
       Select-Object -First 1

if (-not $exe) {
    Write-Host "LlamaMate-*.exe not found in $($PSScriptRoot)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Found: $($exe.Name)" -ForegroundColor Cyan

# Unblock the file (removes Mark-of-the-Web)
try {
    Unblock-File -Path $exe.FullName -ErrorAction SilentlyContinue
    Write-Host "LlamaMate unblocked. Starting..." -ForegroundColor Green
} catch {
    Write-Host "(Could not unblock, trying anyway: $_)" -ForegroundColor Yellow
}

Start-Process -FilePath $exe.FullName -WorkingDirectory $PSScriptRoot
