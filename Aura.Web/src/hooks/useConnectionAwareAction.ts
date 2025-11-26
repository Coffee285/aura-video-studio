/**
 * useConnectionAwareAction Hook
 *
 * Provides utilities to disable UI actions when the backend is unreachable.
 * Handles graceful degradation and provides user feedback.
 */

import { useCallback } from 'react';
import { useActionsDisabled, useConnectionStatus } from '../stores/connectionStore';

interface ConnectionAwareActionOptions {
  /** Custom message to show when action is disabled */
  disabledMessage?: string;
  /** Whether to show a toast when action is attempted while disabled */
  showToast?: boolean;
  /** Allow the action even when offline (for local-only operations) */
  allowOffline?: boolean;
}

interface ConnectionAwareActionResult {
  /** Whether the action should be disabled */
  isDisabled: boolean;
  /** Current connection status */
  connectionStatus: 'online' | 'offline' | 'checking' | 'unknown';
  /** Whether backend is currently reachable */
  isConnected: boolean;
  /** Message to display when disabled (for tooltips, etc.) */
  disabledMessage: string;
  /** Wrapper function that checks connection before executing action */
  wrapAction: <T extends (...args: unknown[]) => unknown>(action: T) => T;
}

/**
 * Hook to make UI actions connection-aware
 *
 * @example
 * ```tsx
 * function GenerateButton() {
 *   const { isDisabled, disabledMessage, wrapAction } = useConnectionAwareAction();
 *
 *   const handleGenerate = wrapAction(async () => {
 *     await generateVideo();
 *   });
 *
 *   return (
 *     <Button
 *       onClick={handleGenerate}
 *       disabled={isDisabled}
 *       title={isDisabled ? disabledMessage : undefined}
 *     >
 *       Generate Video
 *     </Button>
 *   );
 * }
 * ```
 */
export function useConnectionAwareAction(
  options: ConnectionAwareActionOptions = {}
): ConnectionAwareActionResult {
  const { disabledMessage = 'Action unavailable - backend is unreachable', allowOffline = false } =
    options;

  const actionsDisabled = useActionsDisabled();
  const connectionStatus = useConnectionStatus();

  const isConnected = connectionStatus === 'online';
  const isDisabled = !allowOffline && actionsDisabled;

  const wrapAction = useCallback(
    <T extends (...args: unknown[]) => unknown>(action: T): T => {
      const wrappedAction = (...args: unknown[]) => {
        if (isDisabled) {
          console.warn('[ConnectionAwareAction] Action blocked - backend unreachable');
          return undefined;
        }
        return action(...args);
      };
      return wrappedAction as T;
    },
    [isDisabled]
  );

  return {
    isDisabled,
    connectionStatus,
    isConnected,
    disabledMessage,
    wrapAction,
  };
}

/**
 * Simple hook to check if backend is connected
 */
export function useIsBackendConnected(): boolean {
  const status = useConnectionStatus();
  return status === 'online';
}
