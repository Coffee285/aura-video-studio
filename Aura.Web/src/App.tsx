import { FluentProvider, webLightTheme, webDarkTheme, Spinner } from '@fluentui/react-components';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useState, useEffect, createContext, useContext, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { queryClient } from './api/queryClient';
import { KeyboardShortcutsCheatSheet } from './components/Accessibility/KeyboardShortcutsCheatSheet';
import { CommandPalette } from './components/CommandPalette';
import { SuspenseBoundary } from './components/Common/SuspenseBoundary';
import { ConfigurationGate } from './components/ConfigurationGate';
import { ContentPlanningDashboard } from './components/contentPlanning/ContentPlanningDashboard';
import { QualityDashboard } from './components/dashboard';
import { ErrorBoundary, CrashRecoveryScreen } from './components/ErrorBoundary';
import { GlobalStatusFooter } from './components/GlobalStatusFooter';
import { InitializationScreen, StartupErrorScreen } from './components/Initialization';
import type { InitializationError } from './components/Initialization';
import { JobProgressDrawer } from './components/JobProgressDrawer';
import { KeyboardShortcutsPanel } from './components/KeyboardShortcuts/KeyboardShortcutsPanel';
import { KeyboardShortcutsModal } from './components/KeyboardShortcutsModal';
import { Layout } from './components/Layout';
import { NotificationsToaster } from './components/Notifications/Toasts';
import { PlatformDashboard } from './components/Platform';
import { JobStatusBar } from './components/StatusBar/JobStatusBar';
import { ActionHistoryPanel } from './components/UndoRedo/ActionHistoryPanel';
import { VideoCreationWizard } from './components/VideoWizard/VideoCreationWizard';
import { env } from './config/env';
import { AccessibilityProvider } from './contexts/AccessibilityContext';
import { useGlobalUndoShortcuts } from './hooks/useGlobalUndoShortcuts';
import { DashboardPage } from './pages/DashboardPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { FirstRunWizard } from './pages/Onboarding/FirstRunWizard';
import { WelcomePage } from './pages/WelcomePage';

