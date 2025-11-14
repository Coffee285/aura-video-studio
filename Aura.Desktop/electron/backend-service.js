/**
 * Backend Service Module
 * Manages the .NET backend process lifecycle
 */

const { spawn, exec } = require('child_process');
const path = require('path');
const fs = require('fs');
const net = require('net');
const axios = require('axios');

class BackendService {
  constructor(app, isDev, childProcessManager = null) {
    this.app = app;
    this.isDev = isDev;
    this.process = null;
    this.port = null;
    this.isQuitting = false;
    this.isRestarting = false;
    this.restartAttempts = 0;
    this.maxRestartAttempts = 3;
    this.healthCheckInterval = null;
    this.pid = null;
    this.isWindows = process.platform === 'win32';
    this.childProcessManager = childProcessManager;
    
    // Constants
    this.BACKEND_STARTUP_TIMEOUT = 60000; // 60 seconds
    this.HEALTH_CHECK_INTERVAL = 1000; // 1 second
    this.AUTO_RESTART_DELAY = 5000; // 5 seconds
    this.GRACEFUL_SHUTDOWN_TIMEOUT = 10000; // 10 seconds
    this.FORCE_KILL_TIMEOUT = 5000; // 5 seconds after graceful timeout
  }

  /**
   * Start the backend service
   */
  async start() {
    try {
      // Find available port
      this.port = await this._findAvailablePort();
      console.log(`Starting backend on port ${this.port}...`);

      // Determine backend executable path
      const backendPath = this._getBackendPath();
      
      // Check if backend executable exists
      if (!fs.existsSync(backendPath)) {
        throw new Error(`Backend executable not found at: ${backendPath}`);
      }

      // Make executable on Unix-like systems
      if (process.platform !== 'win32') {
        try {
          fs.chmodSync(backendPath, 0o755);
        } catch (error) {
          console.warn('Failed to make backend executable:', error.message);
        }
      }

      // Get FFmpeg path
      const ffmpegPath = this._getFFmpegPath();
      const ffmpegExists = this._verifyFFmpeg(ffmpegPath);
      
      if (!ffmpegExists) {
        console.warn('FFmpeg not found - video rendering may not work');
      }

      // Prepare environment variables
      const env = this._prepareEnvironment();

      // Create necessary directories
      this._createDirectories(env);

      console.log('Backend executable:', backendPath);
      console.log('Backend port:', this.port);
      console.log('Environment:', env.DOTNET_ENVIRONMENT);
      console.log('FFmpeg path:', ffmpegPath);

      // Spawn backend process with detached flag on Windows to get proper process tree control
      this.process = spawn(backendPath, [], {
        env,
        stdio: ['ignore', 'pipe', 'pipe'],
        windowsHide: true, // Hide console window on Windows
        detached: false // Keep attached so we can control it, but we'll handle child processes separately
      });
      
      // Store PID for Windows process tree termination
      this.pid = this.process.pid;

      // Register backend process with child process manager
      if (this.childProcessManager) {
        this.childProcessManager.register(this.pid, 'Backend API Service', this.process);
      }

      // Setup process handlers
      this._setupProcessHandlers();

      // Wait for backend to be ready
      await this._waitForBackend();

      console.log('Backend started successfully');
      
      // Start periodic health checks
      this._startHealthChecks();
      
      return this.port;
    } catch (error) {
      console.error('Backend startup error:', error);
      throw error;
    }
  }

  /**
   * Stop the backend service
   */
  async stop() {
    console.log('Stopping backend service...');
    this.isQuitting = true;

    // Stop health checks
    this._stopHealthChecks();

    // Kill backend process
    if (this.process && !this.process.killed) {
      try {
        await this._terminateBackend();
      } catch (error) {
        console.error('Error terminating backend:', error);
      }
    }

    // Unregister from child process manager
    if (this.childProcessManager && this.pid) {
      this.childProcessManager.unregister(this.pid);
    }

    this.process = null;
    this.pid = null;
    this.port = null;
  }

