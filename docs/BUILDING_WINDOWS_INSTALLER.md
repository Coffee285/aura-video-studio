# Building Windows Installer for Aura Video Studio

This guide shows developers how to build production-grade Windows installers for Aura Video Studio.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Development Environment Setup](#development-environment-setup)
3. [Building Locally](#building-locally)
4. [Build Scripts](#build-scripts)
5. [GitHub Actions CI/CD](#github-actions-cicd)
6. [Testing](#testing)
7. [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Software

#### 1. Node.js (v20 LTS)
```bash
# Check if installed
node --version  # Should be v20.x.x or higher
npm --version   # Should be 9.x.x or higher
```

**Download**: https://nodejs.org/
- Choose LTS version
- Include npm package manager
- Add to PATH (installer option)

#### 2. .NET SDK 8.0
```bash
# Check if installed
dotnet --version  # Should be 8.0.x
```

**Download**: https://dotnet.microsoft.com/download/dotnet/8.0
- Install SDK (not just runtime)
- Verify installation with `dotnet --version`

#### 3. Git
```bash
# Check if installed
git --version
```

**Download**: https://git-scm.com/
- Use default options
- Git Bash recommended

#### 4. Visual Studio Build Tools (Optional but Recommended)
- Download: https://visualstudio.microsoft.com/downloads/
- Install "Desktop development with C++" workload
- Needed for native module compilation

### System Requirements

- **OS**: Windows 10 1809+ or Windows 11
- **RAM**: 8GB minimum, 16GB recommended
- **Disk**: 20GB free space (for builds and dependencies)
- **Internet**: Required for downloading dependencies

## Development Environment Setup

### 1. Clone Repository

```bash
# Clone from GitHub
git clone https://github.com/coffee285/aura-video-studio.git
cd aura-video-studio
```

### 2. Install Dependencies

```bash
# Install frontend dependencies
cd Aura.Web
npm install

# Install desktop dependencies
cd ../Aura.Desktop
npm install

# Restore .NET dependencies
cd ../Aura.Api
dotnet restore
```

### 3. Verify Setup

```bash
# Check all tools
node --version
npm --version
dotnet --version
git --version

# Should see versions for all
```

## Building Locally

### Quick Build (All Steps)

```powershell
# Navigate to desktop directory
cd Aura.Desktop

# Run master build script
.\scripts\build-windows.ps1
```

This will:
1. ✅ Validate prerequisites
2. ✅ Download FFmpeg (if not present)
3. ✅ Build React frontend
4. ✅ Build .NET backend (self-contained)
5. ✅ Build Electron app and installer
6. ✅ Generate checksums

**Build time**: 15-30 minutes (first build)  
**Output**: `Aura.Desktop/dist/*.exe`

### Step-by-Step Build

If you want to build components individually:

#### 1. Download FFmpeg

```powershell
cd Aura.Desktop
.\scripts\download-ffmpeg-windows.ps1
```

**Output**: `Aura.Desktop/resources/ffmpeg/win-x64/`  
**Size**: ~150 MB  
**One-time**: Cached for future builds

#### 2. Build Frontend

```powershell
cd Aura.Web
npm ci  # Clean install
npm run build
```

**Output**: `Aura.Web/dist/`  
**Time**: 2-5 minutes

#### 3. Build Backend

```powershell
cd Aura.Desktop
.\scripts\build-backend-windows.ps1
```

**Output**: `Aura.Desktop/resources/backend/win-x64/`  
**Size**: ~60-80 MB (self-contained)  
**Time**: 3-8 minutes

#### 4. Build Electron Installer

```powershell
cd Aura.Desktop
npm run build:win
```

**Output**: `Aura.Desktop/dist/`  
**Files**:
- `Aura-Video-Studio-Setup-1.0.0.exe` (NSIS installer)
- `Aura-Video-Studio-1.0.0-x64.exe` (Portable)
- `latest.yml` (Auto-update metadata)

**Time**: 10-15 minutes

## Build Scripts

### Master Build Script

**File**: `Aura.Desktop/scripts/build-windows.ps1`

**Usage**:
```powershell
# Full build
.\scripts\build-windows.ps1

# Clean build (remove all artifacts first)
.\scripts\build-windows.ps1 -Clean

# Skip FFmpeg download (use existing)
.\scripts\build-windows.ps1 -SkipFFmpeg

# Build app but skip installer (faster for testing)
.\scripts\build-windows.ps1 -SkipInstaller

# Skip frontend rebuild (use existing)
.\scripts\build-windows.ps1 -SkipFrontend

# Combine options
.\scripts\build-windows.ps1 -SkipFFmpeg -SkipFrontend
```

**What it does**:
1. Validates Node.js, npm, .NET SDK installed
2. Downloads FFmpeg (if needed)
3. Builds React frontend
4. Builds .NET backend as self-contained
5. Installs Electron dependencies
6. Runs electron-builder to create installer
7. Generates SHA256 checksums
8. Displays build summary

**Exit codes**:
- `0` = Success
- `1` = Failure (check error messages)

### Individual Scripts

#### Download FFmpeg
```powershell
.\scripts\download-ffmpeg-windows.ps1 [-Force] [-Help]
```
- Downloads latest FFmpeg GPL build
- Verifies integrity
- Extracts to resources folder

#### Build Backend
```powershell
.\scripts\build-backend-windows.ps1 [-Clean] [-Help]
```
- Builds ASP.NET Core backend
- Self-contained deployment (includes .NET runtime)
- Single file executable with compression
- Publishes to `resources/backend/win-x64/`

### Build Configuration

**File**: `Aura.Desktop/package.json` → `build` section

Key settings:
- **appId**: `com.coffee285.aura-video-studio`
- **productName**: `Aura Video Studio`
- **compression**: `maximum` (slower build, smaller installer)
- **asar**: `true` (package app files)
- **asarUnpack**: Backend and FFmpeg (must be on disk)

## GitHub Actions CI/CD

### Workflow File

**File**: `.github/workflows/build-windows-installer.yml`

### How It Works

1. **Triggered by**:
   - Push to `main` or `develop` branch
   - Push tags matching `v*.*.*` (e.g., v1.0.0)
   - Manual workflow dispatch
   - Pull requests to `main` or `develop`

2. **Build Steps**:
   - Checkout code
   - Setup Node.js and .NET
   - Cache FFmpeg binaries
   - Build frontend, backend, installer
   - Generate checksums
   - Upload artifacts

3. **On Tag Push**:
   - Create GitHub release
   - Upload installer as release asset
   - Mark as draft for review

### Running Workflow Manually

1. Go to GitHub repository
2. Click "Actions" tab
3. Select "Build Windows Installer"
4. Click "Run workflow"
5. Choose branch
6. Click "Run workflow" button

### Artifacts

**Retention**: 90 days

**Files**:
- Windows installer (NSIS)
- Portable executable
- Checksums.txt

**Download**:
1. Go to Actions run
2. Scroll to "Artifacts" section
3. Click to download

### Secrets Configuration

For code signing in CI/CD:

1. Go to repository Settings
2. Secrets and variables → Actions
3. Add repository secrets:
   - `WIN_CSC_LINK`: Base64-encoded PFX certificate
   - `WIN_CSC_KEY_PASSWORD`: Certificate password

**To encode certificate**:
```powershell
$bytes = [System.IO.File]::ReadAllBytes("path\to\cert.pfx")
$base64 = [System.Convert]::ToBase64String($bytes)
$base64 | Set-Clipboard  # Copy to clipboard
```

## Testing

### Test on Development Machine

```powershell
# Build installer
.\scripts\build-windows.ps1

# Find installer
cd dist
dir  # List files

# Install on development machine
.\Aura-Video-Studio-Setup-1.0.0.exe

# Or test portable
.\Aura-Video-Studio-1.0.0-x64.exe
```

**Verify**:
- ✅ Installer runs without errors
- ✅ App appears in Start Menu
- ✅ App launches successfully
- ✅ Backend starts (check Task Manager)
- ✅ Can create and render a test video
- ✅ Uninstaller works

### Test on Clean Windows VM

**Why**: Ensures installer works on fresh system without dev dependencies

**Setup**:
1. Create Windows 10/11 VM (VirtualBox, VMware, Hyper-V)
2. Install Windows updates
3. DO NOT install Node.js, .NET, or any dev tools
4. Copy installer to VM

**Test**:
1. Run installer
2. Follow installation wizard
3. Launch app from Start Menu
4. Create test project
5. Render test video
6. Check for errors in Event Viewer

**Test Matrix**:
- [ ] Windows 10 21H2
- [ ] Windows 10 22H2
- [ ] Windows 11 22H2
- [ ] Windows 11 23H2
- [ ] With Windows Defender enabled
- [ ] With third-party antivirus (if available)
- [ ] On low-spec VM (4GB RAM)
- [ ] On drive with limited space (< 5GB free)

## Troubleshooting

### "dotnet not found"

**Problem**: Build script can't find .NET SDK

**Solution**:
```powershell
# Verify installation
dotnet --version

# If not found, add to PATH
$env:PATH += ";C:\Program Files\dotnet"

# Or reinstall .NET SDK
```

### "node not found"

**Problem**: Build script can't find Node.js

**Solution**:
```powershell
# Verify installation
node --version

# Add to PATH
$env:PATH += ";C:\Program Files\nodejs"

# Or reinstall Node.js
```

### FFmpeg Download Fails

**Problem**: Cannot download FFmpeg from GitHub

**Solution**:
```powershell
# Manual download
# 1. Go to: https://github.com/BtbN/FFmpeg-Builds/releases
# 2. Download: ffmpeg-master-latest-win64-gpl.zip
# 3. Extract to: Aura.Desktop/resources/ffmpeg/win-x64/bin/
# 4. Verify files: ffmpeg.exe, ffprobe.exe

# Then run build skipping FFmpeg
.\scripts\build-windows.ps1 -SkipFFmpeg
```

### Backend Build Fails

**Problem**: .NET publish command fails

**Solution**:
```powershell
# Clean and rebuild
cd Aura.Api
dotnet clean
dotnet restore
dotnet build -c Release

# If still fails, check:
# 1. .NET SDK version (must be 8.0+)
# 2. Disk space (need at least 2GB free)
# 3. Antivirus (may block compilation)
```

### Electron Builder Fails

**Problem**: `npm run build:win` fails

**Solutions**:

**Out of Memory**:
```powershell
# Increase Node.js memory
$env:NODE_OPTIONS="--max-old-space-size=4096"
npm run build:win
```

**Missing Dependencies**:
```powershell
# Clean install
rm -r node_modules
rm package-lock.json
npm install
```

**Icon Not Found**:
```
# Create placeholder icons (temporary)
# See: Aura.Desktop/assets/icons/ICONS_GUIDE.md
```

### Build Succeeds but Installer Won't Run

**Problem**: Installer created but won't execute

**Checks**:
1. **File size**: Should be 300-400 MB (if much smaller, build failed)
2. **SHA256**: Verify checksum matches
3. **Antivirus**: May be blocking installer
4. **Corruption**: Re-download or rebuild

**Test**:
```powershell
# Verify it's a valid executable
Get-FileHash .\Aura-Video-Studio-Setup-1.0.0.exe
# Should return hash, not error
```

### "Application failed to start"

**Problem**: Installed app won't launch

**Checks**:
1. **Backend missing**: Check `Program Files\Aura Video Studio\resources\backend\`
2. **FFmpeg missing**: Check `Program Files\Aura Video Studio\resources\ffmpeg\`
3. **Permissions**: Try running as administrator
4. **Logs**: Check `%APPDATA%\AuraVideoStudio\logs\`

### Slow Build Times

**Problem**: Build takes > 30 minutes

**Optimizations**:
```powershell
# Use incremental builds
.\scripts\build-windows.ps1 -SkipFFmpeg -SkipFrontend

# Disable antivirus real-time scanning for build directory
# (temporarily, while building)

# Use SSD for build directory
# Build on faster machine

# Reduce compression (faster but larger installer)
# Edit package.json: "compression": "normal"
```

## Build Optimization Tips

### Faster Rebuilds

```powershell
# Only rebuild changed components
.\scripts\build-windows.ps1 -SkipFFmpeg  # FFmpeg rarely changes
.\scripts\build-windows.ps1 -SkipFrontend  # If only backend changed
.\scripts\build-windows.ps1 -SkipBackend  # If only frontend changed
```

### Parallel Builds

If you have a powerful machine:
```powershell
# Build frontend and backend simultaneously
# Terminal 1:
cd Aura.Web
npm run build

# Terminal 2:
cd Aura.Desktop
.\scripts\build-backend-windows.ps1

# Terminal 3:
cd Aura.Desktop
.\scripts\download-ffmpeg-windows.ps1

# Then build installer:
cd Aura.Desktop
npm run build:win
```

### Caching

- **FFmpeg**: Downloaded once, cached locally
- **Node modules**: Use `npm ci` instead of `npm install`
- **NuGet packages**: Cached in `~/.nuget/packages`
- **Electron Builder**: Caches in `~/AppData/Local/electron-builder`

## Advanced Topics

### Custom Build Arguments

```powershell
# Pass custom version
$env:AURA_VERSION="1.2.3"
npm run build:win

# Custom output directory
# Edit package.json: "directories": { "output": "releases" }

# Build for specific architecture only
npm run build:win -- --x64
# or
npm run build:win -- --ia32
```

### Build Configuration

Edit `Aura.Desktop/package.json` → `build`:

```json
{
  "build": {
    "compression": "maximum",  // or "normal" for faster builds
    "asar": true,               // Package app files
    "win": {
      "target": ["nsis", "portable"]  // Add "zip" for archive
    }
  }
}
```

### Debug Mode

```powershell
# Build with debug symbols
cd Aura.Desktop
$env:DEBUG="electron-builder"
npm run build:win
```

## CI/CD Best Practices

1. **Branch Protection**: Require CI to pass before merge
2. **Semantic Versioning**: Use tags like `v1.0.0`, `v1.0.1`
3. **Draft Releases**: Review before publishing
4. **Changelog**: Auto-generate from commits
5. **Testing**: Automated tests before build
6. **Notifications**: Slack/Discord webhooks for build status

## Resources

- **Electron Builder Docs**: https://www.electron.build/
- **NSIS Installer**: https://nsis.sourceforge.io/
- **.NET Publishing**: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- **FFmpeg**: https://ffmpeg.org/

## Getting Help

**Build Issues**:
1. Check this troubleshooting section
2. Review build logs in `Aura.Desktop/dist/*.log`
3. Search GitHub issues
4. Ask in GitHub Discussions

**Report Bugs**:
https://github.com/coffee285/aura-video-studio/issues

---

**Last Updated**: 2025-11-10  
**Version**: 1.0.0  
**For**: Developers building Windows installers
