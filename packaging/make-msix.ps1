<#
.SYNOPSIS
    Build the LlamaMate MSIX package for distribution.
.DESCRIPTION
    Publishes the WPF app, creates a self-signed cert if needed,
    and packs the MSIX using makeappx.exe from the Windows SDK.
.PARAMETER Version
    Version string for the package (e.g. 2.3.0.0). Default: from csproj.
.PARAMETER Configuration
    Build configuration (Release or Debug). Default: Release.
.PARAMETER OutputDir
    Directory for the output MSIX. Default: packaging\output.
#>

[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path $PSScriptRoot "output")
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path $PSScriptRoot -Parent
$AppProject = Join-Path $RepoRoot "src\LlamaMate.App\LlamaMate.App.csproj"
$ManifestPath = Join-Path $PSScriptRoot "Package.appxmanifest"

# Resolve version
if (-not $Version) {
    $csproj = [xml](Get-Content $AppProject)
    $versionNode = $csproj.Project.PropertyGroup.Version
    if ($versionNode) {
        $Version = $versionNode
    }
    else {
        $Version = "2.3.0.0"
    }
}

if ($Version.Split('.').Count -eq 3) {
    $Version = "$Version.0"
}

Write-Host "Building LlamaMate MSIX v$Version (Configuration: $Configuration)" -ForegroundColor Cyan

# Step 1: dotnet publish
Write-Host "`nStep 1: Publishing application..." -ForegroundColor Cyan

$publishDir = Join-Path $RepoRoot "src\LlamaMate.App\bin\$Configuration\win-x64\publish"

$null = Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish $AppProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed"
}

Write-Host "  Published to: $publishDir" -ForegroundColor Green

# Step 2: Create the AppX layout directory
Write-Host "`nStep 2: Creating AppX layout..." -ForegroundColor Cyan

$appxLayout = Join-Path $OutputDir "AppX"
$null = Remove-Item -Path $appxLayout -Recurse -Force -ErrorAction SilentlyContinue
$null = New-Item -ItemType Directory -Path $appxLayout -Force

# Copy published files
Copy-Item -Path "$publishDir\*" -Destination $appxLayout -Recurse -Force

# Copy manifest and assets
Copy-Item -Path $ManifestPath -Destination (Join-Path $appxLayout "AppxManifest.xml") -Force

$assetsDir = Join-Path $appxLayout "Assets"
$null = New-Item -ItemType Directory -Path $assetsDir -Force

$icoSource = Join-Path $RepoRoot "src\LlamaMate.App\Assets\llama.ico"
if (Test-Path $icoSource) {
    Copy-Item -Path $icoSource -Destination $assetsDir -Force
}

# Step 3: Create certificate if needed
Write-Host "`nStep 3: Signing certificate..." -ForegroundColor Cyan

$certDir = Join-Path $PSScriptRoot "cert"
$null = New-Item -ItemType Directory -Path $certDir -Force
$certPath = Join-Path $certDir "LlamaMate.pfx"
$cerPath = Join-Path $certDir "LlamaMate.cer"

if (-not (Test-Path $certPath)) {
    Write-Host "  Creating self-signed certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type Custom `
        -Subject "CN=Ito-69" `
        -KeyUsage DigitalSignature `
        -FriendlyName "LlamaMate Development" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

    $securePass = ConvertTo-SecureString -String "LlamaMateDev" -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $securePass
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT

    Write-Host "  Certificate created: $certPath" -ForegroundColor Green
}
else {
    Write-Host "  Using existing certificate: $certPath" -ForegroundColor Yellow
}

# Step 4: Sign the executable with the certificate
Write-Host "`nStep 4: Signing executable..." -ForegroundColor Cyan

$exePath = Join-Path $appxLayout "LlamaMate.exe"
if (Test-Path $exePath) {
    $signtool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if (-not $signtool) {
        # Look in Windows SDK
        $possiblePaths = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x86\signtool.exe"
        )
        $signtoolPath = Get-ChildItem $possiblePaths[0] -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $signtoolPath) {
            $signtoolPath = Get-ChildItem $possiblePaths[1] -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        if ($signtoolPath) {
            $signtool = $signtoolPath.FullName
        }
    }

    if ($signtool) {
        $signArgs = @("sign", "/fd", "SHA256", "/a", "/f", "`"$certPath`"", "/p", "LlamaMateDev", "`"$exePath`"")
        & $signtool $signArgs 2>&1 | Out-Host
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Executable signed" -ForegroundColor Green
        }
        else {
            Write-Host "  [WARN] Signing skipped (signtool exit code: $LASTEXITCODE)" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  [WARN] signtool not found. Skipping executable signing." -ForegroundColor Yellow
    }
}

# Step 5: Create MSIX package
Write-Host "`nStep 5: Creating MSIX package..." -ForegroundColor Cyan

$msixPath = Join-Path $OutputDir "LlamaMate-$Version.msix"
$null = Remove-Item -Path $msixPath -Force -ErrorAction SilentlyContinue

# Try using makeappx.exe
$makeappx = Get-Command "makeappx.exe" -ErrorAction SilentlyContinue
if (-not $makeappx) {
    $possiblePaths = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x86\makeappx.exe"
    )
    $makeappxPath = Get-ChildItem $possiblePaths[0] -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $makeappxPath) {
        $makeappxPath = Get-ChildItem $possiblePaths[1] -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($makeappxPath) {
        $makeappx = $makeappxPath.FullName
    }
}

if ($makeappx) {
    $packArgs = @("pack", "/d", "`"$appxLayout`"", "/p", "`"$msixPath`"", "/l")
    & $makeappx $packArgs 2>&1 | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "makeappx failed with exit code $LASTEXITCODE"
    }
    Write-Host "  MSIX created: $msixPath" -ForegroundColor Green
}
else {
    # Fallback: use .NET to create a ZIP and rename to MSIX
    Write-Host "  makeappx not found. Creating MSIX via ZIP method..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($appxLayout, $msixPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host "  MSIX (ZIP-based) created: $msixPath" -ForegroundColor Green
}

# Step 6: Sign the MSIX
Write-Host "`nStep 6: Signing MSIX..." -ForegroundColor Cyan

if ($signtool) {
    $signArgs = @("sign", "/fd", "SHA256", "/a", "/f", "`"$certPath`"", "/p", "LlamaMateDev", "`"$msixPath`"")
    & $signtool $signArgs 2>&1 | Out-Host
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  MSIX signed" -ForegroundColor Green
    }
    else {
        Write-Host "  [WARN] MSIX signing failed" -ForegroundColor Yellow
    }
}

# Step 7: Compute SHA256
Write-Host "`nStep 7: Computing checksum..." -ForegroundColor Cyan
$hash = Get-FileHash -Path $msixPath -Algorithm SHA256
Write-Host "  SHA256: $($hash.Hash)" -ForegroundColor Green

$hashPath = "$msixPath.sha256"
Set-Content -Path $hashPath -Value $hash.Hash -Encoding ASCII
Write-Host "  Checksum saved: $hashPath" -ForegroundColor Green

Write-Host @"

╔══════════════════════════════════╗
║  MSIX Package Complete!          ║
║                                  ║
║  Package: $msixPath
║  SHA256:  $($hash.Hash)
║                                  ║
╚══════════════════════════════════╝

"@ -ForegroundColor Magenta
