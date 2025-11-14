/**
 * Aura Video Studio - Electron Main Process
 * 
 * This is the main entry point for the Electron application.
 * It orchestrates all modules and manages the application lifecycle.
 */

const { app, dialog, protocol } = require('electron');
const path = require('path');
const fs = require('fs');
const { autoUpdater } = require('electron-updater');

// Import early crash logging (MUST BE ABSOLUTE FIRST)
const EarlyCrashLogger = require('./early-crash-logger');

// Import initialization tracking
const { InitializationTracker, InitializationStep } = require('./initialization-tracker');
const SafeInit = require('./safe-initialization');

// Import startup logging and diagnostics
const StartupLogger = require('./startup-logger');
const StartupDiagnostics = require('./startup-diagnostics');

// Import application modules
const ProtocolHandler = require('./protocol-handler'); // Needed for early protocol registration
const TrayManager = require('./tray-manager');
const MenuBuilder = require('./menu-builder');
const WindowsSetupWizard = require('./windows-setup-wizard');

// Import shutdown orchestration
const ShutdownOrchestrator = require('./shutdown-orchestrator');
const ChildProcessManager = require('./child-process-manager');

// ========================================
// Global State
// ========================================

let earlyCrashLogger = null;
let initializationTracker = null;
let startupLogger = null;
let windowManager = null;
let appConfig = null;
let backendService = null;
let trayManager = null;
let menuBuilder = null;
let protocolHandler = null;
let windowsSetupWizard = null;
let shutdownOrchestrator = null;
let childProcessManager = null;

let ipcHandlers = {
  config: null,
  system: null,
  video: null,
  backend: null,
  ffmpeg: null,
  startupLogs: null
};

let isQuitting = false;
let isCleaningUp = false;
let crashCount = 0;
const MAX_CRASH_COUNT = 3;

// Track safe mode and degraded mode state
let safeMode = false;
let safeModeFeatures = [];
let degradedModeFeatures = [];

// ========================================
// Environment Configuration
// ========================================

const IS_DEV = process.argv.includes('--dev') || !app.isPackaged;
const DEBUG_STARTUP = process.argv.includes('--debug-startup');
const APP_NAME = 'Aura Video Studio';

// Disable hardware acceleration if configured (helps with some Windows issues)
if (process.env.DISABLE_HARDWARE_ACCELERATION === 'true') {
  app.disableHardwareAcceleration();
}

// ========================================
// Protocol Registration (MUST BE BEFORE app.ready)
// ========================================

// Register custom protocol scheme as privileged
// This MUST be called before the app is ready according to Electron API
protocol.registerSchemesAsPrivileged([
  {
    scheme: ProtocolHandler.getProtocolScheme(),
    privileges: {
      standard: true,
      secure: true,
      supportFetchAPI: true
    }
  }
]);

// ========================================
// Single Instance Lock
// ========================================

const gotTheLock = app.requestSingleInstanceLock();

if (!gotTheLock) {
  console.log('Another instance is already running. Exiting...');
  app.quit();
} else {
  app.on('second-instance', (event, commandLine, workingDirectory) => {
    console.log('Second instance detected, focusing main window...');
    
    // Focus main window
    if (windowManager) {
      const mainWindow = windowManager.getMainWindow();
      if (mainWindow) {
        if (mainWindow.isMinimized()) mainWindow.restore();
        mainWindow.focus();
      }
    }

    // Handle protocol URL if present
    const protocolUrl = commandLine.find(arg => arg.startsWith('aura://'));
    if (protocolUrl && protocolHandler) {
      protocolHandler.handleProtocolUrl(protocolUrl);
    }
  });
}

// ========================================
// Error Handling & Logging
// ========================================

/**
 * Setup error handling
 */
