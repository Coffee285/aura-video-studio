import {
  Card,
  CardHeader,
  Button,
  Text,
  Badge,
  Spinner,
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from '@fluentui/react-components';
import {
  Warning24Regular,
  Checkmark24Regular,
  ArrowClockwise24Regular,
  Dismiss24Regular,
} from '@fluentui/react-icons';
import { useState, useEffect } from 'react';
import { apiUrl } from '../config/api';

const useStyles = makeStyles({
  card: {
    marginBottom: tokens.spacingVerticalM,
    boxShadow: tokens.shadow8,
  },
  checksList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  checkItem: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  checkItemFailed: {
    backgroundColor: tokens.colorPaletteRedBackground1,
  },
  checkIcon: {
    marginTop: '2px',
  },
  checkDetails: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  checkName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  checkMessage: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  errorsList: {
    marginTop: tokens.spacingVerticalS,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  actions: {
    marginTop: tokens.spacingVerticalM,
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
});

export interface SubCheck {
  name: string;
  status: 'healthy' | 'degraded' | 'unhealthy';
  message?: string;
  details?: Record<string, unknown>;
}

export interface HealthCheckResult {
  status: 'healthy' | 'degraded' | 'unhealthy';
  checks: SubCheck[];
  errors: string[];
}

interface SystemCheckCardProps {
  onDismiss?: () => void;
  autoRetry?: boolean;
  retryInterval?: number; // milliseconds
}

export function SystemCheckCard({
  onDismiss,
  autoRetry = true,
  retryInterval = 30000,
}: SystemCheckCardProps) {
  const styles = useStyles();
  const [healthStatus, setHealthStatus] = useState<HealthCheckResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastChecked, setLastChecked] = useState<Date | null>(null);

  const checkHealth = async () => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(apiUrl('/api/health/ready'));
      const data = await response.json();

      setHealthStatus(data);
      setLastChecked(new Date());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to check system health');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    checkHealth();

    // Set up auto-retry if enabled and system is unhealthy
    if (autoRetry) {
      const interval = setInterval(() => {
        if (healthStatus?.status === 'unhealthy') {
          checkHealth();
        }
      }, retryInterval);

      return () => clearInterval(interval);
    }
  }, [autoRetry, retryInterval, healthStatus?.status]);

  if (loading && !healthStatus) {
    return (
      <Card className={styles.card}>
        <CardHeader
          header={<Text weight="semibold">System Check</Text>}
          description={<Spinner size="tiny" label="Checking system health..." />}
        />
      </Card>
    );
  }

  if (error) {
    return (
      <Card className={styles.card}>
        <CardHeader
          header={
            <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
              <Warning24Regular color={tokens.colorPaletteRedForeground1} />
              <Text weight="semibold">System Check Failed</Text>
            </div>
          }
          description={<Text size={200}>{error}</Text>}
          action={
            <Button appearance="subtle" icon={<ArrowClockwise24Regular />} onClick={checkHealth}>
              Retry
            </Button>
          }
        />
      </Card>
    );
  }

  if (!healthStatus) {
    return null;
  }

  // Only show card if there are issues or if explicitly requested
  if (healthStatus.status === 'healthy' && !onDismiss) {
    return null;
  }

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'healthy':
        return (
          <Badge color="success" appearance="filled">
            Healthy
          </Badge>
        );
      case 'degraded':
        return (
          <Badge color="warning" appearance="filled">
            Degraded
          </Badge>
        );
      case 'unhealthy':
        return (
          <Badge color="danger" appearance="filled">
            Unhealthy
          </Badge>
        );
      default:
        return <Badge color="subtle">Unknown</Badge>;
    }
  };

  const getCheckIcon = (status: string) => {
    switch (status) {
      case 'healthy':
        return (
          <Checkmark24Regular
            className={styles.checkIcon}
            color={tokens.colorPaletteGreenForeground1}
          />
        );
      case 'degraded':
        return (
          <Warning24Regular
            className={styles.checkIcon}
            color={tokens.colorPaletteYellowForeground1}
          />
        );
      case 'unhealthy':
        return (
          <Warning24Regular
            className={styles.checkIcon}
            color={tokens.colorPaletteRedForeground1}
          />
        );
      default:
        return null;
    }
  };

  const failedChecks = healthStatus.checks.filter((c) => c.status !== 'healthy');
  const showDetails = healthStatus.status !== 'healthy';

  return (
    <Card className={styles.card}>
      <CardHeader
        header={
          <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
            {healthStatus.status === 'unhealthy' && (
              <Warning24Regular color={tokens.colorPaletteRedForeground1} />
            )}
            <Text weight="semibold">System Health</Text>
            {getStatusBadge(healthStatus.status)}
          </div>
        }
        description={
          lastChecked && <Text size={200}>Last checked: {lastChecked.toLocaleTimeString()}</Text>
        }
        action={
          <div style={{ display: 'flex', gap: tokens.spacingHorizontalS }}>
            <Button
              appearance="subtle"
              icon={<ArrowClockwise24Regular />}
              onClick={checkHealth}
              disabled={loading}
            >
              {loading ? 'Checking...' : 'Refresh'}
            </Button>
            {onDismiss && healthStatus.status === 'degraded' && (
              <Button appearance="subtle" icon={<Dismiss24Regular />} onClick={onDismiss}>
                Dismiss
              </Button>
            )}
          </div>
        }
      />

      {healthStatus.status === 'unhealthy' && healthStatus.errors.length > 0 && (
        <div style={{ padding: `0 ${tokens.spacingHorizontalM}` }}>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Critical Issues Detected</MessageBarTitle>
              The application cannot function properly until these issues are resolved.
            </MessageBarBody>
          </MessageBar>
        </div>
      )}

      {showDetails && (
        <div style={{ padding: tokens.spacingHorizontalM }}>
          <div className={styles.checksList}>
            {failedChecks.map((check) => (
              <div
                key={check.name}
                className={`${styles.checkItem} ${
                  check.status === 'unhealthy' ? styles.checkItemFailed : ''
                }`}
              >
                {getCheckIcon(check.status)}
                <div className={styles.checkDetails}>
                  <div
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: tokens.spacingHorizontalS,
                    }}
                  >
                    <Text className={styles.checkName}>{check.name}</Text>
                    {getStatusBadge(check.status)}
                  </div>
                  {check.message && <Text className={styles.checkMessage}>{check.message}</Text>}
                  {check.details && Object.keys(check.details).length > 0 && (
                    <details style={{ marginTop: tokens.spacingVerticalXS }}>
                      <summary style={{ cursor: 'pointer', fontSize: tokens.fontSizeBase200 }}>
                        <Text size={200}>View details</Text>
                      </summary>
                      <pre
                        style={{
                          fontSize: tokens.fontSizeBase200,
                          marginTop: tokens.spacingVerticalXS,
                          padding: tokens.spacingVerticalS,
                          backgroundColor: tokens.colorNeutralBackground1,
                          borderRadius: tokens.borderRadiusSmall,
                          overflow: 'auto',
                        }}
                      >
                        {JSON.stringify(check.details, null, 2)}
                      </pre>
                    </details>
                  )}
                </div>
              </div>
            ))}
          </div>

          {healthStatus.status === 'unhealthy' && (
            <div className={styles.actions}>
              <Button appearance="primary" onClick={() => window.open('/downloads', '_self')}>
                Fix Issues
              </Button>
              <Button appearance="secondary" onClick={() => window.open('/settings', '_self')}>
                Settings
              </Button>
            </div>
          )}
        </div>
      )}
    </Card>
  );
}
