# First Run Guide - Aura Video Studio

This guide will help you get Aura Video Studio running for the first time.

## Quick Start (Windows 11)

### Prerequisites
- Node.js 20.0.0+ installed (18.18.0 recommended)
- .NET 8 SDK installed
- Git with long paths enabled

### Step-by-Step Setup

#### 1. Build the Frontend

The frontend MUST be built before running the backend. Open a terminal in the repository root:

```bash
cd Aura.Web
npm install
npm run build
```

This creates the `dist` folder with the compiled frontend application.

#### 2. Build the Backend

The backend build automatically copies the frontend to `wwwroot`:

```bash
cd ..
dotnet build Aura.sln --configuration Release
```

The build process will:
- Copy `Aura.Web/dist` → `Aura.Api/bin/Release/net8.0/wwwroot`
- Prepare the backend to serve the frontend

#### 3. Run the Application

```bash
cd Aura.Api
dotnet run --configuration Release
```

The application will start on `http://127.0.0.1:5005`

#### 4. Access the Application

Open your browser and navigate to:
```
http://127.0.0.1:5005
```

You should see the Aura Video Studio welcome screen.

## Development Mode

For development, you can run frontend and backend separately:

### Terminal 1 - Frontend Dev Server
```bash
cd Aura.Web
npm run dev
```
Frontend runs on `http://localhost:5173`

### Terminal 2 - Backend API
```bash
cd Aura.Api
dotnet run
```
Backend runs on `http://127.0.0.1:5005`

The frontend dev server proxies API requests to the backend automatically.

## Troubleshooting

### White Screen / "Application Failed to Initialize"

**Cause**: The frontend was not built or not copied to wwwroot.

**Solution**:
1. Build frontend: `cd Aura.Web && npm run build`
2. Rebuild backend: `cd .. && dotnet build Aura.Api --configuration Release`
3. Restart application

### "VITE_API_BASE_URL is not defined"

**Cause**: Missing environment configuration.

**Solution**:
Create `Aura.Web/.env.local`:
```env
VITE_API_BASE_URL=http://127.0.0.1:5005
```

### Backend Builds But Frontend Doesn't Load

**Cause**: The `dist` folder may not exist when backend builds.

**Solution**:
1. Always build frontend FIRST
2. Then build backend
3. Backend build will only copy frontend if `dist` exists

### Port Already in Use

**Cause**: Another process is using port 5005 or 5173.

**Solution**:
- Stop other Aura instances
- Or change ports in configuration files

## Build Order Matters!

**✅ CORRECT ORDER:**
1. `cd Aura.Web && npm run build` (Frontend first)
2. `cd .. && dotnet build` (Backend second - copies frontend)
3. `cd Aura.Api && dotnet run` (Run application)

**❌ WRONG ORDER:**
1. `dotnet build` (Backend built, but no frontend to copy)
2. `npm run build` (Frontend built, but backend already compiled)
3. Result: White screen because wwwroot is empty

## Complete Clean Build

If you encounter persistent issues, perform a clean build:

```bash
# Clean everything
cd Aura.Web
rm -rf node_modules dist
npm install
npm run build

# Clean and rebuild backend
cd ..
dotnet clean
dotnet build Aura.sln --configuration Release

# Run
cd Aura.Api
dotnet run --configuration Release
```

## Environment Files

The application uses different environment files for different scenarios:

- `.env.development` - Used during `npm run dev` (development server)
- `.env.production` - Used during `npm run build` (production build)
- `.env.local` - Local overrides (create this file, not tracked in git)

**Recommended `.env.local` for development:**
```env
VITE_API_BASE_URL=http://127.0.0.1:5005
VITE_APP_VERSION=1.0.0-dev
VITE_ENV=development
VITE_ENABLE_DEBUG=true
```

## Next Steps

Once the application is running:

1. Complete the onboarding wizard
2. Configure your preferred AI providers (see below)
3. Create your first video project

## Provider Setup and API Keys

Aura Video Studio supports multiple AI providers for different capabilities:

