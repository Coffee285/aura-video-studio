# Build and Run Guide - Aura Video Studio

## 🔧 Recent Fixes Applied

### Issue: White Screen on Launch

**Problem:** After extracting and running the portable build, the browser opens to `http://127.0.0.1:5005` but shows a white screen instead of the application UI.

**Root Causes:**
1. Frontend not being built during the publish process
2. Assets not copied to the wwwroot directory
3. Incorrect API base URL configuration in production

**Solutions Applied:**
1. ✅ Added MSBuild targets to auto-build frontend during `dotnet publish`
2. ✅ Added automatic copy of dist folder to wwwroot during publish
3. ✅ Updated frontend to use same-origin API calls in production
4. ✅ Enhanced startup diagnostics to detect missing static files
5. ✅ Added `/diag` endpoint for troubleshooting
6. ✅ Updated Launch.bat to poll health endpoint before opening browser

### Previous Fix: 404 Error When Running Portable Build

**Problem:** The Web UI returned 404 errors because the working directory was incorrect.

**Root Cause:** The launcher script was starting the API without setting the working directory.

**Solution:** Updated the launcher to use `/D` flag:
```batch
start "" /D "Api" "Aura.Api.exe"
```

### API Endpoint Prefix

**Problem:** The Web UI was calling `/api/healthz` but the API served it at `/healthz`.

**Solution:** Added a route group with `/api` prefix to all API endpoints:
```csharp
var apiGroup = app.MapGroup("/api");
apiGroup.MapGet("/healthz", ...);
```

## 🚀 Quick Start - Portable Version (Recommended)

This is the easiest way to build and run Aura Video Studio.

### Prerequisites

