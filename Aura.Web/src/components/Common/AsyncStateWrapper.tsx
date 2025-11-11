/**
 * AsyncStateWrapper Component
 * Handles loading, error, and empty states for async operations
 */

import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { ReactNode, memo } from 'react';
import { ErrorState } from '../Loading/ErrorState';
import { LoadingSpinner } from '../Loading/LoadingSpinner';

const useStyles = makeStyles({
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXXL,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  emptyIcon: {
    fontSize: '48px',
    color: tokens.colorNeutralForeground4,
  },
  container: {
    width: '100%',
    minHeight: '200px',
  },
});

export interface AsyncStateWrapperProps {
  loading?: boolean;
  error?: Error | string | null;
  isEmpty?: boolean;
  children: ReactNode;
  loadingMessage?: string;
  emptyMessage?: string;
  emptyIcon?: ReactNode;
  errorTitle?: string;
  onRetry?: () => void;
  minHeight?: string;
  loadingComponent?: ReactNode;
}

/**
 * Wrapper component that handles common async operation states
 * Automatically displays appropriate UI for loading, error, and empty states
 */
export const AsyncStateWrapper = memo(
  ({
    loading = false,
    error = null,
    isEmpty = false,
    children,
    loadingMessage = 'Loading...',
    emptyMessage = 'No data available',
    emptyIcon,
    errorTitle,
    onRetry,
    minHeight,
    loadingComponent,
  }: AsyncStateWrapperProps) => {
    const styles = useStyles();

    if (loading) {
      return (
        <div className={styles.container} style={{ minHeight }}>
          {loadingComponent || <LoadingSpinner label={loadingMessage} />}
        </div>
      );
    }

    if (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      return (
        <div className={styles.container} style={{ minHeight }}>
          <ErrorState
            message={errorMessage}
            title={errorTitle}
            onRetry={onRetry}
            withCard={false}
          />
        </div>
      );
    }

    if (isEmpty) {
      return (
        <div className={styles.emptyState} style={{ minHeight }}>
          {emptyIcon && <div className={styles.emptyIcon}>{emptyIcon}</div>}
          <Text>{emptyMessage}</Text>
        </div>
      );
    }

    return <>{children}</>;
  }
);

AsyncStateWrapper.displayName = 'AsyncStateWrapper';
