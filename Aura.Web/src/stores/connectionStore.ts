/**
 * Connection Status Store
 *
 * Manages global backend connection status across the application.
 * Used to:
 * - Track backend health status
 * - Enable/disable UI actions based on connectivity
 * - Provide connection status indicators
 */

import { create } from 'zustand';
import { loggingService } from '../services/loggingService';

export type ConnectionStatus = 'online' | 'offline' | 'checking' | 'unknown';

export interface ConnectionState {
  /** Current connection status */
  status: ConnectionStatus;
  /** Last error message if offline */
  lastError: string | null;
  /** Timestamp of last successful connection */
  lastConnected: Date | null;
  /** Timestamp of last status check */
  lastChecked: Date | null;
  /** Whether actions should be disabled due to connection issues */
  actionsDisabled: boolean;
  /** Number of consecutive failures */
  consecutiveFailures: number;
}

interface ConnectionStore extends ConnectionState {
  /** Set connection status to online */
  setOnline: () => void;
  /** Set connection status to offline with optional error message */
  setOffline: (error?: string) => void;
  /** Set connection status to checking */
  setChecking: () => void;
  /** Reset connection state */
  reset: () => void;
  /** Update last checked timestamp */
  updateLastChecked: () => void;
  /** Record a connection failure */
  recordFailure: (error?: string) => void;
  /** Record a connection success */
  recordSuccess: () => void;
}

const CONSECUTIVE_FAILURES_THRESHOLD = 3;

export const useConnectionStore = create<ConnectionStore>((set) => ({
  status: 'unknown',
  lastError: null,
  lastConnected: null,
  lastChecked: null,
  actionsDisabled: false,
  consecutiveFailures: 0,

  setOnline: () => {
    set({
      status: 'online',
      lastError: null,
      lastConnected: new Date(),
      lastChecked: new Date(),
      actionsDisabled: false,
      consecutiveFailures: 0,
    });
    loggingService.info('Backend connection restored', 'connectionStore', 'status');
  },

  setOffline: (error?: string) => {
    set((state) => {
      const newFailures = state.consecutiveFailures + 1;
      const shouldDisableActions = newFailures >= CONSECUTIVE_FAILURES_THRESHOLD;

      if (shouldDisableActions && !state.actionsDisabled) {
        loggingService.warn('Backend unreachable, disabling actions', 'connectionStore', 'status', {
          consecutiveFailures: newFailures,
          error,
        });
      }

      return {
        status: 'offline',
        lastError: error ?? 'Backend unreachable',
        lastChecked: new Date(),
        actionsDisabled: shouldDisableActions,
        consecutiveFailures: newFailures,
      };
    });
  },

  setChecking: () => {
    set({ status: 'checking' });
  },

  reset: () => {
    set({
      status: 'unknown',
      lastError: null,
      lastConnected: null,
      lastChecked: null,
      actionsDisabled: false,
      consecutiveFailures: 0,
    });
  },

  updateLastChecked: () => {
    set({ lastChecked: new Date() });
  },

  recordFailure: (error?: string) => {
    set((state) => {
      const newFailures = state.consecutiveFailures + 1;
      const shouldDisableActions = newFailures >= CONSECUTIVE_FAILURES_THRESHOLD;

      return {
        status: 'offline',
        lastError: error ?? state.lastError ?? 'Connection failed',
        lastChecked: new Date(),
        actionsDisabled: shouldDisableActions,
        consecutiveFailures: newFailures,
      };
    });
  },

  recordSuccess: () => {
    set({
      status: 'online',
      lastError: null,
      lastConnected: new Date(),
      lastChecked: new Date(),
      actionsDisabled: false,
      consecutiveFailures: 0,
    });
  },
}));

/**
 * Hook to check if actions should be disabled
 */
export function useActionsDisabled(): boolean {
  return useConnectionStore((state) => state.actionsDisabled);
}

/**
 * Hook to get connection status
 */
export function useConnectionStatus(): ConnectionStatus {
  return useConnectionStore((state) => state.status);
}
