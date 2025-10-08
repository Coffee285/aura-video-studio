# Build Portable Distribution for Aura Video Studio
# This script builds the portable ZIP distribution only

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Building Aura Video Studio Portable Distribution ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Platform: $Platform"
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check for npm
$npmPath = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npmPath) {
    Write-Host ""
    Write-Host "ERROR: npm is not installed or not in PATH" -ForegroundColor Red
    Write-Host ""
    Write-Host "Node.js and npm are required to build the web UI (Aura.Web)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To fix this:" -ForegroundColor Cyan
    Write-Host "  1. Download and install Node.js from: https://nodejs.org/" -ForegroundColor White
    Write-Host "  2. Recommended version: Node.js 20.x or later (LTS)" -ForegroundColor White
    Write-Host "  3. After installation, restart your PowerShell session" -ForegroundColor White
    Write-Host "  4. Verify installation by running: npm --version" -ForegroundColor White
    Write-Host ""
    Write-Host "Alternatively, install via chocolatey:" -ForegroundColor Cyan
    Write-Host "  choco install nodejs-lts" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host "✓ npm found: $($npmPath.Source)" -ForegroundColor Green
Write-Host ""

# Set paths
$rootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactsDir = Join-Path $rootDir "artifacts\windows"
$portableDir = Join-Path $artifactsDir "portable"

# Create directories
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null

# Step 1: Build Core Projects
Write-Host "Step 1: Building core projects..." -ForegroundColor Yellow
dotnet build "$rootDir\Aura.Core\Aura.Core.csproj" -c $Configuration
dotnet build "$rootDir\Aura.Providers\Aura.Providers.csproj" -c $Configuration
dotnet build "$rootDir\Aura.Api\Aura.Api.csproj" -c $Configuration

# Step 2: Build Web UI
Write-Host "Step 2: Building web UI..." -ForegroundColor Yellow
Push-Location "$rootDir\Aura.Web"
if (Test-Path "node_modules") {
    npm run build
} else {
    npm install
    npm run build
}
Pop-Location

# Step 3: Build Portable Distribution
Write-Host "Step 3: Building portable distribution..." -ForegroundColor Yellow
$portableBuildDir = Join-Path $portableDir "build"
New-Item -ItemType Directory -Force -Path $portableBuildDir | Out-Null

# Publish API as self-contained
dotnet publish "$rootDir\Aura.Api\Aura.Api.csproj" `
    -c $Configuration `
    -r win-$($Platform.ToLower()) `
    --self-contained `
    -o "$portableBuildDir\Api"

# Copy Web UI to wwwroot folder inside the published API
$wwwrootDir = Join-Path "$portableBuildDir\Api" "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwrootDir | Out-Null
Copy-Item "$rootDir\Aura.Web\dist\*" -Destination $wwwrootDir -Recurse -Force

# Copy FFmpeg
Copy-Item "$rootDir\scripts\ffmpeg\*.exe" -Destination "$portableBuildDir\ffmpeg" -Force -ErrorAction SilentlyContinue

# Copy config and docs
Copy-Item "$rootDir\appsettings.json" -Destination $portableBuildDir -Force
Copy-Item "$rootDir\PORTABLE.md" -Destination "$portableBuildDir\README.md" -Force
Copy-Item "$rootDir\LICENSE" -Destination $portableBuildDir -Force -ErrorAction SilentlyContinue

# Create launcher script
$launcherScript = @"
@echo off
echo Starting Aura Video Studio...
start "" /D "Api" "Aura.Api.exe"
timeout /t 3 /nobreak >nul
start "" "http://127.0.0.1:5005"
"@
Set-Content -Path "$portableBuildDir\Launch.bat" -Value $launcherScript

# Create ZIP
$zipPath = Join-Path $portableDir "AuraVideoStudio_Portable_x64.zip"
Compress-Archive -Path "$portableBuildDir\*" -DestinationPath $zipPath -Force
Write-Host "✓ Portable ZIP created: $zipPath" -ForegroundColor Green

# Step 4: Generate Checksums
Write-Host "Step 4: Generating checksums..." -ForegroundColor Yellow
$checksumFile = Join-Path $artifactsDir "checksums.txt"
Get-ChildItem $portableDir -Recurse -Include *.zip | ForEach-Object {
    $hash = Get-FileHash -Path $_.FullName -Algorithm SHA256
    "$($hash.Hash)  $($_.Name)"
} | Out-File $checksumFile -Encoding utf8
Write-Host "✓ Checksums generated: $checksumFile" -ForegroundColor Green

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Artifacts location: $artifactsDir"
Write-Host ""
Get-ChildItem $portableDir -Recurse -Include *.zip | ForEach-Object {
    Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor White
}
