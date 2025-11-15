# Aura Video Studio - Build Guide

Complete guide for building Aura Video Studio on Windows 11 from a fresh installation.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Initial Setup](#initial-setup)
3. [Building the Application](#building-the-application)
4. [Common Issues and Solutions](#common-issues-and-solutions)
5. [Development Workflow](#development-workflow)
6. [CI/CD Integration](#cicd-integration)

---

## Prerequisites

### Required Software

1. **Windows 11 (64-bit)**
   - Build 22000 or higher
   - Long path support recommended (see setup instructions)

2. **Node.js 20.x LTS or higher**
   - Download from: https://nodejs.org/
   - Recommended version: 18.18.0 (specified in `.nvmrc`)
   - Verify: `node --version` (should show v18.x.x)
   - npm 9.0.0 or higher comes bundled

3. **.NET 8 SDK**
   - Download from: https://dot.net/download
   - Verify: `dotnet --version` (should show 8.0.x)

4. **Git for Windows**
   - Download from: https://git-scm.com/download/win
   - Verify: `git --version`

### Recommended Software

1. **Visual Studio 2022** (for C# development)
   - Community Edition or higher
   - Workloads:
     - ASP.NET and web development
     - .NET desktop development
   - Or **Visual Studio Code** with C# extension

2. **Windows Terminal** (recommended)
   - Available from Microsoft Store
   - Better PowerShell/Command Prompt experience

3. **Node Version Manager (nvm for Windows)** (optional)
   - Download from: https://github.com/coreybutler/nvm-windows
   - Allows easy switching between Node.js versions
   - Works with `.nvmrc` file: `nvm use`

---

## Initial Setup

### 1. Enable Windows Long Path Support

Long paths are required for some npm dependencies.

**Option A: Using PowerShell (Run as Administrator)**
```powershell
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" `
  -Name "LongPathsEnabled" -Value 1 -PropertyType DWORD -Force
```

**Option B: Using Registry Editor**
1. Press `Win + R`, type `regedit`, press Enter
2. Navigate to: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem`
3. Create/modify DWORD value: `LongPathsEnabled` = `1`
4. Restart your computer

### 2. Configure Git

```bash
# Enable long paths in Git
git config --global core.longpaths true

# Configure line endings (Windows)
git config --global core.autocrlf true

# Set your identity
git config --global user.name "Your Name"
git config --global user.email "your.email@example.com"
```

### 3. Clone the Repository

```bash
# Clone the repository
git clone https://github.com/Saiyan9001/aura-video-studio.git
cd aura-video-studio
```

### 4. Install Frontend Dependencies

```bash
cd Aura.Web
npm install
```

This will:
- Install all Node.js dependencies
- Set up Husky git hooks (pre-commit validation)
- Verify your environment meets requirements

**Expected output:**
```
✓ Node.js version meets requirements
✓ npm version meets requirements
✓ Git long paths enabled
added 806 packages, and audited 807 packages
found 0 vulnerabilities
```

### 5. Restore .NET Dependencies

```bash
# From repository root
cd ..
dotnet restore Aura.sln
```

---

## Building the Application

### Frontend Build

```bash
cd Aura.Web

# Development build (faster, includes source maps)
npm run build:dev

# Production build (optimized, minified)
npm run build:prod

# Or simply (runs production build by default)
npm run build
```

**Build process:**
1. Validates environment (Node.js version, etc.)
2. Runs TypeScript type checking
3. Compiles and bundles with Vite
4. Verifies build output

**Expected output:**
```
=== Environment Validation ===
✓ Node.js version: 18.18.0
✓ npm version meets requirements
✓ Environment validation passed

> type-check
✓ TypeScript compilation complete

> vite build --mode production
✓ Built in 15.2s

=== Build Output Validation ===
✓ Frontend dist directory exists
✓ index.html exists
✓ Assets directory exists
✓ No source files in dist
✓ No node_modules in dist
Total files: 42
Total size: 2.45 MB
✓ Build verification passed
```

**Output location:** `Aura.Web/dist/`

### Backend Build

```bash
# From repository root

# Build in Debug mode (default)
dotnet build Aura.sln

# Build in Release mode (optimized)
dotnet build Aura.sln --configuration Release

# Build specific project only
dotnet build Aura.Api/Aura.Api.csproj --configuration Release
```

**Expected output:**
```
Microsoft (R) Build Engine version 17.0.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:45.23
```

**Output locations:**
- `Aura.Api/bin/Release/net8.0/`
- `Aura.Core/bin/Release/net8.0/`
- `Aura.Providers/bin/Release/net8.0/`

### Full Solution Build

The API project can automatically build the frontend when publishing:

```bash
# Publish with integrated frontend build
dotnet publish Aura.Api/Aura.Api.csproj --configuration Release

# This will:
# 1. Build the frontend (npm run build)
# 2. Build the .NET API
# 3. Copy frontend dist to wwwroot
# 4. Create a complete deployment package
```

**Output location:** `Aura.Api/bin/Release/net8.0/publish/`

---

## Common Issues and Solutions

### Issue: "Module not found" or dependency errors

**Symptoms:**
```
Error: Cannot find module 'vite'
```

**Solution:**
```bash
cd Aura.Web
rm -rf node_modules package-lock.json
npm install
```

### Issue: "Permission denied" on Windows

**Symptoms:**
```
EPERM: operation not permitted
```

**Possible causes:**
1. Antivirus is scanning/blocking files
2. Another process has files open
3. Insufficient permissions

**Solutions:**
1. Temporarily disable antivirus
2. Close Visual Studio, VS Code, and other editors
3. Run command prompt as Administrator
4. Ensure long paths are enabled (see Initial Setup)

### Issue: "Path too long" error

**Symptoms:**
```
Error: ENAMETOOLONG: name too long
```

**Solution:**
1. Enable long path support (see Initial Setup)
2. Clone repository closer to drive root (e.g., `C:\dev\aura-video-studio`)
3. Restart your computer after enabling long paths

### Issue: TypeScript errors after git pull

**Symptoms:**
```
error TS2307: Cannot find module 'X'
```

**Solution:**
```bash
cd Aura.Web
npm install  # Install any new dependencies
npm run typecheck  # See all errors
```

If type definitions are missing:
```bash
npm install --save-dev @types/package-name
```

### Issue: .NET build fails with CS errors

**Symptoms:**
```
error CS0246: The type or namespace name 'X' could not be found
```

**Solutions:**
1. Verify .NET SDK is installed:
   ```bash
   dotnet --version  # Should be 8.0.x
   ```

2. Clean and rebuild:
   ```bash
   dotnet clean
   dotnet restore
   dotnet build
   ```

3. Check for missing dependencies:
   ```bash
   dotnet restore --force
   ```

### Issue: Husky hooks not working

**Symptoms:**
```
.git can't be found
```

**Solution:**
Husky needs to be in the repository root, not in a subdirectory. The hooks are located in `.husky/` at the root level.

To manually test hooks:
```bash
# Test placeholder scanner
node scripts/audit/find-placeholders.js

# Should output: ✓ No placeholder markers found!
```

### Issue: Build succeeds but app doesn't run

**Frontend:**
```bash
cd Aura.Web
npm run dev  # Start development server
# Open http://localhost:5173
```

**Backend:**
```bash
cd Aura.Api
dotnet run
# API will start on http://localhost:5000
```

---

## Development Workflow

### Starting Development

**Backend (API):**
```bash
cd Aura.Api
dotnet run

# Or with hot reload:
dotnet watch run
```

**Frontend (Web UI):**
```bash
cd Aura.Web
npm run dev

# This will:
# - Start Vite dev server
# - Enable hot module replacement
# - Open browser automatically
```

### Running Tests

**Frontend tests:**
```bash
cd Aura.Web

# Run all tests
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm run test:coverage

# Run tests with UI
npm run test:ui
```

**Backend tests:**
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test Aura.Tests/Aura.Tests.csproj

# Run with detailed output
dotnet test --verbosity detailed
```

### Code Quality Checks

**Lint frontend code:**
```bash
cd Aura.Web

# Check for issues
npm run lint

# Auto-fix issues
npm run lint:fix

# Check CSS
npm run lint:css

# Fix CSS issues
npm run lint:css:fix
```

**Type check:**
```bash
cd Aura.Web
npm run typecheck
```

**Format code:**
```bash
cd Aura.Web

# Check formatting
npm run format:check

# Auto-format code
npm run format
```

**Run all quality checks:**
```bash
cd Aura.Web
npm run quality-check

# Or fix all automatically:
npm run quality-fix
```

### Pre-Commit Validation

Git hooks will automatically run on commit:

1. **Placeholder scanner** - Rejects commits with TODO/FIXME comments
2. **Commit message validator** - Ensures professional commit messages

To bypass (not recommended):
```bash
git commit --no-verify -m "message"
```

Note: CI will still enforce these checks.

---

## CI/CD Integration

### Continuous Integration

All PRs and pushes to `main` trigger automated checks:

1. **Windows Build Test**
   - Full build on Windows
   - Verifies clean installation scenario
   - Runs frontend and backend tests

2. **.NET Build Test**
   - Builds with strict warning settings
   - Ensures code quality

3. **Lint and Type Check**
   - Runs ESLint on all TypeScript/JavaScript
   - Runs TypeScript compiler
   - Checks code formatting

4. **Placeholder Scan**
   - Scans for TODO/FIXME/HACK comments
   - Fails if any found

5. **Environment Validation**
   - Verifies Node.js and npm versions
   - Checks configuration files

### PR Requirements

Before merging, all PRs must:
- ✅ Pass all 5 CI jobs
- ✅ Have no placeholder markers
- ✅ Pass all linting checks
- ✅ Pass all type checks
- ✅ Pass all unit tests
- ✅ Build successfully on Windows

### Local Pre-Push Validation

Run the same checks locally before pushing:

```bash
# From repository root

# 1. Check for placeholders
node scripts/audit/find-placeholders.js

# 2. Lint and type check frontend
cd Aura.Web
npm run quality-check
cd ..

# 3. Build everything
dotnet build Aura.sln --configuration Release
cd Aura.Web
npm run build
cd ..

# 4. Run tests
dotnet test
cd Aura.Web
npm test
cd ..
```

If all pass, you're ready to push!

---

## Additional Resources

- **Main README:** See `README.md` in repository root
- **Contributing Guide:** See `CONTRIBUTING.md` for coding standards
- **API Documentation:** Generated with Swagger at `/swagger` when API is running
- **Frontend Documentation:** Generated with TypeDoc: `npm run docs`

---

## Getting Help

If you encounter issues not covered here:

1. Check existing GitHub Issues
2. Search discussions
3. Create a new issue with:
   - Your OS version
   - Node.js version (`node --version`)
   - .NET SDK version (`dotnet --version`)
   - Complete error message
   - Steps to reproduce

---

**Last Updated:** 2025-10-28  
**Minimum Requirements:** Node.js 20.0.0, .NET 8.0, Windows 11
