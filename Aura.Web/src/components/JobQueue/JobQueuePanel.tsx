/**
 * JobQueuePanel Component
 * Compact sidebar panel for monitoring active jobs
 */

import {
  makeStyles,
  tokens,
  Body1,
  Body1Strong,
  Caption1,
  Button,
  Badge,
  ProgressBar,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Settings24Regular,
  CheckmarkCircle24Regular,
  ErrorCircle24Regular,
  Clock24Regular,
  Play24Regular,
} from '@fluentui/react-icons';
import { memo, useCallback, useMemo } from 'react';
import { useJobQueue } from '../../hooks/useJobQueue';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    padding: tokens.spacingVerticalM,
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  content: {
    flex: 1,
    overflowY: 'auto',
    padding: tokens.spacingVerticalM,
  },
  jobCard: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: tokens.spacingVerticalM,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  jobHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
  },
  jobInfo: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    flex: 1,
  },
  statusBadge: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  progressSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  progressInfo: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  statsBar: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    justifyContent: 'space-around',
  },
  statItem: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalXXS,
  },
  connectionStatus: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    fontSize: '10px',
  },
  connectionDot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
  },
});

interface JobQueuePanelProps {
  onSettingsClick?: () => void;
}

const JobQueuePanelComponent = ({ onSettingsClick }: JobQueuePanelProps) => {
  const styles = useStyles();
  const { statistics, isConnected, cancelJob, pendingJobs, processingJobs } = useJobQueue();

  const activeJobs = useMemo(
    () => [...processingJobs, ...pendingJobs].slice(0, 5),
    [processingJobs, pendingJobs]
  );

  const getStatusIcon = useCallback((status: string) => {
    switch (status) {
      case 'Completed':
        return <CheckmarkCircle24Regular style={{ color: tokens.colorPaletteGreenForeground1 }} />;
      case 'Failed':
      case 'Cancelled':
        return <ErrorCircle24Regular style={{ color: tokens.colorPaletteRedForeground1 }} />;
      case 'Processing':
        return <Play24Regular style={{ color: tokens.colorBrandForeground1 }} />;
      case 'Pending':
        return <Clock24Regular style={{ color: tokens.colorNeutralForeground3 }} />;
      default:
        return <Clock24Regular />;
    }
  }, []);

  const getStatusColor = useCallback((status: string) => {
    switch (status) {
      case 'Completed':
        return 'success';
      case 'Failed':
      case 'Cancelled':
        return 'danger';
      case 'Processing':
        return 'brand';
      case 'Pending':
        return 'subtle';
      default:
        return 'subtle';
    }
  }, []);

  const handleCancelJob = useCallback(
    async (jobId: string) => {
      try {
        await cancelJob(jobId);
      } catch (error) {
        console.error('Failed to cancel job:', error);
      }
    },
    [cancelJob]
  );

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <div>
          <Body1Strong>Job Queue</Body1Strong>
          <div className={styles.connectionStatus}>
            <div
              className={styles.connectionDot}
              style={{
                backgroundColor: isConnected
                  ? tokens.colorPaletteGreenForeground1
                  : tokens.colorPaletteRedForeground1,
              }}
            />
            <Caption1>{isConnected ? 'Connected' : 'Disconnected'}</Caption1>
          </div>
        </div>
        {onSettingsClick && (
          <Button
            appearance="subtle"
            size="small"
            icon={<Settings24Regular />}
            onClick={onSettingsClick}
            aria-label="Queue settings"
          />
        )}
      </div>

      <div className={styles.content}>
        {activeJobs.length === 0 ? (
          <div className={styles.emptyState}>
            <Clock24Regular style={{ fontSize: '48px' }} />
            <Body1>No active jobs</Body1>
            <Caption1>Jobs will appear here when they are queued</Caption1>
          </div>
        ) : (
          activeJobs.map((job) => (
            <div key={job.jobId} className={styles.jobCard}>
              <div className={styles.jobHeader}>
                <div className={styles.jobInfo}>
                  <div className={styles.statusBadge}>
                    {getStatusIcon(job.status)}
                    <Caption1>{job.correlationId || 'Video Job'}</Caption1>
                  </div>
                  <Badge size="small" appearance="tint" color={getStatusColor(job.status)}>
                    {job.status}
                  </Badge>
                </div>
                {(job.status === 'Pending' || job.status === 'Processing') && (
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<Dismiss24Regular />}
                    onClick={() => handleCancelJob(job.jobId)}
                    aria-label="Cancel job"
                  />
                )}
              </div>

              {job.status === 'Processing' && (
                <div className={styles.progressSection}>
                  <div className={styles.progressInfo}>
                    <Caption1>{job.progress}%</Caption1>
                    {job.currentStage && <Caption1>{job.currentStage}</Caption1>}
                  </div>
                  <ProgressBar value={job.progress / 100} />
                </div>
              )}

              {job.status === 'Failed' && job.errorMessage && (
                <Caption1 style={{ color: tokens.colorPaletteRedForeground1 }}>
                  Error: {job.errorMessage}
                </Caption1>
              )}
            </div>
          ))
        )}
      </div>

      {statistics && (
        <div className={styles.statsBar}>
          <div className={styles.statItem}>
            <Caption1>Pending</Caption1>
            <Body1Strong>{statistics.pendingJobs}</Body1Strong>
          </div>
          <div className={styles.statItem}>
            <Caption1>Processing</Caption1>
            <Body1Strong>{statistics.processingJobs}</Body1Strong>
          </div>
          <div className={styles.statItem}>
            <Caption1>Completed</Caption1>
            <Body1Strong>{statistics.completedJobs}</Body1Strong>
          </div>
          <div className={styles.statItem}>
            <Caption1>Failed</Caption1>
            <Body1Strong>{statistics.failedJobs}</Body1Strong>
          </div>
        </div>
      )}
    </div>
  );
};

export const JobQueuePanel = memo(JobQueuePanelComponent);