  /**
   * Gracefully terminate backend process with Windows-specific handling
   */
  async _terminateBackend() {
    return new Promise((resolve) => {
      if (!this.process || this.process.killed) {
        resolve();
        return;
      }

      console.log('Terminating backend process (PID: ' + this.pid + ')...');

      // Set up timeout for graceful shutdown
      const gracefulTimeout = setTimeout(() => {
        console.warn('Backend did not shut down gracefully, forcing termination...');
        this._forceTerminate();
      }, this.GRACEFUL_SHUTDOWN_TIMEOUT);

      // Handle process exit
      const onExit = () => {
        clearTimeout(gracefulTimeout);
        clearTimeout(forceTimeout);
        console.log('Backend process terminated successfully');
        resolve();
      };

      this.process.once('exit', onExit);

      // Attempt graceful shutdown first via API
      this._attemptGracefulShutdown().then(success => {
        if (!success) {
          console.log('Graceful shutdown via API failed, using process termination...');
          // Try process termination
          if (this.isWindows) {
            // On Windows, use taskkill for proper process tree termination
            this._windowsTerminate(false);
          } else {
            // On Unix, use SIGTERM
            try {
              this.process.kill('SIGTERM');
            } catch (err) {
              console.error('Error sending SIGTERM:', err);
              this._forceTerminate();
            }
          }
        }
      }).catch(err => {
        console.error('Error during graceful shutdown:', err);
        if (this.isWindows) {
          this._windowsTerminate(false);
        } else {
          try {
            this.process.kill('SIGTERM');
          } catch (killErr) {
            this._forceTerminate();
          }
        }
      });

      // Final safety net - force kill after extended timeout
      const forceTimeout = setTimeout(() => {
        console.error('Backend still running after graceful timeout, force killing...');
        this._forceTerminate();
        resolve();
      }, this.GRACEFUL_SHUTDOWN_TIMEOUT + this.FORCE_KILL_TIMEOUT);
    });
  }

  /**
   * Attempt graceful shutdown via backend API
   */
  async _attemptGracefulShutdown() {
    try {
      if (!this.port) return false;
      
      console.log('Requesting graceful shutdown via API...');
      await axios.post(`http://localhost:${this.port}/api/system/shutdown`, {}, {
        timeout: 2000
      });
      return true;
    } catch (error) {
      // API call failed, backend may already be down or endpoint doesn't exist
      return false;
    }
  }

  /**
   * Force terminate the backend process
   */
  _forceTerminate() {
    if (!this.process || this.process.killed) return;

    console.log('Force terminating backend process...');

    if (this.isWindows) {
      this._windowsTerminate(true);
    } else {
      try {
        this.process.kill('SIGKILL');
      } catch (error) {
        console.error('Error force killing process:', error);
      }
    }
  }

  /**
   * Windows-specific process termination using taskkill
   * This properly terminates the process tree including child processes
   */
  _windowsTerminate(force = false) {
    if (!this.pid) return;

    const forceFlag = force ? '/F' : '';
    const command = `taskkill /PID ${this.pid} ${forceFlag} /T`;
    
    console.log('Executing:', command);

    exec(command, (error, stdout, stderr) => {
      if (error) {
        console.error('taskkill error:', error);
        // Fallback to Node's kill
        try {
          if (this.process && !this.process.killed) {
            this.process.kill();
          }
        } catch (fallbackError) {
          console.error('Fallback kill failed:', fallbackError);
        }
        return;
      }
      if (stdout) console.log('taskkill output:', stdout);
      if (stderr) console.error('taskkill stderr:', stderr);
    });
  }

  /**
   * Restart the backend service
   */
  async restart() {
    if (this.isRestarting) {
      console.warn('Backend restart already in progress');
      return;
    }

    try {
      this.isRestarting = true;
      console.log('Restarting backend service...');
      
      const wasQuitting = this.isQuitting;
      this.isQuitting = false; // Temporarily disable quitting flag for restart
      
      await this.stop();
      
      // Wait a bit before restarting to ensure port is released
      await new Promise(resolve => setTimeout(resolve, 2000));
      
      await this.start();
      
      this.isQuitting = wasQuitting;
      console.log('Backend restart completed successfully');
    } catch (error) {
      console.error('Backend restart failed:', error);
      throw error;
    } finally {
      this.isRestarting = false;
    }
  }

  /**
   * Get backend port
   */
  getPort() {
    return this.port;
  }

  /**
   * Get backend URL
   */
  getUrl() {
    return this.port ? `http://localhost:${this.port}` : null;
  }

  /**
   * Check if backend is running
   */
  isRunning() {
    return this.process !== null && !this.process.killed;
  }

  /**
   * Check Windows Firewall compatibility
   */
  async checkFirewallCompatibility() {
    if (!this.isWindows) {
      return { compatible: true, message: 'Not Windows' };
    }

    try {
      // Check if port is accessible locally
      const portAccessible = await this._checkPortAccessible();
      
      if (!portAccessible) {
        return {
          compatible: false,
          message: 'Backend port is not accessible. Windows Firewall may be blocking the connection.',
          recommendation: 'Please add Aura Video Studio to Windows Firewall exceptions.'
        };
      }

      // Check if process can bind to port
      const canBind = await this._checkCanBindPort();
      
      if (!canBind) {
        return {
          compatible: false,
          message: 'Cannot bind to port. Another application may be using it.',
          recommendation: 'Please close other applications that might be using the port.'
        };
      }

      return {
        compatible: true,
        message: 'Windows Firewall compatibility check passed'
      };
    } catch (error) {
      console.error('Firewall compatibility check error:', error);
      return {
        compatible: false,
        message: `Firewall check failed: ${error.message}`,
        recommendation: 'Please check Windows Firewall settings manually.'
      };
    }
  }

