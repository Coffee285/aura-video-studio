/**
 * Configuration Recovery Service
 * Handles configuration validation, backup, restore, and corruption detection
 */

import { loggingService } from '../loggingService';

export interface ConfigBackup {
  timestamp: string;
  version: string;
  config: Record<string, unknown>;
  checksum: string;
}

export interface ConfigValidationResult {
  isValid: boolean;
  errors: string[];
  warnings: string[];
  isCorrupted: boolean;
  canRecover: boolean;
}

const CONFIG_STORAGE_KEY = 'aura-app-config';
const CONFIG_BACKUP_PREFIX = 'aura-config-backup-';
const MAX_BACKUPS = 5;

class ConfigRecoveryService {
  /**
   * Validate current configuration
   */
  public async validateConfig(): Promise<ConfigValidationResult> {
    const errors: string[] = [];
    const warnings: string[] = [];
    let isCorrupted = false;

    try {
      const configStr = localStorage.getItem(CONFIG_STORAGE_KEY);

      if (!configStr) {
        warnings.push('No configuration found in storage');
        return {
          isValid: false,
          errors,
          warnings,
          isCorrupted: false,
          canRecover: true,
        };
      }

      // Try to parse JSON
      let config: Record<string, unknown>;
      try {
        config = JSON.parse(configStr);
      } catch (parseError) {
        errors.push('Configuration file is corrupted (invalid JSON)');
        isCorrupted = true;
        return {
          isValid: false,
          errors,
          warnings,
          isCorrupted: true,
          canRecover: this.hasBackups(),
        };
      }

      // Validate structure
      if (!config || typeof config !== 'object') {
        errors.push('Configuration has invalid structure');
        isCorrupted = true;
      }

      // Check for required fields
      const requiredFields = ['version', 'lastModified'];
      for (const field of requiredFields) {
        if (!(field in config)) {
          warnings.push(`Missing field: ${field}`);
        }
      }

      // Validate specific settings
      if ('providers' in config) {
        const providers = config.providers;
        if (typeof providers !== 'object' || providers === null) {
          errors.push('Providers configuration is invalid');
        }
      }

      const isValid = errors.length === 0 && !isCorrupted;

      return {
        isValid,
        errors,
        warnings,
        isCorrupted,
        canRecover: this.hasBackups() || isValid,
      };
    } catch (error: unknown) {
      loggingService.error(
        'Error validating configuration',
        error as Error,
        'ConfigRecoveryService',
        'validateConfig'
      );

      return {
        isValid: false,
        errors: ['Unexpected error during validation'],
        warnings,
        isCorrupted: true,
        canRecover: this.hasBackups(),
      };
    }
  }

  /**
   * Create a backup of the current configuration
   */
  public async createBackup(): Promise<boolean> {
    try {
      const configStr = localStorage.getItem(CONFIG_STORAGE_KEY);

      if (!configStr) {
        loggingService.warn('No configuration to backup', 'ConfigRecoveryService', 'createBackup');
        return false;
      }

      const config = JSON.parse(configStr);
      const backup: ConfigBackup = {
        timestamp: new Date().toISOString(),
        version: config.version || '1.0.0',
        config,
        checksum: this.calculateChecksum(configStr),
      };

      const backupKey = `${CONFIG_BACKUP_PREFIX}${Date.now()}`;
      localStorage.setItem(backupKey, JSON.stringify(backup));

      // Clean up old backups
      this.cleanupOldBackups();

      loggingService.info('Configuration backup created', 'ConfigRecoveryService', 'createBackup', {
        backupKey,
        timestamp: backup.timestamp,
      });

      return true;
    } catch (error: unknown) {
      loggingService.error(
        'Error creating backup',
        error as Error,
        'ConfigRecoveryService',
        'createBackup'
      );
      return false;
    }
  }

