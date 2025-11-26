import {
  makeStyles,
  tokens,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Button,
  Text,
  Title3,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Copy24Regular,
  Folder24Regular,
  Wrench24Regular,
  Settings24Regular,
} from '@fluentui/react-icons';
import { useState } from 'react';
import { openLogsFolder } from '../../services/api/errorHandler';
import { JobFailure } from '../../state/jobs';
import { useNotifications } from '../Notifications/Toasts';
import { navigateToRoute } from '@/utils/navigation';
const useStyles = makeStyles({
  dialogContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  code: {
    backgroundColor: tokens.colorNeutralBackground3,
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    fontFamily: 'monospace',
    fontSize: '12px',
    maxHeight: '200px',
    overflowY: 'auto',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  correlationId: {
    fontFamily: 'monospace',
    fontSize: '13px',
    padding: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusSmall,
    display: 'inline-block',
  },
  actionsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalL,
  },
  actionItem: {
    listStyleType: 'disc',
  },
});

interface FailureModalProps {
  open: boolean;
  onClose: () => void;
  failure: JobFailure;
  jobId: string;
}

export function FailureModal({ open, onClose, failure, jobId: _jobId }: FailureModalProps) {
  const styles = useStyles();
  const { showSuccessToast, showFailureToast } = useNotifications();
  const [copied, setCopied] = useState(false);
  const [repairing, setRepairing] = useState(false);

  const handleCopyCorrelationId = () => {
    navigator.clipboard.writeText(failure.correlationId);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleViewFullLog = () => {
    if (failure.logPath) {
      // Try to open the log file directly
      window.open(`file:///${failure.logPath.replace(/\\/g, '/')}`, '_blank');
    } else {
      // Fallback to opening logs folder
      openLogsFolder();
    }
  };

  const handleRetry = () => {
    onClose();
  };

  const handleInstallFFmpeg = async () => {
    setRepairing(true);
    try {
      const response = await fetch('/api/ffmpeg/install', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ version: null }),
      });

      if (response.ok) {
        const result = await response.json();
        showSuccessToast({
          title: 'FFmpeg Installed',
          message:
            result.message ||
            'Managed FFmpeg has been installed successfully. You can now retry video generation.',
        });
        onClose();
      } else {
        const errorData = await response.json();
        showFailureToast({
          title: 'Installation Failed',
          message:
            errorData.message || errorData.detail || 'Failed to install FFmpeg. Please check logs.',
        });
      }
    } catch (error) {
      console.error('Error installing FFmpeg:', error);
      showFailureToast({
        title: 'Installation Error',
        message: 'Error installing FFmpeg. Please check network connection and try again.',
      });
    } finally {
      setRepairing(false);
    }
  };

  const handleAttachFFmpeg = async () => {
    navigateToRoute('/dependencies');
  };

  const isFFmpegError =
    failure.errorCode?.includes('FFMPEG') || failure.message.toLowerCase().includes('ffmpeg');

  return (
    <Dialog open={open} onOpenChange={(_, data) => data.open || onClose()}>
      <DialogSurface style={{ maxWidth: '600px' }}>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              icon={<Dismiss24Regular />}
              onClick={onClose}
              aria-label="Close dialog"
            />
          }
        >
          Generation Failed
        </DialogTitle>
        <DialogBody>
          <DialogContent className={styles.dialogContent}>
            {/* Error Message */}
            <div className={styles.section}>
              <Title3>Error</Title3>
              <Text>{failure.message}</Text>
              {failure.errorCode && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                  Error Code: {failure.errorCode}
                </Text>
              )}
            </div>

            {/* Correlation ID */}
            <div className={styles.section}>
              <Title3>Correlation ID</Title3>
              <div
                style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}
              >
                <span className={styles.correlationId}>{failure.correlationId}</span>
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<Copy24Regular />}
                  onClick={handleCopyCorrelationId}
                >
                  {copied ? 'Copied!' : 'Copy'}
                </Button>
              </div>
            </div>

            {/* Stderr Snippet */}
            {failure.stderrSnippet && (
              <div className={styles.section}>
                <Title3>Error Output (last 16KB)</Title3>
                <div className={styles.code}>{failure.stderrSnippet}</div>
              </div>
            )}

            {/* Install Log Snippet */}
            {failure.installLogSnippet && (
              <div className={styles.section}>
                <Title3>Install Log</Title3>
                <div className={styles.code}>{failure.installLogSnippet}</div>
              </div>
            )}

            {/* Suggested Actions */}
            {failure.suggestedActions && failure.suggestedActions.length > 0 && (
              <div className={styles.section}>
                <Title3>Suggested Actions</Title3>
                <ul className={styles.actionsList}>
                  {failure.suggestedActions.map((action, index) => (
                    <li key={index} className={styles.actionItem}>
                      <Text>{action}</Text>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </DialogContent>
        </DialogBody>
        <DialogActions>
          {isFFmpegError && (
            <>
              <Button
                appearance="primary"
                icon={<Wrench24Regular />}
                onClick={handleInstallFFmpeg}
                disabled={repairing}
              >
                {repairing ? 'Installing...' : 'Install FFmpeg'}
              </Button>
              <Button
                appearance="secondary"
                icon={<Settings24Regular />}
                onClick={handleAttachFFmpeg}
              >
                Attach FFmpeg
              </Button>
            </>
          )}
          <Button appearance="secondary" icon={<Folder24Regular />} onClick={handleViewFullLog}>
            View Full Log
          </Button>
          <Button appearance="secondary" icon={<Dismiss24Regular />} onClick={handleRetry}>
            Dismiss
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
}
