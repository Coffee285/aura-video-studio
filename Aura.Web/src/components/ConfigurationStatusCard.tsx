/**
 * Configuration Status Card
 *
 * Displays a visual checklist of configuration requirements with status indicators
 */

import {
  makeStyles,
  tokens,
  Card,
  Title3,
  Text,
  Button,
  Spinner,
} from '@fluentui/react-components';
import {
  CheckmarkCircle24Filled,
  DismissCircle24Filled,
  Warning24Filled,
  Settings24Regular,
  ArrowSync24Regular,
} from '@fluentui/react-icons';
import { useEffect, useState } from 'react';
import type { ConfigurationStatus } from '../services/configurationStatusService';
import { configurationStatusService } from '../services/configurationStatusService';

const useStyles = makeStyles({
  card: {
    padding: tokens.spacingVerticalL,
    marginBottom: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: tokens.spacingVerticalM,
  },
  title: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  headerActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
  checklistContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  checkItem: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalXS,
    borderRadius: tokens.borderRadiusMedium,
    transition: 'background-color 0.2s ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2,
    },
  },
  checkIcon: {
    fontSize: '24px',
    flexShrink: 0,
  },
  checkIconSuccess: {
    color: tokens.colorPaletteGreenForeground1,
  },
  checkIconError: {
    color: tokens.colorPaletteRedForeground1,
  },
  checkIconWarning: {
    color: tokens.colorPaletteYellowForeground1,
  },
  checkContent: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  checkLabel: {
    fontWeight: tokens.fontWeightSemibold,
  },
  checkDetail: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  issuesSection: {
    marginTop: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteRedBackground1,
    borderLeft: `4px solid ${tokens.colorPaletteRedBorder2}`,
  },
  issuesList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalS,
  },
  issueItem: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
  },
  statusBanner: {
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: tokens.spacingVerticalM,
    textAlign: 'center',
  },
  statusBannerReady: {
    backgroundColor: tokens.colorPaletteGreenBackground1,
    borderLeft: `4px solid ${tokens.colorPaletteGreenBorder1}`,
  },
  statusBannerNotReady: {
    backgroundColor: tokens.colorPaletteRedBackground1,
    borderLeft: `4px solid ${tokens.colorPaletteRedBorder1}`,
  },
});

interface ConfigurationCheckItem {
  id: string;
  label: string;
  detail?: string;
  status: 'success' | 'error' | 'warning';
}

export interface ConfigurationStatusCardProps {
  onConfigure?: () => void;
  showConfigureButton?: boolean;
  autoRefresh?: boolean;
}

export function ConfigurationStatusCard({
  onConfigure,
  showConfigureButton = true,
  autoRefresh = true,
}: ConfigurationStatusCardProps) {
  const styles = useStyles();
  const [status, setStatus] = useState<ConfigurationStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const loadStatus = async (forceRefresh = false) => {
    try {
      if (forceRefresh) {
        setRefreshing(true);
      }
      const newStatus = await configurationStatusService.getStatus(forceRefresh);
      setStatus(newStatus);
    } catch (error) {
      console.error('Failed to load configuration status:', error);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    loadStatus();

    // Subscribe to status changes
    const unsubscribe = configurationStatusService.subscribe(setStatus);

    // Set up auto-refresh if enabled
    let interval: NodeJS.Timeout | null = null;
    if (autoRefresh) {
      interval = setInterval(() => loadStatus(true), 60000); // Refresh every minute
    }

    return () => {
      unsubscribe();
      if (interval) clearInterval(interval);
    };
  }, [autoRefresh]);

  const handleRefresh = () => {
    loadStatus(true);
  };

  if (loading) {
    return (
      <Card className={styles.card}>
        <div style={{ textAlign: 'center', padding: tokens.spacingVerticalXL }}>
          <Spinner label="Loading configuration status..." />
        </div>
      </Card>
    );
  }

  if (!status) {
    return (
      <Card className={styles.card}>
        <Text>Failed to load configuration status</Text>
      </Card>
    );
  }

  const checks: ConfigurationCheckItem[] = [
    {
      id: 'provider',
      label: 'Provider Configured',
      detail:
        status.details.configuredProviders.length > 0
          ? `Using: ${status.details.configuredProviders.join(', ')}`
          : 'No AI provider configured',
      status: status.checks.providerConfigured ? 'success' : 'error',
    },
    {
      id: 'apiKeys',
      label: 'API Keys Validated',
      detail: status.checks.providerValidated
        ? 'All configured providers tested successfully'
        : 'API keys not validated',
      status: status.checks.apiKeysValid
        ? 'success'
        : status.checks.providerConfigured
          ? 'warning'
          : 'error',
    },
    {
      id: 'workspace',
      label: 'Workspace Created',
      detail: status.details.workspacePath
        ? `Location: ${status.details.workspacePath}`
        : 'Workspace not configured',
      status: status.checks.workspaceCreated ? 'success' : 'error',
    },
    {
      id: 'ffmpeg',
      label: 'FFmpeg Detected',
      detail: status.details.ffmpegVersion
        ? `Version ${status.details.ffmpegVersion} at ${status.details.ffmpegPath}`
        : 'FFmpeg not found',
      status: status.checks.ffmpegDetected ? 'success' : 'error',
    },
  ];

  const getIcon = (itemStatus: 'success' | 'error' | 'warning') => {
    switch (itemStatus) {
      case 'success':
        return (
          <CheckmarkCircle24Filled className={`${styles.checkIcon} ${styles.checkIconSuccess}`} />
        );
      case 'error':
        return <DismissCircle24Filled className={`${styles.checkIcon} ${styles.checkIconError}`} />;
      case 'warning':
        return <Warning24Filled className={`${styles.checkIcon} ${styles.checkIconWarning}`} />;
    }
  };

  return (
    <Card className={styles.card}>
      <div className={styles.header}>
        <div className={styles.title}>
          <Settings24Regular style={{ fontSize: '24px' }} />
          <Title3>Configuration Status</Title3>
        </div>
        <div className={styles.headerActions}>
          <Button
            appearance="subtle"
            icon={<ArrowSync24Regular />}
            onClick={handleRefresh}
            disabled={refreshing}
          >
            {refreshing ? 'Refreshing...' : 'Refresh'}
          </Button>
          {showConfigureButton && onConfigure && (
            <Button appearance="primary" onClick={onConfigure}>
              {status.isConfigured ? 'Reconfigure' : 'Configure Now'}
            </Button>
          )}
        </div>
      </div>

      <div className={styles.checklistContainer}>
        {checks.map((check) => (
          <div key={check.id} className={styles.checkItem}>
            {getIcon(check.status)}
            <div className={styles.checkContent}>
              <Text className={styles.checkLabel}>{check.label}</Text>
              {check.detail && <Text className={styles.checkDetail}>{check.detail}</Text>}
            </div>
          </div>
        ))}
      </div>

      {status.issues && status.issues.length > 0 && (
        <div className={styles.issuesSection}>
          <Text weight="semibold">⚠️ Issues Found:</Text>
          <div className={styles.issuesList}>
            {status.issues.map((issue, index) => (
              <div key={index} className={styles.issueItem}>
                <Text>•</Text>
                <Text>{issue.message}</Text>
              </div>
            ))}
          </div>
        </div>
      )}

      <Text
        size={200}
        style={{
          marginTop: tokens.spacingVerticalM,
          color: tokens.colorNeutralForeground3,
          display: 'block',
        }}
      >
        Last checked: {new Date(status.lastChecked).toLocaleString()}
      </Text>
    </Card>
  );
}