function setupErrorHandling() {
  if (initializationTracker) {
    initializationTracker.startStep(InitializationStep.ERROR_HANDLING);
  }
  
  if (startupLogger) {
    startupLogger.stepStart('error-handling', 'ErrorHandling', 'Setting up error handlers');
  }
  
  // Handle uncaught exceptions
  process.on('uncaughtException', (error) => {
    console.error('Uncaught exception:', error);
    
    crashCount++;
    
    // Log to early crash logger
    if (earlyCrashLogger) {
      earlyCrashLogger.logUncaughtException(error);
    }
    
    // Log error with structured logging
    if (startupLogger) {
      startupLogger.error('UncaughtException', 'Uncaught exception occurred', error, {
        crashCount,
        maxCrashCount: MAX_CRASH_COUNT
      });
    } else {
      logError('UncaughtException', error);
    }
    
    // Show error dialog with specific recovery actions
    if (crashCount < MAX_CRASH_COUNT) {
      dialog.showErrorBox(
        'Application Error',
        `An unexpected error occurred:\n\n${error.message}\n\n` +
        `Recovery Actions:\n` +
        `1. Check if you have enough disk space\n` +
        `2. Verify your antivirus isn't blocking the application\n` +
        `3. Try restarting the application\n\n` +
        `The application will attempt to continue, but you may need to restart.\n` +
        `If this problem persists, please report it.\n\n` +
        `Error logs: ${path.join(app.getPath('userData'), 'logs')}`
      );
    } else {
      dialog.showErrorBox(
        'Critical Error',
        `Multiple critical errors have occurred (${crashCount}/${MAX_CRASH_COUNT}).\n\n` +
        `The application will now close to prevent data corruption.\n\n` +
        `Recovery Actions:\n` +
        `1. Restart your computer\n` +
        `2. Check for system updates\n` +
        `3. Reinstall the application if the problem persists\n\n` +
        `Error logs: ${path.join(app.getPath('userData'), 'logs')}\n\n` +
        `Please report this issue on GitHub with the log files.`
      );
      app.quit();
    }
  });

  // Handle unhandled promise rejections
  process.on('unhandledRejection', (reason, promise) => {
    console.error('Unhandled rejection at:', promise, 'reason:', reason);
    
    // Log to early crash logger
    if (earlyCrashLogger) {
      earlyCrashLogger.logUnhandledRejection(reason);
    }
    
    // Log error
    if (startupLogger) {
      startupLogger.error('UnhandledRejection', 'Unhandled promise rejection', reason instanceof Error ? reason : new Error(String(reason)));
    } else {
      logError('UnhandledRejection', reason);
    }
  });

  // Handle process warnings
  process.on('warning', (warning) => {
    console.warn('Process warning:', warning);
    if (startupLogger) {
      startupLogger.warn('ProcessWarning', warning.message, {
        name: warning.name,
        stack: warning.stack
      });
    }
  });
  
  if (initializationTracker) {
    initializationTracker.succeedStep(InitializationStep.ERROR_HANDLING);
  }
  
  if (startupLogger) {
    startupLogger.stepEnd('error-handling', true);
  }
}

/**
 * Log error to file
 */
function logError(type, error) {
  try {
    const logsDir = path.join(app.getPath('userData'), 'logs');
    if (!fs.existsSync(logsDir)) {
      fs.mkdirSync(logsDir, { recursive: true });
    }

    const logFile = path.join(logsDir, `crash-${Date.now()}.log`);
    const errorInfo = {
      type,
      timestamp: new Date().toISOString(),
      message: error.message || String(error),
      stack: error.stack || 'No stack trace available',
      platform: process.platform,
      arch: process.arch,
      electronVersion: process.versions.electron,
      nodeVersion: process.versions.node,
      appVersion: app.getVersion()
    };

    fs.writeFileSync(logFile, JSON.stringify(errorInfo, null, 2));
    console.log('Error logged to:', logFile);
  } catch (logError) {
    console.error('Failed to log error:', logError);
  }
}

// ========================================
// Auto Updater Configuration
// ========================================

/**
 * Setup auto-updater
 */
function setupAutoUpdater() {
  if (startupLogger) {
    startupLogger.stepStart('auto-updater', 'AutoUpdater', 'Configuring auto-updater');
  }
  
  if (IS_DEV) {
    console.log('Auto-updater disabled in development mode');
    if (startupLogger) {
      startupLogger.info('AutoUpdater', 'Auto-updater disabled in development mode');
      startupLogger.stepEnd('auto-updater', true);
    }
    return;
  }

  // Configure auto-updater
  autoUpdater.autoDownload = false;
  autoUpdater.autoInstallOnAppQuit = true;

  // Update available
  autoUpdater.on('update-available', (info) => {
    console.log('Update available:', info.version);
    if (startupLogger) {
      startupLogger.info('AutoUpdater', 'Update available', { version: info.version });
    }

    const mainWindow = windowManager.getMainWindow();
    dialog.showMessageBox(mainWindow, {
      type: 'info',
      title: 'Update Available',
      message: `A new version (${info.version}) is available!`,
      detail: 'Would you like to download it now? The update will be installed when you restart the application.',
      buttons: ['Download', 'Later'],
      defaultId: 0,
      cancelId: 1
    }).then((result) => {
      if (result.response === 0) {
        autoUpdater.downloadUpdate();
      }
    });
  });

  // Update not available
  autoUpdater.on('update-not-available', () => {
    console.log('Application is up to date');
    if (startupLogger) {
      startupLogger.info('AutoUpdater', 'Application is up to date');
    }
  });

  // Download progress
  autoUpdater.on('download-progress', (progressObj) => {
    const percent = Math.round(progressObj.percent);
    console.log(`Download progress: ${percent}%`);

    const mainWindow = windowManager.getMainWindow();
    if (mainWindow) {
      mainWindow.setTitle(`${APP_NAME} - Downloading update: ${percent}%`);
    }
  });

  // Update downloaded
  autoUpdater.on('update-downloaded', (info) => {
    console.log('Update downloaded:', info.version);
    if (startupLogger) {
      startupLogger.info('AutoUpdater', 'Update downloaded', { version: info.version });
    }

    const mainWindow = windowManager.getMainWindow();
    if (mainWindow) {
      mainWindow.setTitle(APP_NAME);
    }

    dialog.showMessageBox(mainWindow, {
      type: 'info',
      title: 'Update Ready',
      message: `Version ${info.version} has been downloaded.`,
      detail: 'The update will be installed when you restart Aura Video Studio. Would you like to restart now?',
      buttons: ['Restart Now', 'Later'],
      defaultId: 0,
      cancelId: 1
    }).then((result) => {
      if (result.response === 0) {
        isQuitting = true;
        autoUpdater.quitAndInstall();
      }
    });
  });

  // Update error
  autoUpdater.on('error', (error) => {
    console.error('Auto-updater error:', error);
    if (startupLogger) {
      startupLogger.error('AutoUpdater', 'Auto-updater error', error);
    }
  });

  // Check for updates on startup (if enabled)
  const autoUpdate = appConfig ? appConfig.get('autoUpdate', true) : true;
  if (autoUpdate) {
    setTimeout(() => {
      autoUpdater.checkForUpdates().catch(err => {
        console.error('Update check failed:', err);
        if (startupLogger) {
          startupLogger.error('AutoUpdater', 'Update check failed', err);
        }
      });
    }, 5000);
  }
  
  if (startupLogger) {
    startupLogger.stepEnd('auto-updater', true, { autoUpdate });
  }
}