- Windows 10/11
- PowerShell 5.1 or later
- .NET 8 SDK ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- Node.js 20+ ([Download](https://nodejs.org/))

### Build Steps

1. **Clone the repository** (if you haven't already):
   ```powershell
   git clone https://github.com/Coffee285/aura-video-studio.git
   cd aura-video-studio
   ```

2. **Run the portable build script**:
   ```powershell
   .\scripts\packaging\build-portable.ps1
   ```

   This will:
   - Build the .NET API (self-contained, no dependencies needed)
   - **Automatically build the React Web UI**
   - **Automatically copy the frontend to wwwroot**
   - Copy everything to `artifacts\portable\build\`
   - Create `AuraVideoStudio_Portable_x64.zip`

3. **Extract and run**:
   ```powershell
   # Extract the ZIP
   Expand-Archive artifacts\portable\AuraVideoStudio_Portable_x64.zip -DestinationPath test-run

   # Run the application
   cd test-run
   .\Launch.bat
   ```

4. **Use the application**:
   - The launcher will start the API server
   - Wait for the health check to pass
   - Browser opens automatically to `http://127.0.0.1:5005`
   - You should see the Aura Video Studio welcome page

### Expected Output

When you run `Launch.bat`, you should see:
```
========================================
 Aura Video Studio - Portable Edition
========================================

Starting API server...
Waiting for server to become ready...
Server is ready!

Opening web browser...

========================================
Application started successfully!
========================================

The application should open in your web browser.
If not, manually navigate to: http://127.0.0.1:5005

For diagnostics, visit: http://127.0.0.1:5005/diag
```

In the API server window, you should see:
```
[INFO] =================================================================
[INFO] Static UI: ENABLED
[INFO]   Path: C:\path\to\build\Api\wwwroot
[INFO]   Files: 14
[INFO]   index.html: ✓
[INFO]   assets/: ✓
[INFO] =================================================================
[INFO] Now listening on: http://127.0.0.1:5005
[INFO] Application started. Press Ctrl+C to shut down.
```

**Key Success Indicators:**
- ✅ "Static UI: ENABLED" message
- ✅ File count > 0 (should be ~14 files)
- ✅ index.html and assets/ checkmarks
- ✅ NO "wwwroot directory not found" errors

## 🔍 Troubleshooting

### Problem: White Screen or 404 Errors

**Step 1: Check the Diagnostics Page**

Open http://127.0.0.1:5005/diag in your browser. This page will show:
- ✅/❌ wwwroot directory status
- ✅/❌ index.html file status
- ✅/❌ assets directory status
- File count in wwwroot
- API connectivity status

**Step 2: Check Server Logs**

Look at the API server window for these messages:

**Good:**
```
[INFO] Static UI: ENABLED
[INFO]   Files: 14
```

**Bad:**
```
[ERROR] CRITICAL: wwwroot directory not found
```

**Step 3: Verify Files Exist**

Check that these files exist:
- `Api\wwwroot\index.html` ✓
- `Api\wwwroot\assets\` (folder with JS/CSS files) ✓

**Step 4: Re-extract or Rebuild**

If files are missing:
```powershell
# Option 1: Re-extract the portable ZIP
Expand-Archive -Force artifacts\portable\AuraVideoStudio_Portable_x64.zip -DestinationPath test-run

# Option 2: Rebuild from source
.\scripts\packaging\build-portable.ps1
```

### Problem: Server Won't Start

**Check Port Availability:**
```powershell
# Check if port 5005 is in use
netstat -ano | findstr :5005

# If port is in use, kill the process or choose different port
$env:ASPNETCORE_URLS = "http://127.0.0.1:5006"
```

### Problem: Browser Opens Too Early

The new launcher script includes health check polling, but if you still see issues:

**Manual Start:**
1. Start the API: `cd Api && Aura.Api.exe`
2. Wait for "Application started" message
3. Open browser to http://127.0.0.1:5005

## 🛠️ Development Mode

For active development, you can run the API and Web UI separately:

### 1. Start the API Backend

```powershell
cd Aura.Api
dotnet run
```

Expected output:
```
[INFO] Serving static files from: C:\...\Aura.Api\wwwroot
[INFO] Now listening on: http://127.0.0.1:5005
```

### 2. Start the Web UI (in another terminal)

```powershell
cd Aura.Web
npm install
npm run dev
```

Expected output:
```
VITE v5.4.20  ready in 500 ms

➜  Local:   http://localhost:5173/
➜  Network: use --host to expose
```

### 3. Access the Application

- **Development:** Open http://localhost:5173 (Web UI dev server with hot reload)
- **Production-like:** Open http://127.0.0.1:5005 (served by API)

## 🧪 Testing

### Run Unit Tests

```powershell
dotnet test Aura.Tests/Aura.Tests.csproj
```

Expected: 84+ tests passing

### Run E2E Tests

```powershell
dotnet test Aura.E2E/Aura.E2E.csproj
```

Expected: 8+ tests passing

### Test the API Endpoints

```powershell
# Health check
curl http://127.0.0.1:5005/api/healthz

# System capabilities
curl http://127.0.0.1:5005/api/capabilities

# Available profiles
curl http://127.0.0.1:5005/api/profiles/list
```

## 📊 What's Included

The application includes:

### Backend API (Aura.Api)
- ✅ Health check endpoint
- ✅ Hardware detection and capabilities
- ✅ Script generation (rule-based, free)
- ✅ Text-to-speech (Windows SAPI, free)
- ✅ Video composition (FFmpeg)
- ✅ Settings management
- ✅ Render queue management
- ✅ Downloads manifest

### Web UI (Aura.Web)
- ✅ Welcome page with system status
- ✅ Create page (step-by-step video creation wizard)
- ✅ Dashboard (overview and quick actions)
- ✅ Render page (export settings)
- ✅ Publish page (YouTube metadata)
- ✅ Downloads page (dependency management)
- ✅ Settings page (API keys, preferences)

### Core Features (Aura.Core)
- ✅ Hardware detection (CPU, RAM, GPU)
- ✅ Provider system (LLM, TTS, Video)
- ✅ Video orchestration pipeline
- ✅ Timeline builder
- ✅ Render presets (YouTube 1080p, 4K, Shorts)
- ✅ Audio processing and normalization

## 🐛 Troubleshooting

### Issue: "wwwroot directory not found"

**Symptom:**
```
[WARN] wwwroot directory not found at: C:\...\build\wwwroot
[WARN] Static file serving is disabled. Web UI will not be available.
```

**Solution:** This means you're running an old build. Rebuild using the latest code:
```powershell
.\scripts\packaging\build-portable.ps1
```

### Issue: 404 Error in Browser

**Symptom:** Browser shows "404 - Not Found" when accessing http://127.0.0.1:5005

**Causes:**
1. Old launcher script (doesn't set working directory correctly)
2. Missing wwwroot folder
3. API not finding static files

**Solution:**
1. Rebuild the portable version with the latest scripts
2. Verify the directory structure:
   ```
   build\
   ├── Api\
   │   ├── Aura.Api.exe
   │   └── wwwroot\
   │       ├── index.html
   │       └── assets\
   ├── Launch.bat
   └── README.md
   ```

### Issue: API endpoints return 404

**Symptom:** Web UI shows errors like "Failed to fetch system info"

**Solution:** Make sure you're using the latest API code that includes the `/api` prefix. Rebuild:
```powershell
cd Aura.Api
dotnet build
```

### Issue: Port 5005 already in use

**Symptom:**
```
[ERROR] Failed to bind to address http://127.0.0.1:5005: address already in use
```

**Solution:**
1. Close any other instances of Aura.Api.exe
2. Or change the port in `Aura.Api/Program.cs`:
   ```csharp
   builder.WebHost.UseUrls("http://127.0.0.1:5006");
   ```

## 📝 Next Steps

After confirming the application works:

1. **Create your first video:**
   - Click "Create Video" on the welcome page
   - Fill in the brief (topic, audience, tone)
   - Set duration and pacing
   - Click "Generate Video"

2. **Configure providers:**
   - Go to Settings
   - Add API keys for pro providers (optional)
   - Configure hardware preferences

3. **Explore features:**
   - Check hardware capabilities on the dashboard
   - View render queue
   - Manage downloads

## 🆘 Getting Help

If you're still experiencing issues:

1. **Check the console output** - The API logs show exactly what's happening
2. **Verify the directory structure** - Make sure wwwroot is in the right place
3. **Check file permissions** - Ensure the launcher can execute Aura.Api.exe
4. **Review the logs** - API logs are in `logs/aura-api-*.log`

## 📚 Additional Documentation

- [README.md](./README.md) - Project overview and architecture
- [ARCHITECTURE.md](../architecture/ARCHITECTURE.md) - System design and components
- [PORTABLE.md](../../PORTABLE.md) - User guide for the portable version
- [INSTALL.md](./INSTALL.md) - Detailed installation instructions
- [Aura.Api/README.md](../../README.md) - API documentation
- [Aura.Web/README.md](../../README.md) - Web UI documentation

---

**Note:** All the fixes mentioned above are already applied to the codebase. Simply rebuild and run to get the working version!
