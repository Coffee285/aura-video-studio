import {
  Spinner,
  Text,
  makeStyles,
  tokens,
  Button,
  Card,
  CardHeader,
} from '@fluentui/react-components';
import {
  CheckmarkCircle20Filled,
  ErrorCircle20Filled,
  Warning20Filled,
} from '@fluentui/react-icons';
import { useEffect, useState } from 'react';
import { getHealthDetails } from '../../services/api/healthApi';
import type { HealthDetailsResponse } from '../../types/api-v1';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '100vh',
    padding: tokens.spacingVerticalXXXL,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  card: {
    maxWidth: '600px',
    width: '100%',
  },
  header: {
    marginBottom: tokens.spacingVerticalL,
  },
  title: {
    fontSize: tokens.fontSizeHero700,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalS,
  },
  subtitle: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
  },
  progressContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    marginTop: tokens.spacingVerticalL,
  },
  checkItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  checkIcon: {
    flexShrink: 0,
  },
  checkText: {
    flex: 1,
    fontSize: tokens.fontSizeBase300,
  },
  errorContainer: {
    marginTop: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorPaletteRedBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderLeft: `4px solid ${tokens.colorPaletteRedBorder1}`,
  },
  errorTitle: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorPaletteRedForeground1,
    marginBottom: tokens.spacingVerticalS,
  },
  errorDetails: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalM,
  },
  warningContainer: {
    marginTop: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorPaletteYellowBackground2,
    borderRadius: tokens.borderRadiusMedium,
    borderLeft: `4px solid ${tokens.colorPaletteYellowBorder1}`,
  },
  warningTitle: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorPaletteYellowForeground1,
    marginBottom: tokens.spacingVerticalS,
  },
  actionButtons: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalM,
  },
});

export interface InitializationCheck {
  name: string;
  status: 'pending' | 'checking' | 'success' | 'warning' | 'error';
  message?: string;
  errorDetails?: string;
  suggestion?: string;
}

interface StartupScreenProps {
  onComplete: () => void;
  onSafeModeRequested?: () => void;
}