// ========================================
// IPC Handlers Registration
// ========================================

/**
 * Register all IPC handlers
 */
// ========================================
// First Run Check
// ========================================

/**
 * Check if this is the first run and run Windows-specific setup if needed
 */
async function checkFirstRun() {
  if (startupLogger) {
    startupLogger.stepStart('first-run-check', 'FirstRun', 'Checking first run status');
  }
  
  const setupComplete = appConfig.isSetupComplete();
  const firstRun = appConfig.isFirstRun();

  if (startupLogger) {
    startupLogger.debug('FirstRun', 'First run check', { setupComplete, firstRun });
  }

  // Run Windows-specific setup wizard on first run (Windows only)
  if ((firstRun || !setupComplete) && process.platform === 'win32') {
    console.log('First run detected on Windows, running Windows setup wizard...');
    if (startupLogger) {
      startupLogger.info('FirstRun', 'Running Windows setup wizard');
    }
    
    // Initialize Windows setup wizard
    windowsSetupWizard = new WindowsSetupWizard(app, windowManager, appConfig);
    
    // Check if Windows setup has been completed
    if (!windowsSetupWizard.isSetupComplete()) {
      try {
        const setupResult = await windowsSetupWizard.runSetup();
        
        if (!setupResult.success) {
          console.warn('Windows setup wizard encountered issues:', setupResult);
          
          if (startupLogger) {
            startupLogger.warn('FirstRun', 'Windows setup encountered issues', {
              errors: setupResult.results?.errors || []
            });
          }
          
          // Show warning to user if there were critical errors
          if (setupResult.results && setupResult.results.errors.length > 0) {
            const mainWindow = windowManager.getMainWindow();
            if (mainWindow && !mainWindow.isDestroyed()) {
              dialog.showMessageBox(mainWindow, {
                type: 'warning',
                title: 'Setup Incomplete',
                message: 'Some setup steps could not be completed',
                detail: 
                  'Aura Video Studio has started, but some setup steps encountered issues:\n\n' +
                  setupResult.results.errors.join('\n') +
                  '\n\nThe application may not function correctly. Please check the setup guide.',
                buttons: ['Continue Anyway']
              });
            }
          }
        } else {
          console.log('✓ Windows setup wizard completed successfully');
          if (startupLogger) {
            startupLogger.info('FirstRun', 'Windows setup completed successfully');
          }
        }
      } catch (error) {
        console.error('Windows setup wizard error:', error);
        if (startupLogger) {
          startupLogger.error('FirstRun', 'Windows setup wizard error', error);
        }
      }
    } else {
      // Run quick compatibility check on subsequent launches
      try {
        const quickCheck = await windowsSetupWizard.quickCheck();
        if (!quickCheck.compatible) {
          console.warn('Windows compatibility issues detected:', quickCheck.issues);
          
          if (startupLogger) {
            startupLogger.warn('FirstRun', 'Compatibility issues detected', {
              issues: quickCheck.issues
            });
          }
          
          // Show warning for critical issues
          const criticalIssues = quickCheck.issues.filter(i => i.type === 'error');
          if (criticalIssues.length > 0) {
            const mainWindow = windowManager.getMainWindow();
            if (mainWindow && !mainWindow.isDestroyed()) {
              dialog.showMessageBox(mainWindow, {
                type: 'warning',
                title: 'Compatibility Issues',
                message: 'System compatibility issues detected',
                detail: criticalIssues.map(i => `• ${i.message}\n  Action: ${i.action}`).join('\n\n'),
                buttons: ['OK']
              });
            }
          }
        }
      } catch (error) {
        console.error('Quick compatibility check error:', error);
        if (startupLogger) {
          startupLogger.error('FirstRun', 'Quick compatibility check error', error);
        }
      }
    }
  }

  // Navigate to setup wizard in UI if not complete
  if (!setupComplete || firstRun) {
    console.log('First run detected, navigating to setup wizard...');
    
    if (startupLogger) {
      startupLogger.info('FirstRun', 'Navigating to setup wizard in UI');
    }
    
    const mainWindow = windowManager.getMainWindow();
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.webContents.executeJavaScript(`
        if (window.location.hash !== '#/setup') {
          window.location.hash = '#/setup';
        }
      `);
    }
  }
  
  if (startupLogger) {
    startupLogger.stepEnd('first-run-check', true, { setupComplete, firstRun });
  }
}

