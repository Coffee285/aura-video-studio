import { FluentProvider, webLightTheme, webDarkTheme, Spinner } from '@fluentui/react-components';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useState, useEffect, createContext, useContext, lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { queryClient } from './api/queryClient';
import { CommandPalette } from './components/CommandPalette';
import { ConfigurationGate } from './components/ConfigurationGate';
import { ContentPlanningDashboard } from './components/contentPlanning/ContentPlanningDashboard';
import { QualityDashboard } from './components/dashboard';
import { ErrorBoundary } from './components/ErrorBoundary';
import { GlobalStatusFooter } from './components/GlobalStatusFooter';
import { JobProgressDrawer } from './components/JobProgressDrawer';
import { KeyboardShortcutsPanel } from './components/KeyboardShortcuts/KeyboardShortcutsPanel';
import { KeyboardShortcutsModal } from './components/KeyboardShortcutsModal';
import { Layout } from './components/Layout';
import { NotificationsToaster } from './components/Notifications/Toasts';
import { PlatformDashboard } from './components/Platform';
import { StartupScreen } from './components/StartupScreen';
import { JobStatusBar } from './components/StatusBar/JobStatusBar';
import { ActionHistoryPanel } from './components/UndoRedo/ActionHistoryPanel';
import { VideoCreationWizard } from './components/VideoWizard/VideoCreationWizard';
import { env } from './config/env';
import { SafeModeProvider } from './contexts/SafeModeContext';
import { useGlobalUndoShortcuts } from './hooks/useGlobalUndoShortcuts';
import { AestheticsPage } from './pages/Aesthetics/AestheticsPage';
import { AIEditingPage } from './pages/AIEditing/AIEditingPage';
import { AssetLibrary } from './pages/Assets/AssetLibrary';
import AudienceManagementPage from './pages/Audience/AudienceManagementPage';
import { CreatePage } from './pages/CreatePage';
import { DashboardPage } from './pages/DashboardPage';
import { DownloadsPage } from './pages/DownloadsPage';
import { TimelineEditor } from './pages/Editor/TimelineEditor';
import { ExportHistoryPage } from './pages/Export/ExportHistoryPage';
import { ProviderHealthDashboard } from './pages/Health/ProviderHealthDashboard';
import SystemHealthDashboard from './pages/Health/SystemHealthDashboard';
import { IdeationDashboard } from './pages/Ideation/IdeationDashboard';
import { TrendingTopicsExplorer } from './pages/Ideation/TrendingTopicsExplorer';
import { RunDetailsPage } from './pages/Jobs/RunDetailsPage';
import LearningPage from './pages/Learning/LearningPage';
import { TranslationPage } from './pages/Localization/TranslationPage';
import { MLLabPage } from './pages/MLLab/MLLabPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { FirstRunWizard } from './pages/Onboarding/FirstRunWizard';
import { PacingAnalyzerPage } from './pages/PacingAnalyzerPage';
import ABTestManagementPage from './pages/PerformanceAnalytics/ABTestManagementPage';
import PerformanceAnalyticsPage from './pages/PerformanceAnalytics/PerformanceAnalyticsPage';
import { ProjectsPage } from './pages/Projects/ProjectsPage';
import { PromptManagementPage } from './pages/PromptManagement/PromptManagementPage';
import QualityValidationPage from './pages/QualityValidation/QualityValidationPage';
import RagDocumentManager from './pages/RAG/RagDocumentManager';
import { RecentJobsPage } from './pages/RecentJobsPage';
import { RenderPage } from './pages/RenderPage';
import { SettingsPage } from './pages/SettingsPage';
import { SetupWizard } from './pages/Setup/SetupWizard';
import CustomTemplatesPage from './pages/Templates/CustomTemplatesPage';
import TemplatesLibrary from './pages/Templates/TemplatesLibrary';
import ValidationPage from './pages/Validation/ValidationPage';
import VerificationPage from './pages/Verification/VerificationPage';
import { VideoEditorPage } from './pages/VideoEditorPage';
import VoiceEnhancementPage from './pages/VoiceEnhancement/VoiceEnhancementPage';
import { WelcomePage } from './pages/WelcomePage';
import { CreateWizard } from './pages/Wizard/CreateWizard';
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
  const [showCommandPalette, setShowCommandPalette] = useState(false);
  const toasterId = 'notifications-toaster'; // Hardcoded to avoid hook context issues

  // Initialize global undo/redo shortcuts
  useGlobalUndoShortcuts();

  // First-run detection state
  const [isCheckingFirstRun, setIsCheckingFirstRun] = useState(true);
  const [shouldShowOnboarding, setShouldShowOnboarding] = useState(false);

  // Startup initialization state
  const [shouldShowStartup, setShouldShowStartup] = useState(true);
  const [startupInSafeMode, setStartupInSafeMode] = useState(false);

  // Job state for status bar
  const { currentJobId, status, progress, message } = useJobState();
  const [showDrawer, setShowDrawer] = useState(false);

  // Check first-run status on app mount
  useEffect(() => {
    async function checkFirstRun() {
      try {
        // Migrate legacy first-run flag if needed
        migrateLegacyFirstRunStatus();

        // Migrate settings if needed (e.g., placeholder paths)
        await migrateSettingsIfNeeded();

        // Check if user has completed first-run wizard
        const completed = await hasCompletedFirstRun();
        setShouldShowOnboarding(!completed);

        // If first-run is completed, we still need to show startup screen
        if (completed) {
          setShouldShowStartup(true);
        } else {
          // If onboarding needed, skip startup screen
          setShouldShowStartup(false);
        }
      } catch (error) {
        console.error('Error checking first-run status:', error);
        // On error, check localStorage as fallback - if nothing is set, assume first run
        const localStatus =
          localStorage.getItem('hasCompletedFirstRun') === 'true' ||
          localStorage.getItem('hasSeenOnboarding') === 'true';
        setShouldShowOnboarding(!localStatus);
        setShouldShowStartup(localStatus);
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
      // Ctrl+/ or Cmd+/ for old shortcuts modal
      else if ((e.ctrlKey || e.metaKey) && e.key === '/') {
        e.preventDefault();
        setShowShortcuts(true);
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

  // Show mandatory first-run wizard as full-screen overlay if not completed
  if (shouldShowOnboarding) {
    return (
      <QueryClientProvider client={queryClient}>
        <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
          <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
            <SafeModeProvider>
              <BrowserRouter>
                <FirstRunWizard
                  onComplete={async () => {
                    setShouldShowOnboarding(false);
                    await markFirstRunCompleted();
                    // After onboarding, show startup screen
                    setShouldShowStartup(true);
                  }}
                />
              </BrowserRouter>
            </SafeModeProvider>
          </FluentProvider>
        </ThemeContext.Provider>
      </QueryClientProvider>
    );
  }

  // Show startup screen for health checks and initialization
  if (shouldShowStartup && !startupInSafeMode) {
    return (
      <QueryClientProvider client={queryClient}>
        <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
          <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
            <SafeModeProvider>
              <StartupScreen
                onComplete={() => {
                  setShouldShowStartup(false);
                }}
                onSafeModeRequested={() => {
                  setStartupInSafeMode(true);
                  setShouldShowStartup(false);
                }}
              />
            </SafeModeProvider>
          </FluentProvider>
        </ThemeContext.Provider>
      </QueryClientProvider>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
          <SafeModeProvider>
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
                          {/* First-run onboarding route - for re-entry via settings */}
                          <Route path="/onboarding" element={<FirstRunWizard />} />

                          {/* Main routes */}
                          <Route path="/" element={<WelcomePage />} />

                          {/* All other routes */}
                          <Route path="/setup" element={<SetupWizard />} />
                          <Route path="/dashboard" element={<DashboardPage />} />
                          <Route path="/ideation" element={<IdeationDashboard />} />
                          <Route path="/trending" element={<TrendingTopicsExplorer />} />
                          <Route path="/content-planning" element={<ContentPlanningDashboard />} />
                          <Route path="/create" element={<CreateWizard />} />
                          <Route path="/create/new" element={<VideoCreationWizard />} />
                          <Route path="/create/legacy" element={<CreatePage />} />
                          <Route path="/templates" element={<TemplatesLibrary />} />
                          <Route path="/templates/custom" element={<CustomTemplatesPage />} />
                          <Route path="/editor/:jobId" element={<TimelineEditor />} />
                          <Route path="/editor" element={<VideoEditorPage />} />
                          <Route path="/pacing" element={<PacingAnalyzerPage />} />
                          <Route path="/render" element={<RenderPage />} />
                          <Route path="/platform" element={<PlatformDashboard />} />
                          <Route path="/quality" element={<QualityDashboard />} />

                          <Route path="/projects" element={<ProjectsPage />} />
                          <Route path="/export-history" element={<ExportHistoryPage />} />
                          <Route path="/assets" element={<AssetLibrary />} />
                          <Route path="/jobs" element={<RecentJobsPage />} />
                          <Route path="/jobs/:jobId/telemetry" element={<RunDetailsPage />} />
                          <Route path="/downloads" element={<DownloadsPage />} />
                          <Route path="/health" element={<SystemHealthDashboard />} />
                          <Route path="/health/providers" element={<ProviderHealthDashboard />} />
                          <Route path="/ai-editing" element={<AIEditingPage />} />
                          <Route path="/aesthetics" element={<AestheticsPage />} />
                          <Route path="/localization" element={<TranslationPage />} />
                          <Route path="/prompt-management" element={<PromptManagementPage />} />
                          <Route path="/rag" element={<RagDocumentManager />} />
                          <Route path="/voice-enhancement" element={<VoiceEnhancementPage />} />
                          <Route
                            path="/performance-analytics"
                            element={<PerformanceAnalyticsPage />}
                          />
                          <Route path="/ml-lab" element={<MLLabPage />} />
                          <Route path="/ab-tests" element={<ABTestManagementPage />} />
                          <Route path="/audience" element={<AudienceManagementPage />} />
                          <Route path="/learning" element={<LearningPage />} />
                          <Route path="/quality-validation" element={<QualityValidationPage />} />
                          <Route path="/validation" element={<ValidationPage />} />
                          <Route path="/verification" element={<VerificationPage />} />
                          {/* Diagnostics and system information */}
                          <Route
                            path="/diagnostics"
                            element={
                              <Suspense fallback={<Spinner label="Loading..." />}>
                                <DiagnosticDashboardPage />
                              </Suspense>
                            }
                          />
                          {/* Logs page - always available for diagnostics */}
                          <Route
                            path="/logs"
                            element={
                              <Suspense fallback={<Spinner label="Loading..." />}>
                                <LogViewerPage />
                              </Suspense>
                            }
                          />
                          {/* Development-only routes - lazy loaded */}
                          {env.enableDevTools && (
                            <>
                              <Route
                                path="/activity-demo"
                                element={
                                  <Suspense fallback={<Spinner label="Loading..." />}>
                                    <ActivityDemoPage />
                                  </Suspense>
                                }
                              />
                              <Route
                                path="/layout-demo"
                                element={
                                  <Suspense fallback={<Spinner label="Loading..." />}>
                                    <LayoutDemoPage />
                                  </Suspense>
                                }
                              />
                            </>
                          )}
                          <Route path="/settings" element={<SettingsPage />} />
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
                  <GlobalStatusFooter />
                </BrowserRouter>
              </div>
            </ActivityProvider>
          </SafeModeProvider>
        </FluentProvider>
        {/* React Query Devtools - only in development */}
        {env.isDevelopment && <ReactQueryDevtools initialIsOpen={false} />}
      </ThemeContext.Provider>
    </QueryClientProvider>
  );
}

export default App;