const AdminDashboardPage = lazy(() =>
  import('./pages/Admin/AdminDashboardPage').then((m) => ({ default: m.AdminDashboardPage }))
);
const AestheticsPage = lazy(() =>
  import('./pages/Aesthetics/AestheticsPage').then((m) => ({ default: m.AestheticsPage }))
);
const AIEditingPage = lazy(() =>
  import('./pages/AIEditing/AIEditingPage').then((m) => ({ default: m.AIEditingPage }))
);
const AssetLibrary = lazy(() =>
  import('./pages/Assets/AssetLibrary').then((m) => ({ default: m.AssetLibrary }))
);
const AudienceManagementPage = lazy(() => import('./pages/Audience/AudienceManagementPage'));
const CreatePage = lazy(() =>
  import('./pages/CreatePage').then((m) => ({ default: m.CreatePage }))
);
const DownloadsPage = lazy(() =>
  import('./pages/DownloadsPage').then((m) => ({ default: m.DownloadsPage }))
);
const TimelineEditor = lazy(() =>
  import('./pages/Editor/TimelineEditor').then((m) => ({ default: m.TimelineEditor }))
);
const ExportHistoryPage = lazy(() =>
  import('./pages/Export/ExportHistoryPage').then((m) => ({ default: m.ExportHistoryPage }))
);
const ProviderHealthDashboard = lazy(() =>
  import('./pages/Health/ProviderHealthDashboard').then((m) => ({
    default: m.ProviderHealthDashboard,
  }))
);
const SystemHealthDashboard = lazy(() => import('./pages/Health/SystemHealthDashboard'));
const IdeationDashboard = lazy(() =>
  import('./pages/Ideation/IdeationDashboard').then((m) => ({ default: m.IdeationDashboard }))
);
const TrendingTopicsExplorer = lazy(() =>
  import('./pages/Ideation/TrendingTopicsExplorer').then((m) => ({
    default: m.TrendingTopicsExplorer,
  }))
);
const RunDetailsPage = lazy(() =>
  import('./pages/Jobs/RunDetailsPage').then((m) => ({ default: m.RunDetailsPage }))
);
const LearningPage = lazy(() => import('./pages/Learning/LearningPage'));
const TranslationPage = lazy(() =>
  import('./pages/Localization/TranslationPage').then((m) => ({ default: m.TranslationPage }))
);
const MLLabPage = lazy(() =>
  import('./pages/MLLab/MLLabPage').then((m) => ({ default: m.MLLabPage }))
);
const PacingAnalyzerPage = lazy(() =>
  import('./pages/PacingAnalyzerPage').then((m) => ({ default: m.PacingAnalyzerPage }))
);
const ABTestManagementPage = lazy(
  () => import('./pages/PerformanceAnalytics/ABTestManagementPage')
);
const PerformanceAnalyticsPage = lazy(
  () => import('./pages/PerformanceAnalytics/PerformanceAnalyticsPage')
);
const UsageAnalyticsPage = lazy(() =>
  import('./pages/Analytics/UsageAnalyticsPage').then((m) => ({ default: m.default }))
);
const ProjectsPage = lazy(() =>
  import('./pages/Projects/ProjectsPage').then((m) => ({ default: m.ProjectsPage }))
);
const PromptManagementPage = lazy(() =>
  import('./pages/PromptManagement/PromptManagementPage').then((m) => ({
    default: m.PromptManagementPage,
  }))
);
const QualityValidationPage = lazy(() => import('./pages/QualityValidation/QualityValidationPage'));
const RagDocumentManager = lazy(() => import('./pages/RAG/RagDocumentManager'));
const RecentJobsPage = lazy(() =>
  import('./pages/RecentJobsPage').then((m) => ({ default: m.RecentJobsPage }))
);
const RenderPage = lazy(() =>
  import('./pages/RenderPage').then((m) => ({ default: m.RenderPage }))
);
const SettingsPage = lazy(() =>
  import('./pages/SettingsPage').then((m) => ({ default: m.SettingsPage }))
);
const AccessibilitySettingsPage = lazy(() =>
  import('./pages/AccessibilitySettingsPage').then((m) => ({
    default: m.AccessibilitySettingsPage,
  }))
);
const CustomTemplatesPage = lazy(() => import('./pages/Templates/CustomTemplatesPage'));
const TemplatesLibrary = lazy(() => import('./pages/Templates/TemplatesLibrary'));
const ValidationPage = lazy(() => import('./pages/Validation/ValidationPage'));
const VerificationPage = lazy(() => import('./pages/Verification/VerificationPage'));
const VideoEditorPage = lazy(() =>
  import('./pages/VideoEditorPage').then((m) => ({ default: m.VideoEditorPage }))
);
const VoiceEnhancementPage = lazy(() => import('./pages/VoiceEnhancement/VoiceEnhancementPage'));
const CreateWizard = lazy(() =>
  import('./pages/Wizard/CreateWizard').then((m) => ({ default: m.CreateWizard }))
);
import { setupApi } from './services/api/setupApi';
import { crashRecoveryService } from './services/crashRecoveryService';
import { errorReportingService } from './services/errorReportingService';
import {
  hasCompletedFirstRun,
  migrateLegacyFirstRunStatus,
  markFirstRunCompleted,
} from './services/firstRunService';
import { healthMonitorService } from './services/healthMonitorService';
import { keyboardShortcutManager } from './services/keyboardShortcutManager';
import { loggingService } from './services/loggingService';
import { migrateSettingsIfNeeded } from './services/settingsValidationService';
import { ActivityProvider } from './state/activityContext';
import { useJobState } from './state/jobState';

