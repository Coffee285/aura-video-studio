# Electron Installer Build Instructions

## Important Note

**Currently, only Windows builds are supported.** macOS and Linux builds have been disabled to focus on the target platform (Windows 10/11).

## Quick Start

### Windows
```powershell
cd Aura.Desktop
.\build-desktop.ps1
```

### Linux/macOS (Limited Support)
The build-desktop.sh script is available but only supports Windows target:
```bash
cd Aura.Desktop
./build-desktop.sh --target win
```

## Build Options

### Skip Installer Creation (faster, for testing)
```bash
.\build-desktop.ps1 -SkipInstaller
npm run build:dir
```

### Build Windows Platform
```bash
npm run build:win    # Windows only (x64)
npm run build        # Same as build:win
```

## Manual Build Process

### Step 1: Build Frontend
```bash
cd ../Aura.Web
npm install
npm run build
```

### Step 2: Build Backend (requires .NET 8.0 SDK)
```bash
cd ../Aura.Api
dotnet publish -c Release -r win-x64 --self-contained -o ../Aura.Desktop/resources/backend/win-x64
```

### Step 3: Package with Electron
```bash
cd ../Aura.Desktop
npm install
npm run build:win
```

## Output

Installers will be created in `Aura.Desktop/dist/`:
- **Windows**: `*-Setup-*.exe` (NSIS installer), `*-portable.exe` (standalone)

## Code Signing

Code signing is optional. If you have a code signing certificate:

1. Set environment variables:
   ```powershell
   $env:WIN_CSC_LINK = "path\to\certificate.pfx"
   $env:WIN_CSC_KEY_PASSWORD = "your-password"
   ```

2. Or use base64-encoded certificate:
   ```powershell
   $env:WIN_CSC_LINK = "base64-encoded-certificate-content"
   $env:WIN_CSC_KEY_PASSWORD = "your-password"
   ```

If no certificate is provided, the build will succeed but the installer will be unsigned (users will see security warnings).

## Troubleshooting

### "electron-builder cannot execute"
- Ensure Node.js 20+ and .NET 8.0 SDK are installed
- Try building with `-SkipInstaller` flag first to test packaging

### "Backend not found"
- Run the backend build steps above
- Ensure resources/backend/win-x64 directory exists with Aura.Api.exe

### Type errors during build
- Frontend is configured to build without strict type-checking
- To fix type errors, run: `cd Aura.Web && npm run build:prod:strict`

### Code signing errors
- Code signing is optional; build will proceed without it
- Install Windows SDK if you want to enable signing
- Ensure WIN_CSC_LINK environment variable points to valid certificate

## Verification

### Quick Verification
To verify the build works:
```bash
cd Aura.Desktop
npm run build:dir
ls -la dist/win-unpacked/
```

Should show the packaged Electron app.

### Comprehensive Build Validation
To validate the build configuration:
```powershell
cd Aura.Desktop
node scripts/validate-build-config.js
```

This validates:
- Windows-only build configuration
- Correct target architectures (x64)
- Proper file associations
- Build scripts are correctly configured

### Installation Testing

#### Automated Installation Validation
After installing the application on a test system, run:
```powershell
cd Aura.Desktop
.\scripts\validate-installation.ps1
```

This checks:
- All required files are installed
- Registry entries are correct
- File associations are registered
- Shortcuts are created
- .NET 8 Runtime is available
- Windows Firewall rules are configured

#### End-to-End Installation Test
For comprehensive testing on a clean Windows 11 VM:
```powershell
cd Aura.Desktop
.\scripts\test-installation-e2e.ps1 -InstallerPath "C:\Path\To\Installer.exe"
```

Add `-Silent` flag for automated testing:
```powershell
.\scripts\test-installation-e2e.ps1 -InstallerPath "C:\Path\To\Installer.exe" -Silent
```

#### Uninstallation Validation
After uninstalling the application, verify cleanup:
```powershell
cd Aura.Desktop
.\scripts\validate-uninstallation.ps1
```

This verifies:
- Installation directory removed
- Registry entries cleaned up
- File associations removed
- Shortcuts deleted
- Firewall rules removed
- Temporary files cleaned

## Installation Features

### NSIS Installer Capabilities

The Windows installer includes:

**Installation Features:**
- Custom installation directory selection
- Desktop shortcut creation (always)
- Start Menu shortcut creation
- File association registration (.aura, .avsproj)
- Windows Firewall rule creation
- .NET 8 Runtime detection with download prompt
- Visual C++ Redistributable check
- Windows Defender exclusion (optional)
- Per-machine installation (requires admin)

**First-Run Setup:**
- Automatic data directory creation
- Configuration file initialization
- System compatibility check
- Backend service verification

**Uninstallation Features:**
- Complete removal of installed files
- Registry cleanup (uninstall keys, file associations)
- Shortcut removal (desktop, start menu)
- Firewall rule removal
- Optional user data preservation
- Temporary file cleanup

### Bundled Dependencies

The installer includes:

**Backend Components:**
- .NET 8 self-contained deployment (all required DLLs)
- Aura.Api.exe backend server
- Aura.Core, Aura.Providers assemblies

**Frontend Components:**
- React application (compiled and bundled)
- Electron runtime
- Node.js native modules

**Media Processing:**
- FFmpeg binaries (full GPL build with all codecs)
- Hardware acceleration support (NVENC, AMF, QuickSync)

**Resources:**
- Application icons and assets
- Installer graphics (header, sidebar)
- License file

### System Requirements

**Minimum:**
- Windows 10 version 1809 or later
- 4GB RAM
- 2GB free disk space
- .NET 8 Runtime (installer can download)

**Recommended:**
- Windows 11 (22H2 or later)
- 8GB+ RAM
- 10GB free disk space
- Dedicated GPU for hardware acceleration

### Testing Checklist

Before releasing an installer:

- [ ] Build completes without errors
- [ ] Installer file size is reasonable (200-400MB)
- [ ] Code signing works (if certificate available)
- [ ] Fresh install on clean Windows 11 VM succeeds
- [ ] Application launches from Start Menu
- [ ] File associations work (double-click .aura files)
- [ ] Backend starts correctly
- [ ] Frontend loads in Electron window
- [ ] First-run wizard appears
- [ ] Core features work (script generation, TTS, video rendering)
- [ ] Uninstallation removes all files
- [ ] No leftover registry entries after uninstall
- [ ] User data preservation works as expected

See `WINDOWS_11_TESTING_GUIDE.md` for comprehensive testing procedures.
