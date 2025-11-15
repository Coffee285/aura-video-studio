#!/usr/bin/env node
/**
 * Validation script for Electron Desktop configuration
 * Ensures proper electron entry point and module structure
 */

const fs = require('fs');
const path = require('path');

const rootDir = path.join(__dirname, '..');
const packageJsonPath = path.join(rootDir, 'package.json');
const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));

console.log('Validating Electron Desktop configuration...\n');

let hasErrors = false;
let hasWarnings = false;

// Check package.json main field
console.log('='.repeat(60));
console.log('Checking package.json configuration...');
console.log('='.repeat(60));

const expectedMain = 'electron/main.js';
if (packageJson.main !== expectedMain) {
  console.error(`❌ ERROR: package.json "main" field is incorrect`);
  console.error(`   Expected: "${expectedMain}"`);
  console.error(`   Actual: "${packageJson.main}"`);
  hasErrors = true;
} else {
  console.log(`✅ package.json "main" field is correct: "${packageJson.main}"`);
}

// Check that required scripts exist
const requiredScripts = {
  'start': 'electron .',
  'dev': 'electron . --dev'
};

for (const [scriptName, expectedCommand] of Object.entries(requiredScripts)) {
  if (!packageJson.scripts[scriptName]) {
    console.error(`❌ ERROR: Script "${scriptName}" is missing`);
    hasErrors = true;
  } else if (packageJson.scripts[scriptName] !== expectedCommand) {
    console.warn(`⚠️  WARNING: Script "${scriptName}" command differs from expected`);
    console.warn(`   Expected: "${expectedCommand}"`);
    console.warn(`   Actual: "${packageJson.scripts[scriptName]}"`);
    hasWarnings = true;
  } else {
    console.log(`✅ Script "${scriptName}" is correctly configured`);
  }
}

// Check electron entry point file exists
console.log('\n' + '='.repeat(60));
console.log('Checking Electron entry point...');
console.log('='.repeat(60));

const mainFilePath = path.join(rootDir, packageJson.main);
if (!fs.existsSync(mainFilePath)) {
  console.error(`❌ ERROR: Main entry point does not exist: ${mainFilePath}`);
  hasErrors = true;
} else {
  console.log(`✅ Main entry point exists: ${packageJson.main}`);
  
  // Check for syntax errors using Node's syntax checker
  const { execSync } = require('child_process');
  try {
    execSync(`node -c "${mainFilePath}"`, { stdio: 'pipe' });
    console.log('✅ Main entry point has valid syntax');
  } catch (error) {
    console.error(`❌ ERROR: Main entry point has syntax errors: ${error.message}`);
    hasErrors = true;
  }
}

// Check required electron modules exist
console.log('\n' + '='.repeat(60));
console.log('Checking Electron modules...');
console.log('='.repeat(60));

const requiredModules = [
  'electron/window-manager.js',
  'electron/app-config.js',
  'electron/backend-service.js',
  'electron/tray-manager.js',
  'electron/menu-builder.js',
  'electron/protocol-handler.js',
  'electron/windows-setup-wizard.js',
  'electron/preload.js',
  'electron/types.d.ts'
];

const requiredIpcHandlers = [
  'electron/ipc-handlers/config-handler.js',
  'electron/ipc-handlers/system-handler.js',
  'electron/ipc-handlers/video-handler.js',
  'electron/ipc-handlers/backend-handler.js',
  'electron/ipc-handlers/ffmpeg-handler.js'
];

let moduleCount = 0;
for (const modulePath of requiredModules) {
  const fullPath = path.join(rootDir, modulePath);
  if (!fs.existsSync(fullPath)) {
    console.error(`❌ ERROR: Required module missing: ${modulePath}`);
    hasErrors = true;
  } else {
    moduleCount++;
  }
}

let handlerCount = 0;
for (const handlerPath of requiredIpcHandlers) {
  const fullPath = path.join(rootDir, handlerPath);
  if (!fs.existsSync(fullPath)) {
    console.error(`❌ ERROR: Required IPC handler missing: ${handlerPath}`);
    hasErrors = true;
  } else {
    handlerCount++;
  }
}

console.log(`✅ Found ${moduleCount}/${requiredModules.length} required modules`);
console.log(`✅ Found ${handlerCount}/${requiredIpcHandlers.length} required IPC handlers`);

