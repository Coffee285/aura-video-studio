import {
  makeStyles,
  tokens,
  Card,
  Text,
  Title2,
  Title3,
  Button,
  ProgressBar,
  Spinner,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Checkmark24Regular,
  ErrorCircle24Regular,
  ArrowRight24Regular,
  Folder24Regular,
} from '@fluentui/react-icons';
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { openLogsFolder } from '../../services/api/errorHandler';
import { useJobsStore } from '../../state/jobs';
import { openFile, openFolder } from '../../utils/fileSystemUtils';
import { useNotifications } from '../Notifications/Toasts';
import { FailureModal } from './FailureModal';

const useStyles = makeStyles({
  panel: {
    position: 'fixed',
    top: 0,
    right: 0,
    width: '520px',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
    borderLeft: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: '-8px 0 32px rgba(0, 0, 0, 0.12), -2px 0 8px rgba(0, 0, 0, 0.08)',
    display: 'flex',
    flexDirection: 'column',
    zIndex: 1000,
    animation: 'slideInRight 0.4s cubic-bezier(0.4, 0, 0.2, 1)',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: `${tokens.spacingVerticalXL} ${tokens.spacingHorizontalXXL}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    background: `linear-gradient(135deg, ${tokens.colorNeutralBackground2} 0%, ${tokens.colorNeutralBackground1} 100%)`,
    boxShadow: '0 2px 8px rgba(0, 0, 0, 0.04)',
  },
  content: {
    flex: 1,
    overflowY: 'auto',
    padding: tokens.spacingVerticalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  step: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    animation: 'fadeIn 0.3s ease-out',
  },
  stepHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  stepIcon: {
    width: '40px',
    height: '40px',
    borderRadius: '50%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: tokens.colorNeutralBackground3,
    border: `2px solid ${tokens.colorNeutralStroke2}`,
    transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
    boxShadow: '0 2px 8px rgba(0, 0, 0, 0.1)',
  },
  stepIconActive: {
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    borderColor: tokens.colorBrandStroke1,
    boxShadow: `0 0 20px ${tokens.colorBrandBackground}50, 0 4px 12px rgba(0, 0, 0, 0.15)`,
    animation: 'pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite',
  },
  stepIconDone: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    borderColor: tokens.colorPaletteGreenBorder1,
    transform: 'scale(1.05)',
    boxShadow: `0 0 16px ${tokens.colorPaletteGreenBackground3}60, 0 4px 12px rgba(0, 0, 0, 0.1)`,
  },
  stepIconFailed: {
    backgroundColor: tokens.colorPaletteRedBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    borderColor: tokens.colorPaletteRedBorder1,
    animation: 'shake 0.5s cubic-bezier(0.4, 0, 0.2, 1)',
    boxShadow: `0 0 16px ${tokens.colorPaletteRedBackground3}60`,
  },
  logs: {
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusLarge,
    fontFamily: "'SF Mono', 'Monaco', 'Cascadia Code', 'Consolas', monospace",
    fontSize: '13px',
    lineHeight: '1.6',
    maxHeight: '240px',
    overflowY: 'auto',
    whiteSpace: 'pre-wrap',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: 'inset 0 2px 8px rgba(0, 0, 0, 0.06)',
    color: tokens.colorNeutralForeground1,
  },
  actions: {
    padding: `${tokens.spacingVerticalXL} ${tokens.spacingHorizontalXXL}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    justifyContent: 'flex-end',
    backgroundColor: tokens.colorNeutralBackground2,
    boxShadow: '0 -2px 8px rgba(0, 0, 0, 0.04)',
  },
  '@keyframes slideInRight': {
    '0%': {
      transform: 'translateX(100%)',
      opacity: 0,
    },
    '100%': {
      transform: 'translateX(0)',
      opacity: 1,
    },
  },
  '@keyframes pulse': {
    '0%, 100%': {
      opacity: 1,
      transform: 'scale(1)',
    },
    '50%': {
      opacity: 0.8,
      transform: 'scale(1.05)',
    },
  },
  '@keyframes shake': {
    '0%, 100%': { transform: 'translateX(0)' },
    '10%, 30%, 50%, 70%, 90%': { transform: 'translateX(-4px)' },
    '20%, 40%, 60%, 80%': { transform: 'translateX(4px)' },
  },
});

