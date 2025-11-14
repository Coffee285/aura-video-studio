/**
 * Shutdown Orchestrator
 * 
 * Coordinates graceful application shutdown with proper cleanup of:
 * - Backend API service
 * - Child processes (FFmpeg, etc.)
 * - SSE connections
 * - System tray
 * - Window resources
 * - File handles and locks
 * 
 * Ensures application exits within 5 seconds maximum.
 */

const { exec } = require('child_process');

class ShutdownOrchestrator {
  constructor(logger) {
    this.logger = logger;
    this.isShuttingDown = false;
    this.shutdownStartTime = null;
    
    // Component timeouts (in milliseconds)
    this.TIMEOUTS = {
      BACKEND_API: 3000,        // Backend graceful shutdown via API
      BACKEND_PROCESS: 2000,    // Backend process SIGTERM
      BACKEND_FORCE: 1000,      // Backend force kill (SIGKILL)
      CHILD_PROCESSES: 2000,    // FFmpeg and other child processes
      TOTAL_SHUTDOWN: 5000      // Total maximum shutdown time
    };
    
    // Track components and their states
    this.components = {
      backendService: { name: 'Backend Service', stopped: false, error: null },
      childProcesses: { name: 'Child Processes', stopped: false, error: null },
      sseConnections: { name: 'SSE Connections', stopped: false, error: null },
      systemTray: { name: 'System Tray', stopped: false, error: null },
      fileHandles: { name: 'File Handles', stopped: false, error: null }
    };
  }

  /**
   * Execute coordinated shutdown sequence
   */
  async shutdown(context) {
    if (this.isShuttingDown) {
      this.logger?.warn('ShutdownOrchestrator', 'Shutdown already in progress');
      return;
    }

    this.isShuttingDown = true;
    this.shutdownStartTime = Date.now();

    this.logger?.info('ShutdownOrchestrator', 'Starting graceful shutdown sequence', {
      maxTimeout: this.TIMEOUTS.TOTAL_SHUTDOWN
    });

    try {
      // Execute shutdown steps with overall timeout
      await Promise.race([
        this._executeShutdownSteps(context),
        this._createTotalTimeout()
      ]);

      const totalTime = Date.now() - this.shutdownStartTime;
      this.logger?.info('ShutdownOrchestrator', 'Shutdown completed', {
        totalTime,
        components: this._getComponentSummary()
      });

    } catch (error) {
      const totalTime = Date.now() - this.shutdownStartTime;
      this.logger?.error('ShutdownOrchestrator', 'Shutdown completed with errors', 
        error instanceof Error ? error : new Error(String(error)), {
        totalTime,
        components: this._getComponentSummary()
      });
    }
  }

  /**
   * Execute shutdown steps in order
   */
  async _executeShutdownSteps(context) {
    const { backendService, ipcHandlers, trayManager, childProcessManager } = context;

    // Step 1: Stop backend health checks
    this.logger?.info('ShutdownOrchestrator', 'Step 1: Stopping backend health checks');
    try {
      if (ipcHandlers?.backend) {
        ipcHandlers.backend.stopHealthChecks();
      }
    } catch (error) {
      this.logger?.warn('ShutdownOrchestrator', 'Failed to stop health checks', {
        error: error.message
      });
    }

    // Step 2: Notify backend via API endpoint (graceful shutdown)
    this.logger?.info('ShutdownOrchestrator', 'Step 2: Requesting backend graceful shutdown');
    try {
      await this._shutdownBackendViaApi(backendService);
      this.components.backendService.stopped = true;
    } catch (error) {
      this.logger?.warn('ShutdownOrchestrator', 'Backend API shutdown failed, will use process termination', {
        error: error.message
      });
      this.components.backendService.error = error.message;
    }

    // Step 3: Terminate backend process if still running
    if (backendService && !this.components.backendService.stopped) {
      this.logger?.info('ShutdownOrchestrator', 'Step 3: Terminating backend process');
      try {
        await this._terminateBackendProcess(backendService);
        this.components.backendService.stopped = true;
      } catch (error) {
        this.logger?.error('ShutdownOrchestrator', 'Failed to terminate backend process', 
          error instanceof Error ? error : new Error(String(error)));
        this.components.backendService.error = error.message;
      }
    } else {
      this.logger?.info('ShutdownOrchestrator', 'Step 3: Backend already stopped, skipping');
    }

    // Step 4: Terminate child processes (FFmpeg, etc.)
    this.logger?.info('ShutdownOrchestrator', 'Step 4: Terminating child processes');
    try {
      await this._terminateChildProcesses(childProcessManager);
      this.components.childProcesses.stopped = true;
    } catch (error) {
      this.logger?.warn('ShutdownOrchestrator', 'Failed to terminate some child processes', {
        error: error.message
      });
      this.components.childProcesses.error = error.message;
    }

    // Step 5: Destroy system tray
    this.logger?.info('ShutdownOrchestrator', 'Step 5: Destroying system tray');
    try {
      if (trayManager) {
        trayManager.destroy();
      }
      this.components.systemTray.stopped = true;
    } catch (error) {
      this.logger?.warn('ShutdownOrchestrator', 'Failed to destroy system tray', {
        error: error.message
      });
      this.components.systemTray.error = error.message;
    }

    this.logger?.info('ShutdownOrchestrator', 'All shutdown steps completed');
  }

