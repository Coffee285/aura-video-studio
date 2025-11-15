# White Screen Diagnostic Tools

This directory contains diagnostic tools to troubleshoot and fix white screen/blank page issues when accessing Aura Video Studio at `http://127.0.0.1:5005`.

## Quick Start

### Run Diagnostics (Windows)

```powershell
.\scripts\diagnostics\diagnose-white-screen.ps1
```

### Run Diagnostics with Verbose Output

```powershell
.\scripts\diagnostics\diagnose-white-screen.ps1 -Verbose
```

### Apply Nuclear Fix (Clean Rebuild)

```powershell
.\scripts\diagnostics\diagnose-white-screen.ps1 -Fix
```

## What the Diagnostic Script Checks

The `diagnose-white-screen.ps1` script performs comprehensive checks in 6 sections:

### 1. Environment Check
- ✅ Node.js version (>=18 required)
- ✅ npm version (>=9 required)
- ✅ .NET SDK version

### 2. Source Files Check
- ✅ Aura.Web directory structure
- ✅ package.json existence
- ✅ vite.config.ts configuration
- ✅ Previous build artifacts (dist folder)

### 3. Build Output Check
- ✅ artifacts/portable/build/Api directory
- ✅ wwwroot directory existence and contents
- ✅ index.html existence and validity
- ✅ JavaScript module script tags
- ✅ CSS link tags
- ✅ assets directory with .js and .css files
- ✅ Verification that .js files contain JavaScript (not HTML)
- ✅ API executable presence

### 4. Common Issues Check
- ✅ node_modules installation
- ✅ package-lock.json presence
- ✅ .vite cache directory
- ✅ Build timestamp checks (detecting stale builds)

### 5. Manual Browser Checks
Provides guidance on what to check manually in the browser:
- Console tab for JavaScript errors
- Network tab for failed resource loads
- Elements tab for empty root div
- Service worker detection

### 6. Nuclear Fix Option
When run with `-Fix` flag, performs a complete clean rebuild:
1. Removes all build artifacts
2. Cleans frontend build cache
3. Rebuilds frontend (npm run build)
4. Verifies frontend build output
5. Rebuilds API with frontend integration
6. Verifies wwwroot setup

## Common Issues Detected

### Issue: index.html Missing Script Tag
**Symptom**: White screen, no JavaScript executed
**Cause**: Frontend build failed or incomplete
**Fix**: Run script with `-Fix` flag

### Issue: JavaScript Files Contain HTML
**Symptom**: Console errors about unexpected token '<'
**Cause**: Server returning HTML for .js file requests (routing issue)
**Fix**: Check API Program.cs static file configuration

### Issue: assets Directory Missing
**Symptom**: 404 errors for all JavaScript/CSS files
**Cause**: Frontend not copied to wwwroot during build
**Fix**: Re-run publish command or use `-Fix` flag

### Issue: wwwroot Older Than dist
**Symptom**: Changes to frontend not reflected in app
**Cause**: Stale build output
**Fix**: Re-publish API or use `-Fix` flag

## Manual Browser Diagnostics

After starting the API, always check these in your browser:

### 1. Open DevTools (F12)

### 2. Console Tab
Look for:
- ❌ Red error messages (JavaScript errors)
- ⚠️ Yellow warnings
- ✅ No errors = JavaScript is loading

Common errors and meanings:
```
Uncaught SyntaxError: Unexpected token '<'
  → JavaScript file is returning HTML instead of JS

Failed to load resource: net::ERR_FILE_NOT_FOUND
  → JavaScript file missing from server

Uncaught TypeError: Cannot read property 'render' of undefined
  → React failed to initialize (check dependencies)
```

### 3. Network Tab
- Refresh page (F5)
- Filter by "JS"
- Check Status column:
  - ✅ 200 = File loaded successfully
  - ❌ 404 = File not found
  - ❌ 500 = Server error

- Click on a .js file
- Check "Response" tab:
  - ✅ Should show JavaScript code
  - ❌ If shows HTML, server routing is broken

- Check "Headers" tab:
  - Content-Type should be `application/javascript` or `text/javascript`

### 4. Elements Tab
- Find `<div id="root"></div>`
- Is it empty or does it have child elements?
  - Empty = React never mounted
  - Has content = React mounted successfully

### 5. Run These JavaScript Commands in Console

```javascript
// Check if root div exists
document.getElementById('root')

// Check root div content
document.getElementById('root').innerHTML

// Check if React loaded
window.React

// Check script tags
document.querySelectorAll('script[type="module"]')
```

