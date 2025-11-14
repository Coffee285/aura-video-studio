/**
 * Safe Initialization Wrappers
 * Provides safe initialization wrappers with detailed error handling
 * and degraded mode fallbacks for each component
 */

const { InitializationStep } = require('./initialization-tracker');

/**
 * Initialize AppConfig with degraded mode fallback
 */
function initializeAppConfig(app, tracker, logger, crashLogger) {
  const step = InitializationStep.APP_CONFIG;
  tracker.startStep(step);

  try {
    const AppConfig = require('./app-config');
    const appConfig = new AppConfig(app);
    
    // Verify config can be read
    const testRead = appConfig.get('setupComplete', false);
    
    tracker.succeedStep(step, {
      setupComplete: testRead,
      configPath: appConfig.store.path
    });
    
    if (logger) {
      logger.info('AppConfig', 'Configuration initialized successfully', {
        configPath: appConfig.store.path
      });
    }
    
    return {
      success: true,
      component: appConfig,
      degradedMode: false,
      error: null
    };
  } catch (error) {
    tracker.failStep(step, error, 'Application will attempt to run with default configuration');
    
    if (crashLogger) {
      crashLogger.logInitializationFailure(step, error);
    }
    
    if (logger) {
      logger.error('AppConfig', 'Failed to initialize configuration, using defaults', error);
    }
    
    // Create minimal fallback config
    try {
      const MockAppConfig = createMockAppConfig(app);
      const mockConfig = new MockAppConfig(app);
      
      tracker.succeedStep(InitializationStep.APP_CONFIG, {
        degradedMode: true,
        usingFallback: true
      });
      
      return {
        success: true,
        component: mockConfig,
        degradedMode: true,
        error: error,
        recoveryAction: 'Using in-memory configuration with default values'
      };
    } catch (fallbackError) {
      // Critical failure - cannot proceed without config
      return {
        success: false,
        component: null,
        degradedMode: false,
        error: error,
        criticalFailure: true,
        recoveryAction: 'Application cannot start without configuration storage'
      };
    }
  }
}

/**
 * Initialize WindowManager with error handling
 */
function initializeWindowManager(app, isDev, tracker, logger, crashLogger) {
  const step = InitializationStep.WINDOW_MANAGER;
  tracker.startStep(step);

  try {
    const WindowManager = require('./window-manager');
    const windowManager = new WindowManager(app, isDev);
    
    tracker.succeedStep(step);
    
    if (logger) {
      logger.info('WindowManager', 'Window manager initialized successfully');
    }
    
    return {
      success: true,
      component: windowManager,
      degradedMode: false,
      error: null
    };
  } catch (error) {
    tracker.failStep(step, error, 'Application cannot continue without window manager');
    
    if (crashLogger) {
      crashLogger.logInitializationFailure(step, error);
    }
    
    if (logger) {
      logger.error('WindowManager', 'Failed to initialize window manager', error);
    }
    
    // Critical failure - cannot proceed without windows
    return {
      success: false,
      component: null,
      degradedMode: false,
      error: error,
      criticalFailure: true,
      recoveryAction: 'Check Electron installation and display drivers'
    };
  }
}

/**
 * Initialize ProtocolHandler with fallback
 */
function initializeProtocolHandler(windowManager, tracker, logger, crashLogger) {
  const step = InitializationStep.PROTOCOL_HANDLER;
  tracker.startStep(step);

  try {
    const ProtocolHandler = require('./protocol-handler');
    const protocolHandler = new ProtocolHandler(windowManager);
    protocolHandler.register();
    
    tracker.succeedStep(step, {
      scheme: ProtocolHandler.getProtocolScheme()
    });
    
    if (logger) {
      logger.info('ProtocolHandler', 'Protocol handler registered successfully', {
        scheme: ProtocolHandler.getProtocolScheme()
      });
    }
    
    return {
      success: true,
      component: protocolHandler,
      degradedMode: false,
      error: null
    };
  } catch (error) {
    tracker.failStep(step, error, 'Deep linking will not work, but application can continue');
    
    if (crashLogger) {
      crashLogger.logInitializationFailure(step, error);
    }
    
    if (logger) {
      logger.error('ProtocolHandler', 'Failed to initialize protocol handler', error);
    }
    
    // Not critical - app can work without protocol handling
    return {
      success: true,
      component: null,
      degradedMode: true,
      error: error,
      recoveryAction: 'Deep linking disabled - open files manually from the application'
    };
  }
}

/**
 * Initialize BackendService with detailed error reporting
 */