  /**
   * Shutdown backend via API endpoint
   */
  async _shutdownBackendViaApi(backendService) {
    if (!backendService || !backendService.getPort()) {
      this.logger?.debug('ShutdownOrchestrator', 'No backend service to shutdown');
      return;
    }

    const timeout = this.TIMEOUTS.BACKEND_API;
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeout);

    try {
      const axios = require('axios');
      const port = backendService.getPort();
      
      this.logger?.debug('ShutdownOrchestrator', 'Sending shutdown request to backend API', {
        url: `http://localhost:${port}/api/system/shutdown`,
        timeout
      });

      const response = await axios.post(
        `http://localhost:${port}/api/system/shutdown`,
        {},
        {
          timeout,
          signal: controller.signal
        }
      );

      this.logger?.info('ShutdownOrchestrator', 'Backend acknowledged shutdown request', {
        status: response.status
      });

      // Give backend time to shut down gracefully
      await new Promise(resolve => setTimeout(resolve, 1000));

    } catch (error) {
      if (error.name === 'AbortError') {
        throw new Error(`Backend API shutdown timed out after ${timeout}ms`);
      }
      throw error;
    } finally {
      clearTimeout(timeoutId);
    }
  }

  /**
   * Terminate backend process
   */
  async _terminateBackendProcess(backendService) {
    if (!backendService) {
      return;
    }

    try {
      // Use the backend service's stop method which handles Windows process tree termination
      await Promise.race([
        backendService.stop(),
        new Promise((_, reject) => 
          setTimeout(() => reject(new Error('Backend process termination timeout')), 
            this.TIMEOUTS.BACKEND_PROCESS + this.TIMEOUTS.BACKEND_FORCE)
        )
      ]);
    } catch (error) {
      this.logger?.error('ShutdownOrchestrator', 'Backend process termination failed', 
        error instanceof Error ? error : new Error(String(error)));
      throw error;
    }
  }

  /**
   * Terminate child processes (FFmpeg, etc.)
   */
  async _terminateChildProcesses(childProcessManager) {
    if (!childProcessManager) {
      this.logger?.debug('ShutdownOrchestrator', 'No child process manager available');
      return;
    }

    try {
      const timeout = this.TIMEOUTS.CHILD_PROCESSES;
      await Promise.race([
        childProcessManager.terminateAll(),
        new Promise((_, reject) => 
          setTimeout(() => reject(new Error('Child process termination timeout')), timeout)
        )
      ]);
    } catch (error) {
      this.logger?.warn('ShutdownOrchestrator', 'Child process termination timeout, forcing termination');
      
      // Force terminate any remaining processes
      if (childProcessManager.forceTerminateAll) {
        await childProcessManager.forceTerminateAll();
      }
    }
  }

  /**
   * Create timeout promise for total shutdown
   */
  _createTotalTimeout() {
    return new Promise((_, reject) => {
      setTimeout(() => {
        reject(new Error(`Total shutdown timeout exceeded (${this.TIMEOUTS.TOTAL_SHUTDOWN}ms)`));
      }, this.TIMEOUTS.TOTAL_SHUTDOWN);
    });
  }

  /**
   * Get summary of component shutdown states
   */
  _getComponentSummary() {
    const summary = {};
    for (const [key, component] of Object.entries(this.components)) {
      summary[key] = {
        name: component.name,
        stopped: component.stopped,
        error: component.error
      };
    }
    return summary;
  }

  /**
   * Check if user should be warned about active renders
   */
  shouldWarnUser(context) {
    // Check if there are active jobs/renders
    const hasActiveJobs = context.hasActiveJobs?.() || false;
    const hasActiveRenders = context.hasActiveRenders?.() || false;
    
    return hasActiveJobs || hasActiveRenders;
  }

  /**
   * Get elapsed shutdown time
   */
  getElapsedTime() {
    if (!this.shutdownStartTime) {
      return 0;
    }
    return Date.now() - this.shutdownStartTime;
  }
}

module.exports = ShutdownOrchestrator;