// ========================================
// Cleanup
// ========================================

/**
 * Cleanup before quit using ShutdownOrchestrator
 */
async function cleanup() {
  console.log('Cleaning up application resources...');
  
  if (startupLogger) {
    startupLogger.info('Cleanup', 'Starting application cleanup');
  }

  try {
    if (shutdownOrchestrator) {
      // Use shutdown orchestrator for coordinated cleanup
      await shutdownOrchestrator.shutdown({
        backendService,
        ipcHandlers,
        trayManager,
        childProcessManager
      });
    } else {
      // Fallback to legacy cleanup if orchestrator not initialized
      await legacyCleanup();
    }

    console.log('Cleanup completed');
    if (startupLogger) {
      startupLogger.info('Cleanup', 'Application cleanup completed successfully');
    }
  } catch (error) {
    console.error('Error during cleanup:', error);
    if (startupLogger) {
      startupLogger.error('Cleanup', 'Error during cleanup', error instanceof Error ? error : new Error(String(error)));
    }
  }
}

/**
 * Legacy cleanup (fallback when orchestrator unavailable)
 */
async function legacyCleanup() {
  // Stop backend health checks
  if (ipcHandlers.backend) {
    ipcHandlers.backend.stopHealthChecks();
    if (startupLogger) {
      startupLogger.debug('Cleanup', 'Backend health checks stopped');
    }
  }

  // Stop backend service
  if (backendService) {
    console.log('Stopping backend service...');
    if (startupLogger) {
      startupLogger.info('Cleanup', 'Stopping backend service');
    }
    await backendService.stop();
    console.log('Backend service stopped');
    if (startupLogger) {
      startupLogger.info('Cleanup', 'Backend service stopped successfully');
    }
  }

  // Destroy tray
  if (trayManager) {
    trayManager.destroy();
    if (startupLogger) {
      startupLogger.debug('Cleanup', 'System tray destroyed');
    }
  }

  // Cleanup temp files
  const tempPath = path.join(app.getPath('temp'), 'aura-video-studio');
  if (fs.existsSync(tempPath)) {
    try {
      fs.rmSync(tempPath, { recursive: true, force: true });
      console.log('Temp files cleaned up');
      if (startupLogger) {
        startupLogger.debug('Cleanup', 'Temp files cleaned up', { tempPath });
      }
    } catch (error) {
      console.warn('Failed to cleanup temp files:', error.message);
      if (startupLogger) {
        startupLogger.warn('Cleanup', 'Failed to cleanup temp files', { 
          error: error.message,
          tempPath 
        });
      }
    }
  }
}

// ========================================
// Application Lifecycle
// ========================================

/**
 * Main application startup with comprehensive error handling
 */
