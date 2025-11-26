import { useCallback, useEffect, useState } from 'react';
import { apiUrl } from '@/config/api';
import { loggingService } from '@/services/loggingService';
import { useConnectionStore } from '@/stores/connectionStore';
import type { DesktopBridgeDiagnostics } from '@/types/ambient/window';

export type BackendHealthStatus = 'online' | 'offline';

interface DatabaseMigrationStatus {
  current?: string | null;
  latest?: string | null;
  pending?: number;
  isUpToDate?: boolean;
}

interface DatabaseHealthSnapshot {
  connected?: boolean;
  provider?: string;
  dataSource?: string | null;
  responseTimeMs?: number | null;
  error?: string | null;
  migration?: DatabaseMigrationStatus;
}

interface ApiHealthResponse {
  status: string;
  version?: string;
  environment?: string;
  machineName?: string;
  osPlatform?: string;
  osVersion?: string;
  architecture?: string;
  timestamp: string;
  database?: DatabaseHealthSnapshot;
}

export interface BackendHealthSnapshot {
  status: BackendHealthStatus;
  diagnostics: ApiHealthResponse | null;
  bridge: DesktopBridgeDiagnostics | null;
  error?: string | null;
  lastChecked: Date | null;
}

export function useBackendHealth(pollIntervalMs = 15000) {
  const { recordSuccess, recordFailure } = useConnectionStore();

  const getCachedDiagnostics = () =>
    typeof window !== 'undefined'
      ? (window.aura?.runtime?.getCachedDiagnostics?.() ??
        window.desktopBridge?.getCachedDiagnostics?.() ??
        null)
      : null;

  const [snapshot, setSnapshot] = useState<BackendHealthSnapshot>({
    status: 'offline',
    diagnostics: null,
    bridge: getCachedDiagnostics(),
    error: null,
    lastChecked: null,
  });

  const checkHealth = useCallback(
    async (signal: AbortSignal) => {
      try {
        const response = await fetch(apiUrl('/api/health'), {
          headers: {
            'Cache-Control': 'no-cache',
          },
          signal,
        });

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const diagnostics = (await response.json()) as ApiHealthResponse;
        const desktopDiagnostics = getCachedDiagnostics();

        // Update connection store with success
        recordSuccess();

        setSnapshot({
          status: 'online',
          diagnostics,
          bridge: desktopDiagnostics,
          error: null,
          lastChecked: new Date(),
        });
      } catch (error) {
        // Ignore abort errors (component unmounted or cleanup)
        if (error instanceof Error && error.name === 'AbortError') {
          return;
        }

        const err = error instanceof Error ? error : new Error(String(error));
        loggingService.warn('Backend health check failed', 'useBackendHealth', 'poll', {
          message: err.message,
        });

        // Update connection store with failure
        recordFailure(err.message);

        setSnapshot((prev) => ({
          status: 'offline',
          diagnostics: prev.diagnostics,
          bridge: prev.bridge,
          error: err.message,
          lastChecked: new Date(),
        }));
      }
    },
    [recordSuccess, recordFailure]
  );

  useEffect(() => {
    let isMounted = true;
    const abortController = new AbortController();

    const run = () => {
      if (!isMounted) {
        return;
      }
      checkHealth(abortController.signal).catch((error) => {
        // Only log if not aborted and component still mounted
        if (error.name !== 'AbortError' && isMounted) {
          loggingService.error(
            'Unexpected error in health check',
            error,
            'useBackendHealth',
            'run'
          );
        }
      });
    };

    // Run immediately
    run();
    const interval = window.setInterval(run, pollIntervalMs);

    return () => {
      isMounted = false;
      abortController.abort();
      window.clearInterval(interval);
    };
  }, [checkHealth, pollIntervalMs]);

  const refresh = useCallback(() => {
    const controller = new AbortController();
    checkHealth(controller.signal).catch(() => {
      // Errors are handled in checkHealth
    });
  }, [checkHealth]);

  return {
    ...snapshot,
    refresh,
  };
}
