import {
  makeStyles,
  tokens,
  Text,
  Button,
  Badge,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Title3,
  Card,
} from '@fluentui/react-components';
import {
  ArrowDownload24Regular,
  MoreHorizontal24Regular,
  CheckmarkCircle24Filled,
  Warning24Filled,
  ErrorCircle24Filled,
} from '@fluentui/react-icons';
import { useState } from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import type { ProviderHealth, UsageData, QuickInsights } from '../../state/dashboard';
import { ProviderDetailsModal } from './ProviderDetailsModal';

const useStyles = makeStyles({
  widgetContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  card: {
    width: '100%',
  },
  cardHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: tokens.spacingVerticalM,
  },
  toggleButton: {
    marginBottom: tokens.spacingVerticalM,
  },
  chartContainer: {
    width: '100%',
    height: '300px',
  },
  providerList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  providerItem: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      cursor: 'pointer',
    },
  },
  providerInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  providerMetrics: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  statusIcon: {
    fontSize: '24px',
  },
  insightsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  insightItem: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: tokens.spacingVerticalS,
  },
  insightLabel: {
    color: tokens.colorNeutralForeground3,
  },
  insightValue: {
    fontWeight: tokens.fontWeightSemibold,
  },
});

interface UsageChartProps {
  data: UsageData[];
  onExport?: (format: 'png' | 'csv') => void;
}

export function UsageChart({ data, onExport }: UsageChartProps) {
  const styles = useStyles();
  const [showCost, setShowCost] = useState(false);

  const handleExport = (format: 'png' | 'csv') => {
    if (onExport) {
      onExport(format);
    } else {
      // Default implementation
      if (format === 'csv') {
        const csv = [
          'Date,API Calls,Cost',
          ...data.map((d) => `${d.date},${d.apiCalls},${d.cost.toFixed(2)}`),
        ].join('\n');
        const blob = new Blob([csv], { type: 'text/csv' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'usage-data.csv';
        a.click();
        URL.revokeObjectURL(url);
      }
    }
  };

  return (
    <Card className={styles.card}>
      <div className={styles.cardHeader}>
        <Title3>API Usage</Title3>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Button
              appearance="transparent"
              icon={<MoreHorizontal24Regular />}
              aria-label="More options"
            />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<ArrowDownload24Regular />} onClick={() => handleExport('png')}>
                Export as PNG
              </MenuItem>
              <MenuItem icon={<ArrowDownload24Regular />} onClick={() => handleExport('csv')}>
                Export as CSV
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>
      <Button
        appearance="outline"
        size="small"
        className={styles.toggleButton}
        onClick={() => setShowCost(!showCost)}
      >
        Show: {showCost ? 'Cost' : 'API Calls'}
      </Button>
      <div className={styles.chartContainer}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis
              dataKey="date"
              tick={{ fontSize: 12 }}
              tickFormatter={(value) =>
                new Date(value).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
              }
            />
            <YAxis tick={{ fontSize: 12 }} />
            <Tooltip
              formatter={(value: number) => (showCost ? `$${value.toFixed(2)}` : value.toString())}
              labelFormatter={(label) => new Date(label).toLocaleDateString()}
            />
            <Legend />
            <Line
              type="monotone"
              dataKey={showCost ? 'cost' : 'apiCalls'}
              stroke={tokens.colorBrandBackground}
              strokeWidth={2}
              dot={{ r: 4 }}
              activeDot={{ r: 6 }}
              name={showCost ? 'Cost ($)' : 'API Calls'}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
    </Card>
  );
}

interface ProviderHealthWidgetProps {
  providers: ProviderHealth[];
  onProviderClick?: (provider: ProviderHealth) => void;
}

export function ProviderHealthWidget({ providers, onProviderClick }: ProviderHealthWidgetProps) {
  const styles = useStyles();
  const [selectedProvider, setSelectedProvider] = useState<ProviderHealth | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  const handleProviderClick = (provider: ProviderHealth) => {
    setSelectedProvider(provider);
    setModalOpen(true);
    onProviderClick?.(provider);
  };

  const getStatusIcon = (status: ProviderHealth['status']) => {
    switch (status) {
      case 'healthy':
        return (
          <CheckmarkCircle24Filled
            className={styles.statusIcon}
            style={{ color: tokens.colorPaletteGreenForeground1 }}
          />
        );
      case 'degraded':
        return (
          <Warning24Filled
            className={styles.statusIcon}
            style={{ color: tokens.colorPaletteYellowForeground1 }}
          />
        );
      case 'down':
        return (
          <ErrorCircle24Filled
            className={styles.statusIcon}
            style={{ color: tokens.colorPaletteRedForeground1 }}
          />
        );
    }
  };

  const getStatusColor = (status: ProviderHealth['status']): 'success' | 'warning' | 'danger' => {
    switch (status) {
      case 'healthy':
        return 'success';
      case 'degraded':
        return 'warning';
      case 'down':
        return 'danger';
    }
  };

  return (
    <>
      <Card className={styles.card}>
        <div className={styles.cardHeader}>
          <Title3>Provider Health</Title3>
        </div>
        <div className={styles.providerList}>
          {providers.map((provider) => (
            <div
              key={provider.name}
              className={styles.providerItem}
              onClick={() => handleProviderClick(provider)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault();
                  handleProviderClick(provider);
                }
              }}
              role="button"
              tabIndex={0}
              aria-label={`View ${provider.name} details`}
            >
              <div className={styles.providerInfo}>
                {getStatusIcon(provider.status)}
                <div>
                  <Text weight="semibold">{provider.name}</Text>
                  <div className={styles.providerMetrics}>
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                      Response: {provider.responseTime}ms
                    </Text>
                    {provider.errorRate > 1 && (
                      <Text size={200} style={{ color: tokens.colorPaletteRedForeground1 }}>
                        Error rate: {provider.errorRate.toFixed(1)}%
                      </Text>
                    )}
                  </div>
                </div>
              </div>
              <Badge appearance="filled" color={getStatusColor(provider.status)}>
                {provider.status}
              </Badge>
            </div>
          ))}
        </div>
      </Card>
      <ProviderDetailsModal
        provider={selectedProvider}
        open={modalOpen}
        onClose={() => setModalOpen(false)}
      />
    </>
  );
}