interface GenerationPanelProps {
  jobId: string;
  onClose: () => void;
}

const STAGES = ['Script', 'Voice', 'Visuals', 'Compose', 'Render', 'Complete'];

export function GenerationPanel({ jobId, onClose }: GenerationPanelProps) {
  const styles = useStyles();
  const { activeJob, getJob, getFailureDetails, startStreaming, stopStreaming } = useJobsStore();
  const { showSuccessToast, showFailureToast } = useNotifications();
  const navigate = useNavigate();
  const [showLogs, setShowLogs] = useState(false);
  const [notificationShown, setNotificationShown] = useState(false);
  const [showFailureModal, setShowFailureModal] = useState(false);

  useEffect(() => {
    // Start SSE streaming for real-time updates
    startStreaming(jobId);

    // Also fetch initial job state
    getJob(jobId);

    // Cleanup on unmount
    return () => {
      stopStreaming();
    };
  }, [jobId, getJob, startStreaming, stopStreaming]);

  // Show notification when job completes or fails
  useEffect(() => {
    if (!activeJob || notificationShown) return;

    if (activeJob.status === 'Done') {
      const duration =
        activeJob.finishedAt && activeJob.startedAt
          ? formatDuration(activeJob.startedAt, activeJob.finishedAt)
          : '';

      const outputPath = activeJob.outputPath;

      if (outputPath) {
        showSuccessToast({
          title: 'Render complete',
          message: `Your video has been generated successfully!`,
          duration,
          outputPath,
          onOpenFile: () => {
            openFile(outputPath);
          },
          onOpenFolder: () => {
            openFolder(outputPath);
          },
          onViewResults: () => {
            navigate('/projects');
            onClose();
          },
        });
      } else {
        showSuccessToast({
          title: 'Render complete',
          message: `Your video has been generated successfully!`,
          duration,
          onViewResults: () => {
            navigate('/projects');
            onClose();
          },
        });
      }
      setNotificationShown(true);
    } else if (activeJob.status === 'Failed') {
      // Fetch detailed failure information
      getFailureDetails(activeJob.id).then((failureDetails) => {
        if (failureDetails) {
          // Show detailed modal with failure information
          setShowFailureModal(true);
        } else {
          // Fallback to basic toast if failure details not available
          showFailureToast({
            title: 'Generation failed',
            message: activeJob.errorMessage || 'An error occurred during generation',
            correlationId: activeJob.correlationId,
            onRetry: () => {
              onClose();
            },
            onOpenLogs: openLogsFolder,
          });
        }
      });
      setNotificationShown(true);
    }
  }, [
    activeJob,
    notificationShown,
    showSuccessToast,
    showFailureToast,
    navigate,
    onClose,
    getFailureDetails,
  ]);

  const formatDuration = (startedAt: string, finishedAt: string) => {
    const start = new Date(startedAt);
    const end = new Date(finishedAt);
    const diffMs = end.getTime() - start.getTime();
    const minutes = Math.floor(diffMs / 60000);
    const seconds = Math.floor((diffMs % 60000) / 1000);
    return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
  };

  if (!activeJob) {
    return (
      <div className={styles.panel}>
        <div className={styles.header}>
          <Title2>Loading...</Title2>
        </div>
        <div className={styles.content}>
          <Spinner label="Loading job..." />
        </div>
      </div>
    );
  }

  const currentStageIndex = STAGES.indexOf(activeJob.stage);

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <Title2>Video Generation</Title2>
        <Button
          appearance="subtle"
          icon={<Dismiss24Regular />}
          onClick={onClose}
          aria-label="Close panel"
        />
      </div>

      <div className={styles.content}>
        {/* Progress Overview */}
        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <Text weight="semibold">{activeJob.stage}</Text>
                {activeJob.phase && (
                  <Text
                    size={200}
                    style={{
                      color: tokens.colorNeutralForeground3,
                      marginLeft: tokens.spacingHorizontalS,
                    }}
                  >
                    ({activeJob.phase})
                  </Text>
                )}
              </div>
              <Text size={200}>{activeJob.percent}%</Text>
            </div>
            <ProgressBar value={activeJob.percent / 100} />
            {activeJob.status === 'Running' && (
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                {activeJob.progressMessage ||
                  (activeJob.eta ? `ETA: ${activeJob.eta}` : 'Processing...')}
              </Text>
            )}
            {activeJob.status === 'Failed' && (
              <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
                {activeJob.errorMessage || 'Generation failed'}
              </Text>
            )}
          </div>
        </Card>

        {/* Stage Steps */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL }}>
          {STAGES.map((stage, index) => {
            const isActive = index === currentStageIndex;
            const isDone = index < currentStageIndex || activeJob.status === 'Done';
            const isFailed = activeJob.status === 'Failed' && isActive;

            return (
              <div key={stage} className={styles.step}>
                <div className={styles.stepHeader}>
                  <div
                    className={`${styles.stepIcon} ${
                      isFailed
                        ? styles.stepIconFailed
                        : isDone
                          ? styles.stepIconDone
                          : isActive
                            ? styles.stepIconActive
                            : ''
                    }`}
                  >
                    {isFailed ? (
                      <ErrorCircle24Regular />
                    ) : isDone ? (
                      <Checkmark24Regular />
                    ) : (
                      <Text>{index + 1}</Text>
                    )}
                  </div>
                  <div>
                    <Title3>{stage}</Title3>
                    {isActive && activeJob.status === 'Running' && (
                      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        In progress...
                      </Text>
                    )}
                    {isDone && !isFailed && (
                      <Text size={200} style={{ color: tokens.colorPaletteGreenForeground1 }}>
                        Complete
                      </Text>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Logs */}
        {activeJob.logs.length > 0 && (
          <Card>
            <div style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM }}>
              <Button appearance="transparent" onClick={() => setShowLogs(!showLogs)}>
                {showLogs ? 'Hide' : 'Show'} Logs ({activeJob.logs.length})
              </Button>
              {showLogs && (
                <div className={styles.logs}>{activeJob.logs.slice(-20).join('\n')}</div>
              )}
            </div>
          </Card>
        )}

        {/* Artifacts */}
        {activeJob.artifacts.length > 0 && (
          <Card>
            <div style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM }}>
              <Title3>Output Files</Title3>
              {activeJob.artifacts.map((artifact) => (
                <div
                  key={artifact.name}
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                  }}
                >
                  <div>
                    <Text weight="semibold">{artifact.name}</Text>
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                      {(artifact.sizeBytes / 1024 / 1024).toFixed(2)} MB
                    </Text>
                  </div>
                  <Button
                    appearance="secondary"
                    icon={<Folder24Regular />}
                    onClick={() => {
                      openFolder(artifact.path);
                    }}
                  >
                    Open folder
                  </Button>
                </div>
              ))}
            </div>
          </Card>
        )}
      </div>

      <div className={styles.actions}>
        {activeJob.status === 'Done' && (
          <Button appearance="primary" icon={<ArrowRight24Regular />} onClick={onClose}>
            Done
          </Button>
        )}
        {activeJob.status === 'Failed' && (
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        )}
      </div>

      {/* Failure Modal */}
      {activeJob.failureDetails && (
        <FailureModal
          open={showFailureModal}
          onClose={() => setShowFailureModal(false)}
          failure={activeJob.failureDetails}
          jobId={activeJob.id}
        />
      )}
    </div>
  );
}
