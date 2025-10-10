import { useState, useEffect } from 'react';
import {
  makeStyles,
  tokens,
  Title1,
  Text,
  Button,
  Input,
  Card,
  Badge,
  Spinner,
  Dropdown,
  Option,
  Table,
  TableBody,
  TableCell,
  TableRow,
  TableHeader,
  TableHeaderCell,
} from '@fluentui/react-components';
import { 
  ArrowClockwise24Regular, 
  Copy24Regular,
  Filter24Regular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    maxWidth: '1400px',
    margin: '0 auto',
  },
  header: {
    marginBottom: tokens.spacingVerticalXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  subtitle: {
    color: tokens.colorNeutralForeground3,
  },
  controls: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    marginBottom: tokens.spacingVerticalL,
    flexWrap: 'wrap',
    alignItems: 'center',
  },
  filterGroup: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    flex: 1,
  },
  logCard: {
    padding: 0,
    overflow: 'hidden',
  },
  logTable: {
    width: '100%',
  },
  logRow: {
    cursor: 'pointer',
    '&:hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  timestampCell: {
    minWidth: '180px',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
  levelCell: {
    minWidth: '80px',
  },
  correlationCell: {
    minWidth: '150px',
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
  },
  messageCell: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    wordBreak: 'break-word',
  },
  levelBadge: {
    minWidth: '50px',
  },
  emptyState: {
    padding: tokens.spacingVerticalXXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  copyButton: {
    marginLeft: tokens.spacingHorizontalS,
  },
});

interface LogEntry {
  timestamp: string;
  level: string;
  correlationId: string;
  message: string;
  file: string;
}

export function LogViewer() {
  const styles = useStyles();
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [levelFilter, setLevelFilter] = useState<string>('');
  const [searchTerm, setSearchTerm] = useState('');
  const [limit, setLimit] = useState('500');
  const [copiedId, setCopiedId] = useState<string>('');

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (limit) params.append('limit', limit);
      if (levelFilter) params.append('level', levelFilter);
      if (searchTerm) params.append('search', searchTerm);

      const response = await fetch(`/api/logs?${params.toString()}`);
      const data = await response.json();
      setLogs(data.logs || []);
    } catch (error) {
      console.error('Failed to fetch logs:', error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLogs();
  }, []);

  const getLevelBadgeColor = (level: string): 'success' | 'danger' | 'warning' | 'informative' => {
    const upperLevel = level.toUpperCase();
    if (upperLevel.includes('ERR') || upperLevel.includes('FTL')) return 'danger';
    if (upperLevel.includes('WRN')) return 'warning';
    if (upperLevel.includes('INF')) return 'success';
    return 'informative';
  };

  const copyCorrelationId = (correlationId: string) => {
    navigator.clipboard.writeText(correlationId);
    setCopiedId(correlationId);
    setTimeout(() => setCopiedId(''), 2000);
  };

  const copyLogEntry = (log: LogEntry) => {
    const text = `[${log.timestamp}] [${log.level}] [${log.correlationId}] ${log.message}`;
    navigator.clipboard.writeText(text);
  };

  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <Title1>Log Viewer</Title1>
        <Text className={styles.subtitle}>
          View and filter recent API logs with correlation IDs for debugging
        </Text>
      </div>

      <div className={styles.controls}>
        <div className={styles.filterGroup}>
          <Dropdown
            placeholder="Filter by level"
            value={levelFilter}
            onOptionSelect={(_, data) => setLevelFilter(data.optionValue as string)}
          >
            <Option value="">All Levels</Option>
            <Option value="INF">Info</Option>
            <Option value="WRN">Warning</Option>
            <Option value="ERR">Error</Option>
          </Dropdown>

          <Input
            placeholder="Search logs..."
            value={searchTerm}
            onChange={(_, data) => setSearchTerm(data.value)}
            contentBefore={<Filter24Regular />}
          />

          <Dropdown
            placeholder="Limit"
            value={limit}
            onOptionSelect={(_, data) => setLimit(data.optionValue as string)}
          >
            <Option value="100">100 entries</Option>
            <Option value="500">500 entries</Option>
            <Option value="1000">1000 entries</Option>
          </Dropdown>
        </div>

        <Button
          appearance="primary"
          icon={<ArrowClockwise24Regular />}
          onClick={fetchLogs}
          disabled={loading}
        >
          {loading ? 'Loading...' : 'Refresh'}
        </Button>
      </div>

      <Card className={styles.logCard}>
        {loading && logs.length === 0 ? (
          <div className={styles.emptyState}>
            <Spinner size="large" label="Loading logs..." />
          </div>
        ) : logs.length === 0 ? (
          <div className={styles.emptyState}>
            <Text>No logs found matching your filters</Text>
          </div>
        ) : (
          <Table className={styles.logTable}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell className={styles.timestampCell}>Timestamp</TableHeaderCell>
                <TableHeaderCell className={styles.levelCell}>Level</TableHeaderCell>
                <TableHeaderCell className={styles.correlationCell}>Correlation ID</TableHeaderCell>
                <TableHeaderCell className={styles.messageCell}>Message</TableHeaderCell>
                <TableHeaderCell>Actions</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {logs.map((log, index) => (
                <TableRow key={index} className={styles.logRow}>
                  <TableCell className={styles.timestampCell}>{log.timestamp}</TableCell>
                  <TableCell className={styles.levelCell}>
                    <Badge
                      appearance="filled"
                      color={getLevelBadgeColor(log.level)}
                      className={styles.levelBadge}
                    >
                      {log.level}
                    </Badge>
                  </TableCell>
                  <TableCell className={styles.correlationCell}>
                    {log.correlationId || 'N/A'}
                  </TableCell>
                  <TableCell className={styles.messageCell}>{log.message}</TableCell>
                  <TableCell>
                    <Button
                      size="small"
                      appearance="subtle"
                      icon={<Copy24Regular />}
                      onClick={() => copyLogEntry(log)}
                      title="Copy log entry"
                    />
                    {log.correlationId && (
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<Copy24Regular />}
                        onClick={() => copyCorrelationId(log.correlationId)}
                        title="Copy correlation ID"
                        className={styles.copyButton}
                      >
                        {copiedId === log.correlationId ? 'Copied!' : ''}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>
    </div>
  );
}
