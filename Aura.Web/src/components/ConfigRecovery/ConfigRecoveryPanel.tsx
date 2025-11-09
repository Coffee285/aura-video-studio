import {
  Button,
  Card,
  CardHeader,
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  DialogContent,
  Text,
  makeStyles,
  tokens,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  ArrowDownload20Regular,
  ArrowUpload20Regular,
  ArrowReset20Regular,
  Checkmark20Regular,
  Dismiss20Regular,
  Warning20Regular,
} from '@fluentui/react-icons';
import { useState, useEffect } from 'react';
import {
  configRecoveryService,
  type ConfigBackup,
  type ConfigValidationResult,
} from '../../services/config/configRecoveryService';
import { errorReportingService } from '../../services/errorReportingService';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  card: {
    width: '100%',
  },
  actions: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  validationResult: {
    padding: tokens.spacingVerticalM,
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: tokens.spacingVerticalM,
  },
  backupList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    maxHeight: '300px',
    overflowY: 'auto',
  },
  backupItem: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  backupInfo: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
});

interface ConfigRecoveryPanelProps {
  onConfigRestored?: () => void;
}

export function ConfigRecoveryPanel({ onConfigRestored }: ConfigRecoveryPanelProps) {
  const styles = useStyles();
  const [validation, setValidation] = useState<ConfigValidationResult | null>(null);
  const [backups, setBackups] = useState<ConfigBackup[]>([]);
  const [isValidating, setIsValidating] = useState(false);
  const [showBackupDialog, setShowBackupDialog] = useState(false);
  const [showResetDialog, setShowResetDialog] = useState(false);

  useEffect(() => {
    validateConfiguration();
    loadBackups();
  }, []);

  const validateConfiguration = async () => {
    setIsValidating(true);
    try {
      const result = await configRecoveryService.validateConfig();
      setValidation(result);
    } catch (error: unknown) {
      errorReportingService.error('Failed to validate configuration', String(error));
    } finally {
      setIsValidating(false);
    }
  };

  const loadBackups = () => {
    const backupList = configRecoveryService.listBackups();
    setBackups(backupList);
  };

  const handleCreateBackup = async () => {
    try {
      const success = await configRecoveryService.createBackup();
      if (success) {
        errorReportingService.info('Configuration backup created successfully', '');
        loadBackups();
      } else {
        errorReportingService.error('Failed to create backup', 'Check console for details');
      }
    } catch (error: unknown) {
      errorReportingService.error('Failed to create backup', String(error));
    }
  };

  const handleRestoreBackup = async (timestamp: string) => {
    try {
      const success = await configRecoveryService.restoreFromBackup(timestamp);
      if (success) {
        errorReportingService.info('Configuration restored successfully', '');
        setShowBackupDialog(false);
        if (onConfigRestored) {
          onConfigRestored();
        }
        await validateConfiguration();
      } else {
        errorReportingService.error('Failed to restore backup', 'Backup may be corrupted');
      }
    } catch (error: unknown) {
      errorReportingService.error('Failed to restore backup', String(error));
    }
  };

  const handleResetToDefaults = async () => {
    try {
      const success = await configRecoveryService.resetToDefaults();
      if (success) {
        errorReportingService.info('Configuration reset to defaults', '');
        setShowResetDialog(false);
        if (onConfigRestored) {
          onConfigRestored();
        }
        await validateConfiguration();
      } else {
        errorReportingService.error('Failed to reset configuration', 'Check console for details');
      }
    } catch (error: unknown) {
      errorReportingService.error('Failed to reset configuration', String(error));
    }
  };

  const handleExportConfig = async () => {
    try {
      const configData = await configRecoveryService.exportConfig();

      const blob = new Blob([configData], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `aura-config-${new Date().toISOString().split('T')[0]}.json`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);

      errorReportingService.info('Configuration exported successfully', '');
    } catch (error: unknown) {
      errorReportingService.error('Failed to export configuration', String(error));
    }
  };

  const handleImportConfig = () => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json';
    input.onchange = async (e) => {
      const file = (e.target as HTMLInputElement).files?.[0];
      if (!file) return;

      try {
        const text = await file.text();
        const success = await configRecoveryService.importConfig(text);
        if (success) {
          errorReportingService.info('Configuration imported successfully', '');
          if (onConfigRestored) {
            onConfigRestored();
          }
          await validateConfiguration();
        }
      } catch (error: unknown) {
        errorReportingService.error('Failed to import configuration', String(error));
      }
    };
    input.click();
  };

  const formatDate = (timestamp: string) => {
    const date = new Date(timestamp);
    return date.toLocaleString();
  };

  return (
    <div className={styles.container}>
      <Card className={styles.card}>
        <CardHeader header={<Text weight="semibold">Configuration Status</Text>} />

        {isValidating ? (
          <Spinner label="Validating configuration..." />
        ) : validation ? (
          <div className={styles.validationResult}>
            {validation.isValid ? (
              <MessageBar intent="success" icon={<Checkmark20Regular />}>
                <MessageBarBody>Configuration is valid</MessageBarBody>
              </MessageBar>
            ) : validation.isCorrupted ? (
              <MessageBar intent="error" icon={<Dismiss20Regular />}>
                <MessageBarBody>
                  Configuration is corrupted
                  {validation.canRecover && ' - Recovery options available below'}
                </MessageBarBody>
              </MessageBar>
            ) : (
              <MessageBar intent="warning" icon={<Warning20Regular />}>
                <MessageBarBody>Configuration has warnings</MessageBarBody>
              </MessageBar>
            )}

            {validation.errors.length > 0 && (
              <div style={{ marginTop: tokens.spacingVerticalS }}>
                <Text weight="semibold">Errors:</Text>
                <ul style={{ margin: tokens.spacingVerticalXS, paddingLeft: '1.5rem' }}>
                  {validation.errors.map((error, index) => (
                    <li key={index}>
                      <Text size={200}>{error}</Text>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {validation.warnings.length > 0 && (
              <div style={{ marginTop: tokens.spacingVerticalS }}>
                <Text weight="semibold">Warnings:</Text>
                <ul style={{ margin: tokens.spacingVerticalXS, paddingLeft: '1.5rem' }}>
                  {validation.warnings.map((warning, index) => (
                    <li key={index}>
                      <Text size={200}>{warning}</Text>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ) : null}

        <div className={styles.actions}>
          <Button
            appearance="secondary"
            icon={<ArrowDownload20Regular />}
            onClick={handleExportConfig}
          >
            Export Config
          </Button>
          <Button
            appearance="secondary"
            icon={<ArrowUpload20Regular />}
            onClick={handleImportConfig}
          >
            Import Config
          </Button>
          <Button appearance="secondary" onClick={handleCreateBackup}>
            Create Backup
          </Button>
          <Button
            appearance="secondary"
            onClick={() => setShowBackupDialog(true)}
            disabled={backups.length === 0}
          >
            Restore Backup ({backups.length})
          </Button>
          <Button
            appearance="secondary"
            icon={<ArrowReset20Regular />}
            onClick={() => setShowResetDialog(true)}
          >
            Reset to Defaults
          </Button>
        </div>
      </Card>

      <Dialog open={showBackupDialog} onOpenChange={(_, data) => setShowBackupDialog(data.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Restore from Backup</DialogTitle>
            <DialogContent>
              <Text>Select a backup to restore:</Text>
              <div className={styles.backupList}>
                {backups.map((backup) => (
                  <div key={backup.timestamp} className={styles.backupItem}>
                    <div className={styles.backupInfo}>
                      <Text weight="semibold">{formatDate(backup.timestamp)}</Text>
                      <Text size={200}>Version: {backup.version}</Text>
                    </div>
                    <Button
                      appearance="primary"
                      size="small"
                      onClick={() => {
                        handleRestoreBackup(backup.timestamp);
                      }}
                    >
                      Restore
                    </Button>
                  </div>
                ))}
              </div>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setShowBackupDialog(false)}>
                Cancel
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={showResetDialog} onOpenChange={(_, data) => setShowResetDialog(data.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Reset to Defaults</DialogTitle>
            <DialogContent>
              <MessageBar intent="warning">
                <MessageBarBody>
                  This will reset all configuration to default values. A backup of your current
                  configuration will be created automatically.
                </MessageBarBody>
              </MessageBar>
              <Text>Are you sure you want to continue?</Text>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setShowResetDialog(false)}>
                Cancel
              </Button>
              <Button appearance="primary" onClick={handleResetToDefaults}>
                Reset to Defaults
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}