  /**
   * Check if port is accessible
   * Uses /health/live endpoint for faster response
   */
  async _checkPortAccessible() {
    if (!this.port) return false;

    try {
      const response = await axios.get(`http://localhost:${this.port}/health/live`, {
        timeout: 2000
      });
      return response.status === 200;
    } catch (error) {
      return false;
    }
  }

  /**
   * Check if we can bind to a port (test with a temporary server)
   */
  async _checkCanBindPort() {
    return new Promise((resolve) => {
      const testServer = net.createServer();
      
      testServer.once('error', (err) => {
        if (err.code === 'EADDRINUSE') {
          resolve(false);
        } else {
          resolve(false);
        }
      });

      testServer.once('listening', () => {
        testServer.close(() => {
          resolve(true);
        });
      });

      // Try to bind to port 0 (any available port) to test capability
      testServer.listen(0, 'localhost');
    });
  }

  /**
   * Get Windows Firewall rule status (requires elevation)
   */
  async getFirewallRuleStatus() {
    if (!this.isWindows) {
      return { exists: null, error: 'Not Windows' };
    }

    return new Promise((resolve) => {
      const command = 'netsh advfirewall firewall show rule name="Aura Video Studio"';
      
      exec(command, (error, stdout, stderr) => {
        if (error) {
          // Rule doesn't exist or error checking
          resolve({ exists: false, error: error.message });
          return;
        }

        const exists = !stdout.includes('No rules match');
        resolve({ 
          exists, 
          details: exists ? stdout : null 
        });
      });
    });
  }

  /**
   * Suggest firewall rule creation command
   */
  getFirewallRuleCommand() {
    if (!this.isWindows) return null;

    const backendPath = this._getBackendPath();
    
    return `netsh advfirewall firewall add rule name="Aura Video Studio" dir=in action=allow program="${backendPath}" enable=yes profile=any`;
  }

  /**
   * Find an available port for the backend server
   */
  async _findAvailablePort() {
    return new Promise((resolve, reject) => {
      const server = net.createServer();
      server.unref();
      server.on('error', reject);
      server.listen(0, () => {
        const { port } = server.address();
        server.close(() => resolve(port));
      });
    });
  }

  /**
   * Wait for the backend to become healthy
   * Uses /health/live endpoint for faster startup detection
   */
  async _waitForBackend() {
    const maxAttempts = this.BACKEND_STARTUP_TIMEOUT / this.HEALTH_CHECK_INTERVAL;
    
    for (let i = 0; i < maxAttempts; i++) {
      try {
        const response = await axios.get(`http://localhost:${this.port}/health/live`, {
          timeout: 2000,
          validateStatus: () => true
        });
        
        if (response.status === 200) {
          console.log(`Backend is healthy at http://localhost:${this.port}`);
          return true;
        }
      } catch (error) {
        // Backend not ready yet, continue waiting
      }
      
      // Wait before next attempt
      await new Promise(resolve => setTimeout(resolve, this.HEALTH_CHECK_INTERVAL));
      
      if (i % 10 === 0 && i > 0) {
        console.log(`Still waiting for backend... (attempt ${i}/${maxAttempts})`);
      }
    }
    
    throw new Error(`Backend failed to start within ${this.BACKEND_STARTUP_TIMEOUT}ms`);
  }

  /**
   * Get the path to the backend executable
   */
  _getBackendPath() {
    if (this.isDev) {
      // In development, use the compiled backend from Aura.Api/bin
      const platform = process.platform;
      if (platform === 'win32') {
        return path.join(__dirname, '../../Aura.Api/bin/Debug/net8.0/Aura.Api.exe');
      } else {
        return path.join(__dirname, '../../Aura.Api/bin/Debug/net8.0/Aura.Api');
      }
    } else {
      // In production, use the bundled backend from resources
      if (process.platform === 'win32') {
        return path.join(process.resourcesPath, 'backend', 'win-x64', 'Aura.Api.exe');
      } else if (process.platform === 'darwin') {
        return path.join(process.resourcesPath, 'backend', 'osx-x64', 'Aura.Api');
      } else {
        return path.join(process.resourcesPath, 'backend', 'linux-x64', 'Aura.Api');
      }
    }
  }

