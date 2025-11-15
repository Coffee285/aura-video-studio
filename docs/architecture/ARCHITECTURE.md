# Aura Video Studio - Architecture Overview

## Introduction

Aura Video Studio is an **Electron desktop application** for creating AI-powered videos. The application bundles a React frontend and ASP.NET Core backend into a native cross-platform desktop app.

**Note:** For information about the architectural migration from web-based to Electron, see [ARCHITECTURE_MIGRATION_NOTE.md](./ARCHITECTURE_MIGRATION_NOTE.md).

## High-Level Architecture

```
┌──────────────────────────────────────────────────────────┐
│              Electron Main Process                       │
│         (Node.js, Window Mgmt, IPC, Lifecycle)          │
└────┬──────────────────────────────┬────────────────────┘
     │ spawns child process         │ IPC communication
     ▼                              ▼
┌────────────────┐         ┌─────────────────────────────┐
│   ASP.NET      │ HTTP    │    Electron Renderer        │
│   Backend      │◄────────┤      (React UI)             │
│   (Aura.Api)   │────────►│      Sandboxed Browser      │
└────────────────┘         └─────────────────────────────┘
     │
     ▼
┌────────────────────────────────────────────────────────┐
│  Aura.Core (Domain Logic)                              │
│  Aura.Providers (LLM, TTS, Images, Video)              │
│  FFmpeg (Video Rendering)                              │
└────────────────────────────────────────────────────────┘
```

**Distribution:**
- Windows: NSIS installer + portable executable
- macOS: DMG installer (future)
- Linux: AppImage (future)

## Architecture Components

### 1. Aura.Desktop (Electron Application)
**Technology**: Electron 32.2.5 + Node.js  
**Purpose**: Desktop application shell, process management, native OS integration