interface QuickInsightsWidgetProps {
  insights: QuickInsights;
}

export function QuickInsightsWidget({ insights }: QuickInsightsWidgetProps) {
  const styles = useStyles();

  const insightItems = [
    { label: 'Most Used Template', value: insights.mostUsedTemplate },
    { label: 'Avg Video Duration', value: insights.averageVideoDuration },
    { label: 'Peak Usage Hours', value: insights.peakUsageHours },
    { label: 'Favorite Voice', value: insights.favoriteVoice },
  ];

  return (
    <Card className={styles.card}>
      <div className={styles.cardHeader}>
        <Title3>Quick Insights</Title3>
      </div>
      <div className={styles.insightsList}>
        {insightItems.map((item) => (
          <div key={item.label} className={styles.insightItem}>
            <Text className={styles.insightLabel}>{item.label}</Text>
            <Text className={styles.insightValue}>{item.value}</Text>
          </div>
        ))}
      </div>
    </Card>
  );
}

interface DashboardWidgetsProps {
  usageData: UsageData[];
  providerHealth: ProviderHealth[];
  quickInsights: QuickInsights;
  onExportUsage?: (format: 'png' | 'csv') => void;
  onProviderClick?: (provider: ProviderHealth) => void;
}

export function DashboardWidgets({
  usageData,
  providerHealth,
  quickInsights,
  onExportUsage,
  onProviderClick,
}: DashboardWidgetsProps) {
  const styles = useStyles();

  return (
    <div className={styles.widgetContainer}>
      {usageData.length > 0 && <UsageChart data={usageData} onExport={onExportUsage} />}
      {providerHealth.length > 0 && (
        <ProviderHealthWidget providers={providerHealth} onProviderClick={onProviderClick} />
      )}
      {quickInsights.mostUsedTemplate && <QuickInsightsWidget insights={quickInsights} />}
    </div>
  );
}