async function startApplication() {
  let splashWindow = null;
  
  try {
    console.log('='.repeat(60));
    console.log(`${APP_NAME} Starting...`);
    console.log('='.repeat(60));
    console.log('Version:', app.getVersion());
    console.log('Platform:', process.platform);
    console.log('Architecture:', process.arch);
    console.log('Development Mode:', IS_DEV);
    console.log('Debug Startup:', DEBUG_STARTUP);
    console.log('Node:', process.versions.node);
    console.log('Electron:', process.versions.electron);
    console.log('Chrome:', process.versions.chrome);
    console.log('User Data:', app.getPath('userData'));
    console.log('='.repeat(60));

    // Step 1: Initialize early crash logger (ABSOLUTE FIRST)
    earlyCrashLogger = new EarlyCrashLogger(app);
    earlyCrashLogger.installGlobalHandlers();
    console.log('✓ Early crash logger initialized');

    // Step 2: Initialize initialization tracker
    initializationTracker = new InitializationTracker(app);
    initializationTracker.startStep(InitializationStep.EARLY_CRASH_LOGGING);
    initializationTracker.succeedStep(InitializationStep.EARLY_CRASH_LOGGING, {
      crashLogPath: earlyCrashLogger.getCrashLogPath()
    });
    console.log('✓ Initialization tracker initialized');

    // Step 3: Initialize startup logger
    initializationTracker.startStep(InitializationStep.STARTUP_LOGGER);
    startupLogger = new StartupLogger(app, { debugMode: DEBUG_STARTUP });
    startupLogger.info('Main', 'Application startup initiated', {
      version: app.getVersion(),
      platform: process.platform,
      arch: process.arch,
      isDev: IS_DEV,
      debugStartup: DEBUG_STARTUP,
      userDataPath: app.getPath('userData')
    });
    initializationTracker.succeedStep(InitializationStep.STARTUP_LOGGER, {
      logFile: startupLogger.getLogFile()
    });
    console.log('✓ Startup logger initialized');

    // Step 4: Setup error handling
    setupErrorHandling();
    console.log('✓ Error handling configured');

    // Step 5: Run startup diagnostics
    initializationTracker.startStep(InitializationStep.DIAGNOSTICS);
    const diagnostics = new StartupDiagnostics(app, startupLogger);
    const diagnosticsResults = await diagnostics.runDiagnostics();
    
    if (!diagnosticsResults.healthy) {
      startupLogger.warn('StartupDiagnostics', 'System health check detected issues', {
        errors: diagnosticsResults.errors.length,
        warnings: diagnosticsResults.warnings.length
      });
      initializationTracker.succeedStep(InitializationStep.DIAGNOSTICS, {
        healthy: false,
        issues: diagnosticsResults.errors.length + diagnosticsResults.warnings.length
      });
    } else {
      initializationTracker.succeedStep(InitializationStep.DIAGNOSTICS, { healthy: true });
    }
    console.log('✓ Startup diagnostics completed');

    // Step 6: Initialize app config with error handling
    const configResult = SafeInit.initializeAppConfig(app, initializationTracker, startupLogger, earlyCrashLogger);
    
    if (!configResult.success) {
      throw new Error(`Critical: App configuration failed to initialize: ${configResult.error.message}`);
    }
    
    appConfig = configResult.component;
    
    if (configResult.degradedMode) {
      degradedModeFeatures.push('Configuration (using in-memory defaults)');
      console.log('⚠ App configuration running in degraded mode');
      
      // Show warning to user
      setTimeout(() => {
        dialog.showMessageBox({
          type: 'warning',
          title: 'Configuration Warning',
          message: 'Running with Default Configuration',
          detail: 'Failed to load saved configuration. Using default settings.\n\n' +
                  'Your settings will not be saved during this session.\n\n' +
                  configResult.recoveryAction,
          buttons: ['OK']
        });
      }, 2000);
    }
    console.log('✓ App configuration initialized');

    // Step 6a: Check if we should enter safe mode
    safeMode = appConfig.shouldEnterSafeMode(MAX_CRASH_COUNT);
    if (safeMode) {
      console.log('⚠ SAFE MODE ACTIVATED');
      console.log(`  Crash count: ${appConfig.getCrashCount()}/${MAX_CRASH_COUNT}`);
      
      if (startupLogger) {
        startupLogger.warn('SafeMode', 'Application starting in safe mode', {
          crashCount: appConfig.getCrashCount(),
          maxCrashes: MAX_CRASH_COUNT,
          lastCrashTime: appConfig.getLastCrashTime()
        });
      }
      
      appConfig.enableSafeMode();
    } else {
      appConfig.disableSafeMode();
    }

    // Step 6b: Initialize child process manager
    childProcessManager = new ChildProcessManager(startupLogger);
    console.log('✓ Child process manager initialized');

    // Step 6c: Initialize shutdown orchestrator
    shutdownOrchestrator = new ShutdownOrchestrator(startupLogger);
    console.log('✓ Shutdown orchestrator initialized');

    // Step 7: Initialize window manager
    const windowResult = SafeInit.initializeWindowManager(app, IS_DEV, initializationTracker, startupLogger, earlyCrashLogger);
    
    if (!windowResult.success) {
      throw new Error(`Critical: Window manager failed to initialize: ${windowResult.error.message}`);
    }
    
    windowManager = windowResult.component;
    console.log('✓ Window manager initialized');

    // Step 8: Show splash screen
    initializationTracker.startStep(InitializationStep.SPLASH_SCREEN);
    try {
      windowManager.createSplashWindow();
      splashWindow = windowManager.getSplashWindow();
      console.log('✓ Splash screen displayed');
      initializationTracker.succeedStep(InitializationStep.SPLASH_SCREEN);
    } catch (error) {
      console.log('⚠ Splash screen failed to create (non-critical)');
      initializationTracker.skipStep(InitializationStep.SPLASH_SCREEN, 'Failed to create splash window');
    }

    // Step 9: Initialize protocol handler (skip in safe mode)
    if (safeMode) {
      console.log('⚠ Skipping protocol handler (safe mode)');
      safeModeFeatures.push('Protocol handling (deep linking disabled)');
      initializationTracker.skipStep(InitializationStep.PROTOCOL_HANDLER, 'Disabled in safe mode');
      protocolHandler = null;
    } else {
      const protocolResult = SafeInit.initializeProtocolHandler(windowManager, initializationTracker, startupLogger, earlyCrashLogger);
      
      protocolHandler = protocolResult.component;
      
      if (protocolResult.degradedMode) {
        degradedModeFeatures.push('Protocol handling (deep linking disabled)');
        console.log('⚠ Protocol handler running in degraded mode');
      } else {
        console.log('✓ Protocol handler registered');
      }
    }

    // Step 10: Start backend service
    const backendResult = await SafeInit.initializeBackendService(app, IS_DEV, initializationTracker, startupLogger, earlyCrashLogger, childProcessManager);
    
    if (!backendResult.success) {
      // Critical failure - show detailed error and exit
      const errorMessage = [
        'The Aura Video Studio backend service failed to start.',
        '',
        'Technical Details:',
        backendResult.technicalDetails || backendResult.error.message,
        '',
        'Recovery Actions:',
        backendResult.recoveryAction || 'Unknown',
        '',
        `Logs: ${path.join(app.getPath('userData'), 'logs')}`
      ].join('\n');
      
      dialog.showErrorBox('Backend Service Error', errorMessage);
      throw new Error(`Critical: Backend service failed to start: ${backendResult.error.message}`);
    }
    
    backendService = backendResult.component;
    console.log('✓ Backend service started on port:', backendService.getPort());

    // Step 11: Register IPC handlers with individual tracking
    const ipcResult = SafeInit.initializeIpcHandlers(
      app, windowManager, appConfig, backendService, startupLogger,
      initializationTracker, startupLogger, earlyCrashLogger
    );
    
    if (!ipcResult.success && ipcResult.criticalFailure) {
      const errorMessage = [
        'Critical IPC handlers failed to initialize.',
        '',
        'Failed Handlers:',
        ...ipcResult.details.failed.map(h => `  - ${h.name}: ${h.error}`),
        '',
        'Recovery Action:',
        ipcResult.recoveryAction || 'Restart the application',
        '',
        `Logs: ${path.join(app.getPath('userData'), 'logs')}`
      ].join('\n');
      
      dialog.showErrorBox('IPC Handler Error', errorMessage);
      throw new Error('Critical: IPC handlers failed to initialize');
    }
    
    ipcHandlers = ipcResult.component;
    
    if (ipcResult.degradedMode) {
      degradedModeFeatures.push(`IPC handlers (${ipcResult.details.failed.length} handlers failed)`);
      console.log('⚠ Some IPC handlers failed to initialize');
    } else {
      console.log('✓ IPC handlers registered');
    }

    // Step 12: Create main window
    initializationTracker.startStep(InitializationStep.MAIN_WINDOW);
    try {
      const preloadPath = path.join(__dirname, 'preload.js');
      windowManager.createMainWindow(backendService.getPort(), preloadPath);
      const mainWindow = windowManager.getMainWindow();
      console.log('✓ Main window created');
      initializationTracker.succeedStep(InitializationStep.MAIN_WINDOW, { 
        port: backendService.getPort(),
        preloadPath 
      });

      // Handle window close event
      mainWindow.on('close', (event) => {
        const prevented = windowManager.handleWindowClose(
          event,
          isQuitting,
          appConfig.get('minimizeToTray', true)
        );
        
        if (prevented && !isQuitting) {
          if (trayManager && process.platform === 'win32') {
            trayManager.showNotification(
              APP_NAME,
              'Application is still running in the system tray'
            );
          }
        }
      });
    } catch (error) {
      initializationTracker.failStep(InitializationStep.MAIN_WINDOW, error);
      throw new Error(`Critical: Failed to create main window: ${error.message}`);
    }

    // Step 13: Create system tray (skip in safe mode)
    if (safeMode) {
      console.log('⚠ Skipping system tray (safe mode)');
      safeModeFeatures.push('System tray (minimize to tray disabled)');
      initializationTracker.skipStep(InitializationStep.SYSTEM_TRAY, 'Disabled in safe mode');
      trayManager = null;
    } else {
      initializationTracker.startStep(InitializationStep.SYSTEM_TRAY);
      try {
        trayManager = new TrayManager(app, windowManager, IS_DEV);
        const trayCreated = trayManager.create();
        if (trayCreated) {
          trayManager.setBackendUrl(backendService.getUrl());
          console.log('✓ System tray created');
          initializationTracker.succeedStep(InitializationStep.SYSTEM_TRAY);
        } else {
          console.log('⚠ System tray not created (icon not found, but app will continue)');
          initializationTracker.skipStep(InitializationStep.SYSTEM_TRAY, 'Icon not found');
        }
      } catch (error) {
        console.log('⚠ System tray creation failed (non-critical)');
        initializationTracker.skipStep(InitializationStep.SYSTEM_TRAY, error.message);
      }
    }

    // Step 14: Build application menu
    initializationTracker.startStep(InitializationStep.APP_MENU);
    try {
      menuBuilder = new MenuBuilder(app, windowManager, appConfig, IS_DEV);
      menuBuilder.buildMenu();
      console.log('✓ Application menu created');
      initializationTracker.succeedStep(InitializationStep.APP_MENU);
    } catch (error) {
      console.log('⚠ Application menu creation failed');
      initializationTracker.failStep(InitializationStep.APP_MENU, error, 'Some menu items may not work');
      degradedModeFeatures.push('Application menu (may have missing items)');
    }

    // Step 15: Setup auto-updater (skip in safe mode)
    if (safeMode) {
      console.log('⚠ Skipping auto-updater (safe mode)');
      safeModeFeatures.push('Auto-updater (manual updates only)');
      initializationTracker.skipStep(InitializationStep.AUTO_UPDATER, 'Disabled in safe mode');
    } else {
      initializationTracker.startStep(InitializationStep.AUTO_UPDATER);
      try {
        setupAutoUpdater();
        console.log('✓ Auto-updater configured');
        initializationTracker.succeedStep(InitializationStep.AUTO_UPDATER);
      } catch (error) {
        console.log('⚠ Auto-updater setup failed (non-critical)');
        initializationTracker.skipStep(InitializationStep.AUTO_UPDATER, error.message);
        degradedModeFeatures.push('Auto-updater (manual updates only)');
      }
    }

    // Step 16: Check for first run (async)
    const mainWindow = windowManager.getMainWindow();
    mainWindow.once('ready-to-show', async () => {
      initializationTracker.startStep(InitializationStep.FIRST_RUN_CHECK);
      
      try {
        await checkFirstRun();
        initializationTracker.succeedStep(InitializationStep.FIRST_RUN_CHECK);
      } catch (error) {
        console.error('First run check error:', error);
        initializationTracker.failStep(InitializationStep.FIRST_RUN_CHECK, error, 'Setup wizard may not work correctly');
        
        if (startupLogger) {
          startupLogger.error('FirstRun', 'First run check error', error);
        }
      }
      
      if (protocolHandler) {
        protocolHandler.checkPendingUrl();
      }
      
      // Close splash screen only after ALL critical steps complete or explicit failure
      if (initializationTracker.allCriticalStepsSucceeded()) {
        if (splashWindow && !splashWindow.isDestroyed()) {
          setTimeout(() => {
            splashWindow.close();
          }, 1000);
        }
        
        // Log successful startup
        if (earlyCrashLogger) {
          earlyCrashLogger.logStartupComplete();
        }
      } else {
        // Keep splash screen open and show error
        const criticalFailures = initializationTracker.getCriticalFailures();
        const errorMessage = [
          'Application startup encountered critical errors:',
          '',
          ...criticalFailures.map(f => `• ${f.step}: ${f.errorMessage}`),
          '',
          'The application may not function correctly.'
        ].join('\n');
        
        dialog.showErrorBox('Startup Errors', errorMessage);
        
        if (splashWindow && !splashWindow.isDestroyed()) {
          splashWindow.close();
        }
      }
      
      // Show degraded mode warning if applicable
      if (degradedModeFeatures.length > 0 && !safeMode) {
        setTimeout(() => {
          dialog.showMessageBox(mainWindow, {
            type: 'warning',
            title: 'Running in Degraded Mode',
            message: 'Some features are unavailable',
            detail: 'The following features could not be initialized:\n\n' +
                    degradedModeFeatures.map(f => `• ${f}`).join('\n') +
                    '\n\nThe application will continue with reduced functionality.',
            buttons: ['OK']
          });
        }, 2000);
      }
      
      // Show safe mode warning if applicable
      if (safeMode) {
        setTimeout(() => {
          dialog.showMessageBox(mainWindow, {
            type: 'warning',
            title: 'Safe Mode Active',
            message: 'Application started in Safe Mode',
            detail: `The application has been started in safe mode due to ${appConfig.getCrashCount()} recent crashes.\n\n` +
                    'Disabled features:\n' +
                    safeModeFeatures.map(f => `• ${f}`).join('\n') +
                    '\n\nYou can use the Diagnostics panel to identify and fix issues, or reset configuration to defaults.\n\n' +
                    'Once issues are resolved, restart the application to exit safe mode.',
            buttons: ['OK']
          });
        }, 2000);
        
        // Send safe mode status to frontend
        mainWindow.webContents.send('app:safeMode', {
          enabled: true,
          crashCount: appConfig.getCrashCount(),
          disabledFeatures: safeModeFeatures
        });
      }
      
      // Finalize startup logging
      if (startupLogger) {
        const summary = startupLogger.finalize();
        
        // Keep DevTools open in debug startup mode
        if (DEBUG_STARTUP && mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.openDevTools();
          startupLogger.info('Main', 'DevTools opened (debug-startup mode)');
        }
        
        // Log summary to console
        console.log('='.repeat(60));
        console.log('STARTUP SUMMARY');
        console.log('='.repeat(60));
        console.log(`Status: ${summary.success ? 'SUCCESS' : 'COMPLETED WITH ERRORS'}`);
        console.log(`Duration: ${summary.totalDurationSeconds}s`);
        console.log(`Steps: ${summary.statistics.successfulSteps}/${summary.statistics.totalSteps} successful`);
        console.log(`Log File: ${startupLogger.getLogFile()}`);
        console.log(`Summary: ${startupLogger.getSummaryFile()}`);
        console.log('='.repeat(60));
      }
      
      // Write initialization summary
      if (initializationTracker) {
        const summaryFile = initializationTracker.writeSummary();
        if (summaryFile) {
          console.log('Initialization summary:', summaryFile);
        }
      }
    });

    console.log('='.repeat(60));
    console.log(`${APP_NAME} Started Successfully!`);
    console.log('='.repeat(60));

  } catch (error) {
    console.error('Startup failed:', error);
    
    // Log to early crash logger
    if (earlyCrashLogger) {
      earlyCrashLogger.logCrash('STARTUP_FAILURE', 'Application startup failed', error, {
        fatal: true
      });
    }
    
    // Log to startup logger if available
    if (startupLogger) {
      startupLogger.error('Main', 'Application startup failed', error, {
        fatal: true
      });
      startupLogger.finalize();
    }
    
    // Write initialization summary even on failure
    if (initializationTracker) {
      initializationTracker.writeSummary();
    }

    // Close splash if it exists
    if (splashWindow && !splashWindow.isDestroyed()) {
      splashWindow.close();
    } else if (windowManager) {
      const splash = windowManager.getSplashWindow();
      if (splash && !splash.isDestroyed()) {
        splash.close();
      }
    }

    const logsDir = earlyCrashLogger 
      ? earlyCrashLogger.getLogsDirectory() 
      : (startupLogger 
        ? startupLogger.getLogsDirectory() 
        : path.join(app.getPath('userData'), 'logs'));

    // Show detailed error with recovery actions
    const errorDetails = [
      `Failed to start ${APP_NAME}`,
      '',
      'Error:',
      error.message,
      '',
      'Recovery Actions:',
      '1. Restart your computer',
      '2. Check if antivirus is blocking the application',
      '3. Ensure .NET 8 runtime is installed',
      '4. Try reinstalling the application',
      '5. Check disk space and permissions',
      '',
      `Error logs: ${logsDir}`,
      '',
      'Please report this issue on GitHub with the log files.'
    ].join('\n');

    dialog.showErrorBox('Startup Error', errorDetails);

    app.quit();
  }
}