async function initializeBackendService(app, isDev, tracker, logger, crashLogger, childProcessManager = null) {
  const step = InitializationStep.BACKEND_SERVICE;
  tracker.startStep(step);

  try {
    const BackendService = require('./backend-service');
    const backendService = new BackendService(app, isDev, childProcessManager);
    
    // Start backend with timeout
    const startPromise = backendService.start();
    const timeoutPromise = new Promise((_, reject) => 
      setTimeout(() => reject(new Error('Backend startup timeout (60s)')), 60000)
    );
    
    await Promise.race([startPromise, timeoutPromise]);
    
    // Verify backend is responding
    const port = backendService.getPort();
    const url = backendService.getUrl();
    
    tracker.succeedStep(step, {
      port,
      url,
      pid: backendService.pid
    });
    
    if (logger) {
      logger.info('BackendService', 'Backend service started successfully', {
        port,
        url,
        pid: backendService.pid
      });
    }
    
    return {
      success: true,
      component: backendService,
      degradedMode: false,
      error: null
    };
  } catch (error) {
    tracker.failStep(step, error, 'Application cannot function without backend service');
    
    if (crashLogger) {
      crashLogger.logInitializationFailure(step, error, {
        possibleCauses: [
          'Backend executable not found',
          'Port already in use',
          '.NET runtime not installed',
          'Backend crashed on startup'
        ]
      });
    }
    
    if (logger) {
      logger.error('BackendService', 'Failed to start backend service', error);
    }
    
    // Critical failure - cannot proceed without backend
    return {
      success: false,
      component: null,
      degradedMode: false,
      error: error,
      criticalFailure: true,
      recoveryAction: 'Check that .NET 8 runtime is installed and backend executable exists',
      technicalDetails: error.message
    };
  }
}

/**
 * Initialize IPC Handlers with individual handler tracking
 */
