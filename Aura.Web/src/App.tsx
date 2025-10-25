import { useState, useEffect, createContext, useContext } from 'react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  tokens,
} from '@fluentui/react-components';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { Layout } from './components/Layout';
import { WelcomePage } from './pages/WelcomePage';
import { DashboardPage } from './pages/DashboardPage';
import { CreatePage } from './pages/CreatePage';
import { CreateWizard } from './pages/Wizard/CreateWizard';
import { TimelinePage } from './pages/TimelinePage';
import { TimelineEditor } from './pages/Editor/TimelineEditor';
import { RenderPage } from './pages/RenderPage';
import { PublishPage } from './pages/PublishPage';
import { DownloadsPage } from './pages/DownloadsPage';
import { SettingsPage } from './pages/SettingsPage';
import { LogViewerPage } from './pages/LogViewerPage';
import { ProjectsPage } from './pages/Projects/ProjectsPage';
import { RecentJobsPage } from './pages/RecentJobsPage';
import { FirstRunWizard } from './pages/Onboarding/FirstRunWizard';
import { SetupWizard } from './pages/Setup/SetupWizard';
import { ProviderHealthDashboard } from './pages/Health/ProviderHealthDashboard';
import { AssetLibrary } from './pages/Assets/AssetLibrary';
import { KeyboardShortcutsModal } from './components/KeyboardShortcutsModal';
import { CommandPalette } from './components/CommandPalette';
import { NotificationsToaster } from './components/Notifications/Toasts';
import { JobStatusBar } from './components/StatusBar/JobStatusBar';
import { JobProgressDrawer } from './components/JobProgressDrawer';
import { useJobState } from './state/jobState';
import { IdeationDashboard } from './pages/Ideation/IdeationDashboard';
import { TrendingTopicsExplorer } from './pages/Ideation/TrendingTopicsExplorer';
import { PlatformDashboard } from './components/Platform';
import { QualityDashboard } from './components/dashboard';
import { ContentPlanningDashboard } from './components/contentPlanning/ContentPlanningDashboard';
import { VideoEditorPage } from './pages/VideoEditorPage';

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
  // Initialize dark mode from localStorage or system preference
  const [isDarkMode, setIsDarkMode] = useState(() => {
    const saved = localStorage.getItem('darkMode');
    if (saved !== null) {
      return JSON.parse(saved);
    }
    // Detect system preference if no saved preference
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  });
  
  const [showShortcuts, setShowShortcuts] = useState(false);
  const [showCommandPalette, setShowCommandPalette] = useState(false);

  // Job state for status bar
  const { currentJobId, status, progress, message } = useJobState();
  const [showDrawer, setShowDrawer] = useState(false);

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

  // Listen for OS theme changes (only if user hasn't explicitly set preference)
  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    
    const handleChange = (e: MediaQueryListEvent) => {
      const saved = localStorage.getItem('darkMode');
      // Only update if user hasn't explicitly saved a preference
      if (saved === null) {
        setIsDarkMode(e.matches);
      }
    };
    
    // Use addEventListener for modern browsers
    if (mediaQuery.addEventListener) {
      mediaQuery.addEventListener('change', handleChange);
      return () => mediaQuery.removeEventListener('change', handleChange);
    }
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

  // Global keyboard shortcut handler for Ctrl+K (command palette) and Ctrl+/ (shortcuts)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ctrl+K or Cmd+K for command palette
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        setShowCommandPalette(true);
      }
      // Ctrl+P or Cmd+P for command palette (alternative)
      if ((e.ctrlKey || e.metaKey) && e.key === 'p') {
        e.preventDefault();
        setShowCommandPalette(true);
      }
      // Ctrl+/ or Cmd+/ for shortcuts modal
      if ((e.ctrlKey || e.metaKey) && e.key === '/') {
        e.preventDefault();
        setShowShortcuts(true);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, []);

  const toggleTheme = () => {
    setIsDarkMode(!isDarkMode);
  };

  return (
    <ThemeContext.Provider value={{ isDarkMode, toggleTheme }}>
      <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
        <div className={styles.root}>
          <BrowserRouter>
            {/* Status bar for job progress */}
            <JobStatusBar
              status={status}
              progress={progress}
              message={message}
              onViewDetails={() => setShowDrawer(true)}
            />
            <Layout>
              <Routes>
                <Route path="/" element={<WelcomePage />} />
                <Route path="/setup" element={<SetupWizard />} />
                <Route path="/onboarding" element={<FirstRunWizard />} />
                <Route path="/dashboard" element={<DashboardPage />} />
                <Route path="/ideation" element={<IdeationDashboard />} />
                <Route path="/trending" element={<TrendingTopicsExplorer />} />
                <Route path="/content-planning" element={<ContentPlanningDashboard />} />
                <Route path="/create" element={<CreateWizard />} />
                <Route path="/create/legacy" element={<CreatePage />} />
                <Route path="/timeline" element={<TimelinePage />} />
                <Route path="/editor/:jobId" element={<TimelineEditor />} />
                <Route path="/editor" element={<VideoEditorPage />} />
                <Route path="/render" element={<RenderPage />} />
                <Route path="/platform" element={<PlatformDashboard />} />
                <Route path="/quality" element={<QualityDashboard />} />
                <Route path="/publish" element={<PublishPage />} />
                <Route path="/projects" element={<ProjectsPage />} />
                <Route path="/assets" element={<AssetLibrary />} />
                <Route path="/jobs" element={<RecentJobsPage />} />
                <Route path="/downloads" element={<DownloadsPage />} />
                <Route path="/health" element={<ProviderHealthDashboard />} />
                <Route path="/logs" element={<LogViewerPage />} />
                <Route path="/settings" element={<SettingsPage />} />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Layout>
          </BrowserRouter>
          <KeyboardShortcutsModal isOpen={showShortcuts} onClose={() => setShowShortcuts(false)} />
          <CommandPalette isOpen={showCommandPalette} onClose={() => setShowCommandPalette(false)} />
          <NotificationsToaster toasterId={toasterId} />

          {/* Job progress drawer */}
          <JobProgressDrawer
            isOpen={showDrawer}
            onClose={() => setShowDrawer(false)}
            jobId={currentJobId || ''}
          />
        </div>
      </FluentProvider>
    </ThemeContext.Provider>
  );
}

export default App;
