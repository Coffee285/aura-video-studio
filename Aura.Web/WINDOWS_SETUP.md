# Windows 11 Setup Guide for Aura.Web

This guide provides Windows 11 specific instructions for setting up and running the Aura Video Studio frontend.

## Prerequisites

### Required Software

1. **Node.js 20.x or later (LTS recommended)**
   - Download from: https://nodejs.org/
   - Verify installation:
     ```powershell
     node --version
     npm --version
     ```

2. **Git for Windows**
   - Download from: https://git-scm.com/download/win
   - Verify installation:
     ```powershell
     git --version
     ```

3. **Visual Studio Code (Recommended)**
   - Download from: https://code.visualstudio.com/
   - Install recommended extensions when prompted

### Optional Tools

- **Windows Terminal** - Better command line experience
- **PowerShell 7+** - Modern PowerShell version

## Installation

### 1. Clone the Repository

```powershell
# Using PowerShell or Command Prompt
cd C:\Projects  # Or your preferred directory
git clone https://github.com/Coffee285/aura-video-studio.git
cd aura-video-studio\Aura.Web
```

### 2. Install Dependencies

```powershell
# Install all npm packages
npm install

# If you encounter issues, try:
npm cache clean --force
npm install
```

**Note:** On Windows, npm install may take longer due to:
- Antivirus scanning of node_modules
- Windows Defender real-time protection
- File system performance

**Tip:** Temporarily exclude `node_modules` from antivirus scanning to speed up installation.

### 3. Configure Environment

```powershell
# Copy example environment file
copy .env.example .env

# Edit .env with your preferred editor
notepad .env
# Or use VS Code:
code .env
```

Update the API URL if your backend is running on a different port:
```
VITE_API_BASE_URL=http://localhost:5272
```

## Running the Application

### Development Server

```powershell
# Start development server (opens browser automatically)
npm run dev
```

The application will be available at: http://localhost:5173

### Common Issues and Solutions

#### Issue: Port 5173 already in use

**Solution:**
```powershell
# Find process using port 5173
netstat -ano | findstr :5173

# Kill the process (replace PID with actual process ID)
taskkill /F /PID <PID>

# Or modify vite.config.ts to use a different port
```

#### Issue: npm install fails with EACCES error

**Solution:**
```powershell
# Run as administrator or fix npm permissions
npm config set prefix "%APPDATA%\npm"
```

#### Issue: Antivirus blocks npm or node

**Solution:**
1. Add exclusions in Windows Security:
   - `C:\Program Files\nodejs`
   - `%APPDATA%\npm`
   - Your project's `node_modules` folder

#### Issue: Scripts fail with "execution policy" error

**Solution:**
```powershell
# Set execution policy for current user
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

#### Issue: ENOENT error or path too long

**Solution:**
```powershell
# Enable long paths in Windows
# Run as Administrator in PowerShell:
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" `
  -Name "LongPathsEnabled" -Value 1 -PropertyType DWORD -Force

# Or install dependencies closer to root:
cd C:\aura
git clone https://github.com/Coffee285/aura-video-studio.git
```

## Building for Production

```powershell
# Type check
npm run type-check

# Build optimized bundle
npm run build

# Preview production build
npm run preview
```

Build output will be in the `dist` folder.

## Path Handling

### Important Notes

1. **Use forward slashes in import statements:**
   ```typescript
   // Correct
   import { Component } from './components/Component';
   
   // Avoid (even though Windows uses backslashes)
   import { Component } from '.\components\Component';
   ```

2. **Path aliases work with forward slashes:**
   ```typescript
   import { Component } from '@/components/Component';
   ```

3. **Vite handles path resolution automatically** - no need to worry about OS-specific paths in code

## Command Line Options

### PowerShell

```powershell
# Run dev server
npm run dev

# Run with custom port
$env:PORT=3000; npm run dev

# Run build
npm run build

# Check types
npm run type-check

# Lint code
npm run lint

# Format code
npm run format
```

### Command Prompt (cmd)

```cmd
REM Run dev server
npm run dev

REM Run with custom port (not directly supported, modify vite.config.ts)
npm run dev

REM Run build
npm run build
```

## Performance Optimization

### 1. Disable Windows Defender for node_modules

Add exclusion:
1. Open Windows Security
2. Go to Virus & threat protection
3. Manage settings
4. Add or remove exclusions
5. Add folder: `C:\path\to\project\node_modules`

### 2. Use SSD

Place project on SSD drive for better performance:
- Faster npm install
- Faster build times
- Better dev server performance

### 3. Close Unnecessary Programs

During development:
- Close other browsers
- Close heavy applications
- Keep Task Manager open to monitor resource usage

## VS Code Integration

### Recommended Settings

The project includes `.vscode/settings.json` with optimized settings for Windows:

- Auto-save enabled
- Format on save with Prettier
- ESLint auto-fix on save
- Correct line endings (LF)

### Integrated Terminal

Use VS Code's integrated terminal for better experience:
1. Open Terminal: `` Ctrl+` ``
2. Select PowerShell or Command Prompt
3. All npm commands work the same

## Troubleshooting

### Check System Requirements

```powershell
# Node version (should be 20+)
node --version

# npm version (should be 9+)
npm --version

# Check available memory
systeminfo | findstr /C:"Available Physical Memory"

# Check disk space
Get-PSDrive C | Select-Object Used,Free
```

### Clear Caches

```powershell
# Clear npm cache
npm cache clean --force

# Clear Vite cache
Remove-Item -Recurse -Force node_modules\.vite

# Remove and reinstall dependencies
Remove-Item -Recurse -Force node_modules
Remove-Item package-lock.json
npm install
```

### View Logs

```powershell
# npm logs location
dir $env:APPDATA\npm-cache\_logs

# View latest log
Get-Content (Get-ChildItem $env:APPDATA\npm-cache\_logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
```

## Network Configuration

If running backend on a different machine:

1. Update `.env`:
   ```
   VITE_API_BASE_URL=http://192.168.1.100:5272
   ```

2. Allow Node.js through Windows Firewall:
   - Windows Security → Firewall & network protection
   - Allow an app through firewall
   - Add Node.js if not listed

## Testing

```powershell
# Run unit tests
npm test

# Run with watch mode
npm run test:watch

# Run with UI
npm run test:ui

# Run E2E tests with Playwright
npm run playwright

# Install Playwright browsers (first time only)
npm run playwright:install
```

## Additional Resources

- [Node.js Windows Guidelines](https://nodejs.org/en/docs/guides/working-with-different-filesystems/)
- [npm Windows Troubleshooting](https://docs.npmjs.com/troubleshooting/common-errors)
- [Vite Windows Support](https://vitejs.dev/guide/troubleshooting.html)

## Support

If you encounter issues not covered in this guide:

1. Check the main README.md
2. Review GitHub Issues
3. Check Windows Event Viewer for system errors
4. Use Task Manager to identify resource bottlenecks