### Free/Local Providers (No API Keys Required)

- **Ollama** - Local LLM (script generation)
- **Windows TTS** - Built-in text-to-speech (Windows only)
- **Piper TTS** - Free offline text-to-speech
- **Stock Images** - Free image sources (Pexels, Unsplash, Pixabay)

### Premium Providers (API Keys Required)

#### Script Generation (LLM)
- **OpenAI** (GPT-4, GPT-3.5) - [Get API key](https://platform.openai.com/api-keys)
- **Anthropic** (Claude) - [Get API key](https://console.anthropic.com/)
- **Google Gemini** - [Get API key](https://makersuite.google.com/app/apikey)

#### Text-to-Speech
- **ElevenLabs** - Premium realistic voices - [Get API key](https://elevenlabs.io/api)
- **PlayHT** - Voice cloning and synthesis - [Get API key](https://play.ht/api-access)
- **Azure Speech** - Microsoft neural voices - [Get API key](https://azure.microsoft.com/services/cognitive-services/speech-services/)

#### Image Generation
- **Stability AI** - Stable Diffusion models - [Get API key](https://platform.stability.ai/account/keys)

### Adding API Keys

#### Via UI (Recommended)
1. Open **Settings** → **Providers**
2. Click **Add Key** for the provider you want
3. Paste your API key
4. Click **Test** to validate the key
5. Click **Save** to encrypt and store

#### Via CLI (Headless)
```bash
# Add a key
aura keys set openai sk-proj-abc123...

# Test the key
aura keys test openai

# List configured providers
aura keys list
```

#### Via API (Programmatic)
```bash
# Set a key
curl -X POST http://localhost:5005/api/keys/set \
  -H "Content-Type: application/json" \
  -d '{"provider":"openai","apiKey":"sk-proj-abc123..."}'

# Test a key
curl -X POST http://localhost:5005/api/keys/test \
  -H "Content-Type: application/json" \
  -d '{"provider":"openai"}'
```

### Security Features

✅ **All API keys are encrypted at rest**
- **Windows**: DPAPI encryption (CurrentUser scope)
  - Storage: `%LOCALAPPDATA%\Aura\secure\apikeys.dat` (encrypted binary)
  - Encrypted with user's Windows credentials
- **Linux/macOS**: AES-256-CBC encryption with machine-specific key
  - Storage: `$HOME/.local/share/Aura/secure/apikeys.dat` (encrypted binary)
  - Machine Key: `$HOME/.local/share/Aura/secure/.machinekey` (0600 permissions)
  - File permissions: 600 (owner read/write only)

✅ **Automatic migration from legacy plaintext** (all platforms)
- **Windows**: Detects legacy plaintext at `%LOCALAPPDATA%\Aura\apikeys.json`
- **Linux/macOS**: Detects legacy plaintext at `$HOME/.aura-dev/apikeys.json`
- Migrates all keys to encrypted storage automatically on first run
- Securely deletes legacy file (64KB random overwrite + delete)
- One-time operation per installation, logged for audit trail

✅ **Keys are never logged or displayed in full**
- Automatic masking: `sk-12345...wxyz`
- Redaction in logs, errors, and diagnostics

✅ **Unified secure KeyVault API**
- All key operations use `/api/keys/*` endpoints (KeyVaultController)
- Legacy `/api/apikeys/*` endpoints completely removed for security
- Comprehensive REST API: set, list, test, rotate, delete, info
- No secrets in SSE events or API responses
- User settings file (`user-settings.json`) does NOT contain API keys (stored separately in encrypted storage)

✅ **Test before saving**
- Validates key with real provider connection
- Prevents saving invalid keys

✅ **Safe export/import**
- Keys excluded from exports by default
- Explicit opt-in required to include secrets

For more information, see:
- `SECURITY.md` - Detailed security documentation
- `docs/workflows/SETTINGS_SCHEMA.md` - Settings format and schema
- `BUILD_GUIDE.md` - Complete build instructions
- `README.md` - Project overview
- `docs/` - Detailed documentation
