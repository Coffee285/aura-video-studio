/**
 * Child Process Manager
 * 
 * Tracks and manages child processes (FFmpeg renders, etc.)
 * Ensures all child processes are properly terminated during shutdown
 */

const { exec } = require('child_process');

class ChildProcessManager {
  constructor(logger) {
    this.logger = logger;
    this.processes = new Map(); // pid -> { name, process, startTime }
    this.isWindows = process.platform === 'win32';
  }

  /**
   * Register a child process for tracking
   */
  register(pid, name, processObject = null) {
    if (!pid) {
      this.logger?.warn('ChildProcessManager', 'Attempted to register process without PID');
      return;
    }

    this.processes.set(pid, {
      name: name || 'Unknown Process',
      process: processObject,
      startTime: Date.now()
    });

    this.logger?.debug('ChildProcessManager', 'Registered child process', {
      pid,
      name,
      totalProcesses: this.processes.size
    });
  }

  /**
   * Unregister a child process
   */
  unregister(pid) {
    if (this.processes.has(pid)) {
      const process = this.processes.get(pid);
      this.processes.delete(pid);
      
      this.logger?.debug('ChildProcessManager', 'Unregistered child process', {
        pid,
        name: process.name,
        totalProcesses: this.processes.size
      });
    }
  }

  /**
   * Get all tracked PIDs
   */
  getAllPids() {
    return Array.from(this.processes.keys());
  }

  /**
   * Get process info by PID
   */
  getProcess(pid) {
    return this.processes.get(pid);
  }

  /**
   * Check if process is still running
   */
  isRunning(pid) {
    try {
      // Sending signal 0 checks if process exists without actually sending a signal
      process.kill(pid, 0);
      return true;
    } catch (error) {
      return false;
    }
  }

  /**
   * Terminate all tracked child processes gracefully
   */
  async terminateAll() {
    if (this.processes.size === 0) {
      this.logger?.debug('ChildProcessManager', 'No child processes to terminate');
      return;
    }

    this.logger?.info('ChildProcessManager', 'Terminating all child processes', {
      count: this.processes.size
    });

    const terminatePromises = [];

    for (const [pid, info] of this.processes.entries()) {
      terminatePromises.push(
        this._terminateProcess(pid, info, false).catch(error => {
          this.logger?.warn('ChildProcessManager', 'Failed to terminate process', {
            pid,
            name: info.name,
            error: error.message
          });
        })
      );
    }

    await Promise.all(terminatePromises);

    // Verify all processes are terminated
    const stillRunning = this.getAllPids().filter(pid => this.isRunning(pid));
    
    if (stillRunning.length > 0) {
      this.logger?.warn('ChildProcessManager', 'Some processes still running after graceful termination', {
        count: stillRunning.length,
        pids: stillRunning
      });
    } else {
      this.logger?.info('ChildProcessManager', 'All child processes terminated successfully');
    }
  }

  /**
   * Force terminate all processes (SIGKILL)
   */
  async forceTerminateAll() {
    if (this.processes.size === 0) {
      return;
    }

    this.logger?.warn('ChildProcessManager', 'Force terminating all child processes', {
      count: this.processes.size
    });

    const terminatePromises = [];

    for (const [pid, info] of this.processes.entries()) {
      terminatePromises.push(
        this._terminateProcess(pid, info, true).catch(error => {
          this.logger?.error('ChildProcessManager', 'Failed to force terminate process', 
            error instanceof Error ? error : new Error(String(error)), {
            pid,
            name: info.name
          });
        })
      );
    }

    await Promise.all(terminatePromises);
    this.processes.clear();
  }

  /**
   * Terminate a single process
   */
  async _terminateProcess(pid, info, force = false) {
    if (!this.isRunning(pid)) {
      this.logger?.debug('ChildProcessManager', 'Process already terminated', {
        pid,
        name: info.name
      });
      this.unregister(pid);
      return;
    }

    this.logger?.debug('ChildProcessManager', 'Terminating process', {
      pid,
      name: info.name,
      force
    });

    try {
      if (this.isWindows) {
        await this._windowsTerminate(pid, force);
      } else {
        await this._unixTerminate(pid, force);
      }

      // Wait for process to exit
      await this._waitForProcessExit(pid, force ? 1000 : 3000);
      this.unregister(pid);

    } catch (error) {
      this.logger?.warn('ChildProcessManager', 'Process termination error', {
        pid,
        name: info.name,
        error: error.message
      });
      throw error;
    }
  }

  /**
   * Windows-specific process termination using taskkill
   */
  async _windowsTerminate(pid, force = false) {
    return new Promise((resolve, reject) => {
      const forceFlag = force ? '/F' : '';
      const command = `taskkill /PID ${pid} ${forceFlag} /T`;

      exec(command, (error, stdout, stderr) => {
        if (error) {
          // Process might already be dead
          if (error.message.includes('not found')) {
            resolve();
            return;
          }
          reject(error);
          return;
        }
        resolve();
      });
    });
  }

  /**
   * Unix-specific process termination
   */
  async _unixTerminate(pid, force = false) {
    return new Promise((resolve, reject) => {
      try {
        const signal = force ? 'SIGKILL' : 'SIGTERM';
        process.kill(pid, signal);
        resolve();
      } catch (error) {
        // Process might already be dead
        if (error.code === 'ESRCH') {
          resolve();
          return;
        }
        reject(error);
      }
    });
  }

  /**
   * Wait for process to exit
   */
  async _waitForProcessExit(pid, timeout = 3000) {
    const startTime = Date.now();
    
    while (Date.now() - startTime < timeout) {
      if (!this.isRunning(pid)) {
        return;
      }
      await new Promise(resolve => setTimeout(resolve, 100));
    }

    if (this.isRunning(pid)) {
      throw new Error(`Process ${pid} did not exit within ${timeout}ms`);
    }
  }

  /**
   * Get statistics about tracked processes
   */
  getStats() {
    const stats = {
      total: this.processes.size,
      running: 0,
      processes: []
    };

    for (const [pid, info] of this.processes.entries()) {
      const isRunning = this.isRunning(pid);
      if (isRunning) {
        stats.running++;
      }
      
      stats.processes.push({
        pid,
        name: info.name,
        running: isRunning,
        uptime: Date.now() - info.startTime
      });
    }

    return stats;
  }
}

module.exports = ChildProcessManager;