## Nuclear Fix Details

The nuclear fix (`-Fix` flag) performs these steps:

```powershell
# Step 1: Clean all build artifacts
Remove-Item -Recurse -Force artifacts
Remove-Item -Recurse -Force Aura.Web\dist
Remove-Item -Recurse -Force Aura.Web\.vite

# Step 2: Build frontend fresh
cd Aura.Web
npm run build
cd ..

# Step 3: Verify frontend build
# Checks for index.html and script tags

# Step 4: Publish API with frontend
dotnet publish Aura.Api\Aura.Api.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -o artifacts\portable\build\Api

# Step 5: Verify wwwroot
# Checks that all files copied correctly
```

## Development Mode Testing

If the portable build is problematic, test in development mode:

### Terminal 1: Start API
```powershell
cd Aura.Api
dotnet run
```

### Terminal 2: Start Vite Dev Server
```powershell
cd Aura.Web
npm run dev
```

Then open the URL shown by Vite (usually `http://localhost:5173`)

**If dev mode works but portable build doesn't:**
- Issue is with production build process
- Run diagnostic script with `-Verbose` flag

**If dev mode also shows white screen:**
- Issue is with app code or dependencies
- Check for TypeScript compilation errors
- Check for missing dependencies

## Advanced Troubleshooting

### Check if Service Worker is Interfering

```javascript
// Run in browser console
navigator.serviceWorker.getRegistrations().then(registrations => {
    registrations.forEach(reg => {
        console.log('Service Worker:', reg.scope);
        // To unregister: reg.unregister();
    });
});
```

### Check for CSP Violations

Look in Console for messages containing:
- "Content Security Policy"
- "blocked by CSP"

### Check Base URL Mismatch

```javascript
// Run in browser console
console.log('Window Origin:', window.location.origin);
// Should be: http://127.0.0.1:5005
```

### Manual File Inspection

```powershell
# Check index.html script tag
Get-Content artifacts\portable\build\Api\wwwroot\index.html | Select-String "script"

# Check first JS file
Get-ChildItem artifacts\portable\build\Api\wwwroot\assets\*.js | 
    Select-Object -First 1 | 
    Get-Content -TotalCount 5

# Should start with JavaScript code, NOT <!DOCTYPE html>
```

## Getting Help

If the diagnostic script doesn't resolve your issue:

1. Run the script and save output:
   ```powershell
   .\scripts\diagnostics\diagnose-white-screen.ps1 -Verbose > diagnostic-output.txt
   ```

2. Include this information in your bug report:
   - Complete diagnostic output
   - Browser console errors (screenshot or text)
   - Network tab screenshot showing failed requests
   - Node.js and npm versions
   - Windows version

3. Check these resources:
   - `PORTABLE.md` - Troubleshooting section
   - `/diag` endpoint - `http://127.0.0.1:5005/diag`
   - `BLANK_PAGE_FIX_COMPLETE.md` - Previous fix documentation

## Prevention

To avoid white screen issues in the future:

1. **Always verify builds:**
   ```powershell
   # After building, check these exist:
   Test-Path artifacts\portable\build\Api\wwwroot\index.html
   Test-Path artifacts\portable\build\Api\wwwroot\assets\*.js
   ```

2. **Use the build script:**
   ```powershell
   .\scripts\packaging\build-portable.ps1
   ```
   This script includes automatic validation.

3. **Check logs:**
   When starting the API, check the console output for:
   - ✅ "Static UI: ENABLED"
   - ✅ "SPA fallback: ACTIVE"
   - ❌ "wwwroot directory not found"

4. **Test immediately after building:**
   Don't wait to test - verify the app loads right after building.

## Summary of Solutions

| Symptom | Likely Cause | Solution |
|---------|--------------|----------|
| Complete white screen | No wwwroot or empty wwwroot | Run with `-Fix` flag |
| White screen with init guard message | JavaScript failed to load | Check Network tab, run diagnostics |
| Console error: Unexpected token '<' | JS files contain HTML | Check server routing, rebuild |
| 404 errors for .js files | Files missing from wwwroot | Re-publish or `-Fix` |
| Works in dev, not in production | Build configuration issue | Check vite.config.ts, rebuild |
| Intermittent white screen | Browser cache or service worker | Clear cache, disable SW |

## Exit Codes

- `0` - Diagnostics completed successfully
- `1` - Build failed during fix process

## Requirements

- PowerShell 5.1 or later
- Node.js 20+ and npm 9+
- .NET 8 SDK
- Write access to repository directory

## License

Part of Aura Video Studio project.