  /**
   * List available backups
   */
  public listBackups(): ConfigBackup[] {
    const backups: ConfigBackup[] = [];

    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && key.startsWith(CONFIG_BACKUP_PREFIX)) {
          const backupStr = localStorage.getItem(key);
          if (backupStr) {
            try {
              const backup = JSON.parse(backupStr) as ConfigBackup;
              backups.push(backup);
            } catch (parseError) {
              loggingService.warn(
                `Failed to parse backup ${key}`,
                'ConfigRecoveryService',
                'listBackups'
              );
            }
          }
        }
      }

      // Sort by timestamp (newest first)
      backups.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
    } catch (error: unknown) {
      loggingService.error(
        'Error listing backups',
        error as Error,
        'ConfigRecoveryService',
        'listBackups'
      );
    }

    return backups;
  }

  /**
   * Restore configuration from a backup
   */
  public async restoreFromBackup(timestamp: string): Promise<boolean> {
    try {
      const backups = this.listBackups();
      const backup = backups.find((b) => b.timestamp === timestamp);

      if (!backup) {
        loggingService.error(
          'Backup not found',
          new Error(`Backup ${timestamp} not found`),
          'ConfigRecoveryService',
          'restoreFromBackup'
        );
        return false;
      }

      // Validate backup before restoring
      const backupStr = JSON.stringify(backup.config);
      const checksum = this.calculateChecksum(backupStr);

      if (checksum !== backup.checksum) {
        loggingService.error(
          'Backup checksum mismatch',
          new Error('Backup may be corrupted'),
          'ConfigRecoveryService',
          'restoreFromBackup'
        );
        return false;
      }

      // Create backup of current config before restoring
      await this.createBackup();

      // Restore the backup
      localStorage.setItem(CONFIG_STORAGE_KEY, backupStr);

      loggingService.info(
        'Configuration restored from backup',
        'ConfigRecoveryService',
        'restoreFromBackup',
        {
          timestamp: backup.timestamp,
        }
      );

      return true;
    } catch (error: unknown) {
      loggingService.error(
        'Error restoring from backup',
        error as Error,
        'ConfigRecoveryService',
        'restoreFromBackup'
      );
      return false;
    }
  }

  /**
   * Reset configuration to defaults
   */
  public async resetToDefaults(): Promise<boolean> {
    try {
      // Create backup of current config first
      await this.createBackup();

      // Create default configuration
      const defaultConfig = {
        version: '1.0.0',
        lastModified: new Date().toISOString(),
        providers: {
          llm: {
            provider: 'RuleBased',
            apiKey: '',
          },
          tts: {
            provider: 'WindowsSAPI',
            apiKey: '',
          },
          images: {
            provider: 'Stock',
            apiKey: '',
          },
        },
        rendering: {
          quality: 'medium',
          resolution: '1920x1080',
          fps: 30,
        },
        paths: {
          output: '',
          projects: '',
          temp: '',
        },
      };

      localStorage.setItem(CONFIG_STORAGE_KEY, JSON.stringify(defaultConfig));

      loggingService.info(
        'Configuration reset to defaults',
        'ConfigRecoveryService',
        'resetToDefaults'
      );

      return true;
    } catch (error: unknown) {
      loggingService.error(
        'Error resetting to defaults',
        error as Error,
        'ConfigRecoveryService',
        'resetToDefaults'
      );
      return false;
    }
  }

  /**
   * Export configuration to file
   */
  public async exportConfig(): Promise<string> {
    try {
      const configStr = localStorage.getItem(CONFIG_STORAGE_KEY);

      if (!configStr) {
        throw new Error('No configuration to export');
      }

      const config = JSON.parse(configStr);
      const exportData = {
        exportVersion: '1.0',
        exportDate: new Date().toISOString(),
        config,
      };

      return JSON.stringify(exportData, null, 2);
    } catch (error: unknown) {
      loggingService.error(
        'Error exporting config',
        error as Error,
        'ConfigRecoveryService',
        'exportConfig'
      );
      throw error;
    }
  }

  /**
   * Import configuration from file
   */
  public async importConfig(configData: string): Promise<boolean> {
    try {
      // Validate JSON
      let importData: { exportVersion?: string; config?: Record<string, unknown> };
      try {
        importData = JSON.parse(configData);
      } catch (parseError) {
        throw new Error('Invalid configuration file (not valid JSON)');
      }

      if (!importData.config) {
        throw new Error('Invalid configuration file (missing config data)');
      }

      // Create backup of current config first
      await this.createBackup();

      // Import the configuration
      localStorage.setItem(CONFIG_STORAGE_KEY, JSON.stringify(importData.config));

      loggingService.info(
        'Configuration imported successfully',
        'ConfigRecoveryService',
        'importConfig'
      );

      return true;
    } catch (error: unknown) {
      loggingService.error(
        'Error importing config',
        error as Error,
        'ConfigRecoveryService',
        'importConfig'
      );
      throw error;
    }
  }

  /**
   * Check if there are any backups available
   */
  private hasBackups(): boolean {
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && key.startsWith(CONFIG_BACKUP_PREFIX)) {
          return true;
        }
      }
    } catch (error: unknown) {
      loggingService.error(
        'Error checking for backups',
        error as Error,
        'ConfigRecoveryService',
        'hasBackups'
      );
    }
    return false;
  }

  /**
   * Clean up old backups, keeping only the most recent MAX_BACKUPS
   */
  private cleanupOldBackups(): void {
    try {
      const backups = this.listBackups();

      if (backups.length > MAX_BACKUPS) {
        const backupsToDelete = backups.slice(MAX_BACKUPS);

        for (const backup of backupsToDelete) {
          // Find and delete the backup
          for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith(CONFIG_BACKUP_PREFIX)) {
              const backupStr = localStorage.getItem(key);
              if (backupStr) {
                const storedBackup = JSON.parse(backupStr) as ConfigBackup;
                if (storedBackup.timestamp === backup.timestamp) {
                  localStorage.removeItem(key);
                  break;
                }
              }
            }
          }
        }

        loggingService.info(
          `Cleaned up ${backupsToDelete.length} old backups`,
          'ConfigRecoveryService',
          'cleanupOldBackups'
        );
      }
    } catch (error: unknown) {
      loggingService.error(
        'Error cleaning up backups',
        error as Error,
        'ConfigRecoveryService',
        'cleanupOldBackups'
      );
    }
  }

  /**
   * Calculate a simple checksum for the configuration
   */
  private calculateChecksum(data: string): string {
    let hash = 0;
    for (let i = 0; i < data.length; i++) {
      const char = data.charCodeAt(i);
      hash = (hash << 5) - hash + char;
      hash = hash & hash; // Convert to 32bit integer
    }
    return hash.toString(16);
  }
}

// Export singleton instance
export const configRecoveryService = new ConfigRecoveryService();
