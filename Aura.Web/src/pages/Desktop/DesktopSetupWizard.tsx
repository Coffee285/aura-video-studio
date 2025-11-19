/**
 * Desktop Setup Wizard (LEGACY/UNUSED)
 *
 * ⚠️ DEPRECATION NOTICE ⚠️
 * This component is NOT used by the main desktop application.
 * The desktop app loads FirstRunWizard directly via the #/setup route.
 *
 * This was an experimental implementation that included Electron-specific
 * features like auto-detection and separate dependency checking screens.
 *
 * **Current Desktop Flow**:
 * 1. Electron main.js navigates to #/setup
 * 2. Route maps to FirstRunWizard component
 * 3. FirstRunWizard handles all setup steps
 *
 * **DO NOT USE THIS COMPONENT** - It is kept for reference only.
 *
 * See: WIZARD_SETUP_GUIDE.md for details on the active wizard implementation.
 *
 * ---
 *
 * Original Description:
 * Enhanced first-run wizard for Electron desktop app with:
 * - FFmpeg auto-installation
 * - Ollama detection and installation guidance
 * - OS-specific configuration
 * - System requirements validation
 */

import {
  makeStyles,
  tokens,
  Title1,
  Title2,
  Title3,
  Text,
  Button,
  Card,
  CardHeader,
  Spinner,
  ProgressBar,
  Link,
  Badge,
} from '@fluentui/react-components';
import {
  CheckmarkCircle24Filled,
  Warning24Regular,
  ErrorCircle24Regular,
  ArrowDownload24Regular,
  Info24Regular,
  ChevronRight24Regular,
  ChevronLeft24Regular,
} from '@fluentui/react-icons';
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useNotifications } from '../../components/Notifications/Toasts';
import { FirstRunWizard } from '../Onboarding/FirstRunWizard';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  content: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingVerticalXXL,
    maxWidth: '1200px',
    margin: '0 auto',
    width: '100%',
  },
  section: {
    marginBottom: tokens.spacingVerticalXXL,
  },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
    gap: tokens.spacingHorizontalL,
    marginTop: tokens.spacingVerticalL,
  },
  card: {
    padding: tokens.spacingVerticalL,
  },
  statusIcon: {
    marginRight: tokens.spacingHorizontalS,
  },
  footer: {
    padding: tokens.spacingVerticalL,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  actionButton: {
    minWidth: '150px',
  },
});

type SetupMode = 'welcome' | 'dependencies' | 'configuration' | 'complete';

interface DependencyStatus {
  ffmpeg: 'checking' | 'found' | 'not-found' | 'installing' | 'installed' | 'error';
  ollama: 'checking' | 'found' | 'not-found' | 'downloading' | 'installed' | 'skip';
  dotnet: 'checking' | 'found' | 'not-found';
}