// ========================================
// App Event Handlers
// ========================================

// Setup error handling as early as possible
setupErrorHandling();

// App is ready
app.whenReady().then(startApplication);

// Activate (macOS)
app.on('activate', () => {
  // On macOS, re-create window when dock icon is clicked
  if (!windowManager || !windowManager.getMainWindow()) {
    if (!isQuitting) {
      startApplication();
    }
  } else {
    windowManager.showMainWindow();
  }
});

// All windows closed
app.on('window-all-closed', () => {
  // On macOS, apps stay active until user quits explicitly
  if (process.platform !== 'darwin') {
    isQuitting = true;
    app.quit();
  }
});

// Before quit
app.on('before-quit', async (event) => {
  console.log('Application is quitting...');
  
  // Prevent multiple cleanup calls
  if (isCleaningUp) {
    return;
  }
  
  isQuitting = true;
  isCleaningUp = true;
  
  // Prevent immediate quit to allow async cleanup
  event.preventDefault();
  
  try {
    // Perform cleanup with timeout
    await Promise.race([
      cleanup(),
      new Promise((_, reject) => 
        setTimeout(() => reject(new Error('Cleanup timeout')), 30000)
      )
    ]);
  } catch (error) {
    console.error('Cleanup error or timeout:', error);
  } finally {
    // Now actually quit
    app.exit(0);
  }
});

