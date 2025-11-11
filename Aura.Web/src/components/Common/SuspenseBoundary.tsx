/**
 * SuspenseBoundary Component
 * Combines Suspense with ErrorBoundary for robust async component loading
 */

import { makeStyles, tokens, Button, Text } from '@fluentui/react-components';
import { ErrorCircle24Regular } from '@fluentui/react-icons';
import { Suspense, ReactNode, Component, ErrorInfo, memo } from 'react';
import { LoadingSpinner } from '../Loading/LoadingSpinner';

const useStyles = makeStyles({
  errorContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXXXL,
    gap: tokens.spacingVerticalL,
    textAlign: 'center',
    minHeight: '300px',
  },
  errorIcon: {
    fontSize: '64px',
    color: tokens.colorPaletteRedForeground1,
  },
  errorTitle: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  errorMessage: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground3,
    maxWidth: '500px',
  },
});

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
  onReset?: () => void;
}

class ErrorBoundaryClass extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('ErrorBoundary caught an error:', error, errorInfo);
  }

  handleReset = (): void => {
    this.setState({ hasError: false, error: null });
    this.props.onReset?.();
  };

  render(): ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return <ErrorFallback error={this.state.error} onReset={this.handleReset} />;
    }

    return this.props.children;
  }
}

interface ErrorFallbackProps {
  error: Error | null;
  onReset: () => void;
}

const ErrorFallback = memo(({ error, onReset }: ErrorFallbackProps) => {
  const styles = useStyles();

  return (
    <div className={styles.errorContainer} role="alert">
      <ErrorCircle24Regular className={styles.errorIcon} aria-hidden="true" />
      <div>
        <Text className={styles.errorTitle}>Failed to load component</Text>
        <Text className={styles.errorMessage} as="p">
          {error?.message || 'An unexpected error occurred while loading this component.'}
        </Text>
      </div>
      <Button appearance="primary" onClick={onReset}>
        Try Again
      </Button>
    </div>
  );
});

ErrorFallback.displayName = 'ErrorFallback';

export interface SuspenseBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
  loadingMessage?: string;
  errorFallback?: ReactNode;
  onReset?: () => void;
}

/**
 * Combined Suspense and ErrorBoundary component for robust async loading
 * Handles both loading states and errors during component lazy loading
 */
export const SuspenseBoundary = memo(
  ({
    children,
    fallback,
    loadingMessage = 'Loading...',
    errorFallback,
    onReset,
  }: SuspenseBoundaryProps) => {
    const defaultFallback = fallback || <LoadingSpinner label={loadingMessage} />;

    return (
      <ErrorBoundaryClass fallback={errorFallback} onReset={onReset}>
        <Suspense fallback={defaultFallback}>{children}</Suspense>
      </ErrorBoundaryClass>
    );
  }
);

SuspenseBoundary.displayName = 'SuspenseBoundary';
