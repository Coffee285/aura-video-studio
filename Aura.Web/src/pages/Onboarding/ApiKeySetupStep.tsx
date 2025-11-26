import {
  makeStyles,
  tokens,
  Title2,
  Title3,
  Text,
  Button,
  Card,
  Badge,
  Checkbox,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
} from '@fluentui/react-components';
import {
  Checkmark24Regular,
  Warning24Regular,
  ChevronDown24Regular,
  ChevronUp24Regular,
} from '@fluentui/react-icons';
import { useState, useEffect, useCallback } from 'react';
import { EnhancedApiKeyInput } from '../../components/Onboarding/EnhancedApiKeyInput';
import type { FieldValidationError } from '../../components/Onboarding/FieldValidationErrors';
import { ProviderHelpPanel } from '../../components/ProviderHelpPanel';
import { useProviderStatus } from '../../hooks/useProviderStatus';
import { offlineProvidersApi } from '../../services/api/offlineProvidersApi';
import type { OfflineProviderStatus } from '@/types/offlineProviders';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXL,
    maxWidth: '900px',
    margin: '0 auto',
  },
  quickStartSection: {
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  quickStartHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalM,
  },
  quickStartContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  quickStartActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  sectionHeader: {
    marginBottom: tokens.spacingVerticalM,
  },
  sectionTitle: {
    marginBottom: tokens.spacingVerticalXXS,
  },
  sectionDescription: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
  },
  providersGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
    gap: tokens.spacingVerticalM,
  },
  providerCard: {
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    transition: 'all 0.2s ease',
    cursor: 'pointer',
    ':hover': {
      borderColor: tokens.colorNeutralStroke1,
      boxShadow: tokens.shadow4,
    },
  },
  providerCardExpanded: {
    borderColor: tokens.colorBrandStroke1,
    boxShadow: tokens.shadow8,
  },
  providerCardHeader: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: tokens.spacingVerticalS,
  },
  providerCardTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  providerLogo: {
    fontSize: '28px',
    lineHeight: 1,
  },
  providerName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  providerDescription: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase300,
    marginBottom: tokens.spacingVerticalM,
    lineHeight: tokens.lineHeightBase300,
  },
  providerContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    marginTop: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  statusBadge: {
    marginLeft: 'auto',
  },
  localProviderStatus: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusSmall,
    marginBottom: tokens.spacingVerticalM,
  },
  localActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalL,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  footerText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
  },
  warningMessage: {
    marginTop: tokens.spacingVerticalL,
  },
  collapseButton: {
    marginTop: tokens.spacingVerticalS,
  },
});

export interface ApiKeySetupStepProps {
  apiKeys: Record<string, string>;
  validationStatus: Record<string, 'idle' | 'validating' | 'valid' | 'invalid'>;
  validationErrors: Record<string, string>;
  fieldErrors?: Record<string, FieldValidationError[]>;
  accountInfo?: Record<string, string>;
  onApiKeyChange: (provider: string, value: string) => void;
  onValidateApiKey: (provider: string) => void;
  onSkipValidation?: (provider: string) => void;
  onSkipAll: () => void;
  onLocalProviderReady?: (provider: string) => void;
  allowInvalidKeys?: boolean;
  onAllowInvalidKeysChange?: (allow: boolean) => void;
  hasAtLeastOneProvider?: boolean;
}

interface ProviderConfig {
  id: string;
  name: string;
  logo: string;
  description: string;
  usedFor: string;
  signupUrl: string;
  steps: string[];
  pricingInfo: {
    freeTier?: string;
    costEstimate: string;
  };
  keyFormat?: string;
  requiresMultipleKeys?: boolean;
  category: 'llm' | 'tts' | 'image' | 'other';
  requiresApiKey?: boolean;
  localSetup?: {
    downloadUrl?: string;
    instructions?: string[];
    readyHint?: string;
  };
}

interface ProviderSection {
  id: string;
  title: string;
  description: string;
  category: 'llm' | 'tts' | 'image' | 'other';
}

// Provider section definitions (kept for reference/future use)
const _sections: ProviderSection[] = [
  {
    id: 'llm',
    title: 'LLM Providers',
    description: 'Large Language Models for script generation and content creation',
    category: 'llm',
  },
  {
    id: 'tts',
    title: 'Text-to-Speech',
    description: 'AI voice synthesis for narration and voiceovers',
    category: 'tts',
  },
  {
    id: 'image',
    title: 'Image Services',
    description: 'Stock images and AI-generated visuals for video content',
    category: 'image',
  },
];