export function StartupScreen({ onComplete, onSafeModeRequested }: StartupScreenProps) {
  const styles = useStyles();
  const [checks, setChecks] = useState<InitializationCheck[]>([
    { name: 'Backend Connection', status: 'pending' },
    { name: 'Database', status: 'pending' },
    { name: 'FFmpeg', status: 'pending' },
    { name: 'Required Directories', status: 'pending' },
    { name: 'Disk Space', status: 'pending' },
    { name: 'Providers', status: 'pending' },
  ]);
  const [overallStatus, setOverallStatus] = useState<
    'initializing' | 'success' | 'degraded' | 'failed'
  >('initializing');
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [canContinue, setCanContinue] = useState(false);

  useEffect(() => {
    performHealthChecks();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const performHealthChecks = async () => {
    let hasErrors = false;
    let hasWarnings = false;

    try {
      const updateCheck = (
        name: string,
        status: InitializationCheck['status'],
        message?: string,
        errorDetails?: string,
        suggestion?: string
      ) => {
        setChecks((prev) =>
          prev.map((check) =>
            check.name === name ? { ...check, status, message, errorDetails, suggestion } : check
          )
        );
      };

      // Check backend connectivity first
      updateCheck('Backend Connection', 'checking');

      try {
        const healthDetails: HealthDetailsResponse = await getHealthDetails();
        updateCheck('Backend Connection', 'success', 'Backend is responding');

        // Process each health check result
        const checkMapping: Record<string, string> = {
          Database: 'Database',
          FFmpeg: 'FFmpeg',
          RequiredDirectories: 'Required Directories',
          DiskSpace: 'Disk Space',
          TtsProviders: 'Providers',
        };

        for (const [key, checkName] of Object.entries(checkMapping)) {
          const healthCheck = healthDetails.checks.find((c) => c.name === key);

          if (!healthCheck) {
            updateCheck(checkName, 'checking');
            continue;
          }

          updateCheck(checkName, 'checking');

          if (healthCheck.status === 'Healthy') {
            updateCheck(checkName, 'success', healthCheck.message || `${checkName} is healthy`);
          } else if (healthCheck.status === 'Degraded') {
            updateCheck(
              checkName,
              'warning',
              healthCheck.message || `${checkName} is degraded`,
              healthCheck.data ? JSON.stringify(healthCheck.data) : undefined,
              `${checkName} is available but not optimal. Application can continue with limited functionality.`
            );
            hasWarnings = true;
          } else {
            updateCheck(
              checkName,
              'error',
              healthCheck.message || `${checkName} check failed`,
              healthCheck.data ? JSON.stringify(healthCheck.data) : undefined,
              getRecoverySuggestion(checkName)
            );
            hasErrors = true;
          }
        }

        // Determine overall status
        if (hasErrors) {
          setOverallStatus('failed');
          setErrorMessage('Critical services failed to initialize. Please check the errors below.');
          setCanContinue(false);
        } else if (hasWarnings) {
          setOverallStatus('degraded');
          setErrorMessage(
            'Some services are degraded. You can continue with limited functionality.'
          );
          setCanContinue(true);
        } else {
          setOverallStatus('success');
          // Auto-continue after brief delay if all checks pass
          setTimeout(() => {
            onComplete();
          }, 500);
        }
      } catch (backendError: unknown) {
        updateCheck(
          'Backend Connection',
          'error',
          'Cannot connect to backend',
          backendError instanceof Error ? backendError.message : 'Unknown error',
          'Ensure the backend API is running on http://localhost:5005. Check the backend logs for errors.'
        );

        // Mark all other checks as pending since we can't check them
        checks.forEach((check) => {
          if (check.name !== 'Backend Connection' && check.status === 'pending') {
            updateCheck(check.name, 'error', 'Cannot check - backend offline');
          }
        });

        setOverallStatus('failed');
        setErrorMessage(
          'Cannot connect to backend API. The application cannot start without the backend.'
        );
        setCanContinue(false);
        hasErrors = true;
      }
    } catch (error: unknown) {
      console.error('Unexpected error during health checks:', error);
      setOverallStatus('failed');
      setErrorMessage('An unexpected error occurred during initialization.');
      setCanContinue(false);
    }
  };

  const getRecoverySuggestion = (checkName: string): string => {
    switch (checkName) {
      case 'FFmpeg':
        return 'FFmpeg is required for video rendering. Install FFmpeg and ensure it is in your PATH, or configure the path in Settings.';
      case 'Database':
        return 'Database connection failed. The application will create a new database. If the issue persists, check disk permissions.';
      case 'Required Directories':
        return 'Failed to create required directories. Check disk permissions and available space.';
      case 'Disk Space':
        return 'Insufficient disk space. Free up space on your drive to continue.';
      case 'Providers':
        return 'No AI providers are configured. You can continue in offline mode with limited functionality, or configure providers in Settings.';
      default:
        return 'Check the application logs for more details.';
    }
  };

  const handleRetry = () => {
    setChecks((prev) => prev.map((check) => ({ ...check, status: 'pending', message: undefined })));
    setOverallStatus('initializing');
    setErrorMessage('');
    setCanContinue(false);
    setTimeout(performHealthChecks, 100);
  };

  const handleContinue = () => {
    if (canContinue) {
      onComplete();
    }
  };

  const handleSafeMode = () => {
    if (onSafeModeRequested) {
      onSafeModeRequested();
    } else {
      onComplete();
    }
  };

  const getStatusIcon = (status: InitializationCheck['status']) => {
    switch (status) {
      case 'success':
        return (
          <CheckmarkCircle20Filled
            className={styles.checkIcon}
            style={{ color: tokens.colorPaletteGreenForeground1 }}
          />
        );
      case 'warning':
        return (
          <Warning20Filled
            className={styles.checkIcon}
            style={{ color: tokens.colorPaletteYellowForeground1 }}
          />
        );
      case 'error':
        return (
          <ErrorCircle20Filled
            className={styles.checkIcon}
            style={{ color: tokens.colorPaletteRedForeground1 }}
          />
        );
      case 'checking':
        return <Spinner className={styles.checkIcon} size="tiny" />;
      default:
        return <div className={styles.checkIcon} style={{ width: '20px', height: '20px' }} />;
    }
  };

  return (
    <div className={styles.container}>
      <Card className={styles.card}>
        <CardHeader
          header={
            <div className={styles.header}>
              <div className={styles.title}>Aura Video Studio</div>
              <div className={styles.subtitle}>
                {overallStatus === 'initializing' && 'Initializing application...'}
                {overallStatus === 'success' && 'All systems ready!'}
                {overallStatus === 'degraded' && 'Running in degraded mode'}
                {overallStatus === 'failed' && 'Initialization failed'}
              </div>
            </div>
          }
        />

        <div className={styles.progressContainer}>
          {checks.map((check) => (
            <div key={check.name} className={styles.checkItem}>
              {getStatusIcon(check.status)}
              <div className={styles.checkText}>
                <Text weight="semibold">{check.name}</Text>
                {check.message && (
                  <div>
                    <Text size={200}>{check.message}</Text>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>

        {overallStatus === 'failed' && (
          <div className={styles.errorContainer}>
            <div className={styles.errorTitle}>Initialization Failed</div>
            <div className={styles.errorDetails}>{errorMessage}</div>

            {checks
              .filter((c) => c.status === 'error' && c.suggestion)
              .map((check) => (
                <div key={check.name} style={{ marginBottom: tokens.spacingVerticalS }}>
                  <Text weight="semibold" size={200}>
                    {check.name}:
                  </Text>
                  <br />
                  <Text size={200}>{check.suggestion}</Text>
                </div>
              ))}

            <div className={styles.actionButtons}>
              <Button appearance="primary" onClick={handleRetry}>
                Retry
              </Button>
              <Button appearance="secondary" onClick={handleSafeMode}>
                Continue in Safe Mode
              </Button>
            </div>
          </div>
        )}

        {overallStatus === 'degraded' && canContinue && (
          <div className={styles.warningContainer}>
            <div className={styles.warningTitle}>Degraded Mode</div>
            <div className={styles.errorDetails}>{errorMessage}</div>

            {checks
              .filter((c) => c.status === 'warning' && c.suggestion)
              .map((check) => (
                <div key={check.name} style={{ marginBottom: tokens.spacingVerticalS }}>
                  <Text weight="semibold" size={200}>
                    {check.name}:
                  </Text>
                  <br />
                  <Text size={200}>{check.suggestion}</Text>
                </div>
              ))}

            <div className={styles.actionButtons}>
              <Button appearance="primary" onClick={handleContinue}>
                Continue Anyway
              </Button>
              <Button appearance="secondary" onClick={handleRetry}>
                Retry
              </Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}
