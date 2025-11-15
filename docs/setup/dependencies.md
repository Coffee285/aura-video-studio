# Dependencies Setup Guide

This guide covers installing and configuring all dependencies required by Aura Video Studio.

## Quick Navigation

- [FFmpeg](#ffmpeg) (Required for video rendering)
- [.NET Runtime](#net-runtime) (Required for API)
- [Node.js](#nodejs) (Required for Web UI)
- [Database](#database) (SQLite - included)
- [Optional Dependencies](#optional-dependencies)

---

## FFmpeg

**Required**: Yes (for video rendering)  
**Version**: 4.4 or newer recommended

FFmpeg is the core video processing engine used by Aura for all rendering operations.

### Installation

#### Windows

**Option 1: Automatic Installation** (Recommended)
1. Launch Aura Video Studio
2. Navigate to Settings → System
3. Click "Install FFmpeg"
4. Wait for download and installation
5. Restart Aura

**Option 2: Chocolatey**
```powershell
# Install Chocolatey if not installed
# See: https://chocolatey.org/install

# Install FFmpeg
choco install ffmpeg -y

# Verify
ffmpeg -version
```

**Option 3: Manual Installation**
1. Download FFmpeg from https://ffmpeg.org/download.html
2. Choose "Windows builds from gyan.dev"
3. Download "ffmpeg-release-full.7z"
4. Extract to `C:\ffmpeg`
5. Add to PATH:
   ```powershell
   # Run as Administrator
   setx /M PATH "%PATH%;C:\ffmpeg\bin"
   ```
6. Restart terminal
7. Verify: `ffmpeg -version`

#### macOS

**Option 1: Homebrew** (Recommended)
```bash
# Install Homebrew if not installed
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# Install FFmpeg
brew install ffmpeg

# Verify
ffmpeg -version
```

**Option 2: MacPorts**
```bash
sudo port install ffmpeg

# Verify
ffmpeg -version
```

**Option 3: Download Binary**
1. Visit https://ffmpeg.org/download.html
2. Download macOS binary
3. Extract and move to `/usr/local/bin`
4. Make executable: `chmod +x /usr/local/bin/ffmpeg`

#### Linux

**Ubuntu/Debian**:
```bash
sudo apt update
sudo apt install ffmpeg -y

# Verify
ffmpeg -version
```

**Fedora/RHEL**:
```bash
sudo dnf install ffmpeg -y
```

**Arch Linux**:
```bash
sudo pacman -S ffmpeg
```

**From Source** (for latest features):
```bash
# Install build dependencies
sudo apt install build-essential yasm nasm libx264-dev libx265-dev

# Clone and build
git clone https://github.com/FFmpeg/FFmpeg.git
cd FFmpeg
./configure --enable-gpl --enable-libx264 --enable-libx265
make -j$(nproc)
sudo make install
```

### Verification

```bash
# Check FFmpeg version
ffmpeg -version

# Should show:
# ffmpeg version 4.4 or newer

# Check codecs
ffmpeg -codecs | grep h264  # H.264 support
ffmpeg -codecs | grep aac   # AAC audio support

# Check hardware acceleration (optional)
ffmpeg -encoders | grep nvenc  # NVIDIA
ffmpeg -encoders | grep qsv    # Intel
ffmpeg -encoders | grep amf    # AMD
```

### Configuration

**Specify FFmpeg path** (if not in system PATH):
```json
{
  "FFmpeg": {
    "BinaryPath": "C:\\ffmpeg\\bin\\ffmpeg.exe",  // Windows
    // OR
    "BinaryPath": "/usr/local/bin/ffmpeg"  // Linux/Mac
  }
}
```

**Enable hardware acceleration**:
```json
{
  "FFmpeg": {
    "UseHardwareAcceleration": true,
    "HardwareEncoder": "h264_nvenc"  // NVIDIA
    // "h264_qsv"  // Intel
    // "h264_amf"  // AMD
  }
}
```

### Troubleshooting

See [FFmpeg Errors Troubleshooting Guide](../troubleshooting/ffmpeg-errors.md) for detailed solutions.

Common issues:
- **Not found**: Add to PATH or specify path in config
- **Missing codecs**: Install "full" build, not "essentials"
- **Permission errors**: Ensure executable permissions (Linux/Mac)
- **Hardware encoding fails**: Update GPU drivers or use software encoding

---

## .NET Runtime

**Required**: Yes (for API server)  
**Version**: .NET 8.0 or newer

### Installation

#### Windows

**Option 1: Download Installer**
1. Visit: https://dotnet.microsoft.com/download/dotnet/8.0
2. Download ".NET Desktop Runtime" (includes everything)
3. Run installer
4. Verify:
   ```powershell
   dotnet --version
   # Should show: 8.0.x or newer
   ```

**Option 2: Chocolatey**
```powershell
choco install dotnet-8.0-sdk -y
```

#### macOS

```bash
# Using Homebrew
brew install --cask dotnet-sdk

# Or download from:
# https://dotnet.microsoft.com/download/dotnet/8.0

# Verify
dotnet --version
```

#### Linux

**Ubuntu 22.04+**:
```bash
# Add Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET SDK
sudo apt update
sudo apt install dotnet-sdk-8.0 -y

# Verify
dotnet --version
```

**Other distributions**: See https://learn.microsoft.com/en-us/dotnet/core/install/linux

### Development vs Runtime

**For running Aura** (end users):
- Install **.NET Runtime** (smaller download)

**For developing Aura** (developers):
- Install **.NET SDK** (includes runtime + development tools)

### Verification

```bash
# Check .NET version
dotnet --version

# List installed SDKs
dotnet --list-sdks

# List installed runtimes
dotnet --list-runtimes

# Should see:
# Microsoft.NETCore.App 8.0.x
# Microsoft.AspNetCore.App 8.0.x
```

---

## Node.js

**Required**: Yes (for Web UI)  
**Version**: Node.js 20+ or 20+ (LTS recommended)

### Installation

#### Windows

**Option 1: Official Installer** (Recommended)
1. Visit: https://nodejs.org/
2. Download LTS version (e.g., 20.x)
3. Run installer
4. Check "Automatically install necessary tools"
5. Verify:
   ```powershell
   node --version  # Should show v20.x.x
   npm --version   # Should show 10.x.x
   ```

**Option 2: Chocolatey**
```powershell
choco install nodejs-lts -y
```

#### macOS

```bash
# Using Homebrew (recommended)
brew install node@20

# Or download from: https://nodejs.org/

# Verify
node --version
npm --version
```

#### Linux

**Ubuntu/Debian**:
```bash
# Using NodeSource repository
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install nodejs -y

# Verify
node --version
npm --version
```

**Using nvm** (recommended for developers):
```bash
# Install nvm
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash

# Install Node.js
nvm install 20
nvm use 20

# Verify
node --version
```

### Installing Web UI Dependencies

After installing Node.js:

```bash
# Navigate to Web UI directory
cd Aura.Web

# Install dependencies
npm install

# Build for production
npm run build

# Or run development server
npm run dev
```

### Troubleshooting

**"npm not found"**:
- Restart terminal after installation
- Check PATH includes Node.js bin directory

**"Permission denied" (Linux/Mac)**:
- Don't use `sudo npm install`
- Configure npm to use user directory:
  ```bash
  mkdir ~/.npm-global
  npm config set prefix '~/.npm-global'
  echo 'export PATH=~/.npm-global/bin:$PATH' >> ~/.bashrc
  source ~/.bashrc
  ```

**Slow npm install**:
- Use npm cache: `npm cache clean --force`
- Try yarn instead: `npm install -g yarn && yarn install`

---

## Database

**Included**: SQLite (no installation needed)  
**Version**: SQLite 3.x (bundled with .NET)

Aura uses SQLite for project data, configuration, and history.

### Location

**Windows**: `%APPDATA%\Aura\aura.db`  
**Linux/Mac**: `~/.config/aura/aura.db`

### Configuration

```json
{
  "Database": {
    "ConnectionString": "Data Source=~/.config/aura/aura.db",
    "AutoMigrate": true,
    "BackupEnabled": true,
    "BackupDirectory": "~/.config/aura/backups"
  }
}
```

### Backup and Restore

**Manual Backup**:
```bash
# Windows
copy "%APPDATA%\Aura\aura.db" "%APPDATA%\Aura\backups\aura-backup.db"

# Linux/Mac
cp ~/.config/aura/aura.db ~/.config/aura/backups/aura-backup-$(date +%Y%m%d).db
```

**Automatic Backup** (configured by default):
- Backs up daily
- Keeps last 7 backups
- Stored in `backups/` subdirectory

---

## Optional Dependencies

### GPU Drivers (for Hardware Acceleration)

**NVIDIA (CUDA)**:
```bash
# Check current driver
nvidia-smi

# Download latest from:
# https://www.nvidia.com/download/index.aspx

# Linux: Install CUDA toolkit
sudo apt install nvidia-cuda-toolkit
```

**Intel (QuickSync)**:
- Usually built into CPU
- Ensure latest Intel graphics drivers installed
- Windows: Intel Graphics Command Center
- Linux: Install `intel-media-driver`

**AMD (AMF)**:
```bash
# Linux
sudo apt install mesa-vulkan-drivers
```

### Python (for Future ML Features)

**Not currently required**, but may be needed for future features:

```bash
# Windows
choco install python -y

# Mac
brew install python3

# Linux
sudo apt install python3 python3-pip

# Verify
python3 --version
```

### Git (for Development)

**For developers** who want to build from source:

```bash
# Windows
choco install git -y

# Mac
brew install git

# Linux
sudo apt install git

# Verify
git --version
```

---

## Verification Checklist

After installing all dependencies:

```bash
# Check all dependencies
echo "Checking dependencies..."

# FFmpeg
ffmpeg -version && echo "✓ FFmpeg installed" || echo "✗ FFmpeg missing"

# .NET
dotnet --version && echo "✓ .NET installed" || echo "✗ .NET missing"

# Node.js
node --version && echo "✓ Node.js installed" || echo "✗ Node.js missing"
npm --version && echo "✓ npm installed" || echo "✗ npm missing"

# Optional: GPU
nvidia-smi && echo "✓ NVIDIA GPU detected" || echo "ℹ NVIDIA GPU not detected"

echo "Dependency check complete!"
```

### Via Aura Diagnostics

1. Launch Aura Video Studio
2. Go to Settings → Diagnostics
3. View "System Information" tab
4. Check dependency status:
   - ✅ Green: Installed and working
   - ⚠️ Yellow: Installed but issues detected
   - ❌ Red: Missing or not working

---

## Platform-Specific Notes

### Windows

**Windows 10 or 11 required**

Additional considerations:
- Windows Defender may quarantine FFmpeg on first run (allow it)
- Long path support recommended: Enable via Group Policy or Registry
- Visual C++ Redistributable may be needed (usually auto-installed with .NET)

### macOS

**macOS 11 (Big Sur) or newer recommended**

Additional considerations:
- Gatekeeper may block FFmpeg: Right-click → Open to allow
- M1/M2 Macs: Use native ARM builds when available
- Some features may require Rosetta 2 for x86 dependencies

### Linux

**Ubuntu 22.04 LTS or equivalent recommended**

Additional considerations:
- Ensure you have `build-essential` for some npm packages:
  ```bash
  sudo apt install build-essential
  ```
- May need additional codecs: `sudo apt install ubuntu-restricted-extras`
- Wayland vs X11: Some features work better on X11

---

## Updating Dependencies

### FFmpeg

```bash
# Windows (Chocolatey)
choco upgrade ffmpeg

# Mac (Homebrew)
brew upgrade ffmpeg

# Linux
sudo apt update && sudo apt upgrade ffmpeg
```

### .NET

```bash
# Check for updates
dotnet --list-sdks

# Download latest from:
# https://dotnet.microsoft.com/download
```

### Node.js

```bash
# Using nvm
nvm install 20
nvm use 20

# Using Homebrew (Mac)
brew upgrade node

# Using Chocolatey (Windows)
choco upgrade nodejs
```

---

## Minimal vs Full Installation

### Minimal (Basic Functionality)

Required for basic video generation:
- .NET Runtime 8.0+
- FFmpeg 4.4+
- Node.js 20+ (for web UI)

**Total download size**: ~300-500 MB  
**Disk space**: ~1-2 GB

### Full (All Features)

Includes optional dependencies:
- GPU drivers (CUDA/QuickSync/AMF)
- Python (future ML features)
- Development tools (.NET SDK, Git)

**Total download size**: ~2-5 GB  
**Disk space**: ~10-20 GB

---

## Containerized Deployment

For Docker/container deployments, see:
- Docker Deployment Guide
- Container Configuration

Pre-built images include all dependencies.

---

## Related Documentation

- [Installation Guide](../getting-started/INSTALLATION.md)
- [System Requirements](system-requirements.md)
- [FFmpeg Troubleshooting](../troubleshooting/ffmpeg-errors.md)
- [Build Guide](../../BUILD_GUIDE.md)

## Need Help?

If you encounter issues installing dependencies:
1. Check [Troubleshooting Guide](../troubleshooting/Troubleshooting.md)
2. Verify system meets [requirements](system-requirements.md)
3. Check [GitHub Issues](https://github.com/Coffee285/aura-video-studio/issues)
4. Create new issue with:
   - Operating system and version
   - Dependency causing issues
   - Error messages
   - Steps already tried
