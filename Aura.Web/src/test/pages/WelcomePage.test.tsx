import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { render, screen, waitFor } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { WelcomePage } from '../../pages/WelcomePage';
import * as configurationStatusService from '../../services/configurationStatusService';
import * as firstRunService from '../../services/firstRunService';

// Mock the firstRunService
vi.mock('../../services/firstRunService', () => ({
  hasCompletedFirstRun: vi.fn(),
  migrateLegacyFirstRunStatus: vi.fn(),
}));

// Mock the configurationStatusService
vi.mock('../../services/configurationStatusService', () => ({
  configurationStatusService: {
    getStatus: vi.fn(),
    subscribe: vi.fn(() => () => {}),
    markConfigured: vi.fn(),
  },
}));

// Mock the API URL
vi.mock('../../config/api', () => ({
  apiUrl: (path: string) => path,
}));

// Mock the child components that have complex dependencies
vi.mock('../../components/FirstRunDiagnostics', () => ({
  FirstRunDiagnostics: () => null,
}));

vi.mock('../../components/ConfigurationModal', () => ({
  ConfigurationModal: () => null,
}));

vi.mock('../../components/ConfigurationStatusCard', () => ({
  ConfigurationStatusCard: () => null,
}));

vi.mock('../../components/SystemCheckCard', () => ({
  SystemCheckCard: () => null,
}));

vi.mock('../../components/Tooltips', () => ({
  TooltipContent: {},
  TooltipWithLink: () => null,
}));

describe('WelcomePage - Configuration Status', () => {
  const renderWithProviders = (component: React.ReactElement) => {
    return render(
      <FluentProvider theme={webLightTheme}>
        <BrowserRouter>{component}</BrowserRouter>
      </FluentProvider>
    );
  };

  beforeEach(() => {
    vi.clearAllMocks();
    // Mock fetch to prevent actual API calls and return proper data structure
    global.fetch = vi.fn((url) => {
      if (typeof url === 'string' && url.includes('/api/capabilities')) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              tier: 'B',
              cpu: { threads: 8 },
              ram: { gb: 16 },
              gpu: { model: 'NVIDIA RTX 3060', vramGB: 12 },
              enableNVENC: true,
              enableSD: true,
              offlineOnly: false,
            }),
        } as Response);
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: 'healthy' }),
      } as Response);
    });
  });

  it('should show setup required banner when system is not configured', async () => {
    vi.mocked(firstRunService.hasCompletedFirstRun).mockResolvedValue(true);
    vi.mocked(configurationStatusService.configurationStatusService.getStatus).mockResolvedValue({
      isConfigured: false,
      lastChecked: new Date().toISOString(),
      checks: {
        providerConfigured: false,
        providerValidated: false,
        workspaceCreated: false,
        ffmpegDetected: false,
        apiKeysValid: false,
      },
      details: {
        configuredProviders: [],
      },
      issues: [],
    });

    renderWithProviders(<WelcomePage />);

    await waitFor(() => {
      expect(
        screen.getByText(/Complete the quick setup to start creating videos/i)
      ).toBeInTheDocument();
    });

    expect(screen.getByRole('button', { name: /Start Setup Wizard/i })).toBeInTheDocument();
  });

  it('should show ready banner when system is configured', async () => {
    vi.mocked(firstRunService.hasCompletedFirstRun).mockResolvedValue(true);
    vi.mocked(configurationStatusService.configurationStatusService.getStatus).mockResolvedValue({
      isConfigured: true,
      lastChecked: new Date().toISOString(),
      checks: {
        providerConfigured: true,
        providerValidated: true,
        workspaceCreated: true,
        ffmpegDetected: true,
        apiKeysValid: true,
      },
      details: {
        configuredProviders: ['OpenAI'],
        ffmpegVersion: '6.0',
        ffmpegPath: '/usr/bin/ffmpeg',
        diskSpaceAvailable: 100,
        gpuAvailable: true,
      },
      issues: [],
    });

    renderWithProviders(<WelcomePage />);

    await waitFor(() => {
      expect(screen.getByText(/System Ready!/i)).toBeInTheDocument();
    });
  });

  it('should always show Create Video and Settings buttons', async () => {
    vi.mocked(firstRunService.hasCompletedFirstRun).mockResolvedValue(true);
    vi.mocked(configurationStatusService.configurationStatusService.getStatus).mockResolvedValue({
      isConfigured: false,
      lastChecked: new Date().toISOString(),
      checks: {
        providerConfigured: false,
        providerValidated: false,
        workspaceCreated: false,
        ffmpegDetected: false,
        apiKeysValid: false,
      },
      details: {
        configuredProviders: [],
      },
      issues: [],
    });

    renderWithProviders(<WelcomePage />);

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Create Video/i })).toBeInTheDocument();
    });

    expect(screen.getByRole('button', { name: /Settings/i })).toBeInTheDocument();
  });

  it('should handle error gracefully when checking configuration status fails', async () => {
    vi.mocked(firstRunService.hasCompletedFirstRun).mockResolvedValue(true);
    vi.mocked(configurationStatusService.configurationStatusService.getStatus).mockRejectedValue(
      new Error('Failed to check status')
    );

    renderWithProviders(<WelcomePage />);

    await waitFor(() => {
      // Should not crash and should show the page
      expect(screen.getByText(/Welcome to Aura Video Studio/i)).toBeInTheDocument();
    });
  });
});
