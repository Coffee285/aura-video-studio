# PR-A1: Electron Main Process Setup - Testing Checklist

## Pre-Deployment Testing

This checklist ensures all components of the new Electron architecture are functioning correctly before deployment.

## Environment Setup

### Development Environment
- [ ] Node.js 20+ installed
- [ ] npm dependencies installed (`npm install` in Aura.Desktop/)
- [ ] .NET 8 SDK installed
- [ ] Aura.Api backend compiled (Debug mode)
- [ ] Aura.Web frontend built (`npm run build` in Aura.Web/)

### Test Scenarios

## 1. Window Management ✅

### Window Creation
- [ ] Application launches successfully
- [ ] Splash screen displays during startup
- [ ] Main window appears after backend initializes
- [ ] Window has correct default size (1920x1080)
- [ ] Minimum window size enforced (1280x720)
- [ ] Window background is correct (#0F0F0F)

### Window State Persistence
- [ ] Resize window → close → reopen → size restored
- [ ] Move window → close → reopen → position restored
- [ ] Maximize window → close → reopen → maximized state restored
- [ ] Works correctly on primary monitor
- [ ] Works correctly when moved to secondary monitor
- [ ] Invalid saved positions are reset to center

### Window Controls
- [ ] Minimize button works
- [ ] Maximize/restore button toggles correctly
- [ ] Close button behavior:
  - [ ] With minimize-to-tray: hides to tray
  - [ ] Without minimize-to-tray: quits app
- [ ] Title bar drag works
- [ ] Window resizing from edges works

## 2. IPC Communication ✅

### Configuration IPC
- [ ] `config:get` retrieves values correctly
- [ ] `config:set` saves values persistently
- [ ] `config:getAll` returns all settings
- [ ] `config:reset` clears all settings
- [ ] `config:getSecure` retrieves encrypted values
- [ ] `config:setSecure` stores encrypted values
- [ ] Recent projects list updates correctly

### Dialog IPC
- [ ] `dialog:openFolder` opens folder picker
- [ ] `dialog:openFile` opens file picker with filters
- [ ] `dialog:openMultipleFiles` allows multiple selection
- [ ] `dialog:saveFile` opens save dialog
- [ ] `dialog:showMessage` displays message box
- [ ] Dialog results return to renderer correctly

### Shell IPC
- [ ] `shell:openExternal` opens URLs in browser
- [ ] `shell:openPath` opens files/folders in explorer
- [ ] `shell:showItemInFolder` reveals file in explorer
- [ ] `shell:trashItem` moves items to recycle bin
- [ ] Invalid URLs are rejected
- [ ] Path traversal attempts blocked

### Video Generation IPC
- [ ] `video:generate:start` initiates generation
- [ ] `video:generate:pause` pauses generation
- [ ] `video:generate:resume` resumes generation
- [ ] `video:generate:cancel` cancels generation
- [ ] `video:generate:status` retrieves status
- [ ] `video:generate:list` lists all generations
- [ ] Progress events received in renderer
- [ ] Error events received in renderer
- [ ] Complete events received in renderer

### Backend IPC
- [ ] `backend:getUrl` returns correct URL
- [ ] `backend:health` checks backend health
- [ ] `backend:ping` measures response time
- [ ] `backend:info` retrieves backend info
- [ ] `backend:version` retrieves version
- [ ] `backend:providerStatus` gets provider status
- [ ] `backend:ffmpegStatus` checks FFmpeg
- [ ] Health updates received periodically
- [ ] Provider updates received

### App IPC
- [ ] `app:getVersion` returns correct version
- [ ] `app:getName` returns app name
- [ ] `app:getPaths` returns correct paths
- [ ] `app:getLocale` returns system locale
- [ ] `app:isPackaged` returns correct state
- [ ] `app:restart` restarts application
- [ ] `app:quit` quits application

### Window IPC
- [ ] `window:minimize` minimizes window
- [ ] `window:maximize` toggles maximize
- [ ] `window:close` closes window
- [ ] `window:hide` hides window
- [ ] `window:show` shows window

## 3. Security ✅

### Process Isolation
- [ ] Context isolation is enabled
- [ ] Node integration is disabled
- [ ] Sandbox is enabled
- [ ] Remote module is not used
- [ ] DevTools accessible only via menu in production

### IPC Security
- [ ] Invalid channel names are rejected
- [ ] Rate limiting prevents spam (try >10 calls/sec)
- [ ] Path traversal attempts fail (`../../../etc/passwd`)
- [ ] Invalid URLs are rejected (`javascript:alert(1)`)
- [ ] HTML injection in dialogs is prevented

### CSP (Content Security Policy)
- [ ] Check CSP headers in Network tab (DevTools)
- [ ] Production: Strict CSP enforced
- [ ] Development: Permissive CSP for hot-reload
- [ ] No console CSP errors in production mode

## 4. Backend Integration ✅

### Backend Startup
- [ ] Backend process spawns automatically
- [ ] Port is auto-discovered (check console log)
- [ ] Backend health check succeeds within 60 seconds
- [ ] Frontend receives backend URL
- [ ] FFmpeg path configured correctly
- [ ] Backend logs appear in console

### Backend Health Monitoring
- [ ] Periodic health checks run every 30 seconds
- [ ] Health status updates sent to renderer
- [ ] Backend auto-restarts on crash (test by killing process)
- [ ] Max 3 restart attempts enforced
- [ ] Error dialog shows after max attempts

### Backend Shutdown
- [ ] Backend terminates gracefully on app quit
- [ ] Backend killed if doesn't stop in 5 seconds
- [ ] No zombie processes left behind

## 5. Menu System ✅

### File Menu
- [ ] New Project (Ctrl+N) triggers menu event
- [ ] Open Project (Ctrl+O) triggers event
- [ ] Recent Projects submenu populates
- [ ] Recent Projects clickable
- [ ] Save (Ctrl+S) triggers event
- [ ] Save As (Ctrl+Shift+S) triggers event
- [ ] Import submenu items work
- [ ] Export submenu items work
- [ ] Exit quits application (Windows/Linux)

### Edit Menu
- [ ] Undo (Ctrl+Z) works in text fields
- [ ] Redo (Ctrl+Y / Cmd+Shift+Z) works
- [ ] Cut/Copy/Paste (Ctrl+X/C/V) work
- [ ] Select All (Ctrl+A) works
- [ ] Find (Ctrl+F) triggers event
- [ ] Preferences (Ctrl+,) triggers event

### View Menu
- [ ] Reload (Ctrl+R) reloads window
- [ ] Force Reload (Ctrl+Shift+R) clears cache
- [ ] Toggle DevTools (Ctrl+Shift+I) works
- [ ] Actual Size (Ctrl+0) resets zoom
- [ ] Zoom In (Ctrl++) increases zoom
- [ ] Zoom Out (Ctrl+-) decreases zoom
- [ ] Toggle Fullscreen (F11) works

### Tools Menu
- [ ] Provider Settings triggers event
- [ ] FFmpeg Configuration triggers event
- [ ] Clear Cache shows confirmation
- [ ] Reset Settings shows warning
- [ ] View Logs triggers event
- [ ] Open Logs Folder opens explorer
- [ ] Run Diagnostics triggers event

### Help Menu
- [ ] Documentation opens in browser
- [ ] Getting Started triggers event
- [ ] Keyboard Shortcuts triggers event
- [ ] Report Issue opens GitHub
- [ ] View on GitHub opens repo
- [ ] Check for Updates works
- [ ] About shows version info

## 6. System Tray (Windows) ✅

### Tray Icon
- [ ] Tray icon appears in system tray
- [ ] Icon is correct (not broken)
- [ ] Tooltip shows "Aura Video Studio"
- [ ] Click toggles window visibility
- [ ] Double-click shows window
- [ ] Right-click opens context menu

### Tray Menu
- [ ] "Show Aura Studio" shows window
- [ ] "Hide" hides window
- [ ] Backend URL displayed (if available)
- [ ] Operation status displayed (if active)
- [ ] "Cancel Current Operation" appears during processing
- [ ] "New Project" shows window and triggers event
- [ ] "Open Project" shows window and triggers event
- [ ] "Settings" shows window and triggers event
- [ ] "Check for Updates" works (production only)
- [ ] Version displayed correctly
- [ ] "Quit Aura Studio" quits app

### Tray Notifications (Windows)
- [ ] Balloon notification on minimize-to-tray
- [ ] Custom notifications can be shown

## 7. Protocol Handler ✅

### Protocol Registration
- [ ] `aura://` protocol registered with OS
- [ ] Second instance focuses existing window
- [ ] Protocol URL passed to existing instance

### Protocol URLs
Test these URLs (create shortcuts or type in browser):
- [ ] `aura://open?path=C:\test\project.aura`
- [ ] `aura://create?template=basic`
- [ ] `aura://generate?script=test`
- [ ] `aura://settings`
- [ ] `aura://help`
- [ ] `aura://about`
- [ ] Invalid action rejected (e.g., `aura://invalid`)
- [ ] XSS attempts blocked (e.g., `aura://<script>alert(1)</script>`)

## 8. Error Handling ✅

### Uncaught Exceptions
- [ ] Exception logged to file
- [ ] Error dialog shows to user
- [ ] App attempts to continue (first 3 crashes)
- [ ] App quits after 3rd crash
- [ ] Log file created with details
- [ ] Log file includes stack trace

### Promise Rejections
- [ ] Unhandled rejections logged
- [ ] No app crash on rejection
- [ ] Error visible in console

### Process Crashes
- [ ] GPU crash: App attempts recovery
- [ ] Renderer crash: User sees recovery dialog
- [ ] Backend crash: Auto-restart triggered
- [ ] Child process crash: Logged but app continues

### Error Logs
- [ ] Logs saved to correct location
- [ ] Log location: `%APPDATA%/aura-video-studio/logs/`
- [ ] Filename format: `crash-{timestamp}.log`
- [ ] JSON format with all details
- [ ] System info included

## 9. Development vs Production ✅

### Development Mode (--dev flag)
- [ ] DevTools open automatically
- [ ] Verbose logging in console
- [ ] Permissive CSP
- [ ] Backend from local build path
- [ ] Auto-updater disabled
- [ ] "Development Mode: true" in startup log

### Production Mode (packaged)
- [ ] DevTools closed by default
- [ ] DevTools accessible via menu
- [ ] Minimal logging
- [ ] Strict CSP enforced
- [ ] Backend from bundled resources
- [ ] Auto-updater checks on startup
- [ ] "Development Mode: false" in startup log

## 10. First Run Experience ✅

### First Launch
- [ ] Setup wizard detected
- [ ] Navigates to `#/setup` route
- [ ] `setupComplete` config is false
- [ ] `firstRun` config is true

### Subsequent Launches
- [ ] Setup wizard skipped
- [ ] Opens to last used route
- [ ] Settings persisted

## 11. Cleanup and Shutdown ✅

### Graceful Shutdown
- [ ] Backend process terminated
- [ ] Temp files cleaned up
- [ ] Window state saved
- [ ] No processes left running
- [ ] No error on quit

### Cleanup Locations
- [ ] Temp: `%TEMP%/aura-video-studio/` deleted
- [ ] Logs: `%APPDATA%/aura-video-studio/logs/` preserved
- [ ] Config: `%APPDATA%/aura-video-studio/` preserved

## 12. TypeScript Integration ✅

### Type Definitions
- [ ] `types.d.ts` exists and is valid
- [ ] All IPC methods have types
- [ ] Supporting types defined
- [ ] Window global augmented
- [ ] No TypeScript errors when importing

### Frontend Usage
```typescript
// Test this in React components
import type { ElectronAPI } from '../Aura.Desktop/electron/types';

const config = await window.electron.config.get('theme');
// Should have IntelliSense and type checking
```

## Performance Testing

### Startup Time
- [ ] Cold start < 5 seconds
- [ ] Backend initialization < 30 seconds
- [ ] Window visible in < 3 seconds after backend ready

### Memory Usage
- [ ] Idle memory < 200 MB
- [ ] No memory leaks over time (monitor for 10 minutes)
- [ ] Backend memory reasonable

### IPC Performance
- [ ] IPC calls respond in < 100ms
- [ ] Rate limiting doesn't affect normal usage
- [ ] No noticeable lag in UI

## Platform-Specific Testing

### Windows 10
- [ ] All features work
- [ ] Tray icon works
- [ ] Protocol handler works
- [ ] Scaling: 100%, 125%, 150%
- [ ] Multi-monitor setup

### Windows 11
- [ ] All features work
- [ ] Tray icon works
- [ ] Protocol handler works
- [ ] New Windows 11 UI elements
- [ ] Snap layouts work

### Windows Server (if applicable)
- [ ] Application launches
- [ ] Core features work

## Code Quality

### Linting
- [ ] No console errors in production
- [ ] No console warnings in production
- [ ] Proper error handling everywhere

### Documentation
- [ ] All modules have JSDoc comments
- [ ] README.md is comprehensive
- [ ] Implementation summary complete

## Known Issues

Document any issues found:

1. [ ] Issue: _______________
   - [ ] Severity: Critical / Major / Minor
   - [ ] Workaround: _______________
   - [ ] Fix planned: Yes / No

## Sign-Off

Testing completed by: _______________
Date: _______________
Environment: Windows ___ (10/11/Server)
Node version: _______________
Electron version: _______________

**Overall Status**: ☐ Pass / ☐ Fail / ☐ Pass with Issues

**Notes**:
_______________________________________________
_______________________________________________
_______________________________________________

## Deployment Readiness

- [ ] All critical tests pass
- [ ] No major issues found
- [ ] Documentation complete
- [ ] Code reviewed
- [ ] Security audit complete

**Ready for Production**: ☐ Yes / ☐ No

**Recommended Actions Before Deployment**:
1. _______________
2. _______________
3. _______________
