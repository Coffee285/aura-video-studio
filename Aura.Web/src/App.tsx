import {
  Button,
  Card,
  FluentProvider,
  Spinner,
  Title1,
  Body1,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { createContext, useContext, useEffect, useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { queryClient } from './api/queryClient';
import { AppRouterContent } from './components/AppRouterContent';
import { CrashRecoveryScreen } from './components/ErrorBoundary';
import type { InitializationError } from './components/Initialization';
import { InitializationScreen, StartupErrorScreen } from './components/Initialization';
import { SplashScreen } from './components/SplashScreen/SplashScreen';
import { env } from './config/env';
import { ROUTE_METADATA_ENHANCED } from './config/routesWithGuards';
import { AccessibilityProvider } from './contexts/AccessibilityContext';
import { useGlobalUndoShortcuts } from './hooks/useGlobalUndoShortcuts';
import { useWindowsNativeUI } from './hooks/useWindowsNativeUI';
import { FirstRunWizard } from './pages/Onboarding/FirstRunWizard';
import { resetCircuitBreaker } from './services/api/apiClient';
import { PersistentCircuitBreaker } from './services/api/circuitBreakerPersistence';
import { setupApi } from './services/api/setupApi';
import { crashRecoveryService } from './services/crashRecoveryService';
import { registerCustomEventHandlers } from './services/customEventHandlers';
import { errorReportingService } from './services/errorReportingService';
import {
  hasCompletedFirstRun,
  markFirstRunCompleted,
  migrateLegacyFirstRunStatus,
  clearFirstRunCache,
} from './services/firstRunService';
import { healthMonitorService } from './services/healthMonitorService';
import { keyboardShortcutManager } from './services/keyboardShortcutManager';
import { loggingService } from './services/loggingService';
import { navigationService } from './services/navigationService';
import { initializeRouteRegistry } from './services/routeRegistry';
import { migrateSettingsIfNeeded } from './services/settingsValidationService';
import { ActivityProvider } from './state/activityContext';
import { useJobState } from './state/jobState';
import { useEnvironmentStore } from './stores/environmentStore';
import { getAuraTheme } from './themes/auraTheme';

interface ThemeContextType {
  isDarkMode: boolean;
  toggleTheme: () => void;
}

// eslint-disable-next-line react-refresh/only-export-components
export const ThemeContext = createContext<ThemeContextType>({
  isDarkMode: false,
  toggleTheme: () => {},
});

// eslint-disable-next-line react-refresh/only-export-components
export const useTheme = () => useContext(ThemeContext);

function App() {
  // Initialize Windows native UI integration
  const windowsUI = useWindowsNativeUI();
  const hydrateEnvironment = useEnvironmentStore((state) => state.hydrate);

  useEffect(() => {
    hydrateEnvironment().catch((error) =>
      console.warn('[App] Failed to hydrate environment diagnostics:', error)
    );
  }, [hydrateEnvironment]);

  // Initialize dark mode - default to dark on first run, then use localStorage
  const [isDarkMode, setIsDarkMode] = useState(() => {
    const saved = localStorage.getItem('darkMode');
    if (saved !== null) {
      return JSON.parse(saved);
    }
    // On Windows, sync with system theme by default
    if (windowsUI.isWindows) {
      return windowsUI.systemTheme === 'dark';
    }
    // Default to dark mode for first-run users (creative app standard)
    return true;
  });

  // Initialize theme preference - default to 'aura' theme
  const [_themeName, _setThemeName] = useState(() => {
    const saved = localStorage.getItem('themeName');
    return saved || 'aura';
  });

  const [showShortcuts, setShowShortcuts] = useState(false);
  const [showShortcutsPanel, setShowShortcutsPanel] = useState(false);
  const [showShortcutsCheatSheet, setShowShortcutsCheatSheet] = useState(false);
  const [showCommandPalette, setShowCommandPalette] = useState(false);
  const toasterId = 'notifications-toaster';

  useGlobalUndoShortcuts();

  // Note: useElectronMenuEvents is now called inside AppRouterContent
  // because it requires Router context (uses useNavigate())

  const [isCheckingFirstRun, setIsCheckingFirstRun] = useState(true);
  const [shouldShowOnboarding, setShouldShowOnboarding] = useState(false);

  const [isInitializing, setIsInitializing] = useState(true);
  const [initializationError, setInitializationError] = useState<InitializationError | null>(null);
  const [initializationTimeout, setInitializationTimeout] = useState(false);
  const [showCrashRecovery, setShowCrashRecovery] = useState(false);

  const [showSplash, setShowSplash] = useState(() => {
    // Only show splash on first load or after crashes
    const hasShownSplash = sessionStorage.getItem('aura-splash-shown');
    return !hasShownSplash || showCrashRecovery;
  });

  const [_showDiagnostics, _setShowDiagnostics] = useState(false);

  // Job state for progress tracking (needed for polling logic)
  const { currentJobId, status } = useJobState();

  // Initialize crash recovery on app mount
  useEffect(() => {
    const state = crashRecoveryService.initialize();

    if (crashRecoveryService.shouldShowRecoveryScreen()) {
      setShowCrashRecovery(true);
      loggingService.warn(
        `Crash recovery triggered after ${state.consecutiveCrashes} consecutive crashes`,
        'App',
        'crashRecovery'
      );
    }

    // Mark clean shutdown on beforeunload
    const handleBeforeUnload = () => {
      crashRecoveryService.markCleanShutdown();
    };
    window.addEventListener('beforeunload', handleBeforeUnload);

    return () => {
      window.removeEventListener('beforeunload', handleBeforeUnload);
    };
  }, []);

  // Initialize route registry, navigation service, and custom event handlers on app mount
  // REQUIREMENT 6: Route registry validates all menu paths exist at app startup
  useEffect(() => {
    try {
      initializeRouteRegistry();
      navigationService.registerRoutes(ROUTE_METADATA_ENHANCED);
      registerCustomEventHandlers();
    } catch (error) {
      loggingService.error('Failed to initialize route registry', { error });
      console.error('[App] Route registry initialization failed:', error);
    }
  }, []);

  // Check first-run status on app mount
  useEffect(() => {
    // Set timeout to prevent infinite loading
    const timeoutId = setTimeout(() => {
      console.error('[App] First-run check timed out after 30s');
      setInitializationTimeout(true);
      setIsCheckingFirstRun(false);
      setIsInitializing(false);
    }, 30000); // 30 second timeout

    async function checkFirstRun() {
      try {
        // CRITICAL: Clear circuit breaker state BEFORE checking first run
        // This prevents false "service unavailable" errors from stale circuit breaker state
        PersistentCircuitBreaker.clearState();
        resetCircuitBreaker();
        console.info('[App] Circuit breaker state cleared before first-run check');

        // Migrate legacy first-run flag if needed
        migrateLegacyFirstRunStatus();

        // Migrate settings if needed (e.g., placeholder paths)
        await migrateSettingsIfNeeded();

        // Check system setup status from backend (primary source of truth)
        try {
          const systemStatus = await setupApi.getSystemStatus();
          if (!systemStatus.isComplete) {
            // Backend says setup is not complete - clear any stale localStorage flags
            localStorage.removeItem('hasCompletedFirstRun');
            localStorage.removeItem('hasSeenOnboarding');

            setShouldShowOnboarding(true);
            setIsCheckingFirstRun(false);
            return;
          } else {
            // Backend says setup IS complete - ensure localStorage is synced
            localStorage.setItem('hasCompletedFirstRun', 'true');
          }
        } catch (error) {
          console.warn(
            'Could not check system setup status, falling back to user wizard status:',
            error
          );

          // If backend check fails, fall back to localStorage but be cautious
          // If we can't reach the backend, don't force the wizard unnecessarily
          const localStatus =
            localStorage.getItem('hasCompletedFirstRun') === 'true' ||
            localStorage.getItem('hasSeenOnboarding') === 'true';

          if (!localStatus) {
            // No local completion flag and can't reach backend - assume first run
            setShouldShowOnboarding(true);
            setIsCheckingFirstRun(false);
            return;
          }
        }

        // Check if user has completed first-run wizard (secondary check)
        const completed = await hasCompletedFirstRun();
        setShouldShowOnboarding(!completed);
      } catch (error) {
        console.error('Error checking first-run status:', error);
        // On error, check localStorage as fallback - if nothing is set, assume first run
        const localStatus =
          localStorage.getItem('hasCompletedFirstRun') === 'true' ||
          localStorage.getItem('hasSeenOnboarding') === 'true';
        setShouldShowOnboarding(!localStatus);
      } finally {
        clearTimeout(timeoutId); // Clear timeout on success
        setIsCheckingFirstRun(false);
        // CRITICAL FIX: Set isInitializing to false after first-run check completes
        // This prevents the app from getting stuck on InitializationScreen
        // The InitializationScreen will be shown separately if explicitly needed
        setIsInitializing(false);
      }
    }

    checkFirstRun();

    return () => clearTimeout(timeoutId);
  }, []);

  // Global error handlers for uncaught errors and promise rejections
  useEffect(() => {
    const handleError = (event: ErrorEvent) => {
      event.preventDefault(); // Prevent default browser error handling
      loggingService.error('Uncaught error', event.error, 'window', 'error', {
        message: event.message,
        filename: event.filename,
        lineno: event.lineno,
        colno: event.colno,
      });
    };

    const handleUnhandledRejection = (event: PromiseRejectionEvent) => {
      event.preventDefault(); // Prevent default browser error handling
      const error = event.reason instanceof Error ? event.reason : new Error(String(event.reason));
      loggingService.error('Unhandled promise rejection', error, 'window', 'unhandledrejection', {
        reason: event.reason,
      });
    };

    window.addEventListener('error', handleError);
    window.addEventListener('unhandledrejection', handleUnhandledRejection);

    return () => {
      window.removeEventListener('error', handleError);
      window.removeEventListener('unhandledrejection', handleUnhandledRejection);
    };
  }, []);

  // Start health monitoring on app mount
  useEffect(() => {
    healthMonitorService.start();

    // Add listener for health warnings
    const handleHealthWarning = (warning: { message: string; suggestion?: string }) => {
      errorReportingService.warning(warning.message, warning.suggestion ?? '', { duration: 10000 });
    };

    healthMonitorService.addWarningListener(handleHealthWarning);

    return () => {
      healthMonitorService.removeWarningListener(handleHealthWarning);
      healthMonitorService.stop();
    };
  }, []);

  // Validate backend readiness on app mount
  useEffect(() => {
    const validateBackendReady = async () => {
      try {
        const response = await fetch(`${env.apiBaseUrl}/health/ready`);
        const data = await response.json();

        if (!data.ready) {
          console.error('Backend not ready:', data);
          // Show user-friendly warning
          errorReportingService.warning(
            'System Initializing',
            'Some features may not be available yet. Please wait...',
            { duration: 8000 }
          );
        }
      } catch (error: unknown) {
        console.error('Backend readiness check failed:', error);
        const errorMessage = error instanceof Error ? error.message : String(error);
        loggingService.warn(
          `Backend readiness check failed: ${errorMessage}`,
          'App',
          'backendReadiness'
        );
      }
    };

    validateBackendReady();
  }, []);

  // Apply dark mode class to document root and save preference
  useEffect(() => {
    const root = document.documentElement;
    if (isDarkMode) {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
    localStorage.setItem('darkMode', JSON.stringify(isDarkMode));
    // themeName saved in setThemeName
  }, [isDarkMode]);

  // Optionally sync theme with Windows system theme changes
  useEffect(() => {
    // Only auto-sync if user hasn't explicitly set a preference
    const hasManualPreference = localStorage.getItem('darkMode') !== null;

    if (windowsUI.isWindows && !hasManualPreference) {
      // Sync with system theme
      setIsDarkMode(windowsUI.systemTheme === 'dark');
    }
  }, [windowsUI.systemTheme, windowsUI.isWindows]);

  // Register global shortcuts
  useEffect(() => {
    // Register global shortcuts that are always available
    keyboardShortcutManager.registerMultiple([
      {
        id: 'open-settings',
        keys: 'Ctrl+,',
        description: 'Open Settings',
        context: 'global',
        handler: () => {
          navigationService.push('/settings');
        },
      },
      {
        id: 'new-project',
        keys: 'Ctrl+N',
        description: 'New Project',
        context: 'global',
        handler: () => {
          navigationService.push('/create');
        },
      },
      {
        id: 'open-project',
        keys: 'Ctrl+O',
        description: 'Open Project',
        context: 'global',
        handler: () => {
          navigationService.push('/projects');
        },
      },
      {
        id: 'open-ideation',
        keys: 'Ctrl+I',
        description: 'Open Ideation',
        context: 'global',
        handler: () => {
          navigationService.push('/ideation');
        },
      },
      {
        id: 'open-editor',
        keys: 'Ctrl+E',
        description: 'Open Video Editor',
        context: 'global',
        handler: () => {
          navigationService.push('/editor');
        },
      },
      {
        id: 'save-project',
        keys: 'Ctrl+S',
        description: 'Save Project',
        context: 'global',
        handler: (e) => {
          e.preventDefault();
          // Placeholder for save functionality
        },
      },
      {
        id: 'generate-video',
        keys: 'Ctrl+G',
        description: 'Generate Video',
        context: 'global',
        handler: () => {
          // Navigate to the create/wizard page to generate video
          const currentPath = navigationService.getCurrentPath();
          if (!currentPath.includes('/create')) {
            navigationService.push('/create');
          }
        },
      },
    ]);

    // Clean up on unmount
    return () => {
      keyboardShortcutManager.unregisterContext('global');
    };
  }, []);

  // Poll job progress when a job is active
  useEffect(() => {
    // Early exit if no active job
    if (!currentJobId || status === 'completed' || status === 'failed') {
      return;
    }

    let isActive = true; // Prevent state updates after unmount
    const pollInterval = setInterval(async () => {
      // Skip if component unmounted or job completed
      if (!isActive) return;

      try {
        const response = await fetch(`/api/jobs/${currentJobId}/progress`);
        if (response.ok && isActive) {
          const data = await response.json();

          // Update progress only if still active
          if (isActive) {
            useJobState.getState().updateProgress(data.progress, data.currentStage);

            if (data.status === 'completed') {
              useJobState.getState().setStatus('completed');
              useJobState.getState().updateProgress(100, 'Video generation complete!');
              clearInterval(pollInterval); // Stop polling on completion
            } else if (data.status === 'failed') {
              useJobState.getState().setStatus('failed');
              useJobState.getState().updateProgress(data.progress, 'Generation failed');
              clearInterval(pollInterval); // Stop polling on failure
            }
          }
        }
      } catch (error) {
        if (isActive) {
          console.error('Error polling job progress:', error);
        }
      }
    }, 1000);

    return () => {
      isActive = false;
      clearInterval(pollInterval);
    };
  }, [currentJobId, status]);

  // Global keyboard shortcut handler
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Let the keyboard shortcut manager handle registered shortcuts first
      const handled = keyboardShortcutManager.handleKeyEvent(e);
      if (handled) {
        return;
      }

      // Handle special global shortcuts not in the manager
      // Ctrl+Shift+L for log viewer
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'L') {
        e.preventDefault();
        navigationService.push('/logs');
      }
      // Ctrl+K or Cmd+K for command palette
      else if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        setShowCommandPalette(true);
      }
      // Ctrl+Shift+K for shortcuts panel (alternative)
      else if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'K') {
        e.preventDefault();
        setShowShortcutsPanel(true);
      }
      // ? key for shortcuts panel (alternative)
      else if (e.key === '?' && !e.ctrlKey && !e.metaKey && !e.altKey) {
        // Don't trigger if user is typing in an input
        if (!(e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement)) {
          e.preventDefault();
          setShowShortcutsPanel(true);
        }
      }
      // Ctrl+P or Cmd+P for quick open (alternative)
      else if ((e.ctrlKey || e.metaKey) && e.key === 'p') {
        e.preventDefault();
        setShowCommandPalette(true);
      }
      // Ctrl+/ or Cmd+/ for comprehensive shortcuts cheat sheet
      else if ((e.ctrlKey || e.metaKey) && e.key === '/') {
        e.preventDefault();
        setShowShortcutsCheatSheet(true);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  // Handle unsaved changes warning before closing window
  useEffect(() => {
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      // Check if we're on the video editor page and have unsaved changes
      // This is a simple check - in a real implementation, you'd want to check the actual state
      const currentPath = window.location.pathname;
      const isEditorPage = currentPath === '/editor' || currentPath === '/timeline';

      if (isEditorPage) {
        // Check localStorage for unsaved project data
        const autosaveData = localStorage.getItem('aura-project-autosave');
        if (autosaveData) {
          e.preventDefault();
          e.returnValue = '';
        }
      }
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, []);

  const toggleTheme = () => {
    setIsDarkMode(!isDarkMode);
  };

  // Apply theme background color to document body
  useEffect(() => {
    const themeName = localStorage.getItem('themeName') || 'aura';
    const theme =
      themeName === 'aura' ? getAuraTheme(isDarkMode) : isDarkMode ? webDarkTheme : webLightTheme;
    document.body.style.backgroundColor = theme.colorNeutralBackground1;
  }, [isDarkMode]);

  // Get current theme
  const themeName = localStorage.getItem('themeName') || 'aura';
  const currentTheme =
    themeName === 'aura' ? getAuraTheme(isDarkMode) : isDarkMode ? webDarkTheme : webLightTheme;

  // Show splash screen on first load
  if (showSplash) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <SplashScreen
            onComplete={() => {
              sessionStorage.setItem('aura-splash-shown', 'true');
              setShowSplash(false);
            }}
          />
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  // Show crash recovery screen if multiple crashes detected
  if (showCrashRecovery) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <CrashRecoveryScreen
            onRecovered={() => {
              setShowCrashRecovery(false);
            }}
          />
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  // Show loading spinner while checking first-run status
  if (isCheckingFirstRun) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <div
            style={{
              height: '100vh',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Spinner size="large" label="Loading..." />
          </div>
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  if (shouldShowOnboarding) {
    return (
      <QueryClientProvider client={queryClient}>
        <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
          <FluentProvider theme={currentTheme}>
            <MemoryRouter initialEntries={['/']}>
              <FirstRunWizard
                onComplete={async () => {
                  console.info('[App] FirstRunWizard onComplete called');
                  // Mark first run as completed
                  await markFirstRunCompleted();
                  // Clear cache to ensure fresh status check
                  clearFirstRunCache();
                  // Update state to hide onboarding and show main app
                  setShouldShowOnboarding(false);
                  console.info('[App] Transitioning to main app');
                }}
              />
            </MemoryRouter>
          </FluentProvider>
        </ThemeContext.Provider>
      </QueryClientProvider>
    );
  }

  if (isInitializing && !initializationError) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <InitializationScreen
            onComplete={() => setIsInitializing(false)}
            onError={(error) => setInitializationError(error)}
            enableSafeMode={true}
          />
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  if (initializationTimeout) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <div
            style={{
              height: '100vh',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              padding: '20px',
              backgroundColor: 'var(--colorNeutralBackground1)',
            }}
          >
            <Card style={{ maxWidth: '600px', width: '100%', padding: '32px' }}>
              <Title1 style={{ marginBottom: '16px' }}>Initialization Timeout</Title1>
              <Body1 style={{ marginBottom: '20px' }}>
                The application took too long to initialize. This may be caused by:
                <ul style={{ marginTop: '12px' }}>
                  <li>Backend server not responding</li>
                  <li>Network connectivity issues</li>
                  <li>Firewall blocking local connections</li>
                </ul>
              </Body1>
              <div style={{ display: 'flex', gap: '12px' }}>
                <Button appearance="primary" onClick={() => window.location.reload()}>
                  Retry
                </Button>
                <Button
                  onClick={() => {
                    setInitializationTimeout(false);
                    setIsInitializing(false);
                    setShouldShowOnboarding(false);
                  }}
                >
                  Continue Anyway
                </Button>
              </div>
            </Card>
          </div>
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  if (initializationError) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <StartupErrorScreen
            error={initializationError}
            onRetry={() => {
              setInitializationError(null);
              setIsInitializing(true);
            }}
            enableSafeMode={true}
            enableOfflineMode={true}
          />
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  // Get initial route from navigation service (considers safe mode and persistence)
  const initialRoute = navigationService.getInitialRoute();

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={currentTheme}>
          <AccessibilityProvider>
            <ActivityProvider>
              <div style={{ height: '100vh', display: 'flex', flexDirection: 'column' }}>
                <MemoryRouter initialEntries={[initialRoute]}>
                  <AppRouterContent
                    showShortcuts={showShortcuts}
                    showShortcutsPanel={showShortcutsPanel}
                    showShortcutsCheatSheet={showShortcutsCheatSheet}
                    showCommandPalette={showCommandPalette}
                    setShowShortcuts={setShowShortcuts}
                    setShowShortcutsPanel={setShowShortcutsPanel}
                    setShowShortcutsCheatSheet={setShowShortcutsCheatSheet}
                    setShowCommandPalette={setShowCommandPalette}
                    toasterId={toasterId}
                    showDiagnostics={_showDiagnostics}
                    setShowDiagnostics={_setShowDiagnostics}
                  />
                </MemoryRouter>
              </div>
            </ActivityProvider>
          </AccessibilityProvider>
        </FluentProvider>
        {/* React Query Devtools - only in development */}
        {env.isDevelopment && <ReactQueryDevtools initialIsOpen={false} />}
      </ThemeContext.Provider>
    </QueryClientProvider>
  );
}

export default App;
