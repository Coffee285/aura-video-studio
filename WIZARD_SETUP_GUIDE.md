# Wizard Setup Guide

## Overview

Aura Video Studio uses a **single, streamlined 6-step setup wizard** called `FirstRunWizard`. This guide explains the wizard flow and how to troubleshoot common issues.

## The Setup Wizard

**Location**: `Aura.Web/src/pages/Onboarding/FirstRunWizard.tsx`

**Route**: The desktop app navigates to `#/setup` which maps directly to `FirstRunWizard`.

**This is the ONLY wizard that should be used.** Previous implementations (like `SetupWizard.tsx`) have been removed to avoid confusion. `DesktopSetupWizard.tsx` exists but is NOT used by the main desktop app - it was an experimental implementation and should be considered legacy.

### 6-Step Flow

The wizard displays "Step X of 6" where X is 1-indexed for the user:

1. **Step 1 of 6: Welcome** - Introduction to Aura Video Studio
2. **Step 2 of 6: FFmpeg Check** - Quick detection of existing FFmpeg installation  
   *(This step uses `FFmpegDependencyCard` with auto-check enabled)*
3. **Step 3 of 6: FFmpeg Install** - Guided installation or manual configuration  
   *(This step uses `FFmpegDependencyCard` with auto-expand details)*
4. **Step 4 of 6: Provider Configuration** - Set up at least one LLM provider (or use offline mode)
5. **Step 5 of 6: Workspace Setup** - Configure default save locations
6. **Step 6 of 6: Complete** - Summary and transition to main app

**Note**: Internally, steps are 0-indexed (0-5), but displayed to users as 1-6.

### Key Features

- **Circuit Breaker Clearing**: On mount, the wizard clears circuit breaker state to prevent false "backend not running" errors
- **Auto-Save Progress**: Your progress is saved automatically to both backend and localStorage
- **Resume Capability**: If you exit mid-setup, you'll see a dialog asking if you want to resume or start fresh
- **Backend Status Check**: Shows warnings only when backend is actually unreachable (not on initial load)
- **Process Cleanup**: All FFmpeg processes are properly terminated when the app exits

### FFmpeg Integration

The wizard uses `FFmpegDependencyCard` component which calls these backend endpoints:

- **`GET /api/system/ffmpeg/status`** - Extended status with hardware acceleration details (called on step load and after operations)
- **`POST /api/ffmpeg/install`** - Install managed FFmpeg (triggered by "Install Managed FFmpeg" button)
- **`POST /api/ffmpeg/rescan`** - Rescan system PATH and common locations (triggered by "Re-scan" button)
- **`POST /api/ffmpeg/use-existing`** - Validate and use a custom FFmpeg path (triggered by "Validate Path" button in Step 3)

**All FFmpeg endpoints skip the circuit breaker** (via `_skipCircuitBreaker: true` config) to prevent false failures during setup.

**Backend Status Banner**: The `BackendStatusBanner` component is shown on steps 2-4 (not on Welcome or Complete screens). It checks backend health via `GET /api/system/health/status` and only displays when the backend is genuinely unreachable.

## Common Issues

### Issue 1: "You have incomplete setup. Would you like to resume where you left off?"

**Cause**: You started the wizard but didn't complete it in a previous session.

**Solution**: 
- Click **"Resume Setup"** to continue from where you left off
- Click **"Start Fresh"** to begin the wizard from the beginning
- This is normal behavior and helps you avoid losing progress

### Issue 2: "Backend Server Not Running" or "Backend unreachable" Error

**Cause**: The Aura backend API is not running or not reachable.

**In the Desktop App**: The backend is automatically started by Electron, but it may fail to start due to:
- Port 5005 already in use
- .NET runtime not installed or incompatible version
- Antivirus or firewall blocking the backend
- Backend crashed during startup

**In Development Mode**: You need to manually start the backend.

