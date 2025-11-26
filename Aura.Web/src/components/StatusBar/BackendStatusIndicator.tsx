import { Badge, Button, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import {
  PresenceAvailableRegular,
  PresenceBlockedRegular,
  PresenceUnknownRegular,
  ArrowClockwise16Regular,
  Warning16Regular,
} from '@fluentui/react-icons';
import { useMemo } from 'react';
import { env } from '@/config/env';
import { useBackendHealth } from '@/hooks/useBackendHealth';
import { useActionsDisabled } from '@/stores/connectionStore';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  tooltipContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    maxWidth: '280px',
  },
  label: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  meta: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
  },
  actionsDisabledWarning: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorPaletteYellowForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
});

const statusCopy = {
  online: {
    label: 'Backend Online',
    icon: PresenceAvailableRegular,
    color: 'success' as const,
  },
  offline: {
    label: 'Backend Offline',
    icon: PresenceBlockedRegular,
    color: 'danger' as const,
  },
  unknown: {
    label: 'Backend Status Unknown',
    icon: PresenceUnknownRegular,
    color: 'informative' as const,
  },
};

export function BackendStatusIndicator() {
  const styles = useStyles();
  const { status, diagnostics, bridge, error, lastChecked, refresh } = useBackendHealth();
  const actionsDisabled = useActionsDisabled();
  const statusKey = status ?? 'unknown';
  const copy = statusCopy[statusKey] ?? statusCopy.unknown;
  const Icon = copy.icon;
  const resolvedBaseUrl = bridge?.backend?.baseUrl ?? env.apiBaseUrl;
  const databaseInfo = diagnostics?.database;
  const migrationInfo = diagnostics?.database?.migration;

  const tooltip = useMemo(() => {
    // Create serverTimestamp inside useMemo to avoid recreating Date on every render
    const serverTimestamp = diagnostics ? new Date(diagnostics.timestamp) : null;

    return (
      <div className={styles.tooltipContent}>
        <span className={styles.label}>{copy.label}</span>
        {actionsDisabled && (
          <span className={styles.actionsDisabledWarning}>
            <Warning16Regular aria-hidden="true" /> Actions disabled - connection issues detected
          </span>
        )}
        {resolvedBaseUrl && <span className={styles.meta}>API Base: {resolvedBaseUrl}</span>}
        {bridge?.backend?.baseUrl && (
          <span className={styles.meta}>Desktop Bridge: {bridge.backend.baseUrl}</span>
        )}
        {diagnostics?.environment && (
          <span className={styles.meta}>Server Environment: {diagnostics.environment}</span>
        )}
        {diagnostics?.version && (
          <span className={styles.meta}>API Version: v{diagnostics.version}</span>
        )}
        {databaseInfo && (
          <span className={styles.meta}>
            Database: {databaseInfo.provider ?? 'SQLite'} •{' '}
            {databaseInfo.connected ? 'Connected' : 'Unavailable'}
            {migrationInfo && (
              <>
                {' '}
                · Migration {migrationInfo.current ?? 'n/a'}
                {typeof migrationInfo.pending === 'number' && migrationInfo.pending > 0
                  ? ` (${migrationInfo.pending} pending)`
                  : migrationInfo.isUpToDate
                    ? ' (up-to-date)'
                    : null}
              </>
            )}
          </span>
        )}
        {serverTimestamp && (
          <span className={styles.meta}>
            Server Time: {serverTimestamp.toLocaleString(undefined, { hour12: false })}
          </span>
        )}
        {lastChecked && (
          <span className={styles.meta}>Last checked: {lastChecked.toLocaleTimeString()}</span>
        )}
        {error && <span className={styles.meta}>Last error: {error}</span>}
      </div>
    );
  }, [
    actionsDisabled,
    bridge,
    copy.label,
    databaseInfo,
    diagnostics,
    error,
    lastChecked,
    migrationInfo,
    resolvedBaseUrl,
    styles.actionsDisabledWarning,
    styles.label,
    styles.meta,
    styles.tooltipContent,
  ]);

  return (
    <Tooltip content={tooltip} relationship="label">
      <div className={styles.container} role="status" aria-live="polite">
        <Badge
          appearance="tint"
          icon={<Icon aria-hidden="true" />}
          size="small"
          shape="rounded"
          color={copy.color}
        >
          {copy.label}
        </Badge>
        <Button
          icon={<ArrowClockwise16Regular />}
          appearance="subtle"
          size="small"
          aria-label="Refresh backend status"
          onClick={refresh}
        />
      </div>
    </Tooltip>
  );
}