  /**
   * Get the path to FFmpeg binaries
   */
  _getFFmpegPath() {
    let ffmpegBinPath;
    
    if (this.isDev) {
      // In development, look for FFmpeg in resources directory
      const platform = process.platform;
      if (platform === 'win32') {
        ffmpegBinPath = path.join(__dirname, '../resources', 'ffmpeg', 'win-x64', 'bin');
      } else if (platform === 'darwin') {
        ffmpegBinPath = path.join(__dirname, '../resources', 'ffmpeg', 'osx-x64', 'bin');
      } else {
        ffmpegBinPath = path.join(__dirname, '../resources', 'ffmpeg', 'linux-x64', 'bin');
      }
    } else {
      // In production, use the bundled FFmpeg from resources
      const platform = process.platform;
      if (platform === 'win32') {
        ffmpegBinPath = path.join(process.resourcesPath, 'ffmpeg', 'win-x64', 'bin');
      } else if (platform === 'darwin') {
        ffmpegBinPath = path.join(process.resourcesPath, 'ffmpeg', 'osx-x64', 'bin');
      } else {
        ffmpegBinPath = path.join(process.resourcesPath, 'ffmpeg', 'linux-x64', 'bin');
      }
    }
    
    return ffmpegBinPath;
  }

  /**
   * Verify FFmpeg installation
   */
  _verifyFFmpeg(ffmpegPath) {
    const ffmpegExe = process.platform === 'win32' ? 'ffmpeg.exe' : 'ffmpeg';
    const ffmpegFullPath = path.join(ffmpegPath, ffmpegExe);
    
    if (!fs.existsSync(ffmpegFullPath)) {
      console.warn(`FFmpeg not found at: ${ffmpegFullPath}`);
      return false;
    }
    
    console.log('FFmpeg found at:', ffmpegFullPath);
    return true;
  }

  /**
   * Prepare environment variables for backend
   */
  _prepareEnvironment() {
    const ffmpegPath = this._getFFmpegPath();
    
    return {
      ...process.env,
      ASPNETCORE_URLS: `http://localhost:${this.port}`,
      DOTNET_ENVIRONMENT: this.isDev ? 'Development' : 'Production',
      ASPNETCORE_DETAILEDERRORS: this.isDev ? 'true' : 'false',
      LOGGING__LOGLEVEL__DEFAULT: this.isDev ? 'Debug' : 'Information',
      // Set paths for user data
      AURA_DATA_PATH: this.app.getPath('userData'),
      AURA_LOGS_PATH: path.join(this.app.getPath('userData'), 'logs'),
      AURA_TEMP_PATH: path.join(this.app.getPath('temp'), 'aura-video-studio'),
      // Set FFmpeg path for backend
      FFMPEG_PATH: ffmpegPath,
      FFMPEG_BINARIES_PATH: ffmpegPath
    };
  }

  /**
   * Create necessary directories
   */
  _createDirectories(env) {
    const directories = [env.AURA_DATA_PATH, env.AURA_LOGS_PATH, env.AURA_TEMP_PATH];
    directories.forEach(dir => {
      if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
      }
    });
  }

  /**
   * Setup process event handlers
   */
  _setupProcessHandlers() {
    // Handle backend output
    this.process.stdout.on('data', (data) => {
      const message = data.toString().trim();
      if (message) {
        console.log(`[Backend] ${message}`);
      }
    });

    this.process.stderr.on('data', (data) => {
      const message = data.toString().trim();
      if (message) {
        console.error(`[Backend Error] ${message}`);
      }
    });

    // Handle backend exit
    this.process.on('exit', (code, signal) => {
      console.log(`Backend exited with code ${code} and signal ${signal}`);
      
      // Don't restart if we're intentionally quitting or already restarting
      if (!this.isQuitting && !this.isRestarting && code !== 0) {
        // Backend crashed unexpectedly
        console.error('Backend crashed unexpectedly!');
        
        // Attempt auto-restart
        if (this.restartAttempts < this.maxRestartAttempts) {
          this.restartAttempts++;
          console.log(`Attempting to restart backend (${this.restartAttempts}/${this.maxRestartAttempts})...`);
          
          setTimeout(() => {
            this.restart().catch(error => {
              console.error('Failed to restart backend:', error);
            });
          }, this.AUTO_RESTART_DELAY);
        } else {
          console.error('Max restart attempts reached. Backend will not auto-restart.');
          // Emit event for main process to handle
          if (this.app) {
            this.app.emit('backend-crash');
          }
        }
      }
    });

    this.process.on('error', (error) => {
      console.error('Backend process error:', error);
    });
  }

  /**
   * Start periodic health checks
   */
  _startHealthChecks() {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval);
    }

    this.healthCheckInterval = setInterval(async () => {
      try {
        await axios.get(`http://localhost:${this.port}/health`, {
          timeout: 5000
        });
        // Reset restart attempts on successful health check
        this.restartAttempts = 0;
      } catch (error) {
        console.warn('Backend health check failed:', error.message);
      }
    }, 30000); // Check every 30 seconds
  }

  /**
   * Stop health checks
   */
  _stopHealthChecks() {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval);
      this.healthCheckInterval = null;
    }
  }
}

module.exports = BackendService;