// Lazy load development-only features to reduce production bundle size
const LogViewerPage = lazy(() =>
  import('./pages/LogViewerPage').then((m) => ({ default: m.LogViewerPage }))
);
const DiagnosticDashboardPage = lazy(() =>
  import('./pages/DiagnosticDashboardPage').then((m) => ({ default: m.DiagnosticDashboardPage }))
);
const ActivityDemoPage = lazy(() =>
  import('./pages/ActivityDemoPage').then((m) => ({ default: m.ActivityDemoPage }))
);
const LayoutDemoPage = lazy(() =>
  import('./pages/LayoutDemoPage').then((m) => ({ default: m.LayoutDemoPage }))
);

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
  // Initialize dark mode - default to dark on first run, then use localStorage
  const [isDarkMode, setIsDarkMode] = useState(() => {
    const saved = localStorage.getItem('darkMode');
    if (saved !== null) {
      return JSON.parse(saved);
    }
    // Default to dark mode for first-run users (creative app standard)
    return true;
  });

  const [showShortcuts, setShowShortcuts] = useState(false);
  const [showShortcutsPanel, setShowShortcutsPanel] = useState(false);
  const [showShortcutsCheatSheet, setShowShortcutsCheatSheet] = useState(false);
  const [showCommandPalette, setShowCommandPalette] = useState(false);
  const toasterId = 'notifications-toaster';

  useGlobalUndoShortcuts();

  const [isCheckingFirstRun, setIsCheckingFirstRun] = useState(true);
  const [shouldShowOnboarding, setShouldShowOnboarding] = useState(false);

  const [isInitializing, setIsInitializing] = useState(true);
  const [initializationError, setInitializationError] = useState<InitializationError | null>(null);
  const [showCrashRecovery, setShowCrashRecovery] = useState(false);

  const { currentJobId, status, progress, message } = useJobState();
  const [showDrawer, setShowDrawer] = useState(false);

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

  // Check first-run status on app mount
  useEffect(() => {
    async function checkFirstRun() {
      try {
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
        setIsCheckingFirstRun(false);
      }
    }

    checkFirstRun();
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

  // Apply dark mode class to document root and save preference
  useEffect(() => {
    const root = document.documentElement;
    if (isDarkMode) {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
    localStorage.setItem('darkMode', JSON.stringify(isDarkMode));
  }, [isDarkMode]);

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
          window.location.href = '/settings';
        },
      },
      {
        id: 'new-project',
        keys: 'Ctrl+N',
        description: 'New Project',
        context: 'global',
        handler: () => {
          window.location.href = '/create';
        },
      },
      {
        id: 'open-project',
        keys: 'Ctrl+O',
        description: 'Open Project',
        context: 'global',
        handler: () => {
          window.location.href = '/projects';
        },
      },
      {
        id: 'open-ideation',
        keys: 'Ctrl+I',
        description: 'Open Ideation',
        context: 'global',
        handler: () => {
          window.location.href = '/ideation';
        },
      },
      {
        id: 'open-editor',
        keys: 'Ctrl+E',
        description: 'Open Video Editor',
        context: 'global',
        handler: () => {
          window.location.href = '/editor';
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
          const currentPath = window.location.pathname;
          if (!currentPath.includes('/create')) {
            window.location.href = '/create';
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
        window.location.href = '/logs';
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
    const theme = isDarkMode ? webDarkTheme : webLightTheme;
    document.body.style.backgroundColor = theme.colorNeutralBackground1;
  }, [isDarkMode]);

  // Show crash recovery screen if multiple crashes detected
  if (showCrashRecovery) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
          <CrashRecoveryScreen
            crashCount={crashRecoveryService.getRecoveryState()?.crashCount || 0}
            consecutiveCrashes={crashRecoveryService.getRecoveryState()?.consecutiveCrashes || 0}
            lastCrashUrl={crashRecoveryService.getRecoveryState()?.lastCrash?.url}
            onContinue={() => {
              crashRecoveryService.resetCrashCounter();
              setShowCrashRecovery(false);
            }}
            onClearData={() => {
              crashRecoveryService.clearRecoveryData();
              localStorage.clear();
              sessionStorage.clear();
              window.location.reload();
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
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
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
          <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
            <BrowserRouter>
              <FirstRunWizard
                onComplete={async () => {
                  setShouldShowOnboarding(false);
                  await markFirstRunCompleted();
                }}
              />
            </BrowserRouter>
          </FluentProvider>
        </ThemeContext.Provider>
      </QueryClientProvider>
    );
  }

  if (isInitializing && !initializationError) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
          <InitializationScreen
            onComplete={() => setIsInitializing(false)}
            onError={(error) => setInitializationError(error)}
            enableSafeMode={true}
          />
        </FluentProvider>
      </ThemeContext.Provider>
    );
  }

  if (initializationError) {
    return (
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
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

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
          <AccessibilityProvider>
            <ActivityProvider>
              <div style={{ height: '100vh', display: 'flex', flexDirection: 'column' }}>
                <BrowserRouter>
                  {/* Status bar for job progress */}
                  <JobStatusBar
                    status={status}
                    progress={progress}
                    message={message}
                    onViewDetails={() => setShowDrawer(true)}
                  />
                  <Layout>
                    <ErrorBoundary>
                      <ConfigurationGate>
                        <Routes>
                          {/* Setup wizard - unified entry point for first-run and reconfiguration */}
                          <Route path="/setup" element={<FirstRunWizard />} />
                          {/* Legacy route redirect for backward compatibility */}
                          <Route path="/onboarding" element={<Navigate to="/setup" replace />} />

                          {/* Main routes */}
                          <Route path="/" element={<WelcomePage />} />
                          <Route path="/dashboard" element={<DashboardPage />} />
                          <Route
                            path="/ideation"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <IdeationDashboard />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/trending"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <TrendingTopicsExplorer />
                              </SuspenseBoundary>
                            }
                          />
                          <Route path="/content-planning" element={<ContentPlanningDashboard />} />
                          <Route path="/create" element={<VideoCreationWizard />} />
                          <Route
                            path="/create/advanced"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <CreateWizard />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/create/legacy"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <CreatePage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/templates"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <TemplatesLibrary />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/templates/custom"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <CustomTemplatesPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/editor/:jobId"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <TimelineEditor />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/editor"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <VideoEditorPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/pacing"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <PacingAnalyzerPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/render"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <RenderPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route path="/platform" element={<PlatformDashboard />} />
                          <Route path="/quality" element={<QualityDashboard />} />

                          <Route
                            path="/projects"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <ProjectsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/export-history"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <ExportHistoryPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/assets"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AssetLibrary />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/jobs"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <RecentJobsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/jobs/:jobId/telemetry"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <RunDetailsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/downloads"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <DownloadsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/health"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <SystemHealthDashboard />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/health/providers"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <ProviderHealthDashboard />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/ai-editing"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AIEditingPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/aesthetics"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AestheticsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/localization"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <TranslationPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/prompt-management"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <PromptManagementPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/rag"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <RagDocumentManager />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/voice-enhancement"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <VoiceEnhancementPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/performance-analytics"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <PerformanceAnalyticsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/usage-analytics"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <UsageAnalyticsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/ml-lab"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <MLLabPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/ab-tests"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <ABTestManagementPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/audience"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AudienceManagementPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/learning"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <LearningPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/quality-validation"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <QualityValidationPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/validation"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <ValidationPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/verification"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <VerificationPage />
                              </SuspenseBoundary>
                            }
                          />
                          {/* Diagnostics and system information */}
                          <Route
                            path="/diagnostics"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <DiagnosticDashboardPage />
                              </SuspenseBoundary>
                            }
                          />
                          {/* Logs page - always available for diagnostics */}
                          <Route
                            path="/logs"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <LogViewerPage />
                              </SuspenseBoundary>
                            }
                          />
                          {/* Development-only routes - lazy loaded */}
                          {env.enableDevTools && (
                            <>
                              <Route
                                path="/activity-demo"
                                element={
                                  <SuspenseBoundary loadingMessage="Loading...">
                                    <ActivityDemoPage />
                                  </SuspenseBoundary>
                                }
                              />
                              <Route
                                path="/layout-demo"
                                element={
                                  <SuspenseBoundary loadingMessage="Loading...">
                                    <LayoutDemoPage />
                                  </SuspenseBoundary>
                                }
                              />
                            </>
                          )}
                          <Route
                            path="/admin"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AdminDashboardPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/settings"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <SettingsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route
                            path="/settings/accessibility"
                            element={
                              <SuspenseBoundary loadingMessage="Loading...">
                                <AccessibilitySettingsPage />
                              </SuspenseBoundary>
                            }
                          />
                          <Route path="/models" element={<Navigate to="/settings" replace />} />
                          <Route path="*" element={<NotFoundPage />} />
                        </Routes>
                      </ConfigurationGate>
                    </ErrorBoundary>
                  </Layout>

                  {/* These components need to be inside BrowserRouter for navigation hooks */}
                  <KeyboardShortcutsModal
                    isOpen={showShortcuts}
                    onClose={() => setShowShortcuts(false)}
                  />
                  <KeyboardShortcutsPanel
                    isOpen={showShortcutsPanel}
                    onClose={() => setShowShortcutsPanel(false)}
                  />
                  <KeyboardShortcutsCheatSheet
                    open={showShortcutsCheatSheet}
                    onClose={() => setShowShortcutsCheatSheet(false)}
                  />
                  <CommandPalette
                    isOpen={showCommandPalette}
                    onClose={() => setShowCommandPalette(false)}
                  />
                  <NotificationsToaster toasterId={toasterId} />

                  {/* Job progress drawer */}
                  <JobProgressDrawer
                    isOpen={showDrawer}
                    onClose={() => setShowDrawer(false)}
                    jobId={currentJobId || ''}
                  />

                  {/* Action history panel for undo/redo */}
                  <ActionHistoryPanel />

                  {/* Global activity status footer */}
                  <footer id="global-footer">
                    <GlobalStatusFooter />
                  </footer>
                </BrowserRouter>
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