// Provider configurations (kept for reference/future use)
const _providers: ProviderConfig[] = [
  {
    id: 'openai',
    name: 'OpenAI',
    logo: '🤖',
    description: 'Advanced AI models for high-quality script generation',
    usedFor: 'Script generation with OpenAI GPT models, industry-leading AI language models',
    signupUrl: 'https://platform.openai.com/signup',
    steps: [
      'Create an account at platform.openai.com',
      'Add a payment method to your account',
      'Go to the API Keys section',
      'Create a new secret key',
      'Copy the key (starts with "sk-" or "sk-proj-")',
    ],
    pricingInfo: {
      freeTier: '$5 credit for new users',
      costEstimate: '~$0.15 per 5-minute video script',
    },
    keyFormat: 'start with "sk-" or "sk-proj-"',
    category: 'llm',
  },
  {
    id: 'anthropic',
    name: 'Anthropic',
    logo: '🧠',
    description: 'Claude for alternative script generation',
    usedFor: 'Alternative script generation with Claude, known for detailed and creative content',
    signupUrl: 'https://console.anthropic.com',
    steps: [
      'Create an account at console.anthropic.com',
      'Add a payment method',
      'Navigate to API Keys section',
      'Create a new key',
      'Copy the key (starts with "sk-ant-")',
    ],
    pricingInfo: {
      freeTier: '$5 credit for new users',
      costEstimate: '~$0.12 per 5-minute video script',
    },
    keyFormat: 'start with "sk-ant-"',
    category: 'llm',
  },
  {
    id: 'gemini',
    name: 'Google Gemini',
    logo: '✨',
    description: 'Gemini Pro for script generation',
    usedFor: "Script generation with Google's Gemini Pro model",
    signupUrl: 'https://makersuite.google.com/app/apikey',
    steps: [
      'Sign in with your Google account',
      'Navigate to MakerSuite',
      'Click "Create API Key"',
      'Copy the generated key (39 characters)',
    ],
    pricingInfo: {
      freeTier: 'Free tier available with rate limits',
      costEstimate: '~$0.10 per 5-minute script on paid tier',
    },
    keyFormat: 'are 39 characters long',
    category: 'llm',
  },
  {
    id: 'ollama',
    name: 'Ollama',
    logo: '🦙',
    description: 'Local LLM for offline script generation',
    usedFor: 'Run AI models locally on your machine for script generation without API costs',
    signupUrl: 'https://ollama.ai',
    steps: [
      'Download and install Ollama from ollama.ai',
      'Run Ollama in the background',
      'Pull a model (e.g., "ollama pull llama2")',
      'No API key required - runs locally',
    ],
    pricingInfo: {
      freeTier: 'Completely free - runs on your hardware',
      costEstimate: 'Free (requires local compute resources)',
    },
    keyFormat: 'No API key needed for local Ollama',
    requiresApiKey: false,
    localSetup: {
      downloadUrl: 'https://ollama.ai/download',
      instructions: [
        'Install Ollama and ensure the Ollama service is running.',
        'Pull at least one model (e.g., `ollama pull llama3.1`)',
        'Leave the Ollama tray/service running while Aura is open.',
      ],
      readyHint: 'Click "Mark as Ready" once Ollama is installed and running.',
    },
    category: 'llm',
  },
  {
    id: 'elevenlabs',
    name: 'ElevenLabs',
    logo: '🎙️',
    description: 'High-quality AI voice synthesis',
    usedFor: 'Professional AI voice synthesis with natural-sounding voices',
    signupUrl: 'https://elevenlabs.io/sign-up',
    steps: [
      'Create an account at elevenlabs.io',
      'Subscribe to a plan (or use free tier)',
      'Go to Profile Settings',
      'Find the API Key section',
      'Copy your API key (32-character hex string)',
    ],
    pricingInfo: {
      freeTier: '10,000 characters/month (~10 minutes of audio)',
      costEstimate: '$5/month for 30,000 characters (~30 minutes)',
    },
    keyFormat: 'are 32-character hex strings',
    category: 'tts',
  },
  {
    id: 'playht',
    name: 'PlayHT',
    logo: '🔊',
    description: 'Alternative AI voice synthesis',
    usedFor: 'Alternative AI voice synthesis with multiple voice options',
    signupUrl: 'https://play.ht/signup',
    steps: [
      'Create an account at play.ht',
      'Subscribe to a plan',
      'Navigate to Settings',
      'Find API Credentials section',
      'Copy both User ID and Secret Key',
    ],
    pricingInfo: {
      freeTier: '12,500 characters (~10 minutes)',
      costEstimate: '$31/month for 12.5 hours of audio',
    },
    requiresMultipleKeys: true,
    category: 'tts',
  },
  {
    id: 'windows',
    name: 'Windows TTS',
    logo: '🪟',
    description: 'Built-in Windows text-to-speech',
    usedFor: 'Native Windows speech synthesis - always available, no setup required',
    signupUrl: '',
    steps: [
      'No setup required - uses Windows built-in TTS',
      'Available on all Windows 10+ systems',
      'No API key needed - completely free',
    ],
    pricingInfo: {
      freeTier: 'Completely free - built into Windows',
      costEstimate: 'Free (no costs, no limits)',
    },
    keyFormat: 'No API key needed - built into Windows',
    requiresApiKey: false,
    localSetup: {
      downloadUrl: '',
      instructions: [
        'Windows TTS is built into Windows 10 and later',
        'No installation or configuration needed',
        'Automatically available and ready to use',
      ],
      readyHint: 'Windows TTS is always ready - click "Mark as Ready" to confirm.',
    },
    category: 'tts',
  },
  {
    id: 'piper',
    name: 'Piper TTS',
    logo: '🎤',
    description: 'Fast local TTS, works offline',
    usedFor: 'High-quality offline text-to-speech without API costs',
    signupUrl: 'https://github.com/rhasspy/piper',
    steps: [
      'Install Piper via Download Center in Settings',
      'Download voice models (50+ voices available)',
      'No API key required - runs locally',
    ],
    pricingInfo: {
      freeTier: 'Completely free - runs on your hardware',
      costEstimate: 'Free (requires local installation)',
    },
    keyFormat: 'No API key needed for local Piper',
    requiresApiKey: false,
    localSetup: {
      downloadUrl: 'https://github.com/rhasspy/piper/releases',
      instructions: [
        'Install Piper via Settings → Download Center → Engines',
        'Download at least one voice model',
        'Piper will be automatically detected when installed',
      ],
      readyHint: 'Click "Mark as Ready" once Piper is installed and voice models are downloaded.',
    },
    category: 'tts',
  },
  {
    id: 'mimic3',
    name: 'Mimic3 TTS',
    logo: '🎭',
    description: 'Neural TTS, works offline',
    usedFor: 'High-quality neural text-to-speech with natural voices',
    signupUrl: 'https://github.com/MycroftAI/mimic3',
    steps: [
      'Install Mimic3 server (runs on port 59125)',
      'Ensure the Mimic3 service is running',
      'No API key required - runs locally',
    ],
    pricingInfo: {
      freeTier: 'Completely free - runs on your hardware',
      costEstimate: 'Free (requires local installation)',
    },
    keyFormat: 'No API key needed for local Mimic3',
    requiresApiKey: false,
    localSetup: {
      downloadUrl: 'https://github.com/MycroftAI/mimic3',
      instructions: [
        'Install Mimic3 server (see Download Center in Settings)',
        'Start the Mimic3 service (runs on port 59125)',
        'Leave the Mimic3 service running while Aura is open',
      ],
      readyHint: 'Click "Mark as Ready" once Mimic3 is installed and the service is running.',
    },
    category: 'tts',
  },
  {
    id: 'placeholder',
    name: 'Placeholder Images',
    logo: '🎨',
    description: 'Solid color backgrounds with text - always available',
    usedFor:
      'Guaranteed fallback image provider - generates solid color backgrounds with text overlays',
    signupUrl: '',
    steps: [
      'No setup required - always available',
      'Generates solid color backgrounds automatically',
      'No API key needed - built-in fallback',
    ],
    pricingInfo: {
      freeTier: 'Completely free - built into Aura',
      costEstimate: 'Free (no costs, no limits)',
    },
    keyFormat: 'No API key needed - always available',
    requiresApiKey: false,
    localSetup: {
      downloadUrl: '',
      instructions: [
        'Placeholder images are built into Aura',
        'No installation or configuration needed',
        'Automatically available as fallback when other providers fail',
      ],
      readyHint: 'Placeholder images are always ready - click "Mark as Ready" to confirm.',
    },
    category: 'image',
  },
  {
    id: 'replicate',
    name: 'Replicate',
    logo: '🎨',
    description: 'Image generation with Stable Diffusion',
    usedFor: 'Custom image generation using Stable Diffusion and other AI models',
    signupUrl: 'https://replicate.com/signin',
    steps: [
      'Sign in with GitHub at replicate.com',
      'Go to Account Settings',
      'Navigate to API Tokens',
      'Create a new token',
      'Copy the token (starts with "r8_")',
    ],
    pricingInfo: {
      freeTier: '$5 credit for new users',
      costEstimate: '~$0.0023 per image (~$0.10 per video)',
    },
    keyFormat: 'start with "r8_"',
    category: 'image',
  },
  {
    id: 'pexels',
    name: 'Pexels',
    logo: '📷',
    description: 'Free stock photos and videos',
    usedFor: 'Access to high-quality stock images and video content for your projects',
    signupUrl: 'https://www.pexels.com/api/',
    steps: [
      'Create a free account at pexels.com',
      'Navigate to the API page',
      'Click "Get Started"',
      'Copy your API key',
    ],
    pricingInfo: {
      freeTier: 'Free with 200 requests/hour',
      costEstimate: 'Completely free for all usage',
    },
    keyFormat: 'are alphanumeric strings',
    category: 'image',
  },
];