// Check preload.js compatibility
console.log('\n' + '='.repeat(60));
console.log('Checking preload.js configuration...');
console.log('='.repeat(60));

const rootPreloadPath = path.join(rootDir, 'preload.js');
if (fs.existsSync(rootPreloadPath)) {
  const rootPreloadContent = fs.readFileSync(rootPreloadPath, 'utf8');
  if (rootPreloadContent.includes("require('./electron/preload')")) {
    console.log('✅ Root preload.js correctly redirects to electron/preload.js');
  } else {
    console.warn('⚠️  WARNING: Root preload.js exists but may not redirect properly');
    hasWarnings = true;
  }
} else {
  console.warn('⚠️  WARNING: Root preload.js not found (not critical if using electron/preload.js directly)');
  hasWarnings = true;
}

// Check if legacy electron.js exists
console.log('\n' + '='.repeat(60));
console.log('Checking for legacy files...');
console.log('='.repeat(60));

const legacyElectronJs = path.join(rootDir, 'electron.js');
if (fs.existsSync(legacyElectronJs)) {
  console.warn('⚠️  WARNING: Legacy electron.js file found');
  console.warn('   This file is not used by the current configuration');
  console.warn('   It can be safely removed if no longer needed for reference');
  hasWarnings = true;
} else {
  console.log('✅ No legacy electron.js file found');
}

// Check for proper initialization
console.log('\n' + '='.repeat(60));
console.log('Checking app initialization...');
console.log('='.repeat(60));

const mainContent = fs.readFileSync(mainFilePath, 'utf8');

const requiredInitChecks = [
  { pattern: /app\.whenReady\(\)/, name: 'app.whenReady() handler' },
  { pattern: /(WindowManager|initializeWindowManager)/, name: 'Window manager initialization' },
  { pattern: /(BackendService|initializeBackendService)/, name: 'Backend service initialization' },
  { pattern: /(createMainWindow|createWindow)/, name: 'Window creation' },
  { pattern: /(backendService|backend.*start|initializeBackendService)/, name: 'Backend service start' },
  { pattern: /(registerIpcHandlers|ipc.*handler|IpcHandler)/, name: 'IPC handler registration' }
];

for (const check of requiredInitChecks) {
  if (check.pattern.test(mainContent)) {
    console.log(`✅ ${check.name} found`);
  } else {
    console.error(`❌ ERROR: Missing ${check.name}`);
    hasErrors = true;
  }
}

// Check dependencies
console.log('\n' + '='.repeat(60));
console.log('Checking npm dependencies...');
console.log('='.repeat(60));

const requiredDependencies = [
  'electron-updater',
  'electron-store',
  'axios'
];

const requiredDevDependencies = [
  'electron',
  'electron-builder'
];

for (const dep of requiredDependencies) {
  if (packageJson.dependencies && packageJson.dependencies[dep]) {
    console.log(`✅ Dependency "${dep}" found: ${packageJson.dependencies[dep]}`);
  } else {
    console.error(`❌ ERROR: Missing dependency: ${dep}`);
    hasErrors = true;
  }
}

for (const dep of requiredDevDependencies) {
  if (packageJson.devDependencies && packageJson.devDependencies[dep]) {
    console.log(`✅ Dev dependency "${dep}" found: ${packageJson.devDependencies[dep]}`);
  } else {
    console.error(`❌ ERROR: Missing dev dependency: ${dep}`);
    hasErrors = true;
  }
}

// Final summary
console.log('\n' + '='.repeat(60));
console.log('Validation Summary');
console.log('='.repeat(60));

if (!hasErrors && !hasWarnings) {
  console.log('✅ All validation checks passed!');
  console.log('\nElectron Desktop configuration is correct and ready to use.');
  console.log('\nTo start the application:');
  console.log('  Development mode: npm run dev');
  console.log('  Production mode:  npm start');
  process.exit(0);
} else if (!hasErrors && hasWarnings) {
  console.log('✅ Validation passed with warnings');
  console.log('⚠️  Please review the warnings above');
  console.log('\nConfiguration is functional but could be improved.');
  process.exit(0);
} else {
  console.log('❌ Validation failed!');
  console.log('\nPlease fix the errors listed above before running the application.');
  process.exit(1);
}