**Key Components**:
- `electron/main.js` - Main process entry point, orchestration
- `electron/window-manager.js` - Window lifecycle and state persistence
- `electron/backend-service.js` - Backend process spawning and management
- `electron/menu-builder.js` - Application menu system
- `electron/tray-manager.js` - System tray integration
- `electron/protocol-handler.js` - Custom protocol (aura://) support
- `electron/ipc-handlers/` - IPC handlers (config, system, video, diagnostics)
- `electron/preload.js` - Security bridge for renderer ↔ main IPC

**Features**:
- Single instance lock (prevents multiple instances)
- Auto-updater (electron-updater)
- Native dialogs (file pickers, save dialogs)
- System tray with quick actions
- Custom protocol handling (aura:// URLs)
- Window state persistence
- Crash recovery and logging

**Security**:
- Context isolation enabled
- Node integration disabled (renderer sandboxed)
- Web security enabled
- IPC channel whitelisting
- Input validation and sanitization
- Content Security Policy (CSP)

**See:** Aura.Desktop/electron/README.md for detailed architecture

### 2. Aura.Core (Business Logic)
**Technology**: .NET 8 Class Library  
**Purpose**: Platform-agnostic business logic, models, and orchestration

**Key Components**:
- `Models/` - Data models (Brief, PlanSpec, Scene, etc.)
- `Hardware/` - Hardware detection and capability tiering
- `Orchestrator/` - Video generation pipeline orchestration
- `Rendering/` - FFmpeg plan building
- `VideoOptimization/` - Frame analysis, transitions, optimization
- `Dependencies/` - Dependency manager with SHA-256 verification

**Hardware Tiers:**
- Tier S: High-end (32GB+ RAM, RTX 3080+)
- Tier A: Upper mid (16GB+ RAM, RTX 3060+)
- Tier B: Mid-range (16GB RAM, GTX 1660+)
- Tier C: Lower mid (8GB RAM, integrated GPU)
- Tier D: Minimum (8GB RAM, CPU only)

**Platforms**: Cross-platform (.NET 8)

### 3. Aura.Providers (Provider Implementations)
**Technology**: .NET 8 Class Library  
**Purpose**: Concrete implementations of provider interfaces

**LLM Providers:**
- `OpenAiLlmProvider` - GPT-4/GPT-3.5 via OpenAI API
- `AnthropicProvider` - Claude models
- `GoogleGeminiProvider` - Google Gemini
- `OllamaProvider` - Local models via Ollama
- `RuleBasedLlmProvider` - Template-based (offline fallback)

**TTS Providers:**
- `ElevenLabsProvider` - Premium realistic voices
- `PlayHtProvider` - Premium with voice cloning
- `WindowsSapiProvider` - Windows native TTS
- `PiperTtsProvider` - Free offline neural TTS
- `Mimic3Provider` - Free offline TTS

**Image Providers:**
- Stable Diffusion WebUI (local GPU)
- Stock image APIs (Pexels, Pixabay, Unsplash)
- Replicate (cloud-based models)

**Video Rendering:**
- FFmpeg 4.0+ with hardware acceleration (NVENC, AMF, QuickSync)
- Multi-pass encoding support

**Platforms**: Cross-platform (.NET 8)

### 4. Aura.Api (Backend API)
**Technology**: ASP.NET Core 8 (Minimal API + Controllers)  
**Purpose**: RESTful API backend embedded in Electron app

**Key Endpoints**:
- `GET /health/live`, `/health/ready` - Health checks
- `GET /api/jobs`, `POST /api/jobs` - Job management
- `GET /api/jobs/{id}/events` - Server-Sent Events (SSE) for progress
- `POST /api/quick/demo` - Quick demo generation
- `GET /api/settings`, `POST /api/settings` - Settings persistence
- `GET /api/system/capabilities` - Hardware detection
- `GET /api/providers/status` - Provider health checks

**Configuration:**
- Runs on random available port in Electron (spawned by main process)
- Uses Serilog for structured logging
- Integrates with Aura.Core and Aura.Providers
- SQLite database for persistence
- Server-Sent Events (SSE) for real-time progress

**Platforms**: Cross-platform (ASP.NET Core 8)

### 5. Aura.Web (Frontend UI)
**Technology**: React 18.2 + TypeScript + Vite 6.4 + Fluent UI 9.47  
**Purpose**: Modern user interface bundled into Electron

**Key Features**:
- Fluent UI React components for modern UI
- TypeScript strict mode for type safety
- Vite for fast development builds
- Zustand for state management
- React Router for navigation
- Axios with circuit breaker for API calls

**Views:**
- Dashboard (quick actions, recent projects)
- Create Wizard (Brief → Plan → Voice → Generate)
- Job Queue (monitoring, cancellation)
- Settings (providers, hardware, preferences)
- Advanced Mode (ML Lab, deep customization)

**Development:**
- `npm run dev` - Vite dev server on port 5173 (component mode)
- `npm run build:prod` - Production build to `dist/` (bundled into Electron)

**In Electron:**
- Loaded from bundled files (not via network)
- Communicates with backend via HTTP (localhost on random port)
- Communicates with Electron main process via IPC (through preload script)

**Platforms**: Cross-platform (bundled in Electron)

### 6. Aura.Cli (Command Line Interface)
**Technology**: .NET 8 Console Application  
**Purpose**: Headless automation and scripting

**Key Features:**
- Complete video generation pipeline without UI
- Batch processing support
- JSON configuration files
- Progress reporting to console or file
- CI/CD integration support

**Platforms**: Cross-platform (.NET 8)

## Data Flow

### User Interaction Flow

```
User Interaction
    ↓
Electron Window (React UI)
    ↓ HTTP (localhost)
Backend API (ASP.NET Core child process)
    ↓ In-process calls
Aura.Core (Business Logic)
    ↓
Aura.Providers (LLM, TTS, Images, Video)
    ↓
External Services / Local Tools (FFmpeg, APIs)
```

### IPC Flow (Electron)

```
React Component
    ↓ window.electron.* API
Preload Script (contextBridge)
    ↓ IPC
Main Process IPC Handler
    ↓
Native OS APIs / Backend Control
```

## Development Workflows

### Desktop App Development (Recommended for Production Testing)

```bash
# 1. Build frontend
cd Aura.Web
npm run build:prod

# 2. Run Electron app
cd ../Aura.Desktop
npm run dev
```

**Characteristics:**
- Complete desktop app experience
- Backend spawned automatically
- Native OS integration (dialogs, menus, tray)
- IPC communication available
- Production-like environment

### Component Development (Rapid Iteration)

```bash
# Terminal 1: Backend
cd Aura.Api
dotnet watch run
# Runs at http://localhost:5005

# Terminal 2: Frontend
cd Aura.Web
npm run dev
# Runs at http://localhost:5173 with HMR
```

**Characteristics:**
- Fast iteration with hot reload
- Browser-based (not Electron)
- No Electron features (IPC, native APIs)
- Quick testing of API and UI components

## Deployment Scenarios

### Production (Desktop Application)

**Windows:**
- NSIS installer: `Aura Video Studio-Setup-1.0.0.exe`
- Portable executable: `Aura Video Studio-1.0.0.exe`
- Installed to: `C:\Program Files\Aura Video Studio`
- User data: `%APPDATA%\aura-video-studio`

**macOS (planned):**
- DMG installer
- Installed to: `/Applications/Aura Video Studio.app`
- User data: `~/Library/Application Support/aura-video-studio`

**Linux (planned):**
- AppImage: `Aura-Video-Studio-1.0.0.AppImage`
- User data: `~/.config/aura-video-studio`

### Development Environments

**Electron Development:**
```bash
cd Aura.Desktop
npm run dev
# Electron app with dev tools enabled
```

**Component Development:**
```bash
# Backend: http://localhost:5005
# Frontend: http://localhost:5173 (browser)
```

## Technology Stack Summary

### Frontend
- **React**: 18.2.0
- **TypeScript**: 5.3.3 (strict mode)
- **Vite**: 6.4.1
- **Fluent UI**: 9.47.0
- **State**: Zustand 5.0.8
- **Router**: React Router 6.21.0
- **HTTP**: Axios 1.6.5

### Backend
- **.NET**: 8.0
- **ASP.NET Core**: 8.0 (Minimal API + Controllers)
- **Logging**: Serilog
- **Database**: SQLite

### Desktop
- **Electron**: 32.2.5
- **Node.js**: 20.0.0+ (Both Desktop and Web)
- **electron-builder**: 25.1.8
- **electron-updater**: 6.3.9

### Video Processing
- **FFmpeg**: 4.0+
- **Hardware Acceleration**: NVENC, AMF, QuickSync

## Key Design Principles

### Security
- Context isolation in Electron renderer
- Node integration disabled (sandboxed renderer)
- IPC channel whitelisting
- Input validation and sanitization
- Content Security Policy (CSP)
- Secrets encryption at rest

### Performance
- Hardware-accelerated video rendering
- Frame analysis and optimization
- Proxy media support
- Cached waveforms and thumbnails
- Efficient state management (Zustand)

### Reliability
- Provider fallback chains
- Circuit breaker pattern for API calls
- Automatic retry with exponential backoff
- Crash recovery and logging
- Health checks and diagnostics

### User Experience
- Guided Mode for beginners
- Advanced Mode for power users
- Real-time progress via SSE
- Offline mode support
- Native OS integration

## References

- [ARCHITECTURE_MIGRATION_NOTE.md](./ARCHITECTURE_MIGRATION_NOTE.md) - Migration from web to Electron
- Electron README - Electron architecture details
- [DESKTOP_APP_GUIDE.md](../../DESKTOP_APP_GUIDE.md) - Desktop app development
- [DEVELOPMENT.md](../../DEVELOPMENT.md) - Component development workflows

---

**Last Updated:** November 2025 (Electron migration complete)
```

**Distribution Policy**: Aura Video Studio follows a portable-only distribution model. MSIX/APPX packages and traditional installers are not supported.

## Platform Strategy

### Linux (Development & CI)
- **Purpose**: Cross-platform development, automated testing
- **What Works**: Aura.Core, Aura.Api, Aura.Web, unit tests
- **What Doesn't**: Windows-specific providers (Windows TTS, hardware detection details)
- **Strategy**: Use mocks and stubs for Windows-only features

### Windows (Production)
- **Purpose**: Final deployment target
- **What Works**: Everything, including full provider support and hardware acceleration
- **Requirements**: Windows 10/11 x64, Modern web browser (Chrome, Edge, Firefox recommended)
- **Distribution**: Portable ZIP only - no installation required

## Build & CI Strategy

### ci-linux.yml
- Runs on `ubuntu-latest`
- Builds Aura.Core, Aura.Providers, Aura.Api
- Builds Aura.Web (npm install + build)
- Runs unit tests
- Starts API and tests basic functionality
- Produces build artifacts

### ci-windows.yml
- Runs on `windows-latest`
- Builds all .NET projects
- Runs unit tests
- Builds portable ZIP distribution
- Generates checksums (SHA-256)
- Uploads portable artifacts and test results
- Enforces portable-only policy (no MSIX/installer packaging)

## Security Considerations

### API Keys
- Stored in `%LOCALAPPDATA%\Aura\settings.json`
- **Windows**: Encrypted with DPAPI (planned)
- **Linux dev**: Plaintext in `~/.aura-dev/` with warnings

### Code Signing
- Portable distributions can be signed with PFX certificate (optional)
- Certificate stored in GitHub Secrets for CI/CD
- Unsigned builds clearly marked in documentation

### Browser Security
- Application served over localhost HTTP (127.0.0.1:5005)
- CORS restricted to localhost only
- Uses standard browser security sandbox
- HTTPS-only for external API calls (OpenAI, etc.)

## Directory Structure

```
aura-video-studio/
├── Aura.Core/              # Business logic (.NET 8)
├── Aura.Providers/         # Provider implementations (.NET 8)
├── Aura.Api/               # Backend API (ASP.NET Core 8)
├── Aura.Web/               # Frontend UI (React + Vite)
├── Aura.Tests/             # Unit tests
├── Aura.E2E/               # Integration tests
├── scripts/
│   ├── ffmpeg/             # FFmpeg binaries
│   ├── packaging/          # Build scripts (Portable ZIP only)
│   └── cleanup/            # Cleanup scripts and CI guard
├── .github/workflows/
│   ├── ci-linux.yml        # Linux CI
│   └── ci-windows.yml      # Windows CI + packaging
└── artifacts/              # Build outputs (created during build)
    └── windows/
        ├── portable/       # Portable ZIP distribution
        ├── checksums.txt
        ├── sbom.json
        └── attributions.txt
```

## Technology Stack Summary

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Aura.Core | .NET 8 | Business logic |
| Aura.Providers | .NET 8 | Provider implementations |
| Aura.Api | ASP.NET Core 8 | Backend API |
| Aura.Web | React 18 + TypeScript | Frontend UI |
| UI Framework | Fluent UI React | Windows 11 design system |
| Build Tool | Vite | Fast bundling |
| Packaging | Portable ZIP | No-install distribution |
| Video | FFmpeg | Rendering engine |
| Audio | NAudio | DSP and mixing |
| Logging | Serilog | Structured logging |
| Testing | xUnit | Unit tests |

## Getting Started

### Prerequisites
- .NET 8 SDK
- Node.js 20+
- (Windows only) Visual Studio 2022 or Build Tools
- (Windows only) Windows 11 SDK

### Development Workflow

```bash
# 1. Clone repository
git clone https://github.com/Coffee285/aura-video-studio.git
cd aura-video-studio

# 2. Restore .NET dependencies
dotnet restore

# 3. Install npm dependencies
cd Aura.Web
npm install
cd ..

# 4. Start API (Terminal 1)
dotnet run --project Aura.Api

# 5. Start Web UI (Terminal 2)
cd Aura.Web
npm run dev

# 6. Open browser to http://localhost:5173
```

### Building for Production (Windows)

```powershell
# Run the build script
.\scripts\packaging\build-all.ps1

# Generate SBOM and attributions
.\scripts\packaging\generate-sbom.ps1

# Output in artifacts/windows/
```

## Support & Contact

- **Issues**: https://github.com/Coffee285/aura-video-studio/issues
- **Documentation**: See README.md and individual project READMEs
- **License**: See LICENSE file
