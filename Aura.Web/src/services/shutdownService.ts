/**
 * Shutdown Service
 * Handles graceful shutdown of frontend resources when window is closing
 */

import { loggingService } from './loggingService';

class ShutdownService {
  private cleanupCallbacks: Array<() => void | Promise<void>> = [];
  private isShuttingDown = false;

  constructor() {
    // Register beforeunload handler
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', this.handleBeforeUnload);
    }
  }

  /**
   * Register a cleanup callback
   */
  registerCleanup(callback: () => void | Promise<void>): () => void {
    this.cleanupCallbacks.push(callback);

    // Return unregister function
    return () => {
      const index = this.cleanupCallbacks.indexOf(callback);
      if (index > -1) {
        this.cleanupCallbacks.splice(index, 1);
      }
    };
  }

  /**
   * Handle beforeunload event
   */
  private handleBeforeUnload = (event: BeforeUnloadEvent): void => {
    if (this.isShuttingDown) {
      return;
    }

    this.isShuttingDown = true;

    loggingService.info('Window closing, performing cleanup', 'shutdownService', 'beforeUnload');

    // Execute all cleanup callbacks synchronously
    // Note: async operations are not reliable in beforeunload
    for (const callback of this.cleanupCallbacks) {
      try {
        const result = callback();
        // If it's a promise, we can't await it reliably in beforeunload
        if (result instanceof Promise) {
          loggingService.warn(
            'Async cleanup callback registered (may not complete)',
            'shutdownService',
            'beforeUnload'
          );
        }
      } catch (error) {
        loggingService.error(
          'Error during cleanup callback',
          error instanceof Error ? error : new Error(String(error)),
          'shutdownService',
          'beforeUnload'
        );
      }
    }

    // Close all SSE connections
    this.closeAllEventSources();

    // Note: We don't prevent default or show confirmation dialogs
    // as this should be handled at the Electron level for active renders
  };

  /**
   * Close all active EventSource connections
   */
  private closeAllEventSources(): void {
    // This is a fallback for any EventSource instances that weren't cleaned up
    // In practice, hooks should handle their own cleanup
    try {
      // We can't easily enumerate all EventSource instances
      // but we can ensure our hooks have proper cleanup
      loggingService.debug(
        'EventSource cleanup handled by hooks',
        'shutdownService',
        'closeAllEventSources'
      );
    } catch (error) {
      loggingService.error(
        'Error closing EventSources',
        error instanceof Error ? error : new Error(String(error)),
        'shutdownService',
        'closeAllEventSources'
      );
    }
  }

  /**
   * Manually trigger shutdown (for testing or explicit cleanup)
   */
  async shutdown(): Promise<void> {
    if (this.isShuttingDown) {
      return;
    }

    this.isShuttingDown = true;

    loggingService.info('Manual shutdown initiated', 'shutdownService', 'shutdown');

    // Execute all cleanup callbacks (can await async ones here)
    const cleanupPromises: Promise<void>[] = [];

    for (const callback of this.cleanupCallbacks) {
      try {
        const result = callback();
        if (result instanceof Promise) {
          cleanupPromises.push(result);
        }
      } catch (error) {
        loggingService.error(
          'Error during cleanup callback',
          error instanceof Error ? error : new Error(String(error)),
          'shutdownService',
          'shutdown'
        );
      }
    }

    // Wait for all async cleanup to complete (with timeout)
    await Promise.race([
      Promise.all(cleanupPromises),
      new Promise((_, reject) => setTimeout(() => reject(new Error('Cleanup timeout')), 3000)),
    ]).catch((error) => {
      loggingService.error(
        'Cleanup timeout or error',
        error instanceof Error ? error : new Error(String(error)),
        'shutdownService',
        'shutdown'
      );
    });

    loggingService.info('Shutdown complete', 'shutdownService', 'shutdown');
  }

  /**
   * Cleanup the service itself (remove event listeners)
   */
  destroy(): void {
    if (typeof window !== 'undefined') {
      window.removeEventListener('beforeunload', this.handleBeforeUnload);
    }
    this.cleanupCallbacks = [];
  }
}

// Export singleton instance
export const shutdownService = new ShutdownService();