**Solution for Desktop App**:
1. Close the desktop app completely
2. Check Task Manager for any lingering ".NET Host" or "Aura.Api" processes and terminate them
3. Restart the desktop app
4. If the error persists, check the Electron logs: `%LOCALAPPDATA%\aura-video-studio\logs\`

**Solution for Development Mode**:
1. Open a terminal in the project root
2. Run: `dotnet run --project Aura.Api`
3. Wait for the message "Application started. Press Ctrl+C to shut down."
4. Click "Retry" in the wizard or refresh the page

**Note**: The wizard only shows this error after confirming the backend is actually unreachable, not on initial load. The `FFmpegDependencyCard` will show specific error messages distinguishing between:
- Backend unreachable (network error)
- Backend timeout (slow response)
- Backend running but FFmpeg not configured (setup required)

### Issue 3: Multiple ".NET Host" or "AI-Powered Video Generation Studio" Processes in Task Manager

**Cause**: Previous backend processes weren't properly cleaned up on shutdown.

**Solution**: 
- **Fixed in this PR** - The backend now properly terminates all FFmpeg and child processes on shutdown
- To clean up existing orphaned processes:
  1. Open Task Manager (Ctrl+Shift+Esc)
  2. Look for ".NET Host", "ffmpeg", or "AI-Powered Video Generation Studio" processes
  3. Right-click and select "End Task" for each one
  4. Restart the application - it will now clean up properly on exit

### Issue 4: "Not Ready" Status Even When FFmpeg is Installed

**Cause**: The backend may not be detecting FFmpeg correctly, or there's a path/validation issue.

**Solution**:
1. Click the "Re-scan" button - this forces the backend to re-check all common FFmpeg locations
2. If "Re-scan" doesn't work, check that the backend is actually running (see Issue 2)
3. If FFmpeg is installed in a non-standard location, use the "Use an Existing FFmpeg" section in Step 3:
   - Enter the full path to the FFmpeg executable
   - Click "Validate Path" to confirm it works
4. As a last resort, use "Install Managed FFmpeg" to download and install a bundled version

**Debug Steps**:
1. Open browser DevTools (F12) → Console tab
2. Look for log messages starting with `[FFmpegDependencyCard]` or `[Rescan FFmpeg]`
3. These logs will show whether the backend was contacted and what response was received
4. Common issues:
   - "Backend unreachable" = Backend is not running
   - "Circuit breaker" = Previous failures have temporarily blocked requests (wait 30s or refresh)
   - "404" or "428" = Endpoint mismatch or setup not complete

### Issue 5: "Failed to save progress" Error

**Cause**: The wizard auto-saves your progress to the backend, but the save operation failed.

**Specific Error Messages**:
- "Backend unreachable" = Backend is not running
- "Network Error" = Connection lost during save
- "Validation error" = The data being saved is invalid

**Solution**:
1. If backend is offline, start it (see Issue 2)
2. Click "Retry" in the auto-save indicator if it appears
3. Continue with the wizard - your progress is also saved to localStorage as a backup
4. If the issue persists, you can still complete the wizard; the backend state will sync on completion

## How the Wizard is Invoked

### Desktop App (Electron)
When you launch the desktop app:
1. Electron's `main.js` checks first-run status via `appConfig.isSetupComplete()`
2. If not complete, it navigates the main window to `#/setup`
3. The route `#/setup` maps to `FirstRunWizard` component (defined in `AppRouterContent.tsx`)
4. The wizard runs full-screen until completion
5. On completion, the app navigates to the main dashboard

**Note**: `DesktopSetupWizard.tsx` exists in the codebase but is NOT used by the main desktop app. It was an experimental implementation that has been superseded by the direct navigation to FirstRunWizard.

### Web App / Development
When you launch the web app:
1. `App.tsx` checks if first-run is complete (`hasCompletedFirstRun()`)
2. If not complete, it renders `FirstRunWizard` full-screen
3. On completion, it marks first-run as complete and shows the main app

### Reconfiguration
After initial setup, you can reconfigure from the Welcome page:
1. Click the "Setup Wizard" or "Reconfigure" button
2. Opens `ConfigurationModal` which wraps `FirstRunWizard`
3. Shows in a modal dialog instead of full-screen

## Backend Process Management

### How It Works
- FFmpeg processes are tracked via `ProcessManager` in `Aura.Core`
- On application shutdown, `Program.cs` calls `ProcessManager.KillAllProcessesAsync()`
- This ensures all FFmpeg child processes are terminated
- Shutdown timeout is set to 30 seconds to allow graceful cleanup

### Logging
Shutdown process is logged in detail:
```
[Info] === Application Shutdown Initiated ===
[Info] Shutdown Phase 1a: Terminating all FFmpeg processes
[Warn] Found 2 tracked FFmpeg processes to terminate
[Info] All FFmpeg processes terminated successfully
[Info] Shutdown Phase 2: Background services stopped and processes cleaned up (Elapsed: 1234ms)
```

## Developer Notes

### Circuit Breaker State
The wizard clears circuit breaker state on mount:
```typescript
PersistentCircuitBreaker.clearState();
resetCircuitBreaker();
```
This prevents false "service unavailable" errors from persisted circuit breaker state.