// Will quit
app.on('will-quit', () => {
  console.log('Application will quit');
});

// Quit
app.on('quit', (event, exitCode) => {
  console.log(`Application quit with exit code: ${exitCode}`);
});

// GPU process crashed (attempt recovery)
app.on('gpu-process-crashed', (event, killed) => {
  console.error('GPU process crashed, killed:', killed);
  
  if (!killed) {
    console.log('Attempting to recover from GPU process crash...');
    // Electron will attempt to restart the GPU process automatically
  }
});

// Renderer process crashed (attempt recovery)
app.on('render-process-gone', (event, webContents, details) => {
  console.error('Renderer process gone:', details);
  
  if (details.reason !== 'clean-exit') {
    const mainWindow = windowManager ? windowManager.getMainWindow() : null;
    
    if (mainWindow && !mainWindow.isDestroyed()) {
      dialog.showMessageBox(mainWindow, {
        type: 'error',
        title: 'Renderer Process Crashed',
        message: 'The application interface has crashed.',
        detail: `Reason: ${details.reason}\n\nWould you like to reload the application?`,
        buttons: ['Reload', 'Quit'],
        defaultId: 0,
        cancelId: 1
      }).then((result) => {
        if (result.response === 0) {
          mainWindow.reload();
        } else {
          app.quit();
        }
      });
    }
  }
});