function initializeIpcHandlers(app, windowManager, appConfig, backendService, startupLogger, tracker, logger, crashLogger) {
  const step = InitializationStep.IPC_HANDLERS;
  tracker.startStep(step);

  const handlers = {
    config: null,
    system: null,
    video: null,
    backend: null,
    ffmpeg: null,
    startupLogs: null,
    diagnostics: null
  };

  const failedHandlers = [];
  const succeededHandlers = [];

  try {
    // Config handler
    try {
      const ConfigHandler = require('./ipc-handlers/config-handler');
      handlers.config = new ConfigHandler(appConfig);
      handlers.config.register();
      succeededHandlers.push('config');
    } catch (error) {
      failedHandlers.push({ name: 'config', error: error.message });
      if (logger) logger.warn('IPC', 'Config handler failed to initialize', { error: error.message });
    }

    // System handler
    try {
      const SystemHandler = require('./ipc-handlers/system-handler');
      handlers.system = new SystemHandler(app, windowManager, appConfig);
      handlers.system.register();
      succeededHandlers.push('system');
    } catch (error) {
      failedHandlers.push({ name: 'system', error: error.message });
      if (logger) logger.warn('IPC', 'System handler failed to initialize', { error: error.message });
    }

    // Video handler
    try {
      const VideoHandler = require('./ipc-handlers/video-handler');
      const backendUrl = backendService.getUrl();
      handlers.video = new VideoHandler(backendUrl);
      handlers.video.register();
      succeededHandlers.push('video');
    } catch (error) {
      failedHandlers.push({ name: 'video', error: error.message });
      if (logger) logger.warn('IPC', 'Video handler failed to initialize', { error: error.message });
    }

    // Backend handler
    try {
      const BackendHandler = require('./ipc-handlers/backend-handler');
      const backendUrl = backendService.getUrl();
      handlers.backend = new BackendHandler(backendUrl, backendService);
      handlers.backend.register();
      succeededHandlers.push('backend');
      
      // Start health checks
      const mainWindow = windowManager.getMainWindow();
      if (mainWindow) {
        handlers.backend.startHealthChecks(mainWindow);
      }
    } catch (error) {
      failedHandlers.push({ name: 'backend', error: error.message });
      if (logger) logger.warn('IPC', 'Backend handler failed to initialize', { error: error.message });
    }

    // FFmpeg handler
    try {
      const FFmpegHandler = require('./ipc-handlers/ffmpeg-handler');
      handlers.ffmpeg = new FFmpegHandler(app, windowManager);
      handlers.ffmpeg.register();
      succeededHandlers.push('ffmpeg');
    } catch (error) {
      failedHandlers.push({ name: 'ffmpeg', error: error.message });
      if (logger) logger.warn('IPC', 'FFmpeg handler failed to initialize', { error: error.message });
    }

    // Startup logs handler
    try {
      const StartupLogsHandler = require('./ipc-handlers/startup-logs-handler');
      handlers.startupLogs = new StartupLogsHandler(app, startupLogger);
      handlers.startupLogs.register();
      succeededHandlers.push('startupLogs');
    } catch (error) {
      failedHandlers.push({ name: 'startupLogs', error: error.message });
      if (logger) logger.warn('IPC', 'Startup logs handler failed to initialize', { error: error.message });
    }

    // Diagnostics handler
    try {
      const DiagnosticsHandler = require('./ipc-handlers/diagnostics-handler');
      const backendUrl = backendService.getUrl();
      handlers.diagnostics = new DiagnosticsHandler(app, backendUrl, windowManager);
      handlers.diagnostics.register();
      succeededHandlers.push('diagnostics');
    } catch (error) {
      failedHandlers.push({ name: 'diagnostics', error: error.message });
      if (logger) logger.warn('IPC', 'Diagnostics handler failed to initialize', { error: error.message });
    }

    // Determine if this is a critical failure
    const criticalHandlers = ['backend', 'system'];
    const criticalFailures = failedHandlers.filter(h => criticalHandlers.includes(h.name));

    if (criticalFailures.length > 0) {
      const error = new Error(`Critical IPC handlers failed: ${criticalFailures.map(h => h.name).join(', ')}`);
      tracker.failStep(step, error, 'Some IPC communication will not work');
      
      return {
        success: false,
        component: handlers,
        degradedMode: false,
        error: error,
        criticalFailure: true,
        recoveryAction: 'Restart the application',
        details: {
          succeeded: succeededHandlers,
          failed: failedHandlers
        }
      };
    }

    if (failedHandlers.length > 0) {
      tracker.succeedStep(step, {
        succeededHandlers,
        failedHandlers,
        degradedMode: true
      });
      
      if (logger) {
        logger.warn('IPC', 'Some IPC handlers failed to initialize', {
          succeeded: succeededHandlers,
          failed: failedHandlers
        });
      }
      
      return {
        success: true,
        component: handlers,
        degradedMode: true,
        error: null,
        recoveryAction: 'Some features may not work. Check logs for details.',
        details: {
          succeeded: succeededHandlers,
          failed: failedHandlers
        }
      };
    }

    tracker.succeedStep(step, {
      handlersRegistered: succeededHandlers.length
    });
    
    if (logger) {
      logger.info('IPC', 'All IPC handlers registered successfully', {
        handlersRegistered: succeededHandlers
      });
    }
    
    return {
      success: true,
      component: handlers,
      degradedMode: false,
      error: null
    };
  } catch (error) {
    tracker.failStep(step, error, 'IPC communication may not work properly');
    
    if (crashLogger) {
      crashLogger.logInitializationFailure(step, error);
    }
    
    if (logger) {
      logger.error('IPC', 'Failed to initialize IPC handlers', error);
    }
    
    return {
      success: false,
      component: handlers,
      degradedMode: false,
      error: error,
      criticalFailure: true,
      recoveryAction: 'Restart the application'
    };
  }
}

/**
 * Create a mock AppConfig for degraded mode
 */
function createMockAppConfig(app) {
  return class MockAppConfig {
    constructor(app) {
      this.app = app;
      this._storage = new Map();
      this._defaults = {
        setupComplete: false,
        firstRun: true,
        language: 'en',
        theme: 'dark',
        autoUpdate: false,
        telemetry: false,
        crashReporting: false,
        minimizeToTray: true,
        startMinimized: false,
        hardwareAcceleration: true
      };
    }

    get(key, defaultValue) {
      if (this._storage.has(key)) {
        return this._storage.get(key);
      }
      return this._defaults[key] !== undefined ? this._defaults[key] : defaultValue;
    }

    set(key, value) {
      this._storage.set(key, value);
    }

    getAll() {
      return { ...this._defaults, ...Object.fromEntries(this._storage) };
    }

    reset() {
      this._storage.clear();
    }

    isFirstRun() {
      return this.get('firstRun', true);
    }

    isSetupComplete() {
      return this.get('setupComplete', false);
    }

    markSetupComplete() {
      this.set('setupComplete', true);
      this.set('firstRun', false);
    }

    getSecure() {
      return null;
    }

    setSecure() {
      // No-op in degraded mode
    }

    deleteSecure() {
      // No-op in degraded mode
    }

    getPaths() {
      return {
        userData: this.app.getPath('userData'),
        temp: this.app.getPath('temp'),
        downloads: this.app.getPath('downloads'),
        documents: this.app.getPath('documents')
      };
    }
  };
}

module.exports = {
  initializeAppConfig,
  initializeWindowManager,
  initializeProtocolHandler,
  initializeBackendService,
  initializeIpcHandlers
};
