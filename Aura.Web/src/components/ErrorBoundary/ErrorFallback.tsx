/**
 * Error Fallback Component
 * Enhanced UI shown when the error boundary catches an error
 * Displays friendly error message with recovery options
 */

import {
  Button,
  Title1,
  Body1,
  Caption1,
  makeStyles,
  tokens,
  Link,
} from '@fluentui/react-components';
import {
  ErrorCircle24Regular,
  ArrowClockwise24Regular,
  Send24Regular,
  ChevronDown24Regular,
  ChevronUp24Regular,
  DocumentSave24Regular,
  ArrowLeft24Regular,
} from '@fluentui/react-icons';
import React, { useState, useEffect } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { autoSaveService } from '../../services/autoSaveService';
import { loggingService } from '../../services/loggingService';
import { getErrorInfo } from '../../utils/errorCodes';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '400px',
    padding: '2rem',
    textAlign: 'center',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  icon: {
    color: tokens.colorPaletteRedBorder1,
    marginBottom: '1rem',
    fontSize: '64px',
  },
  title: {
    marginBottom: '0.5rem',
    color: tokens.colorNeutralForeground1,
  },
  message: {
    marginBottom: '1rem',
    color: tokens.colorNeutralForeground2,
    maxWidth: '600px',
  },
  recoveryInfo: {
    marginBottom: '1.5rem',
    padding: '1rem',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground1,
    maxWidth: '600px',
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
  },
  actions: {
    display: 'flex',
    gap: '1rem',
    marginBottom: '1rem',
    flexWrap: 'wrap',
    justifyContent: 'center',
  },
  detailsSection: {
    marginTop: '2rem',
    width: '100%',
    maxWidth: '800px',
  },
  detailsToggle: {
    marginBottom: '0.5rem',
  },
  details: {
    padding: '1rem',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    textAlign: 'left',
    maxHeight: '300px',
    overflowY: 'auto',
    fontFamily: 'monospace',
    fontSize: '0.875rem',
  },
  helpText: {
    marginTop: '1rem',
    color: tokens.colorNeutralForeground3,
    fontSize: '0.875rem',
  },
  errorCode: {
    padding: '0.5rem 1rem',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    fontFamily: 'monospace',
    fontSize: '0.875rem',
    marginBottom: '1rem',
    color: tokens.colorNeutralForeground2,
  },
  suggestions: {
    textAlign: 'left',
    padding: '1rem',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: '1rem',
    maxWidth: '600px',
  },
  suggestionsList: {
    paddingLeft: '1.5rem',
    margin: '0.5rem 0 0 0',
  },
});

export interface ErrorFallbackProps {
  error: Error;
  errorInfo: React.ErrorInfo;
  onReset: () => void;
  onReport: () => void;
}

