import { Badge, Tooltip, makeStyles } from '@fluentui/react-components';
import {
  CheckmarkCircle20Regular,
  ErrorCircle20Regular,
  Warning20Regular,
} from '@fluentui/react-icons';
import { useState, useEffect } from 'react';
import { getHealthSummary } from '../../services/api/healthApi';
import type { HealthSummaryResponse } from '../../types/api-v1';

const useStyles = makeStyles({
  badge: {
    cursor: 'pointer',
  },
});

export function HealthStatusBadge() {
  const styles = useStyles();
  const [healthStatus, setHealthStatus] = useState<
    'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown'
  >('Unknown');
  const [tooltip, setTooltip] = useState<string>('Checking system health...');

  useEffect(() => {
    checkHealth();

    // Poll health status every 30 seconds
    const interval = setInterval(checkHealth, 30000);

    return () => clearInterval(interval);
  }, []);

  const checkHealth = async () => {
    try {
      const summary: HealthSummaryResponse = await getHealthSummary();

      // Determine health status based on checks
      let status: 'Healthy' | 'Degraded' | 'Unhealthy' = 'Healthy';
      if (summary.failedChecks > 0) {
        status = 'Unhealthy';
      } else if (summary.warningChecks > 0) {
        status = 'Degraded';
      }

      setHealthStatus(status);

      if (status === 'Healthy') {
        setTooltip('All systems operational');
      } else if (status === 'Degraded') {
        setTooltip(`${summary.warningChecks} service(s) degraded. Click for details.`);
      } else {
        setTooltip(`${summary.failedChecks} service(s) failed. Click for details.`);
      }
    } catch (error: unknown) {
      setHealthStatus('Unhealthy');
      setTooltip('Cannot connect to backend. Click for details.');
    }
  };

  const handleClick = () => {
    window.location.href = '/health';
  };

  const getIcon = () => {
    switch (healthStatus) {
      case 'Healthy':
        return <CheckmarkCircle20Regular />;
      case 'Degraded':
        return <Warning20Regular />;
      case 'Unhealthy':
        return <ErrorCircle20Regular />;
      default:
        return <Warning20Regular />;
    }
  };

  const getAppearance = (): 'filled' | 'ghost' | 'outline' | 'tint' => {
    switch (healthStatus) {
      case 'Healthy':
        return 'tint';
      case 'Degraded':
        return 'tint';
      case 'Unhealthy':
        return 'filled';
      default:
        return 'ghost';
    }
  };

  const getColor = (): 'success' | 'warning' | 'danger' | 'subtle' => {
    switch (healthStatus) {
      case 'Healthy':
        return 'success';
      case 'Degraded':
        return 'warning';
      case 'Unhealthy':
        return 'danger';
      default:
        return 'subtle';
    }
  };

  return (
    <Tooltip content={tooltip} relationship="description">
      <Badge
        className={styles.badge}
        appearance={getAppearance()}
        color={getColor()}
        icon={getIcon()}
        onClick={handleClick}
      >
        {healthStatus}
      </Badge>
    </Tooltip>
  );
}