// Child process gone (backend)
app.on('child-process-gone', (event, details) => {
  console.error('Child process gone:', details);
  
  if (details.type === 'Utility' && details.reason !== 'clean-exit') {
    console.warn('A utility process has crashed, but the app should continue functioning');
  }
});

// Backend crash handler
app.on('backend-crash', () => {
  console.error('Backend has crashed after max restart attempts');
  
  const mainWindow = windowManager ? windowManager.getMainWindow() : null;
  if (mainWindow && !mainWindow.isDestroyed()) {
    dialog.showMessageBox(mainWindow, {
      type: 'error',
      title: 'Backend Service Error',
      message: 'The Aura backend service has stopped unexpectedly and could not be restarted.',
      detail: 'The application needs to close. Please check the logs and try restarting.\n\n' +
              `Logs: ${path.join(app.getPath('userData'), 'logs')}`,
      buttons: ['Close Application'],
      defaultId: 0
    }).then(() => {
      isQuitting = true;
      app.quit();
    });
  } else {
    isQuitting = true;
    app.quit();
  }
});

// ========================================
// Exports (for testing)
// ========================================

module.exports = {
  app,
  windowManager,
  appConfig,
  backendService,
  trayManager,
  menuBuilder,
  protocolHandler,
  startupLogger
};