### Resume State Management
- Progress is saved to both backend (via `setupApi`) and localStorage
- Backend is the primary source of truth
- localStorage is used as fallback if backend is unreachable

### Wizard State Flow
```
Check saved progress → Show resume dialog (if applicable) → Run wizard → Complete → Mark first-run complete
```

## Testing the Wizard

### Manual Test Checklist
- [ ] Fresh install shows wizard on first launch
- [ ] Can complete all 6 steps successfully
- [ ] Progress is saved and resumable
- [ ] "Start Fresh" clears all saved state
- [ ] Backend status banner only shows when backend is actually down
- [ ] FFmpeg processes are cleaned up on app exit
- [ ] No orphaned processes remain in Task Manager after exit

### Verification Matrix (from PR requirement)

#### Test 1: Fresh environment, backend running, FFmpeg not installed
1. Clean install or cleared wizard state
2. Backend is running (desktop app auto-starts it, or `dotnet run --project Aura.Api`)
3. **Expected**: Step 2 shows "Not Ready" badge on FFmpegDependencyCard
4. **Expected**: No "Backend unreachable" error
5. Click "Install Managed FFmpeg"
6. **Expected**: Progress bar shows installation
7. **Expected**: After completion, badge changes to "Ready" and shows green checkmark
8. **Expected**: "Next" button becomes enabled
9. Click "Next" and complete wizard
10. **Expected**: No "Failed to save progress" error

#### Test 2: Backend stopped when entering Step 2
1. Start wizard
2. Stop backend (kill process or Ctrl+C if running manually)
3. Navigate to Step 2
4. **Expected**: BackendStatusBanner appears with clear message:
   - "Backend Server Not Running"
   - Instructions on how to start it
   - "Retry" and "Dismiss" buttons
5. **Expected**: FFmpegDependencyCard shows specific error: "Backend unreachable. Please ensure the Aura backend is running."
6. Start backend
7. Click "Retry" in BackendStatusBanner
8. **Expected**: Banner disappears
9. Click "Re-scan" in FFmpegDependencyCard
10. **Expected**: Status updates correctly (either "Ready" if FFmpeg found, or "Not Ready" with install option)

#### Test 3: Backend running with FFmpeg already installed
1. Install FFmpeg manually (or use managed install from previous run)
2. Ensure FFmpeg is in PATH or in a common location
3. Start wizard
4. Navigate to Step 2
5. **Expected**: FFmpegDependencyCard shows "Ready" badge immediately
6. **Expected**: Shows detected path, version, and source
7. **Expected**: "Next" button is enabled
8. Can proceed through wizard without installing again

#### Test 4: Clean desktop and rebuild
1. Run clean-desktop script (if available: `clean-desktop.ps1`)
2. Rebuild application
3. Launch app
4. **Expected**: Wizard starts from Step 1 (Welcome)
5. **Expected**: No stale progress from previous session
6. **Expected**: FFmpeg detection behaves same as Test 1 or Test 3 (depending on whether FFmpeg is installed on system)

### Testing Backend Cleanup
1. Start the backend: `dotnet run --project Aura.Api`
2. Generate a video (which spawns FFmpeg processes)
3. Stop the backend: Press Ctrl+C
4. Check Task Manager - no FFmpeg or .NET Host processes should remain

## Troubleshooting Backend Issues

If you're still seeing backend errors after these fixes:

1. **Check if backend is actually running**:
   ```bash
   curl http://localhost:5005/api/healthz
   ```
   Should return: `{"status":"Healthy",...}`

2. **Check backend logs**:
   ```bash
   # Logs are in: %LOCALAPPDATA%\Aura\logs\
   # On Windows: C:\Users\YourName\AppData\Local\Aura\logs\
   # Recent errors are in: errors-YYYYMMDD.log
   ```

3. **Clear circuit breaker state manually**:
   - Open browser DevTools (F12)
   - Go to Application → Local Storage
   - Delete any keys starting with `circuit-breaker-`
   - Refresh the page

4. **Reset wizard state completely**:
   - Open browser DevTools (F12)
   - Go to Application → Local Storage
   - Delete keys: `hasCompletedFirstRun`, `wizard-state`, `wizard-progress`
   - Refresh the page to restart wizard from step 0

## Need Help?

If you're still experiencing issues:
1. Check the [Installation Guide](INSTALLATION.md)
2. Review the [Troubleshooting Guide](TROUBLESHOOTING.md)
3. Check backend logs in `%LOCALAPPDATA%\Aura\logs\`
4. Open an issue on GitHub with:
   - Steps to reproduce
   - Error messages
   - Relevant log excerpts
   - Screenshot of the issue