export function DesktopSetupWizard() {
  const styles = useStyles();
  const _navigate = useNavigate(); // Prefix unused variables with underscore
  const { showSuccess, showError, showInfo } = useNotifications();

  const [mode, setMode] = useState<SetupMode>('welcome');
  const [setupType, setSetupType] = useState<'express' | 'custom'>('express');
  const [dependencies, setDependencies] = useState<DependencyStatus>({
    ffmpeg: 'checking',
    ollama: 'checking',
    dotnet: 'checking',
  });
  const [installProgress, _setInstallProgress] = useState(0); // Prefix unused
  const [systemInfo, setSystemInfo] = useState<{
    platform: string;
    arch: string;
    paths: Record<string, string>;
  } | null>(null);

  // Check if running in Electron
  const isElectron =
    typeof window !== 'undefined' &&
    (window as { electron?: { platform?: { isElectron?: boolean } } }).electron?.platform
      ?.isElectron;

  useEffect(() => {
    if (isElectron) {
      loadSystemInfo();
      checkDependencies();
    }
  }, [isElectron]);

  const loadSystemInfo = async () => {
    try {
      const electron = (
        window as {
          electron?: {
            app?: { getPaths: () => Promise<Record<string, string>> };
            platform?: { os: string; arch: string };
          };
        }
      ).electron;
      if (!electron) {
        return;
      }
      const paths = electron.app ? await electron.app.getPaths() : {};
      const platform = electron.platform?.os || 'unknown';
      const arch = electron.platform?.arch || 'unknown';

      setSystemInfo({ platform, arch, paths });
    } catch (_error) {
      console.error('Failed to load system info:', _error);
    }
  };

  const checkDependencies = async () => {
    // Check FFmpeg
    try {
      const response = await fetch('/api/health/ffmpeg');
      const data = await response.json();
      setDependencies((prev) => ({
        ...prev,
        ffmpeg: data.isAvailable ? 'found' : 'not-found',
      }));
    } catch {
      setDependencies((prev) => ({ ...prev, ffmpeg: 'not-found' }));
    }

    // Check Ollama
    try {
      const response = await fetch('http://localhost:11434/api/tags', {
        method: 'GET',
        signal: AbortSignal.timeout(2000),
      });
      if (response.ok) {
        setDependencies((prev) => ({ ...prev, ollama: 'found' }));
      } else {
        setDependencies((prev) => ({ ...prev, ollama: 'not-found' }));
      }
    } catch {
      setDependencies((prev) => ({ ...prev, ollama: 'not-found' }));
    }

    // .NET is bundled, always available
    setDependencies((prev) => ({ ...prev, dotnet: 'found' }));
  };

  const installFFmpeg = async () => {
    try {
      setDependencies((prev) => ({ ...prev, ffmpeg: 'installing' }));
      showInfo('Downloading FFmpeg...');

      // Platform-specific installation
      const platform = systemInfo?.platform || 'unknown';

      if (platform === 'win32' || platform === 'Windows') {
        // Windows: Use unified FFmpeg installation endpoint
        const response = await fetch('/api/ffmpeg/install', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ version: 'latest' }),
        });

        const result = await response.json();

        if (!result.success) {
          throw new Error(result.message || 'FFmpeg installation failed');
        }

        // Show success with version info
        showSuccess(`FFmpeg ${result.version || 'latest'} installed successfully!`);
        setDependencies((prev) => ({ ...prev, ffmpeg: 'installed' }));
      } else {
        // macOS/Linux: Guide user to use package manager
        const electron = (
          window as { electron?: { shell?: { openExternal: (url: string) => Promise<void> } } }
        ).electron;
        if (platform === 'darwin' || platform === 'macOS') {
          showInfo('Opening Homebrew installation guide...');
          if (electron?.shell) {
            await electron.shell.openExternal('https://formulae.brew.sh/formula/ffmpeg');
          }
        } else {
          showInfo('Opening FFmpeg installation guide...');
          if (electron?.shell) {
            await electron.shell.openExternal('https://ffmpeg.org/download.html');
          }
        }
        setDependencies((prev) => ({ ...prev, ffmpeg: 'not-found' }));
      }
    } catch (error: unknown) {
      const errorObj = error instanceof Error ? error : new Error(String(error));
      console.error('FFmpeg installation error:', errorObj);
      setDependencies((prev) => ({ ...prev, ffmpeg: 'error' }));

      // Show detailed error message if available
      const errorMessage = errorObj.message || 'Failed to install FFmpeg. Please install manually.';
      showError(errorMessage);
    }
  };

  const openOllamaDownload = async () => {
    try {
      const electron = (
        window as { electron?: { shell?: { openExternal: (url: string) => Promise<void> } } }
      ).electron;
      if (electron?.shell) {
        await electron.shell.openExternal('https://ollama.ai/download');
      }
      showInfo('Opening Ollama download page. After installing, click "Recheck" below.');
    } catch {
      // Silently ignore errors when opening external links
      showError('Failed to open Ollama download page');
    }
  };

  const skipOllama = () => {
    setDependencies((prev) => ({ ...prev, ollama: 'skip' }));
    showInfo('You can install Ollama later from Settings');
  };

  const renderWelcomeScreen = () => (
    <div className={styles.content}>
      <div style={{ textAlign: 'center', maxWidth: '600px', margin: '0 auto' }}>
        <Title1>Welcome to Aura Video Studio</Title1>
        <Text as="p" size={500} style={{ marginTop: tokens.spacingVerticalL }}>
          Let&apos;s get you set up to create amazing AI-powered videos!
        </Text>

        <div
          style={{
            marginTop: tokens.spacingVerticalXXL,
            display: 'flex',
            gap: tokens.spacingHorizontalL,
            justifyContent: 'center',
          }}
        >
          <Card
            className={styles.card}
            onClick={() => {
              setSetupType('express');
              setMode('dependencies');
            }}
          >
            <CardHeader
              header={<Title3>Express Setup</Title3>}
              description="Recommended for most users. Auto-detect and configure everything."
            />
            <Badge appearance="filled" color="brand">
              Recommended
            </Badge>
          </Card>

          <Card
            className={styles.card}
            onClick={() => {
              setSetupType('custom');
              setMode('configuration');
            }}
          >
            <CardHeader
              header={<Title3>Custom Setup</Title3>}
              description="Choose providers, paths, and settings manually."
            />
          </Card>
        </div>

        {!isElectron && (
          <div
            style={{
              marginTop: tokens.spacingVerticalXXL,
              padding: tokens.spacingVerticalL,
              backgroundColor: tokens.colorNeutralBackground3,
              borderRadius: tokens.borderRadiusMedium,
            }}
          >
            <Warning24Regular className={styles.statusIcon} />
            <Text>
              You&apos;re running the web version. Some features (like auto-installation) are only
              available in the desktop app.
            </Text>
          </div>
        )}
      </div>
    </div>
  );

  const renderDependenciesScreen = () => (
    <div className={styles.content}>
      <div className={styles.section}>
        <Title2>System Dependencies</Title2>
        <Text>Checking and installing required software...</Text>

        <div className={styles.grid}>
          {/* FFmpeg Card */}
          <Card className={styles.card}>
            <CardHeader
              image={
                dependencies.ffmpeg === 'found' || dependencies.ffmpeg === 'installed' ? (
                  <CheckmarkCircle24Filled color={tokens.colorPaletteGreenForeground1} />
                ) : dependencies.ffmpeg === 'error' ? (
                  <ErrorCircle24Regular color={tokens.colorPaletteRedForeground1} />
                ) : dependencies.ffmpeg === 'installing' ? (
                  <Spinner size="small" />
                ) : (
                  <Warning24Regular color={tokens.colorPaletteYellowForeground1} />
                )
              }
              header={<Title3>FFmpeg</Title3>}
              description="Required for video rendering"
            />

            {dependencies.ffmpeg === 'not-found' && (
              <>
                <Text size={300}>FFmpeg not detected. Install to render videos.</Text>
                <Button
                  appearance="primary"
                  icon={<ArrowDownload24Regular />}
                  onClick={installFFmpeg}
                  style={{ marginTop: tokens.spacingVerticalM }}
                >
                  Install FFmpeg
                </Button>
              </>
            )}

            {dependencies.ffmpeg === 'installing' && (
              <>
                <Text size={300}>Installing FFmpeg...</Text>
                <ProgressBar
                  value={installProgress}
                  max={100}
                  style={{ marginTop: tokens.spacingVerticalS }}
                />
              </>
            )}

            {(dependencies.ffmpeg === 'found' || dependencies.ffmpeg === 'installed') && (
              <Text size={300}>✓ FFmpeg is ready!</Text>
            )}

            {dependencies.ffmpeg === 'error' && (
              <>
                <Text size={300}>Installation failed. Please install manually:</Text>
                <Link href="https://ffmpeg.org/download.html" target="_blank">
                  FFmpeg Download
                </Link>
              </>
            )}
          </Card>

          {/* Ollama Card */}
          <Card className={styles.card}>
            <CardHeader
              image={
                dependencies.ollama === 'found' ? (
                  <CheckmarkCircle24Filled color={tokens.colorPaletteGreenForeground1} />
                ) : dependencies.ollama === 'skip' ? (
                  <Info24Regular color={tokens.colorNeutralForeground3} />
                ) : (
                  <Warning24Regular color={tokens.colorPaletteYellowForeground1} />
                )
              }
              header={<Title3>Ollama</Title3>}
              description="Optional: Run AI models locally"
            />

            {dependencies.ollama === 'not-found' && (
              <>
                <Text size={300}>
                  Ollama lets you run AI models locally for free, with complete privacy.
                </Text>
                <div
                  style={{
                    marginTop: tokens.spacingVerticalM,
                    display: 'flex',
                    gap: tokens.spacingHorizontalS,
                  }}
                >
                  <Button
                    appearance="primary"
                    icon={<ArrowDownload24Regular />}
                    onClick={openOllamaDownload}
                  >
                    Download Ollama
                  </Button>
                  <Button onClick={skipOllama}>Skip</Button>
                </div>
              </>
            )}

            {dependencies.ollama === 'found' && (
              <Text size={300}>✓ Ollama detected and ready!</Text>
            )}

            {dependencies.ollama === 'skip' && (
              <Text size={300}>Skipped. You can install later in Settings.</Text>
            )}
          </Card>

          {/* .NET Backend Card */}
          <Card className={styles.card}>
            <CardHeader
              image={<CheckmarkCircle24Filled color={tokens.colorPaletteGreenForeground1} />}
              header={<Title3>.NET Backend</Title3>}
              description="Bundled with the app"
            />
            <Text size={300}>✓ Backend server is ready!</Text>
          </Card>
        </div>

        <div style={{ marginTop: tokens.spacingVerticalXXL, textAlign: 'center' }}>
          <Button
            appearance="primary"
            size="large"
            icon={<ChevronRight24Regular />}
            iconPosition="after"
            onClick={() => setMode('configuration')}
            disabled={
              dependencies.ffmpeg !== 'found' &&
              dependencies.ffmpeg !== 'installed' &&
              dependencies.ffmpeg !== 'error'
            }
          >
            Continue to Configuration
          </Button>
        </div>
      </div>
    </div>
  );

  const renderFooter = () => (
    <div className={styles.footer}>
      <div>
        {mode !== 'welcome' && (
          <Button
            icon={<ChevronLeft24Regular />}
            onClick={() => {
              if (mode === 'dependencies') setMode('welcome');
              else if (mode === 'configuration') {
                setMode(setupType === 'express' ? 'dependencies' : 'welcome');
              }
            }}
          >
            Back
          </Button>
        )}
      </div>

      <Text size={300}>
        Need help?{' '}
        <Link href="https://docs.aura-video-studio.com/setup" target="_blank">
          Setup Guide
        </Link>
      </Text>
    </div>
  );

  // If not in dependencies or welcome mode, show the regular FirstRunWizard
  if (mode === 'configuration' || mode === 'complete') {
    return <FirstRunWizard />;
  }

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Title2>Aura Video Studio Setup</Title2>
        {systemInfo && (
          <Text size={300}>
            {systemInfo.platform} • {systemInfo.arch}
          </Text>
        )}
      </div>

      {mode === 'welcome' && renderWelcomeScreen()}
      {mode === 'dependencies' && renderDependenciesScreen()}

      {renderFooter()}
    </div>
  );
}