export function ApiKeySetupStep({
  apiKeys,
  validationStatus,
  validationErrors: _validationErrors,
  fieldErrors = {},
  accountInfo = {},
  onApiKeyChange,
  onValidateApiKey,
  onSkipValidation,
  onSkipAll,
  onLocalProviderReady,
  allowInvalidKeys = false,
  onAllowInvalidKeysChange,
  hasAtLeastOneProvider = false,
}: ApiKeySetupStepProps) {
  const styles = useStyles();
  const [rateLimit, setRateLimit] = useState<Record<string, number>>({});
  const [expandedProviders, setExpandedProviders] = useState<Set<string>>(new Set());
  const [showQuickStart, setShowQuickStart] = useState(true);
  const [localTtsStatus, setLocalTtsStatus] = useState<{
    windows: OfflineProviderStatus | null;
    piper: OfflineProviderStatus | null;
    mimic3: OfflineProviderStatus | null;
  }>({
    windows: {
      name: 'Windows TTS',
      isAvailable: true,
      message: 'Built into Windows - always available',
    },
    piper: null,
    mimic3: null,
  });
  const [checkingTts, setCheckingTts] = useState<{
    windows: boolean;
    piper: boolean;
    mimic3: boolean;
  }>({
    windows: false,
    piper: false,
    mimic3: false,
  });

  // Real-time provider status polling (every 15 seconds)
  const {
    llmProviders,
    ttsProviders,
    imageProviders,
    isLoading: isLoadingProviderStatus,
  } = useProviderStatus(15000);

  const checkLocalTtsStatus = useCallback(async (provider: 'windows' | 'piper' | 'mimic3') => {
    setCheckingTts((prev) => ({ ...prev, [provider]: true }));
    try {
      let status: OfflineProviderStatus;
      if (provider === 'windows') {
        // Check Windows TTS via API (handles platform detection)
        try {
          status = await offlineProvidersApi.checkWindowsTts();
        } catch (error) {
          // If API fails, assume available on Windows (graceful degradation)
          console.warn('Windows TTS API check failed, assuming available:', error);
          status = {
            name: 'Windows TTS',
            isAvailable: true,
            message: 'Built into Windows - always available',
          };
        }
      } else {
        status =
          provider === 'piper'
            ? await offlineProvidersApi.checkPiper()
            : await offlineProvidersApi.checkMimic3();
      }
      setLocalTtsStatus((prev) => ({ ...prev, [provider]: status }));
    } catch (error) {
      console.error(`Failed to check ${provider} status:`, error);
      setLocalTtsStatus((prev) => ({
        ...prev,
        [provider]: {
          name:
            provider === 'windows'
              ? 'Windows TTS'
              : provider === 'piper'
                ? 'Piper TTS'
                : 'Mimic3 TTS',
          isAvailable: provider === 'windows', // Windows is always available on Windows
          message: provider === 'windows' ? 'Built into Windows' : 'Status check failed',
        },
      }));
    } finally {
      setCheckingTts((prev) => ({ ...prev, [provider]: false }));
    }
  }, []);

  // Check local TTS status on mount
  useEffect(() => {
    checkLocalTtsStatus('windows');
    checkLocalTtsStatus('piper');
    checkLocalTtsStatus('mimic3');
  }, [checkLocalTtsStatus]);

  // Auto-mark Windows TTS and Placeholder as ready on mount (they're always available)
  useEffect(() => {
    if (onLocalProviderReady) {
      // Windows TTS is always available
      onLocalProviderReady('windows');
      // Placeholder images are always available
      onLocalProviderReady('placeholder');
    }
  }, [onLocalProviderReady]);

  const openExternalLink = (url: string) => {
    if (typeof window === 'undefined') {
      return;
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const handleValidate = (providerId: string) => {
    // Check rate limiting - reduced from 20s to 5s for better UX
    const now = Date.now();
    const lastAttempt = rateLimit[providerId] || 0;
    const timeSinceLastAttempt = now - lastAttempt;

    if (timeSinceLastAttempt < 5000) {
      // 5 seconds between attempts (reduced from 20)
      alert('Too many attempts. Please wait a moment before trying again.');
      return;
    }

    setRateLimit({ ...rateLimit, [providerId]: now });
    onValidateApiKey(providerId);
  };

  const toggleProvider = (providerId: string) => {
    setExpandedProviders((prev) => {
      const next = new Set(prev);
      if (next.has(providerId)) {
        next.delete(providerId);
      } else {
        next.add(providerId);
      }
      return next;
    });
  };

  const hasAtLeastOneValidKey = Object.values(validationStatus).some(
    (status) => status === 'valid'
  );

  const configuredKeys = Object.entries(apiKeys).filter(([_, key]) => key && key.trim().length > 0);
  const invalidKeys = configuredKeys.filter(
    ([provider, _]) => validationStatus[provider] === 'invalid'
  );
  const hasInvalidKeys = invalidKeys.length > 0;

  // Note: llmProviders, ttsProviders, imageProviders come from useProviderStatus hook (line 564)
  // The duplicate declarations below were removed to fix build error

  // CRITICAL FIX: Add error state to handle rendering errors gracefully
  const [renderError, setRenderError] = useState<Error | null>(null);

  // Safety check: Ensure we always render something visible
  if (!styles || !styles.container) {
    console.error('[ApiKeySetupStep] Styles not loaded, rendering fallback');
    return (
      <div
        style={{
          padding: '2rem',
          backgroundColor: tokens.colorNeutralBackground1 || '#1e1e1e',
          color: tokens.colorNeutralForeground1 || '#ffffff',
          minHeight: '400px',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          alignItems: 'center',
        }}
      >
        <Spinner size="large" />
        <Text style={{ marginTop: '1rem' }}>Loading provider configuration...</Text>
      </div>
    );
  }

  // CRITICAL FIX: If there's a render error, show error UI instead of black screen
  if (renderError) {
    return (
      <div
        style={{
          padding: tokens.spacingVerticalXXL,
          textAlign: 'center',
          backgroundColor: tokens.colorNeutralBackground1 || '#1e1e1e',
          color: tokens.colorNeutralForeground1 || '#ffffff',
          minHeight: '400px',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          alignItems: 'center',
        }}
      >
        <Warning24Regular
          style={{
            fontSize: '48px',
            color: tokens.colorPaletteRedForeground1,
            marginBottom: tokens.spacingVerticalM,
          }}
        />
        <Title2>Error Loading Provider Configuration</Title2>
        <Text style={{ display: 'block', marginTop: tokens.spacingVerticalS }}>
          An error occurred while loading the provider configuration. Please try refreshing the
          page.
        </Text>
        <Button
          appearance="primary"
          onClick={() => window.location.reload()}
          style={{ marginTop: tokens.spacingVerticalM }}
        >
          Reload Page
        </Button>
      </div>
    );
  }

  // CRITICAL FIX: Wrap handlers to catch errors and prevent black screen
  const safeOnApiKeyChange = (provider: string, value: string) => {
    try {
      onApiKeyChange(provider, value);
    } catch (error) {
      console.error('[ApiKeySetupStep] Error in onApiKeyChange:', error);
      setRenderError(error instanceof Error ? error : new Error(String(error)));
    }
  };

  // Safe wrapper for API key validation (prefixed with underscore as it's reserved for future use)
  const _safeOnValidateApiKey = async (provider: string) => {
    try {
      await onValidateApiKey(provider);
    } catch (error) {
      console.error('[ApiKeySetupStep] Error in onValidateApiKey:', error);
      // Don't set render error for validation errors - they're expected
    }
  };

  return (
    <div
      className={styles.container}
      style={{
        // Ensure background is always set to prevent black screen
        backgroundColor: tokens.colorNeutralBackground1 || '#1e1e1e',
        color: tokens.colorNeutralForeground1 || '#ffffff',
        minHeight: '400px',
      }}
    >
      {showQuickStart && (
        <Card className={styles.quickStartSection}>
          <div className={styles.quickStartHeader}>
            <div>
              <Title3 style={{ marginBottom: tokens.spacingVerticalXXS }}>Quick Start</Title3>
              <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                Don&apos;t have API keys? Get started with one of these options.
              </Text>
            </div>
            <Button
              appearance="subtle"
              icon={<ChevronUp24Regular />}
              onClick={() => setShowQuickStart(false)}
            />
          </div>
          {showQuickStart && (
            <div className={styles.quickStartContent}>
              <div className={styles.quickStartActions}>
                <Button
                  appearance="primary"
                  onClick={() => openExternalLink('https://platform.openai.com/signup')}
                >
                  Get OpenAI Key
                </Button>
                <Button
                  appearance="secondary"
                  onClick={() => openExternalLink('https://ollama.ai/download')}
                >
                  Install Ollama
                </Button>
                <Button appearance="subtle" onClick={onSkipAll}>
                  Use Offline Mode
                </Button>
              </div>
            </div>
          )}
        </Card>
      )}

      {/* LLM Providers - Main Focus */}
      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <Title3 className={styles.sectionTitle}>LLM Providers</Title3>
          <Text className={styles.sectionDescription}>
            Choose at least one provider for script generation
          </Text>
        </div>

        <div className={styles.providersGrid}>
          {/* eslint-disable-next-line sonarjs/cognitive-complexity -- Complex rendering logic for provider cards with multiple states */}
          {llmProviders.map((provider) => {
            const isExpanded = expandedProviders.has(provider.id);
            const status = validationStatus[provider.id] || 'idle';

            return (
              <Card
                key={provider.id}
                className={`${styles.providerCard} ${isExpanded ? styles.providerCardExpanded : ''}`}
              >
                <div
                  className={styles.providerCardHeader}
                  onClick={() => toggleProvider(provider.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      toggleProvider(provider.id);
                    }
                  }}
                  style={{ cursor: 'pointer' }}
                  role="button"
                  tabIndex={0}
                  aria-expanded={isExpanded}
                  aria-label={`${provider.name} provider settings, ${isExpanded ? 'expanded' : 'collapsed'}`}
                >
                  <div className={styles.providerCardTitle}>
                    <span className={styles.providerLogo}>{provider.logo}</span>
                    <div>
                      <Text className={styles.providerName}>{provider.name}</Text>
                    </div>
                  </div>
                  <Badge
                    appearance="filled"
                    color={
                      status === 'valid'
                        ? 'success'
                        : status === 'invalid'
                          ? 'danger'
                          : status === 'validating'
                            ? 'informative'
                            : 'informative'
                    }
                    className={styles.statusBadge}
                  >
                    {status === 'valid'
                      ? 'Valid'
                      : status === 'validating'
                        ? 'Validating...'
                        : status === 'invalid'
                          ? 'Invalid'
                          : 'Not Set'}
                  </Badge>
                </div>

                <Text
                  className={styles.providerDescription}
                  onClick={() => toggleProvider(provider.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      toggleProvider(provider.id);
                    }
                  }}
                  style={{ cursor: 'pointer' }}
                  role="button"
                  tabIndex={0}
                >
                  {provider.description}
                </Text>

                {isExpanded && (
                  <div className={styles.providerContent}>
                    {provider.requiresApiKey === false ? (
                      <>
                        {provider.localSetup?.instructions && (
                          <div className={styles.localProviderStatus}>
                            {checkingTts[provider.id as 'windows' | 'piper' | 'mimic3'] ? (
                              <>
                                <Spinner size="tiny" />
                                <Text size={300}>Checking...</Text>
                              </>
                            ) : provider.id === 'ollama' ? (
                              <>
                                {(() => {
                                  // Use real-time status if available, otherwise fall back to validation status
                                  const ollamaStatus = llmProviders.find(
                                    (p) => p.name === 'Ollama'
                                  );
                                  const isAvailable = ollamaStatus?.available ?? false;
                                  const errorMessage = ollamaStatus?.errorMessage;

                                  if (validationStatus[provider.id] === 'valid' || isAvailable) {
                                    return (
                                      <>
                                        <Checkmark24Regular
                                          style={{ color: tokens.colorPaletteGreenForeground1 }}
                                        />
                                        <Text size={300}>
                                          {accountInfo[provider.id] ||
                                            ollamaStatus?.details ||
                                            'Ollama is running and validated'}
                                        </Text>
                                      </>
                                    );
                                  }

                                  if (validationStatus[provider.id] === 'invalid') {
                                    return (
                                      <>
                                        <Warning24Regular
                                          style={{ color: tokens.colorPaletteRedForeground1 }}
                                        />
                                        <div>
                                          <Text size={300}>
                                            {_validationErrors[provider.id] ||
                                              errorMessage ||
                                              'Ollama validation failed'}
                                          </Text>
                                          {ollamaStatus?.howToFix &&
                                            ollamaStatus.howToFix.length > 0 && (
                                              <div style={{ marginTop: tokens.spacingVerticalXS }}>
                                                <Text
                                                  size={200}
                                                  weight="semibold"
                                                  style={{
                                                    color: tokens.colorNeutralForeground2,
                                                    display: 'block',
                                                    marginBottom: tokens.spacingVerticalXXS,
                                                  }}
                                                >
                                                  How to fix:
                                                </Text>
                                                <ul
                                                  style={{
                                                    margin: 0,
                                                    paddingLeft: tokens.spacingHorizontalM,
                                                    color: tokens.colorNeutralForeground3,
                                                  }}
                                                >
                                                  {ollamaStatus.howToFix.map((step, idx) => (
                                                    <li key={idx}>
                                                      <Text size={200}>{step}</Text>
                                                    </li>
                                                  ))}
                                                </ul>
                                              </div>
                                            )}
                                        </div>
                                      </>
                                    );
                                  }

                                  // Show real-time detection status
                                  if (isLoadingProviderStatus) {
                                    return (
                                      <>
                                        <Spinner size="tiny" />
                                        <Text size={300}>Detecting Ollama...</Text>
                                      </>
                                    );
                                  }

                                  return (
                                    <>
                                      <Warning24Regular
                                        style={{ color: tokens.colorPaletteYellowForeground1 }}
                                      />
                                      <div>
                                        <Text size={300}>
                                          {errorMessage ||
                                            'Install and run Ollama locally. Status updates automatically.'}
                                        </Text>
                                        {ollamaStatus?.howToFix &&
                                          ollamaStatus.howToFix.length > 0 && (
                                            <div style={{ marginTop: tokens.spacingVerticalXS }}>
                                              <Text
                                                size={200}
                                                weight="semibold"
                                                style={{
                                                  color: tokens.colorNeutralForeground2,
                                                  display: 'block',
                                                  marginBottom: tokens.spacingVerticalXXS,
                                                }}
                                              >
                                                How to fix:
                                              </Text>
                                              <ul
                                                style={{
                                                  margin: 0,
                                                  paddingLeft: tokens.spacingHorizontalM,
                                                  color: tokens.colorNeutralForeground3,
                                                }}
                                              >
                                                {ollamaStatus.howToFix.map((step, idx) => (
                                                  <li key={idx}>
                                                    <Text size={200}>{step}</Text>
                                                  </li>
                                                ))}
                                              </ul>
                                            </div>
                                          )}
                                      </div>
                                    </>
                                  );
                                })()}
                              </>
                            ) : null}
                          </div>
                        )}

                        <div className={styles.localActions}>
                          {provider.id === 'ollama' ? (
                            <>
                              <Button
                                appearance="primary"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  handleValidate(provider.id);
                                }}
                                disabled={validationStatus[provider.id] === 'validating'}
                              >
                                {validationStatus[provider.id] === 'validating'
                                  ? 'Validating...'
                                  : validationStatus[provider.id] === 'valid'
                                    ? 'Revalidate'
                                    : 'Validate'}
                              </Button>
                              <Button
                                appearance="secondary"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  onLocalProviderReady?.(provider.id);
                                }}
                              >
                                Mark as Ready
                              </Button>
                            </>
                          ) : (
                            <Button
                              appearance="primary"
                              onClick={(e) => {
                                e.stopPropagation();
                                onLocalProviderReady?.(provider.id);
                              }}
                              disabled={
                                (provider.id === 'piper' || provider.id === 'mimic3') &&
                                !localTtsStatus[provider.id as 'piper' | 'mimic3']?.isAvailable &&
                                !checkingTts[provider.id as 'piper' | 'mimic3']
                              }
                            >
                              Mark as Ready
                            </Button>
                          )}
                          {provider.localSetup?.downloadUrl && (
                            <Button
                              appearance="secondary"
                              onClick={(e) => {
                                e.stopPropagation();
                                openExternalLink(provider.localSetup!.downloadUrl!);
                              }}
                            >
                              Download
                            </Button>
                          )}
                        </div>

                        {provider.localSetup?.instructions && (
                          <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
                            {provider.localSetup.instructions.join(' ')}
                          </Text>
                        )}
                      </>
                    ) : (
                      <div onClick={(e) => e.stopPropagation()} role="presentation">
                        <ProviderHelpPanel
                          providerName={provider.name}
                          signupUrl={provider.signupUrl}
                          steps={provider.steps}
                          usedFor={provider.usedFor}
                          pricingInfo={provider.pricingInfo}
                          keyFormat={provider.keyFormat}
                        />
                        <EnhancedApiKeyInput
                          providerDisplayName={provider.name}
                          value={apiKeys[provider.id] || ''}
                          onChange={(value) => safeOnApiKeyChange(provider.id, value)}
                          onValidate={() => handleValidate(provider.id)}
                          validationStatus={status}
                          fieldErrors={fieldErrors[provider.id]}
                          accountInfo={accountInfo[provider.id]}
                          onSkipValidation={
                            onSkipValidation ? () => onSkipValidation(provider.id) : undefined
                          }
                        />
                      </div>
                    )}
                  </div>
                )}

                <Button
                  appearance="subtle"
                  icon={isExpanded ? <ChevronUp24Regular /> : <ChevronDown24Regular />}
                  onClick={(e) => {
                    e.stopPropagation();
                    toggleProvider(provider.id);
                  }}
                  className={styles.collapseButton}
                >
                  {isExpanded ? 'Less' : 'Configure'}
                </Button>
              </Card>
            );
          })}
        </div>
      </div>

      {/* TTS Providers */}
      {ttsProviders.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <Title3 className={styles.sectionTitle}>Text-to-Speech Providers</Title3>
            <Text className={styles.sectionDescription}>
              Optional TTS providers for voice synthesis (can be configured later)
            </Text>
          </div>

          <div className={styles.providersGrid}>
            {/* eslint-disable-next-line sonarjs/cognitive-complexity -- Complex rendering logic for provider cards with multiple states */}
            {ttsProviders.map((provider) => {
              const isExpanded = expandedProviders.has(provider.id);
              const status = validationStatus[provider.id] || 'idle';

              return (
                <Card
                  key={provider.id}
                  className={`${styles.providerCard} ${isExpanded ? styles.providerCardExpanded : ''}`}
                >
                  <div
                    className={styles.providerCardHeader}
                    onClick={() => toggleProvider(provider.id)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        toggleProvider(provider.id);
                      }
                    }}
                    style={{ cursor: 'pointer' }}
                    role="button"
                    tabIndex={0}
                    aria-expanded={isExpanded}
                    aria-label={`${provider.name} provider settings`}
                  >
                    <div className={styles.providerCardTitle}>
                      <span className={styles.providerLogo}>{provider.logo}</span>
                      <div>
                        <Text className={styles.providerName}>{provider.name}</Text>
                      </div>
                    </div>
                    <Badge
                      appearance="filled"
                      color={
                        status === 'valid'
                          ? 'success'
                          : status === 'invalid'
                            ? 'danger'
                            : 'informative'
                      }
                      className={styles.statusBadge}
                    >
                      {status === 'valid'
                        ? 'Valid'
                        : status === 'validating'
                          ? 'Validating...'
                          : status === 'invalid'
                            ? 'Invalid'
                            : 'Not Set'}
                    </Badge>
                  </div>

                  <Text
                    className={styles.providerDescription}
                    onClick={() => toggleProvider(provider.id)}
                    style={{ cursor: 'pointer' }}
                  >
                    {provider.description}
                  </Text>

                  {isExpanded && (
                    <div className={styles.providerContent}>
                      {provider.requiresApiKey === false ? (
                        <>
                          {(provider.id === 'windows' ||
                            provider.id === 'piper' ||
                            provider.id === 'mimic3' ||
                            provider.id === 'placeholder') && (
                            <div className={styles.localProviderStatus}>
                              {checkingTts[provider.id as 'windows' | 'piper' | 'mimic3'] ? (
                                <>
                                  <Spinner size="tiny" />
                                  <Text size={300}>Checking...</Text>
                                </>
                              ) : provider.id === 'placeholder' ? (
                                <>
                                  <Checkmark24Regular
                                    style={{ color: tokens.colorPaletteGreenForeground1 }}
                                  />
                                  <Text size={300}>Always available</Text>
                                </>
                              ) : localTtsStatus[provider.id as 'windows' | 'piper' | 'mimic3']
                                  ?.isAvailable ? (
                                <>
                                  <Checkmark24Regular
                                    style={{ color: tokens.colorPaletteGreenForeground1 }}
                                  />
                                  <Text size={300}>
                                    {localTtsStatus[provider.id as 'windows' | 'piper' | 'mimic3']
                                      ?.message || 'Available'}
                                  </Text>
                                </>
                              ) : (
                                <>
                                  <Warning24Regular
                                    style={{ color: tokens.colorPaletteYellowForeground1 }}
                                  />
                                  <Text size={300}>
                                    {localTtsStatus[provider.id as 'windows' | 'piper' | 'mimic3']
                                      ?.message || 'Not detected'}
                                  </Text>
                                </>
                              )}
                            </div>
                          )}

                          <div className={styles.localActions}>
                            <Button
                              appearance="primary"
                              onClick={(e) => {
                                e.stopPropagation();
                                onLocalProviderReady?.(provider.id);
                              }}
                              disabled={
                                (provider.id === 'piper' || provider.id === 'mimic3') &&
                                !localTtsStatus[provider.id as 'piper' | 'mimic3']?.isAvailable &&
                                !checkingTts[provider.id as 'piper' | 'mimic3']
                              }
                            >
                              Mark as Ready
                            </Button>
                            {provider.localSetup?.downloadUrl && (
                              <Button
                                appearance="secondary"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openExternalLink(provider.localSetup!.downloadUrl!);
                                }}
                              >
                                Download
                              </Button>
                            )}
                          </div>
                        </>
                      ) : (
                        <>
                          <ProviderHelpPanel
                            providerName={provider.name}
                            signupUrl={provider.signupUrl}
                            steps={provider.steps}
                            usedFor={provider.usedFor}
                            pricingInfo={provider.pricingInfo}
                            keyFormat={provider.keyFormat}
                          />
                          <div onClick={(e) => e.stopPropagation()} role="presentation">
                            <EnhancedApiKeyInput
                              providerDisplayName={provider.name}
                              value={apiKeys[provider.id] || ''}
                              onChange={(value) => onApiKeyChange(provider.id, value)}
                              onValidate={() => handleValidate(provider.id)}
                              validationStatus={status}
                              fieldErrors={fieldErrors[provider.id]}
                              accountInfo={accountInfo[provider.id]}
                              onSkipValidation={
                                onSkipValidation ? () => onSkipValidation(provider.id) : undefined
                              }
                            />
                          </div>
                        </>
                      )}
                    </div>
                  )}

                  <Button
                    appearance="subtle"
                    icon={isExpanded ? <ChevronUp24Regular /> : <ChevronDown24Regular />}
                    onClick={(e) => {
                      e.stopPropagation();
                      toggleProvider(provider.id);
                    }}
                    className={styles.collapseButton}
                  >
                    {isExpanded ? 'Less' : 'Configure'}
                  </Button>
                </Card>
              );
            })}
          </div>
        </div>
      )}

      {/* Image/Visual Providers */}
      {imageProviders.length > 0 && (
        <div className={styles.section}>
          <div className={styles.sectionHeader}>
            <Title3 className={styles.sectionTitle}>Image & Visual Providers</Title3>
            <Text className={styles.sectionDescription}>
              Optional image providers for visuals and backgrounds (can be configured later)
            </Text>
          </div>

          <div className={styles.providersGrid}>
            {imageProviders.map((provider) => {
              const isExpanded = expandedProviders.has(provider.id);
              const status = validationStatus[provider.id] || 'idle';

              return (
                <Card
                  key={provider.id}
                  className={`${styles.providerCard} ${isExpanded ? styles.providerCardExpanded : ''}`}
                >
                  <div
                    className={styles.providerCardHeader}
                    onClick={() => toggleProvider(provider.id)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        toggleProvider(provider.id);
                      }
                    }}
                    style={{ cursor: 'pointer' }}
                    role="button"
                    tabIndex={0}
                    aria-label={`${provider.name} provider settings`}
                  >
                    <div className={styles.providerCardTitle}>
                      <span className={styles.providerLogo}>{provider.logo}</span>
                      <div>
                        <Text className={styles.providerName}>{provider.name}</Text>
                      </div>
                    </div>
                    <Badge
                      appearance="filled"
                      color={
                        status === 'valid'
                          ? 'success'
                          : status === 'invalid'
                            ? 'danger'
                            : 'informative'
                      }
                      className={styles.statusBadge}
                    >
                      {status === 'valid'
                        ? 'Valid'
                        : status === 'validating'
                          ? 'Validating...'
                          : status === 'invalid'
                            ? 'Invalid'
                            : 'Not Set'}
                    </Badge>
                  </div>

                  <Text
                    className={styles.providerDescription}
                    onClick={() => toggleProvider(provider.id)}
                    style={{ cursor: 'pointer' }}
                  >
                    {provider.description}
                  </Text>

                  {isExpanded && (
                    <div className={styles.providerContent}>
                      {provider.requiresApiKey === false ? (
                        <>
                          {provider.id === 'placeholder' && (
                            <div className={styles.localProviderStatus}>
                              <Checkmark24Regular
                                style={{ color: tokens.colorPaletteGreenForeground1 }}
                              />
                              <Text size={300}>Always available</Text>
                            </div>
                          )}

                          <div className={styles.localActions}>
                            <Button
                              appearance="primary"
                              onClick={(e) => {
                                e.stopPropagation();
                                onLocalProviderReady?.(provider.id);
                              }}
                            >
                              Mark as Ready
                            </Button>
                            {provider.localSetup?.downloadUrl && (
                              <Button
                                appearance="secondary"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  openExternalLink(provider.localSetup!.downloadUrl!);
                                }}
                              >
                                Download
                              </Button>
                            )}
                          </div>
                        </>
                      ) : (
                        <>
                          <ProviderHelpPanel
                            providerName={provider.name}
                            signupUrl={provider.signupUrl}
                            steps={provider.steps}
                            usedFor={provider.usedFor}
                            pricingInfo={provider.pricingInfo}
                            keyFormat={provider.keyFormat}
                          />
                          <div onClick={(e) => e.stopPropagation()} role="presentation">
                            <EnhancedApiKeyInput
                              providerDisplayName={provider.name}
                              value={apiKeys[provider.id] || ''}
                              onChange={(value) => onApiKeyChange(provider.id, value)}
                              onValidate={() => handleValidate(provider.id)}
                              validationStatus={status}
                              fieldErrors={fieldErrors[provider.id]}
                              accountInfo={accountInfo[provider.id]}
                              onSkipValidation={
                                onSkipValidation ? () => onSkipValidation(provider.id) : undefined
                              }
                            />
                          </div>
                        </>
                      )}
                    </div>
                  )}

                  <Button
                    appearance="subtle"
                    icon={isExpanded ? <ChevronUp24Regular /> : <ChevronDown24Regular />}
                    onClick={(e) => {
                      e.stopPropagation();
                      toggleProvider(provider.id);
                    }}
                    className={styles.collapseButton}
                  >
                    {isExpanded ? 'Less' : 'Configure'}
                  </Button>
                </Card>
              );
            })}
          </div>
        </div>
      )}

      {hasInvalidKeys && (
        <MessageBar intent="warning" className={styles.warningMessage}>
          <MessageBarBody>
            <MessageBarTitle>Some API keys are invalid</MessageBarTitle>
            <Text style={{ display: 'block', marginBottom: tokens.spacingVerticalS }}>
              The following providers have invalid API keys:{' '}
              {invalidKeys.map(([p]) => p).join(', ')}. You can continue setup and fix them later in
              Settings.
            </Text>
            {onAllowInvalidKeysChange && (
              <Checkbox
                checked={allowInvalidKeys}
                onChange={(_, data) => onAllowInvalidKeysChange(data.checked as boolean)}
                label="Allow me to continue with invalid API keys (I'll fix them later)"
              />
            )}
          </MessageBarBody>
        </MessageBar>
      )}

      {!hasAtLeastOneProvider && (
        <MessageBar intent="error" className={styles.warningMessage}>
          <MessageBarBody>
            <MessageBarTitle>Provider Required</MessageBarTitle>
            <Text style={{ display: 'block', marginBottom: tokens.spacingVerticalS }}>
              Configure at least one LLM provider or use offline mode to continue.
            </Text>
          </MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.footer}>
        <Button appearance="subtle" onClick={onSkipAll}>
          Use Offline Mode
        </Button>
        <Text className={styles.footerText}>
          {hasAtLeastOneValidKey
            ? '✓ Ready to continue'
            : 'Configure at least one provider or use offline mode'}
        </Text>
      </div>
    </div>
  );
}