export function ErrorFallback({ error, errorInfo, onReset, onReport }: ErrorFallbackProps) {
  const styles = useStyles();
  const navigate = useNavigate();
  const location = useLocation();
  const [showDetails, setShowDetails] = useState(false);
  const [hasAutosave, setHasAutosave] = useState(false);
  const [autosaveVersion, setAutosaveVersion] = useState<number | null>(null);

  // Get error code and information
  const errorData = getErrorInfo(error);
  const { code: errorCode, correlationId } = errorData;

  useEffect(() => {
    // Check if there's recoverable autosave data
    const hasRecovery = autoSaveService.hasRecoverableData();
    setHasAutosave(hasRecovery);

    if (hasRecovery) {
      const metadata = autoSaveService.getMetadata();
      if (metadata) {
        setAutosaveVersion(metadata.currentVersion);
      }
    }
  }, []);

  const handleReload = () => {
    loggingService.info('User reloading after error', 'ErrorFallback', 'reload');
    window.location.reload();
  };

  const handleTryAgain = () => {
    loggingService.info('User attempting to recover from error', 'ErrorFallback', 'tryAgain');
    onReset();
  };

  const handleRestoreAutosave = () => {
    loggingService.info('User restoring from autosave', 'ErrorFallback', 'restoreAutosave');
    // Reload the page - the autosave will be detected on next load
    window.location.reload();
  };

  const handleGoBack = () => {
    loggingService.info('User going back after error', 'ErrorFallback', 'goBack');
    // If we're on the onboarding/downloads page, go to onboarding
    if (location.pathname.includes('/downloads') || location.pathname.includes('/onboarding')) {
      navigate('/onboarding', { replace: true });
    } else {
      // Check if there's history to go back to, otherwise go home
      if (window.history.length > 1) {
        navigate(-1);
      } else {
        navigate('/', { replace: true });
      }
    }
    onReset();
  };

  return (
    <div className={styles.container}>
      <ErrorCircle24Regular className={styles.icon} />
      <Title1 className={styles.title}>{errorCode.title}</Title1>
      <Body1 className={styles.message}>{errorCode.userMessage}</Body1>

      <div className={styles.errorCode}>
        Error Code: {errorCode.code} | Correlation ID: {correlationId.substring(0, 8)}
      </div>

      {errorCode.recoverySuggestions.length > 0 && (
        <div className={styles.suggestions}>
          <Body1 style={{ fontWeight: 600, marginBottom: '0.5rem' }}>Recovery Suggestions:</Body1>
          <ul className={styles.suggestionsList}>
            {errorCode.recoverySuggestions.map((suggestion, index) => (
              <li key={index}>
                <Caption1>{suggestion}</Caption1>
              </li>
            ))}
          </ul>
        </div>
      )}

      {hasAutosave && autosaveVersion && (
        <div className={styles.recoveryInfo}>
          <DocumentSave24Regular />
          <Caption1>
            Good news! We found an auto-saved version of your project (version {autosaveVersion}).
            Click &quot;Restore Auto-save&quot; to recover your work.
          </Caption1>
        </div>
      )}

      <div className={styles.actions}>
        {hasAutosave && (
          <Button
            appearance="primary"
            icon={<DocumentSave24Regular />}
            onClick={handleRestoreAutosave}
          >
            Restore Auto-save
          </Button>
        )}
        <Button
          appearance={hasAutosave ? 'secondary' : 'primary'}
          icon={<ArrowLeft24Regular />}
          onClick={handleGoBack}
        >
          Go Back
        </Button>
        <Button appearance="secondary" icon={<ArrowClockwise24Regular />} onClick={handleTryAgain}>
          Try Again
        </Button>
        <Button appearance="secondary" onClick={handleReload}>
          Reload Page
        </Button>
        <Button appearance="secondary" icon={<Send24Regular />} onClick={onReport}>
          Report Bug
        </Button>
      </div>

      <div className={styles.detailsSection}>
        <Button
          appearance="subtle"
          icon={showDetails ? <ChevronUp24Regular /> : <ChevronDown24Regular />}
          onClick={() => setShowDetails(!showDetails)}
          className={styles.detailsToggle}
        >
          {showDetails ? 'Hide Error Details' : 'Show Error Details'}
        </Button>
        {showDetails && (
          <div className={styles.details}>
            <div>
              <strong>Error: </strong> {error.name}
            </div>
            <div>
              <strong>Message: </strong> {error.message}
            </div>
            {error.stack && (
              <div>
                <strong>Stack Trace:</strong>
                <pre style={{ whiteSpace: 'pre-wrap', marginTop: '0.5rem' }}>{error.stack}</pre>
              </div>
            )}
            {errorInfo.componentStack && (
              <div>
                <strong>Component Stack:</strong>
                <pre style={{ whiteSpace: 'pre-wrap', marginTop: '0.5rem' }}>
                  {errorInfo.componentStack}
                </pre>
              </div>
            )}
          </div>
        )}
      </div>

      <Caption1 className={styles.helpText}>
        If this problem persists, please{' '}
        <Link href="https://github.com/Saiyan9001/aura-video-studio/issues" target="_blank">
          report it on GitHub
        </Link>{' '}
        or contact support.
      </Caption1>
    </div>
  );
}
