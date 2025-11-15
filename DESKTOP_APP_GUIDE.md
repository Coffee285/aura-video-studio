# Aura Video Studio Desktop Application - Developer Guide

This guide covers building, developing, and maintaining the Aura Video Studio desktop application built with Electron.

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Development Setup](#development-setup)
- [Building the Desktop App](#building-the-desktop-app)
- [Platform-Specific Builds](#platform-specific-builds)
- [Code Signing](#code-signing)
- [Testing](#testing)
- [Distribution](#distribution)
- [Troubleshooting](#troubleshooting)

---

## Architecture Overview

The Aura Video Studio desktop app uses **Electron** to wrap the React frontend and ASP.NET Core backend into a native desktop application.

### Components

```
Aura.Desktop/
├── electron/
│   ├── main.js            # Main process entry point
│   ├── preload.js         # Preload script (sandboxed bridge)
│   ├── window-manager.js  # Window lifecycle management
│   ├── backend-service.js # Backend process management
│   ├── menu-builder.js    # Application menu
│   ├── tray-manager.js    # System tray integration
│   ├── protocol-handler.js # Custom protocol (aura://)
│   └── ipc-handlers/      # IPC handlers (config, system, video, etc.)
├── package.json           # Electron dependencies and build config
├── assets/
│   ├── splash.html        # Splash screen
│   └── icons/             # Platform-specific app icons
├── build/                 # Build configuration
│   ├── installer.nsh      # Windows NSIS installer script
│   ├── entitlements.mac.plist  # macOS permissions
│   └── dmg-background.png      # macOS DMG background
└── resources/             # Bundled resources (generated during build)
    ├── backend/           # Published .NET backend
    ├── frontend/          # Built React app
    └── ffmpeg/            # FFmpeg binaries (optional)
```

### Process Model

1. **Main Process** (`electron/main.js`)
   - Spawns the ASP.NET Core backend on a random port
   - Creates the main window via WindowManager
   - Manages system tray via TrayManager
   - Handles auto-updates
   - Manages IPC communication via IPC handlers
   - Orchestrates all Electron modules

2. **Renderer Process** (React frontend)
   - Runs in a sandboxed web page (Electron window)
   - Communicates with main process via IPC (through `preload.js`)
   - Connects to the embedded backend via HTTP
   - Loaded from bundled frontend files

3. **Backend Process** (ASP.NET Core)
   - Spawned as a child process by main process
   - Runs on `http://localhost:<random-port>`
   - Provides REST API and Server-Sent Events (SSE)
   - Fully embedded in the Electron app

### Security Model

- **Context Isolation:** Enabled (renderer can't access Node.js directly)
- **Node Integration:** Disabled (renderer is sandboxed)
- **Preload Script:** Exposes safe IPC API via `contextBridge`
- **Web Security:** Enabled (prevents loading arbitrary remote content)

---

## Development Setup

### Prerequisites

- Node.js 20+ and npm 9+
- .NET 8.0 SDK
- Git

### Install Dependencies

```bash
# Clone the repository
git clone https://github.com/coffee285/aura-video-studio.git
cd aura-video-studio

# Install frontend dependencies
cd Aura.Web
npm install
npm run build

# Install desktop dependencies
cd ../Aura.Desktop
npm install

# Build the backend (for development)
cd ../Aura.Api
dotnet build
```

### Running in Development Mode

```bash
# Terminal 1: Start the backend
cd Aura.Api
dotnet run

# Terminal 2: Start Electron (in dev mode)
cd Aura.Desktop
npm start
```

**Development Mode Features:**
- Opens DevTools automatically
- Uses backend from `Aura.Api/bin/Debug/net8.0`
- Uses frontend from `Aura.Web/dist`
- Hot reload (restart required for Electron changes)

### Project Structure Integration

The desktop app integrates with the existing codebase:

- **Frontend:** `Aura.Web/dist` → Bundled into `resources/frontend`
- **Backend:** `Aura.Api` → Published and bundled into `resources/backend`
- **Configuration:** Stored in OS-specific locations via `electron-store`

---

## Building the Desktop App

### Quick Build (All Platforms)

```bash
cd Aura.Desktop
./build-desktop.sh
# or on Windows:
.\build-desktop.ps1
```

This will:
1. Build the React frontend
2. Publish the .NET backend for all platforms
3. Build Electron installers for all platforms

### Build Output

```
Aura.Desktop/dist/
├── win-unpacked/                    # Windows unpacked app
├── Aura-Video-Studio-Setup-1.0.0.exe   # Windows installer
├── Aura-Video-Studio-1.0.0-portable.exe # Windows portable
├── mac/                              # macOS unpacked app
├── Aura-Video-Studio-1.0.0.dmg       # macOS disk image
├── Aura-Video-Studio-1.0.0-universal.dmg  # macOS universal
├── linux-unpacked/                   # Linux unpacked app
├── Aura-Video-Studio-1.0.0.AppImage  # Linux AppImage
├── aura-video-studio_1.0.0_amd64.deb # Debian package
├── aura-video-studio-1.0.0.x86_64.rpm # RPM package
└── aura-video-studio_1.0.0_amd64.snap # Snap package
```

### Build Steps Explained

#### Step 1: Frontend Build

```bash
cd Aura.Web
npm run build
```

**Output:** `Aura.Web/dist/` - Production-optimized React bundle

#### Step 2: Backend Build

```bash
cd Aura.Api
dotnet publish -c Release -r <runtime-id> --self-contained -o ../Aura.Desktop/backend/<platform>
```

**Runtimes:**
- Windows: `win-x64`
- macOS Intel: `osx-x64`
- macOS Apple Silicon: `osx-arm64`
- Linux: `linux-x64`

**Output:** Self-contained .NET application with all dependencies

#### Step 3: Electron Build

```bash
cd Aura.Desktop
npm run build:<platform>
```

**Platforms:** `win`, `mac`, `linux`, or `all`

Electron Builder:
1. Packages the Electron app
2. Bundles frontend and backend
3. Applies platform-specific configurations
4. Creates installer/package

---

## Platform-Specific Builds

### Windows

#### Requirements

- Windows 10+ (for building)
- Optional: Authenticode certificate for code signing

#### Build Configuration

```json
{
  "win": {
    "target": ["nsis", "portable"],
    "icon": "assets/icons/icon.ico"
  },
  "nsis": {
    "oneClick": false,
    "allowToChangeInstallationDirectory": true,
    "createDesktopShortcut": true,
    "createStartMenuShortcut": true,
    "perMachine": false
  }
}
```

#### Building

```bash
npm run build:win
```

**Output:**
- `Aura-Video-Studio-Setup-1.0.0.exe` - NSIS installer
- `Aura-Video-Studio-1.0.0-portable.exe` - Portable executable

#### File Associations

The Windows installer registers `.aura` file associations:
- Double-clicking `.aura` files opens them in Aura Video Studio
- Right-click → Open with → Aura Video Studio

### macOS

#### Requirements

- macOS 10.15+ (for building)
- Xcode Command Line Tools: `xcode-select --install`
- Optional: Apple Developer certificate for code signing

#### Build Configuration

```json
{
  "mac": {
    "target": ["dmg", "zip"],
    "category": "public.app-category.video",
    "icon": "assets/icons/icon.icns",
    "hardenedRuntime": true,
    "entitlements": "build/entitlements.mac.plist"
  }
}
```

#### Building

```bash
npm run build:mac
```

**Output:**
- `Aura-Video-Studio-1.0.0.dmg` - Disk image (current arch)
- `Aura-Video-Studio-1.0.0-universal.dmg` - Universal binary (Intel + Apple Silicon)

#### Universal Builds

To build a universal binary (works on both Intel and Apple Silicon):

1. Build backend for both architectures:
   ```bash
   dotnet publish -r osx-x64 --self-contained
   dotnet publish -r osx-arm64 --self-contained
   ```

2. electron-builder automatically creates a universal app when both are present

### Linux

#### Requirements

- Linux (any distro)
- Optional: `fuse` for AppImage testing

#### Build Configuration

```json
{
  "linux": {
    "target": ["AppImage", "deb", "rpm", "snap"],
    "category": "Video",
    "icon": "assets/icons/"
  }
}
```

#### Building

```bash
npm run build:linux
```

**Output:**
- `Aura-Video-Studio-1.0.0.AppImage` - Universal, no installation required
- `aura-video-studio_1.0.0_amd64.deb` - Debian/Ubuntu package
- `aura-video-studio-1.0.0.x86_64.rpm` - Fedora/RHEL package
- `aura-video-studio_1.0.0_amd64.snap` - Snap package

#### AppImage

AppImage is the recommended format for Linux:
- No installation required
- Works on most distros
- Self-contained

**Testing AppImage:**
```bash
chmod +x dist/Aura-Video-Studio-1.0.0.AppImage
./dist/Aura-Video-Studio-1.0.0.AppImage
```

---

## Code Signing

Code signing is **highly recommended** for production releases to avoid security warnings.

### Windows Code Signing

#### Requirements

- Authenticode certificate (`.pfx` or `.p12`)
- Certificate password

#### Setup

1. **Obtain Certificate**
   - Purchase from a Certificate Authority (DigiCert, Sectigo, etc.)
   - Or use a self-signed certificate (for testing only)

2. **Configure**
   ```json
   {
     "win": {
       "certificateFile": "certs/win-cert.pfx",
       "certificatePassword": "${env.WIN_CERT_PASSWORD}"
     }
   }
   ```

3. **Build**
   ```bash
   export WIN_CERT_PASSWORD="your-password"
   npm run build:win
   ```

**Result:** Signed executable bypasses Windows SmartScreen

### macOS Code Signing

#### Requirements

- Apple Developer account ($99/year)
- Developer ID Application certificate
- Xcode with signing configured

#### Setup

1. **Install Certificate**
   - Download certificate from Apple Developer portal
   - Import into Keychain Access

2. **Configure**
   ```json
   {
     "mac": {
       "identity": "Developer ID Application: Your Name (TEAMID)",
       "hardenedRuntime": true,
       "entitlements": "build/entitlements.mac.plist",
       "notarize": true
     }
   }
   ```

3. **Build and Notarize**
   ```bash
   export APPLE_ID="your@email.com"
   export APPLE_APP_SPECIFIC_PASSWORD="xxxx-xxxx-xxxx-xxxx"
   export APPLE_TEAM_ID="YOUR_TEAM_ID"
   npm run build:mac
   ```

**Result:** Notarized app bypasses macOS Gatekeeper

**Notarization Process:**
1. Build creates the app
2. App is uploaded to Apple's notarization service
3. Apple scans for malware
4. Notarization ticket is stapled to the app
5. App can now run without warnings

### Linux Code Signing

Linux packages don't require code signing for most use cases. For enterprise deployment:

- **DEB/RPM:** Sign with GPG
- **Snap:** Sign with Snap Store key
- **Flatpak:** Sign with GPG key

---

## Testing

### Manual Testing

1. **Build the app:**
   ```bash
   npm run build:dir
   ```

2. **Run the unpacked app:**
   - Windows: `dist/win-unpacked/Aura Video Studio.exe`
   - macOS: `dist/mac/Aura Video Studio.app`
   - Linux: `dist/linux-unpacked/aura-video-studio`

### Automated Testing

```bash
# Unit tests (Electron main process)
npm test

# E2E tests (with Spectron)
npm run test:e2e
```

### Testing Checklist

- [ ] App launches successfully
- [ ] Backend starts and becomes healthy
- [ ] Frontend loads and connects to backend
- [ ] First-run wizard appears on first launch
- [ ] FFmpeg detection works
- [ ] Ollama detection works
- [ ] Video can be created and rendered
- [ ] System tray icon appears and functions
- [ ] Auto-update check works (staging server)
- [ ] App closes gracefully (backend terminates)
- [ ] App can be uninstalled cleanly

---

## Distribution

### GitHub Releases

1. **Tag a release:**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. **Build all platforms:**
   ```bash
   npm run build:all
   ```

3. **Create GitHub Release:**
   - Go to Releases → New Release
   - Choose tag `v1.0.0`
   - Upload all artifacts from `dist/`
   - Write release notes

4. **Auto-update configuration:**
   - electron-updater automatically checks GitHub releases
   - Users get update notifications

### Platform Stores

#### Microsoft Store (Windows)

1. Create a Microsoft Partner Center account
2. Use electron-builder's `appx` target
3. Submit for review

#### Mac App Store

1. Build with `mas` target
2. Create App Store Connect listing
3. Submit for review

**Note:** Mac App Store has restrictions (no child processes), so direct DMG distribution is recommended.

#### Snap Store (Linux)

1. Create a Snapcraft developer account
2. Build snap: `npm run build:linux`
3. Upload: `snapcraft upload dist/*.snap`

#### Flathub (Linux)

1. Create Flathub submission repo
2. Submit pull request to Flathub
3. Wait for review

---

## Troubleshooting

### Build Issues

#### "Backend not found"

**Solution:** Build the backend first:
```bash
cd Aura.Api
dotnet publish -c Release -r win-x64 --self-contained -o ../Aura.Desktop/backend/win-x64
```

#### "Frontend not found"

**Solution:** Build the frontend first:
```bash
cd Aura.Web
npm run build
```

#### "electron-builder fails with ENOENT"

**Solution:** Clear build cache:
```bash
rm -rf dist/
rm -rf node_modules/
npm install
```

### Runtime Issues

#### "Backend failed to start"

**Debugging:**
1. Check backend logs in user data directory
2. Verify backend executable has execute permissions
3. Check port conflicts
4. Run backend manually: `./backend/Aura.Api.exe`

#### "Frontend won't load"

**Debugging:**
1. Open DevTools: View → Toggle Developer Tools
2. Check console for errors
3. Verify backend URL is correct
4. Check network tab for failed requests

#### "App crashes on startup"

**Debugging:**
1. Check crash logs in user data directory
2. Run from terminal to see error output:
   - Windows: `"C:\Program Files\Aura Video Studio\Aura Video Studio.exe"`
   - macOS: `/Applications/Aura\ Video\ Studio.app/Contents/MacOS/Aura\ Video\ Studio`
   - Linux: `./aura-video-studio`

### Auto-Update Issues

#### "Update check fails"

**Debugging:**
1. Check network connectivity
2. Verify release exists on GitHub
3. Check `package.json` → `repository` URL is correct
4. Enable debug logging:
   ```javascript
   autoUpdater.logger = console;
   ```

---

## Advanced Topics

### Custom Protocols

Custom URL protocols are registered via the `protocol-handler.js` module:

```javascript
// electron/protocol-handler.js handles:
// aura://open?path=/path/to/project
// aura://create?template=basic
// aura://generate?script=...
// aura://settings
// aura://help
// aura://about

// Protocol is registered in electron/main.js before app.ready
protocol.registerSchemesAsPrivileged([...]);
```

### Deep Linking

File associations are handled via electron-builder configuration in `package.json`:

```javascript
// Configured in package.json build.win.fileAssociations
// Handles .aura and .avsproj files
// When user opens these files, Electron app receives the file path
```

See `electron/protocol-handler.js` for the complete implementation.

### Performance Monitoring

Integrate with analytics:

```javascript
// electron.js
const { powerMonitor } = require('electron');

powerMonitor.on('suspend', () => {
  console.log('System going to sleep');
});

powerMonitor.on('resume', () => {
  console.log('System waking up');
});
```

### Custom Menus

Create native menus:

```javascript
const { Menu } = require('electron');

const template = [
  {
    label: 'File',
    submenu: [
      { label: 'New Project', accelerator: 'CmdOrCtrl+N', click: createNewProject },
      { label: 'Open Project', accelerator: 'CmdOrCtrl+O', click: openProject },
      { type: 'separator' },
      { role: 'quit' }
    ]
  },
  // ...
];

const menu = Menu.buildFromTemplate(template);
Menu.setApplicationMenu(menu);
```

---

## Contributing

When working on the desktop app:

1. **Follow the existing patterns**
   - Use IPC for renderer ↔ main communication
   - Keep security best practices
   - Test on all platforms

2. **Update documentation**
   - Document new IPC handlers
   - Update build scripts if needed

3. **Test thoroughly**
   - Test on Windows, macOS, and Linux
   - Test installers, not just unpacked builds

4. **Submit PRs**
   - Include screenshots/videos of new features
   - Describe testing performed

---

## Resources

- [Electron Documentation](https://www.electronjs.org/docs)
- [electron-builder Documentation](https://www.electron.build/)
- [Electron Security](https://www.electronjs.org/docs/latest/tutorial/security)
- [Auto-Update Best Practices](https://www.electron.build/auto-update)

---

**Questions?** Open an issue or ask in [Discord](https://discord.gg/aura-video-studio)!
