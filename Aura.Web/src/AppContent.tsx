import { useState, useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { tokens } from '@fluentui/react-components';
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

interface AppContentProps {
  showShortcuts: boolean;
  setShowShortcuts: (show: boolean) => void;
  showCommandPalette: boolean;
  setShowCommandPalette: (show: boolean) => void;
}

export function AppContent({ showShortcuts, setShowShortcuts, showCommandPalette, setShowCommandPalette }: AppContentProps) {
  // const { toasterId } = useNotifications(); // TEMPORARILY DISABLED
  const toasterId = 'notifications-toaster'; // Hardcode for now
  const { currentJobId, status, progress, message } = useJobState();
  const [showDrawer, setShowDrawer] = useState(false);

  // Poll job progress when a job is active
  useEffect(() => {
    if (!currentJobId || status === 'completed' || status === 'failed') {
      return;
    }

    let isActive = true;
    const pollInterval = setInterval(async () => {
      if (!isActive) return;
      
      try {
        const response = await fetch(`/api/jobs/${currentJobId}/progress`);
        if (response.ok && isActive) {
          const data = await response.json();
          
          if (isActive) {
            useJobState.getState().updateProgress(data.progress, data.currentStage);

            if (data.status === 'completed') {
              useJobState.getState().setStatus('completed');
              useJobState.getState().updateProgress(100, 'Video generation complete!');
              clearInterval(pollInterval);
            } else if (data.status === 'failed') {
              useJobState.getState().setStatus('failed');
              useJobState.getState().updateProgress(data.progress, 'Generation failed');
              clearInterval(pollInterval);
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

  return (
    <div style={{ height: '100vh', display: 'flex', flexDirection: 'column', backgroundColor: tokens.colorNeutralBackground1 }}>
      <BrowserRouter>
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
      <JobProgressDrawer
        isOpen={showDrawer}
        onClose={() => setShowDrawer(false)}
        jobId={currentJobId || ''}
      />
    </div>
  );
}
