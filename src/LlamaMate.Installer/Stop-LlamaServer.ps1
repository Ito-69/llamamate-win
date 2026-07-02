<#
.SYNOPSIS
    Stop the llama.cpp server process.
.DESCRIPTION
    Terminates all llama-server.exe processes and logs the shutdown.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"

$logFile = "$env:LOCALAPPDATA\LlamaMate\Logs\server.log"

$procs = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host "llama-server is not running."
    return
}

$count = $procs.Count
foreach ($p in $procs) {
    try {
        $p.Kill()
        Write-Host "Stopped llama-server (PID: $($p.Id))"
    }
    catch {
        Write-Warning "Failed to stop PID $($p.Id): $_"
    }
}

$msg = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Server stopped ($count process(es))"
Add-Content -Path $logFile -Value $msg -ErrorAction SilentlyContinue
