# Windows 11 Installation & Testing Guide

This comprehensive guide covers testing the Aura Video Studio Windows installer and first-run experience on a clean Windows 11 system.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Test Environment Setup](#test-environment-setup)
- [Building the Windows Installer](#building-the-windows-installer)
- [Installation Testing](#installation-testing)
- [First-Run Experience Testing](#first-run-experience-testing)
- [Runtime Testing](#runtime-testing)
- [Update Testing](#update-testing)
- [Uninstallation Testing](#uninstallation-testing)
- [Known Issues & Troubleshooting](#known-issues--troubleshooting)
- [Test Checklist](#test-checklist)

---

## Prerequisites

### Development Machine Requirements

- Windows 10/11 with build tools
- Node.js 20+ and npm 9+
- .NET 8.0 SDK
- Git with long paths enabled
- Visual Studio Build Tools (for native modules)

### Test Machine Requirements (Clean Windows 11 System)

- **OS**: Windows 11 (22H2 or later recommended)
- **RAM**: 8GB minimum, 16GB recommended
- **Disk Space**: 10GB free for application and test data
- **Internet**: Required for dependency downloads
- **User Account**: Administrator privileges for installation

### Optional: Virtual Machine Setup

For comprehensive testing, use a clean Windows 11 VM:

```powershell
# Hyper-V (Windows 11 Pro/Enterprise)
New-VM -Name "AuraTest-Win11" -MemoryStartupBytes 8GB -Generation 2

# VirtualBox
VBoxManage createvm --name "AuraTest-Win11" --ostype Windows11_64 --register
VBoxManage modifyvm "AuraTest-Win11" --memory 8192 --cpus 2
```

**Recommended VM Tools:**
- Hyper-V (Built into Windows 11 Pro/Enterprise)
- VMware Workstation Player
- VirtualBox
- Parallels Desktop (for Mac development)

---

## Test Environment Setup

### 1. Prepare Clean Windows 11 System

**Option A: Physical Machine**
1. Fresh Windows 11 installation
2. Complete Windows Update
3. Disable Windows Defender temporarily (for clean testing)
4. Create restore point before testing

**Option B: Virtual Machine**
1. Create new Windows 11 VM
2. Take snapshot before installation ("Clean State")
3. Enable Enhanced Session Mode (Hyper-V)
4. Configure shared folders for installer transfer

### 2. System Configuration Verification

Run these commands in PowerShell (as Administrator):

```powershell
# Check Windows version
winver

# Verify Windows 11 build
Get-ComputerInfo | Select-Object WindowsVersion, OsBuildNumber, OSArchitecture

# Check if .NET 8 is installed (should be absent for clean testing)
dotnet --list-runtimes

# Check PowerShell version (should be 5.1+ or 7+)
$PSVersionTable.PSVersion

# Check execution policy
Get-ExecutionPolicy
```

**Expected Clean State:**
- Windows 11 22H2+ (Build 22621+)
- No .NET 8 Runtime installed
- PowerShell 5.1 or later
- Execution Policy: RemoteSigned or Restricted

### 3. Prepare Test Data Locations

Create test folders to monitor installation:

```powershell
# Monitor these paths during installation
$testPaths = @(
    "$env:ProgramFiles\Aura Video Studio",
    "$env:LOCALAPPDATA\aura-video-studio",
    "$env:APPDATA\aura-video-studio",
    "$env:USERPROFILE\Documents\Aura Video Studio",
    "$env:USERPROFILE\Videos\Aura Studio",
    "$env:TEMP\aura-video-studio"
)

# Check paths before installation (should not exist)
foreach ($path in $testPaths) {
    Write-Host "Checking: $path"
    Test-Path $path
}
```

---

## Building the Windows Installer

### 1. Build on Development Machine

```bash
# 1. Build Frontend
cd Aura.Web
npm install
npm run build

# 2. Build Backend
cd ../Aura.Api
dotnet publish -c Release -r win-x64 --self-contained -o ../Aura.Desktop/resources/backend/win-x64

# 3. Build Electron Installer
cd ../Aura.Desktop
npm install
npm run build:win

# 4. Verify output
ls dist/
```

**Expected Output:**
```
dist/
├── Aura-Video-Studio-Setup-1.0.0.exe    # NSIS installer (~200-400MB)
├── Aura-Video-Studio-1.0.0-portable.exe # Portable version
└── win-unpacked/                         # Unpacked application
```

### 2. Transfer Installer to Test Machine

**Via Network Share:**
```powershell
# On test machine
Copy-Item "\\DevMachine\Share\Aura-Video-Studio-Setup-1.0.0.exe" -Destination "C:\Temp\"
```

**Via USB/Cloud:**
- Copy installer to USB drive
- Or upload to cloud storage (OneDrive, Google Drive)

### 3. Verify Installer Integrity

```powershell
# Check file size (should be 200-400MB)
Get-Item "C:\Temp\Aura-Video-Studio-Setup-1.0.0.exe" | Select-Object Name, Length

# Compute SHA256 hash for integrity verification
Get-FileHash "C:\Temp\Aura-Video-Studio-Setup-1.0.0.exe" -Algorithm SHA256
```

---

## Installation Testing

### Phase 1: Pre-Installation Checks

#### 1.1. Run Installer as Administrator

```powershell
# Right-click installer → Run as Administrator
Start-Process "C:\Temp\Aura-Video-Studio-Setup-1.0.0.exe" -Verb RunAs
```

**Expected:**
- UAC prompt appears
- User grants administrator permission
- Installer window opens

#### 1.2. License Agreement Screen

**Test Points:**
- [ ] License text is readable
- [ ] "I accept" checkbox is functional
- [ ] "Next" button enables after accepting
- [ ] "Cancel" button exits installer

#### 1.3. Installation Directory Selection

**Test Points:**
- [ ] Default path: `C:\Program Files\Aura Video Studio`
- [ ] "Browse" button works
- [ ] Path validation (no invalid characters)
- [ ] Disk space check (requires ~500MB)
- [ ] Warning if path already exists

**Custom Path Testing:**
```powershell
# Try custom installation path
"D:\CustomApps\Aura\"
```

#### 1.4. .NET 8 Runtime Check

**Expected Behavior (Clean System):**
```
✓ Installer detects .NET 8 is NOT installed
✓ Dialog appears: ".NET 8 Runtime Required"
✓ Options: "Download .NET 8" / "Skip (Not Recommended)" / "Cancel"
```

**Test Scenarios:**

**A. Download .NET 8 (Recommended Path)**
1. Click "Download .NET 8"
2. Browser opens: https://dotnet.microsoft.com/download/dotnet/8.0
3. Download .NET 8 Runtime (or ASP.NET Core 8 Runtime)
4. Install .NET Runtime
5. Restart Aura installer
6. Installer should now detect .NET 8

**B. Skip .NET 8 (Warning Path)**
1. Click "Skip (Not Recommended)"
2. Installer continues
3. Warning message should appear
4. Application will fail to start without .NET

**Verification:**
```powershell
# After .NET installation
dotnet --list-runtimes

# Expected output includes:
# Microsoft.NETCore.App 8.0.x [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]
```

#### 1.5. Visual C++ Redistributable Check

**Expected Behavior:**
- Installer checks for VC++ Redistributable
- If missing, prompts to download
- Not critical for .NET 8 apps but recommended

#### 1.6. Installation Progress

**Monitor Installation Steps:**
```
[✓] Checking for .NET 8 Runtime...
[✓] Configuring Windows registry...
[✓] Registering file associations...
[✓] Configuring Windows Firewall...
[✓] Adding Windows Defender exclusion...
[✓] Creating user data directories...
[✓] Copying application files...
[✓] Creating shortcuts...
[✓] Refreshing shell...
[✓] Installation completed successfully!
```

**Test Points:**
- [ ] Progress bar updates smoothly
- [ ] Each step completes without errors
- [ ] Detailed log available in installer window
- [ ] Estimated time remaining is accurate

### Phase 2: Post-Installation Verification

#### 2.1. File System Verification

```powershell
# Verify installation directory
Get-ChildItem "C:\Program Files\Aura Video Studio" -Recurse | Measure-Object

# Check key files exist
$requiredFiles = @(
    "Aura Video Studio.exe",
    "resources/app.asar",
    "resources/backend/win-x64/Aura.Api.exe",
    "resources/elevate.exe"
)

foreach ($file in $requiredFiles) {
    $path = "C:\Program Files\Aura Video Studio\$file"
    Write-Host "Checking: $path - $(Test-Path $path)"
}
```

**Expected Structure:**
```
C:\Program Files\Aura Video Studio\
├── Aura Video Studio.exe        # Main executable
├── resources/
│   ├── app.asar                  # Electron app bundle
│   ├── backend/
│   │   └── win-x64/
│   │       ├── Aura.Api.exe      # Backend executable
│   │       └── [.NET dependencies]
│   └── frontend/                 # React frontend
├── locales/                      # Electron locales
└── [Electron runtime files]
```

#### 2.2. AppData Directory Verification

```powershell
# Check user data directories
$appDataPath = "$env:LOCALAPPDATA\aura-video-studio"
Write-Host "AppData: $appDataPath - $(Test-Path $appDataPath)"

Get-ChildItem $appDataPath
```

**Expected Structure:**
```
%LOCALAPPDATA%\aura-video-studio\
├── logs/                         # Application logs
├── cache/                        # Cached data
├── aura-config.json             # Main configuration
└── aura-secure.json             # Encrypted settings
```

#### 2.3. Registry Verification

```powershell
# Check uninstall registry
$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\f6c6e9f0-8b1a-4e5a-9c2d-1e8f4a6b7c8d"
Get-ItemProperty $uninstallKey

# Check file associations
Get-ItemProperty "HKLM:\Software\Classes\.aura"
Get-ItemProperty "HKLM:\Software\Classes\.avsproj"
```

**Expected Registry Keys:**
- DisplayName: "Aura Video Studio"
- DisplayVersion: "1.0.0"
- Publisher: "Coffee285"
- InstallLocation: "C:\Program Files\Aura Video Studio"
- UninstallString: "[...]\Uninstall.exe"

#### 2.4. Shortcut Verification

```powershell
# Desktop shortcut
Test-Path "$env:USERPROFILE\Desktop\Aura Video Studio.lnk"

# Start Menu shortcut
Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Aura Video Studio.lnk"
```

**Test Shortcuts:**
- [ ] Desktop shortcut exists
- [ ] Start Menu shortcut exists
- [ ] Shortcut icon displays correctly
- [ ] Double-click launches application

#### 2.5. Windows Firewall Rule

```powershell
# Check firewall rule
Get-NetFirewallRule -DisplayName "Aura Video Studio" | Format-List
```

**Expected:**
- Rule exists: "Aura Video Studio"
- Direction: Inbound
- Action: Allow
- Enabled: Yes
- Profile: Any

---

## First-Run Experience Testing

### Phase 1: Application Launch

#### 1.1. Launch Application

```powershell
# Launch from Start Menu or Desktop shortcut
# Or manually:
Start-Process "C:\Program Files\Aura Video Studio\Aura Video Studio.exe"
```

**Expected Startup Sequence:**
```
[1] Splash screen appears
[2] Windows Setup Wizard runs (first run only)
[3] Backend starts
[4] Main window loads
[5] First-run setup wizard appears in UI
```

#### 1.2. Windows Setup Wizard (Background)

**Automated Checks Performed:**

**Step 1: .NET 8 Runtime Check**
```
✓ Checking .NET 8 Runtime...
✓ .NET 8 Runtime detected - OK
```

If .NET 8 is missing:
- Dialog appears: ".NET 8 Runtime Required"
- Options: "Download .NET 8" / "Skip" / "Cancel"
- Browser opens to download page

**Step 2: Data Paths Configuration**
```
✓ Configuring data paths...
✓ Created userData directory: %LOCALAPPDATA%\aura-video-studio
✓ Created documents directory: %USERPROFILE%\Documents\Aura Video Studio
✓ Created videos directory: %USERPROFILE%\Videos\Aura Studio
✓ Created logs directory: %LOCALAPPDATA%\aura-video-studio\logs
✓ Created cache directory: %LOCALAPPDATA%\aura-video-studio\cache
```

**Step 3: Windows Firewall Check**
```
✓ Checking Windows Firewall...
✓ Windows Firewall rule exists
```

If firewall rule is missing:
- Dialog appears: "Windows Firewall Configuration"
- Instructions provided
- Command copied to clipboard

**Step 4: Shortcuts Verification**
```
✓ Verifying shortcuts...
✓ Desktop shortcut exists
✓ Start menu shortcut exists
```

**Step 5: Windows Version Compatibility**
```
✓ Checking Windows compatibility...
✓ Detected: Windows 11
✓ Compatible: Yes
✓ Recommendation: Fully compatible
```

**Console Output:**
```
============================================================
Windows First-Run Setup Wizard
============================================================
Step 1: Checking .NET 8 Runtime...
✓ .NET 8 Runtime found: Microsoft.NETCore.App 8.0.8
Step 2: Configuring data paths...
  User Data: C:\Users\TestUser\AppData\Local\aura-video-studio
  Documents: C:\Users\TestUser\Documents\Aura Video Studio
  Videos: C:\Users\TestUser\Videos\Aura Studio
  ...
✓ All directories created
Step 3: Checking Windows Firewall...
✓ Windows Firewall rule exists
Step 4: Verifying shortcuts...
✓ Desktop shortcut exists: True
✓ Start menu shortcut exists: True
Step 5: Checking Windows compatibility...
✓ Detected: Windows 11
✓ Compatible: True
============================================================
Windows Setup Wizard Complete
============================================================
```

#### 1.3. UI First-Run Setup Wizard

After Windows setup completes, the UI wizard appears:

**Welcome Screen**
- [ ] Welcome message displays
- [ ] "Get Started" button works
- [ ] Can skip wizard (not recommended)

**Prerequisites Screen**
- [ ] Lists requirements (.NET, disk space, etc.)
- [ ] All checks pass (green checkmarks)
- [ ] If issues detected, shows warnings/errors

**Provider Configuration**
- [ ] Can add API keys for OpenAI, Anthropic, etc.
- [ ] Keys are validated before saving
- [ ] Can skip and configure later

**FFmpeg Configuration**
- [ ] Detects FFmpeg if installed
- [ ] Offers to download if missing
- [ ] Shows installation instructions

**Completion Screen**
- [ ] Summary of configuration
- [ ] "Start Using Aura" button
- [ ] Sets setupComplete flag

### Phase 2: Backend Startup Verification

#### 2.1. Backend Process

```powershell
# Check backend process is running
Get-Process | Where-Object { $_.ProcessName -like "*Aura*" }

# Expected processes:
# - Aura Video Studio.exe (main Electron process)
# - Aura.Api.exe (backend process)
```

#### 2.2. Backend Health Check

```powershell
# Check backend is responding
Invoke-WebRequest -Uri "http://localhost:<port>/health" -UseBasicParsing

# Should return 200 OK with health status
```

#### 2.3. Backend Logs

```powershell
# View backend logs
Get-Content "$env:LOCALAPPDATA\aura-video-studio\logs\backend-*.log" -Tail 50
```

**Expected Log Entries:**
```
[INFO] Aura.Api starting...
[INFO] ASP.NET Core 8.0.x
[INFO] Listening on http://localhost:<port>
[INFO] Application started successfully
```

---

## Runtime Testing

### Phase 1: Core Functionality

#### 1.1. Main Window

**Test Points:**
- [ ] Main window loads completely
- [ ] Menu bar is functional
- [ ] Toolbar buttons work
- [ ] Status bar shows backend status
- [ ] Window can be resized
- [ ] Window can be minimized/maximized
- [ ] Window can be closed (minimizes to tray)

#### 1.2. System Tray

**Test Points:**
- [ ] Tray icon appears
- [ ] Right-click shows context menu
- [ ] "Show" restores window
- [ ] "Quit" exits application
- [ ] Tray tooltip shows app name

#### 1.3. Backend Communication

**Test Points:**
- [ ] Frontend connects to backend
- [ ] API requests succeed
- [ ] WebSocket connection (if used)
- [ ] SSE events received
- [ ] Error handling works

### Phase 2: Feature Testing

#### 2.1. Create New Project

**Steps:**
1. Click "New Project"
2. Enter project name
3. Select project location
4. Click "Create"

**Verify:**
- [ ] Project created successfully
- [ ] Project appears in recent projects
- [ ] Project files saved to Documents

#### 2.2. Video Generation

**Steps:**
1. Enter video script or topic
2. Configure settings (duration, style, etc.)
3. Click "Generate"
4. Wait for generation

**Verify:**
- [ ] Script generation works (requires LLM provider)
- [ ] TTS generation works (requires TTS provider)
- [ ] Image generation works (requires image provider)
- [ ] Video rendering works (requires FFmpeg)
- [ ] Progress updates in UI
- [ ] Can cancel generation
- [ ] Generated video is playable

#### 2.3. Settings Configuration

**Test Points:**
- [ ] Can open settings dialog
- [ ] Can add/edit API keys
- [ ] Keys are encrypted
- [ ] Can test API connections
- [ ] Settings persist after restart
- [ ] Can reset to defaults

### Phase 3: Performance & Stability

#### 3.1. Resource Usage

```powershell
# Monitor CPU and memory usage
Get-Process "Aura Video Studio" | Select-Object Name, CPU, WorkingSet
Get-Process "Aura.Api" | Select-Object Name, CPU, WorkingSet
```

**Expected (Idle):**
- CPU: < 5%
- Memory: < 500MB (combined)

**Expected (Active Generation):**
- CPU: 20-80% (depending on task)
- Memory: < 2GB (combined)

#### 3.2. Long-Running Test

**Test Procedure:**
1. Launch application
2. Generate 5-10 videos
3. Leave running for 1+ hours
4. Monitor for memory leaks
5. Check logs for errors

**Verify:**
- [ ] No crashes
- [ ] No memory leaks
- [ ] No backend restarts
- [ ] Responsive throughout

#### 3.3. Stress Testing

**Test Scenarios:**
- Rapid project creation/deletion
- Concurrent video generations (if supported)
- Large video projects (10+ scenes)
- Network interruption handling
- Disk space exhaustion

---

## Update Testing

### Phase 1: Auto-Update Configuration

#### 1.1. Verify Update Settings

```powershell
# Check configuration
Get-Content "$env:LOCALAPPDATA\aura-video-studio\aura-config.json"
```

**Expected:**
- `autoUpdate: true` (default)
- `checkForUpdatesOnStartup: true`

#### 1.2. Simulate Update Check

**Method A: Using Dev Tools**
1. Open DevTools (Ctrl+Shift+I)
2. Console: `require('electron').remote.app.emit('check-for-updates')`

**Method B: Using Menu**
1. Help → Check for Updates
2. Should check GitHub releases

**Expected Behavior:**
```
✓ Checking for updates...
✓ Current version: 1.0.0
✓ Latest version: 1.0.1
✓ Update available!
```

### Phase 2: Update Installation

#### 2.1. Download Update

**Test Points:**
- [ ] Update notification appears
- [ ] "Download" button works
- [ ] Download progress shown
- [ ] Download can be canceled
- [ ] Downloaded update is cached

#### 2.2. Install Update

**Test Points:**
- [ ] "Restart and Update" prompt appears
- [ ] Can defer installation
- [ ] Can restart and install immediately
- [ ] Application closes gracefully
- [ ] Update installs
- [ ] Application restarts
- [ ] New version is running

#### 2.3. Update Verification

```powershell
# Check version after update
$exePath = "C:\Program Files\Aura Video Studio\Aura Video Studio.exe"
(Get-Item $exePath).VersionInfo.FileVersion
```

**Verify:**
- [ ] Version number updated
- [ ] All data/settings preserved
- [ ] No file corruption
- [ ] Application functions normally

---

## Uninstallation Testing

### Phase 1: Pre-Uninstall State

#### 1.1. Document Current State

```powershell
# Take snapshot of installation
$installPath = "C:\Program Files\Aura Video Studio"
$appDataPath = "$env:LOCALAPPDATA\aura-video-studio"
$documentsPath = "$env:USERPROFILE\Documents\Aura Video Studio"

# Count files
Get-ChildItem $installPath -Recurse | Measure-Object
Get-ChildItem $appDataPath -Recurse | Measure-Object
Get-ChildItem $documentsPath -Recurse | Measure-Object
```

#### 1.2. Close Application

**Important:** Close all instances of Aura Video Studio before uninstalling

```powershell
# Close gracefully via tray menu: Right-click → Quit

# Or force close if needed:
Stop-Process -Name "Aura Video Studio" -Force
Stop-Process -Name "Aura.Api" -Force
```

### Phase 2: Uninstallation Process

#### 2.1. Run Uninstaller

**Method A: Control Panel**
1. Settings → Apps → Installed apps
2. Find "Aura Video Studio"
3. Click "Uninstall"
4. Confirm

**Method B: Direct Uninstaller**
```powershell
Start-Process "C:\Program Files\Aura Video Studio\Uninstall.exe"
```

#### 2.2. Uninstaller Dialogs

**Expected Sequence:**

**Dialog 1: Stop Running Instances**
```
Stopping any running instances...
✓ All instances stopped
```

**Dialog 2: Confirm Uninstallation**
```
Are you sure you want to uninstall Aura Video Studio?
[Yes] [No]
```

**Dialog 3: Remove User Data (Optional)**
```
Would you like to remove all user data, settings, and cached files?

This will delete:
- Application settings
- Cached files
- Logs

Location: C:\Users\...\AppData\Local\aura-video-studio

(Your video projects in Documents will NOT be deleted)

[Yes, remove all data] [No, keep my data]
```

**Test Points:**
- [ ] Both options work correctly
- [ ] "Yes" removes AppData completely
- [ ] "No" preserves AppData
- [ ] Documents folder is NEVER deleted

#### 2.3. Uninstallation Progress

**Monitor Uninstallation Steps:**
```
[✓] Stopping any running instances...
[✓] Removing file associations...
[✓] Removing Windows Firewall rule...
[✓] Removing registry entries...
[✓] Removing Windows Defender exclusion...
[✓] Removing user data (if selected)...
[✓] Cleaning temporary files...
[✓] Removing shortcuts...
[✓] Removing application files...
[✓] Refreshing shell...
[✓] Uninstallation cleanup completed!
```

### Phase 3: Post-Uninstall Verification

#### 3.1. File System Verification

```powershell
# Verify files are removed
$paths = @(
    "C:\Program Files\Aura Video Studio",
    "$env:USERPROFILE\Desktop\Aura Video Studio.lnk",
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Aura Video Studio.lnk"
)

foreach ($path in $paths) {
    Write-Host "$path exists: $(Test-Path $path)"
}
```

**Expected (Complete Removal):**
- [ ] Installation directory removed
- [ ] Desktop shortcut removed
- [ ] Start Menu shortcut removed
- [ ] AppData removed (if user selected)
- [ ] Temp files removed

**Expected (Keep User Data):**
- [ ] AppData preserved if user selected
- [ ] Documents folder preserved (always)

#### 3.2. Registry Verification

```powershell
# Verify registry cleaned
$uninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\f6c6e9f0-8b1a-4e5a-9c2d-1e8f4a6b7c8d"
Test-Path $uninstallKey

# Check file associations removed
Test-Path "HKLM:\Software\Classes\.aura"
Test-Path "HKLM:\Software\Classes\.avsproj"
```

**Expected:**
- [ ] Uninstall registry key removed
- [ ] File association registry keys removed
- [ ] No orphaned registry entries

#### 3.3. Firewall Rule Verification

```powershell
# Verify firewall rule removed
Get-NetFirewallRule -DisplayName "Aura Video Studio" -ErrorAction SilentlyContinue
```

**Expected:**
- [ ] Firewall rule removed

#### 3.4. Windows Defender Exclusion

```powershell
# Check if exclusion removed
Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
```

**Expected:**
- [ ] No Aura-related paths in exclusions

---

## Known Issues & Troubleshooting

### Common Issues

#### Issue 1: .NET 8 Runtime Missing

**Symptoms:**
- Application fails to start
- Error: "The application requires .NET Runtime 8.0"

**Solution:**
```powershell
# Install .NET 8 Runtime
winget install Microsoft.DotNet.Runtime.8

# Or download from:
# https://dotnet.microsoft.com/download/dotnet/8.0

# Restart application
```

#### Issue 2: Windows Firewall Blocking Backend

**Symptoms:**
- Application starts but shows "Backend Unavailable"
- Cannot connect to localhost

**Solution:**
```powershell
# Add firewall rule manually
$exePath = "C:\Program Files\Aura Video Studio\Aura Video Studio.exe"
netsh advfirewall firewall add rule name="Aura Video Studio" dir=in action=allow program="$exePath" enable=yes profile=any

# Restart application
```

#### Issue 3: Backend Process Doesn't Start

**Symptoms:**
- Frontend loads but shows connection error
- Backend process not in Task Manager

**Diagnosis:**
```powershell
# Check backend executable
$backendPath = "C:\Program Files\Aura Video Studio\resources\backend\win-x64\Aura.Api.exe"
Test-Path $backendPath

# Try running backend manually
& $backendPath

# Check logs
Get-Content "$env:LOCALAPPDATA\aura-video-studio\logs\*.log" -Tail 100
```

**Solutions:**
- Verify .NET 8 Runtime installed
- Check antivirus isn't blocking backend
- Verify backend executable is not corrupted
- Check port isn't already in use

#### Issue 4: SmartScreen Blocks Installation

**Symptoms:**
- "Windows protected your PC" warning
- Installer won't run

**Solution:**
```
1. Click "More info"
2. Click "Run anyway"
```

**For Developers:**
- Sign installer with code signing certificate
- Build reputation with Microsoft

#### Issue 5: Application Won't Close

**Symptoms:**
- Clicking X doesn't close app
- App minimizes to tray instead

**Solution:**
```
This is expected behavior!
- Right-click tray icon → Quit to close
- Or disable "Minimize to Tray" in settings
```

#### Issue 6: Updates Fail to Download

**Symptoms:**
- Update check fails
- "Network error" message

**Diagnosis:**
```powershell
# Check internet connectivity
Test-NetConnection github.com -Port 443

# Check GitHub API
Invoke-WebRequest -Uri "https://api.github.com/repos/coffee285/aura-video-studio/releases/latest"
```

**Solutions:**
- Check internet connection
- Verify GitHub is accessible
- Check corporate firewall/proxy settings
- Try manual download from GitHub releases

### Diagnostic Commands

```powershell
# System Information
Get-ComputerInfo | Select-Object WindowsVersion, OsBuildNumber, OSArchitecture

# Installed .NET Runtimes
dotnet --list-runtimes

# Application Processes
Get-Process | Where-Object { $_.ProcessName -like "*Aura*" } | Format-Table

# Application Logs
Get-Content "$env:LOCALAPPDATA\aura-video-studio\logs\*.log" -Tail 100

# Firewall Rules
Get-NetFirewallRule -DisplayName "Aura*" | Format-List

# Registry Keys
Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*" | Where-Object { $_.DisplayName -like "*Aura*" }

# File Associations
Get-ItemProperty "HKLM:\Software\Classes\.aura"
```

---

## Test Checklist

### Pre-Installation Testing

- [ ] Clean Windows 11 system prepared
- [ ] Snapshot/restore point created
- [ ] System meets minimum requirements
- [ ] Installer integrity verified (hash check)

### Installation Testing

- [ ] Installer launches with admin privileges
- [ ] License agreement screen works
- [ ] Installation directory selection works
- [ ] .NET 8 runtime check works
- [ ] .NET download link opens
- [ ] Installation completes without errors
- [ ] All files copied correctly
- [ ] Registry keys created
- [ ] File associations registered
- [ ] Firewall rule added
- [ ] Shortcuts created
- [ ] AppData directories created

### First-Run Testing

- [ ] Application launches successfully
- [ ] Windows setup wizard runs
- [ ] .NET runtime detected
- [ ] Data paths configured
- [ ] Firewall rule verified
- [ ] Shortcuts verified
- [ ] Windows compatibility checked
- [ ] UI setup wizard appears
- [ ] Backend starts successfully
- [ ] Health check passes
- [ ] Frontend connects to backend

### Functional Testing

- [ ] Main window loads
- [ ] System tray icon works
- [ ] Menu bar functional
- [ ] Can create new project
- [ ] Can configure settings
- [ ] Can add API keys
- [ ] Can generate video (with providers configured)
- [ ] FFmpeg integration works
- [ ] Settings persist
- [ ] Application responsive

### Update Testing

- [ ] Update check works
- [ ] Update notification appears
- [ ] Download works
- [ ] Installation works
- [ ] Settings preserved after update
- [ ] Application works after update

### Uninstallation Testing

- [ ] Application closes gracefully
- [ ] Uninstaller launches
- [ ] All processes stopped
- [ ] User data prompt appears
- [ ] Files removed correctly
- [ ] Registry cleaned
- [ ] Shortcuts removed
- [ ] Firewall rule removed
- [ ] No orphaned files/registry keys
- [ ] User data handling correct (keep/remove option)

### Performance Testing

- [ ] Application starts in < 30 seconds
- [ ] Backend ready in < 60 seconds
- [ ] Memory usage acceptable (< 500MB idle)
- [ ] CPU usage acceptable (< 5% idle)
- [ ] No memory leaks over time
- [ ] Handles multiple video generations
- [ ] Recovers from errors gracefully

### Edge Case Testing

- [ ] Installation on drive with limited space
- [ ] Installation with non-English characters in path
- [ ] Multiple installation/uninstallation cycles
- [ ] Upgrade from previous version
- [ ] Installation with antivirus active
- [ ] Installation without internet (after dependencies)
- [ ] Running from non-admin account (after install)
- [ ] Multiple user accounts on same machine

### Documentation

- [ ] Installation logs captured
- [ ] Screenshots taken
- [ ] Issues documented
- [ ] Performance metrics recorded
- [ ] Test results summarized

---

## Test Report Template

```markdown
# Aura Video Studio - Windows 11 Installation Test Report

**Test Date:** YYYY-MM-DD
**Tester:** [Name]
**Test Environment:** 
- OS: Windows 11 [Build Number]
- VM/Physical: [VM Name / Physical Machine]
- .NET Pre-installed: Yes/No

## Installation Test Results

### Pre-Installation
- Installer Integrity: ✓/✗
- System Requirements: ✓/✗

### Installation Process
- .NET Check: ✓/✗
- Installation Success: ✓/✗
- Registry Configuration: ✓/✗
- Shortcuts Created: ✓/✗
- Duration: [X] minutes

### First Run
- Windows Setup Wizard: ✓/✗
- Backend Startup: ✓/✗
- Frontend Load: ✓/✗
- UI Setup Wizard: ✓/✗

### Functional Testing
- Project Creation: ✓/✗
- Settings Configuration: ✓/✗
- Video Generation: ✓/✗

### Uninstallation
- Clean Removal: ✓/✗
- User Data Handling: ✓/✗

## Issues Found

1. [Issue Description]
   - Severity: Critical/High/Medium/Low
   - Steps to Reproduce: ...
   - Expected: ...
   - Actual: ...

## Performance Metrics

- Installation Time: X minutes
- First Launch Time: X seconds
- Memory Usage (Idle): X MB
- Memory Usage (Active): X MB

## Recommendations

- [Recommendation 1]
- [Recommendation 2]

## Overall Assessment

- Installation: ✓ Pass / ✗ Fail
- First Run: ✓ Pass / ✗ Fail
- Functionality: ✓ Pass / ✗ Fail
- Uninstallation: ✓ Pass / ✗ Fail

**Final Grade:** Pass / Fail (with notes)
```

---

## Additional Resources

### Documentation

- [Desktop App Guide](../DESKTOP_APP_GUIDE.md)
- [First Run Guide](../FIRST_RUN_GUIDE.md)
- [Build Instructions](BUILD_INSTRUCTIONS.md)

### External Resources

- [Windows 11 Download (Official)](https://www.microsoft.com/software-download/windows11)
- [.NET 8 Download](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Electron Builder Documentation](https://www.electron.build/)
- [NSIS Documentation](https://nsis.sourceforge.io/Docs/)

### Support

- GitHub Issues: https://github.com/coffee285/aura-video-studio/issues
- Discord: [Link if available]
- Email: support@aura-video-studio.com

---

**Last Updated:** 2025-11-11
**Version:** 1.0.0
**Maintainer:** Aura Development Team
