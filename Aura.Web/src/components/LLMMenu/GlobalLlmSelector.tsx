/**
 * Global LLM Selector Component
 * Compact LLM model selector for the top bar that's available on every page
 *
 * Features:
 * - Fetches providers and models dynamically from API
 * - Properly syncs with persisted Zustand store state
 * - Validates persisted model is still available
 * - Shows loading states and error recovery options
 * - Handles all providers (OpenAI, Anthropic, Gemini, Azure, Ollama)
 */

import {
  Button,
  Dropdown,
  Option,
  makeStyles,
  tokens,
  Tooltip,
  Spinner,
  shorthands,
  Badge,
  Text,
} from '@fluentui/react-components';
import {
  BrainCircuit20Regular,
  ArrowSync20Regular,
  ErrorCircle20Regular,
} from '@fluentui/react-icons';
import React, { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import { useGlobalLlmStore } from '../../state/globalLlmStore';
import type { LlmModelInfo } from '../ModelSelection/LlmModelSelector';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  selectorGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  dropdown: {
    minWidth: '140px',
    maxWidth: '200px',
  },
  compactDropdown: {
    minWidth: '120px',
    maxWidth: '160px',
  },
  refreshButton: {
    minWidth: 'auto',
    ...shorthands.padding(tokens.spacingHorizontalXS),
  },
  loadingSpinner: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  errorIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    cursor: 'pointer',
  },
  modelBadge: {
    maxWidth: '150px',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  providerStatus: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    marginLeft: tokens.spacingHorizontalXXS,
  },
});

interface ProviderInfo {
  id: string;
  name: string;
  requiresApiKey: boolean;
  modelCount?: number;
  isAvailable?: boolean;
}

export function GlobalLlmSelector() {
  const styles = useStyles();
  const { selection, setSelection } = useGlobalLlmStore();
  const [availableModels, setAvailableModels] = useState<Record<string, LlmModelInfo[]>>({});
  const [providers, setProviders] = useState<ProviderInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isInitialized, setIsInitialized] = useState(false);
  const initRef = useRef(false);

  const selectedProvider = selection?.provider || '';
  const selectedModel = selection?.modelId || '';

  // Fetch available models from the API
  const fetchModels = useCallback(async (showLoadingState = true) => {
    if (showLoadingState) {
      setIsLoading(true);
    }
    setError(null);

    try {
      const response = await fetch('/api/models/available');
      if (!response.ok) {
        throw new Error(`Failed to load models: ${response.statusText}`);
      }

      const data = await response.json();
      const providersData = data.providers || {};

      setAvailableModels(providersData);

      // Build provider list dynamically from API response
      const providerList: ProviderInfo[] = Object.keys(providersData).map((providerId) => {
        const models = providersData[providerId] || [];
        return {
          id: providerId,
          name: providerId,
          requiresApiKey: providerId !== 'Ollama',
          modelCount: models.length,
          isAvailable: models.length > 0,
        };
      });

      // Add common providers that might not have models yet
      const commonProviders = ['OpenAI', 'Anthropic', 'Gemini', 'Ollama', 'Azure'];
      for (const provider of commonProviders) {
        if (!providerList.some((p) => p.id === provider)) {
          providerList.push({
            id: provider,
            name: provider,
            requiresApiKey: provider !== 'Ollama',
            modelCount: 0,
            isAvailable: false,
          });
        }
      }

      // Sort providers: available first, then alphabetically
      providerList.sort((a, b) => {
        if (a.isAvailable && !b.isAvailable) return -1;
        if (!a.isAvailable && b.isAvailable) return 1;
        return a.name.localeCompare(b.name);
      });

      setProviders(providerList);

      return providersData;
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to load models';
      setError(errorMessage);
      console.error('[GlobalLlmSelector] Error fetching models:', err);
      return null;
    } finally {
      if (showLoadingState) {
        setIsLoading(false);
      }
    }
  }, []);

  // Validate and sync persisted selection with available models
  const validateAndSyncSelection = useCallback(
    async (modelsData: Record<string, LlmModelInfo[]>) => {
      // If no selection exists, try to set a default from available models
      if (!selection?.provider || !selection?.modelId) {
        // Find the first available provider with models
        // Priority: Ollama (local/detected in setup) > other providers
        const providerPriority = ['Ollama', 'OpenAI', 'Anthropic', 'Gemini', 'Azure'];

        for (const provider of providerPriority) {
          const models = modelsData[provider];
          if (models && models.length > 0) {
            console.info(
              `[GlobalLlmSelector] No selection found, defaulting to ${provider}:${models[0].modelId}`
            );
            setSelection({
              provider,
              modelId: models[0].modelId,
            });
            return;
          }
        }

        // If none of the priority providers have models, use any available provider
        for (const [provider, models] of Object.entries(modelsData)) {
          if (models && models.length > 0) {
            console.info(
              `[GlobalLlmSelector] No selection found, defaulting to ${provider}:${models[0].modelId}`
            );
            setSelection({
              provider,
              modelId: models[0].modelId,
            });
            return;
          }
        }

        console.warn('[GlobalLlmSelector] No available models to set as default');
        return;
      }

      const providerModels = modelsData[selection.provider] || [];

      // Check if the selected model is still available
      const modelExists = providerModels.some((m) => m.modelId === selection.modelId);

      if (!modelExists && providerModels.length > 0) {
        // Model no longer available, but provider has other models - select first available
        console.info(
          `[GlobalLlmSelector] Model "${selection.modelId}" no longer available for ${selection.provider}, selecting first available model`
        );
        setSelection({
          provider: selection.provider,
          modelId: providerModels[0].modelId,
        });
      } else if (!modelExists && providerModels.length === 0) {
        // Provider has no models available
        console.warn(
          `[GlobalLlmSelector] Provider "${selection.provider}" has no available models`
        );
        // Keep the selection but show a warning (don't clear it automatically)
      }
    },
    [selection, setSelection]
  );

  // Initial load and sync with persisted selection
  useEffect(() => {
    if (initRef.current) return;
    initRef.current = true;

    const initializeSelector = async () => {
      setIsLoading(true);
      try {
        const modelsData = await fetchModels(false);
        if (modelsData) {
          await validateAndSyncSelection(modelsData);
        }
      } finally {
        setIsLoading(false);
        setIsInitialized(true);
      }
    };

    initializeSelector();
  }, [fetchModels, validateAndSyncSelection]);

  // Refresh models for a specific provider (force re-fetch from API)
  const refreshModels = useCallback(async () => {
    setIsRefreshing(true);
    setError(null);

    try {
      // First refresh the catalog from the provider
      await fetch('/api/models/llm/refresh', { method: 'POST' });

      // Then fetch the updated models
      const modelsData = await fetchModels(false);
      if (modelsData) {
        await validateAndSyncSelection(modelsData);
      }
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to refresh models';
      setError(errorMessage);
      console.error('[GlobalLlmSelector] Error refreshing models:', err);
    } finally {
      setIsRefreshing(false);
    }
  }, [fetchModels, validateAndSyncSelection]);

  // Get models for the selected provider
  const providerModels = useMemo(() => {
    if (!selectedProvider) return [];
    return availableModels[selectedProvider] || [];
  }, [availableModels, selectedProvider]);

  // Check if selected model exists in current provider's models
  const selectedModelExists = useMemo(() => {
    if (!selectedModel || !selectedProvider) return false;
    return providerModels.some((m) => m.modelId === selectedModel);
  }, [selectedModel, selectedProvider, providerModels]);

  // Handle provider change
  const handleProviderChange = useCallback(
    (providerId: string) => {
      const models = availableModels[providerId] || [];
      const firstModel = models[0]?.modelId || '';
      setSelection({ provider: providerId, modelId: firstModel });
    },
    [availableModels, setSelection]
  );

  // Handle model change
  const handleModelChange = useCallback(
    (modelId: string) => {
      if (selectedProvider) {
        setSelection({ provider: selectedProvider, modelId });
      }
    },
    [selectedProvider, setSelection]
  );

  // Get display text for provider with model count
  const getProviderDisplayText = useCallback(
    (providerId: string): string => {
      const provider = providers.find((p) => p.id === providerId);
      return provider?.name || providerId;
    },
    [providers]
  );

  // Get display text for model (truncate if too long)
  const getModelDisplayText = useCallback((modelId: string): string => {
    if (!modelId) return 'Select model';
    if (modelId.length > 20) {
      return modelId.substring(0, 17) + '...';
    }
    return modelId;
  }, []);

  // Get provider option text with status
  const getProviderOptionText = useCallback((provider: ProviderInfo): string => {
    let text = provider.name;
    if (!provider.requiresApiKey) {
      text += ' (Local)';
    }
    if (provider.modelCount !== undefined && provider.modelCount > 0) {
      text += ` [${provider.modelCount}]`;
    }
    return text;
  }, []);

  // Show loading state during initialization
  if (!isInitialized && isLoading) {
    return (
      <div className={styles.container}>
        <Tooltip content="Loading AI models..." relationship="label">
          <div className={styles.loadingSpinner}>
            <Spinner size="tiny" />
            <Text size={200}>Loading...</Text>
          </div>
        </Tooltip>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <Tooltip content="AI Model Selection" relationship="label">
        <BrainCircuit20Regular style={{ color: tokens.colorNeutralForeground3 }} />
      </Tooltip>

      <div className={styles.selectorGroup}>
        {/* Provider Dropdown */}
        <Dropdown
          className={styles.compactDropdown}
          placeholder="Provider"
          value={selectedProvider ? getProviderDisplayText(selectedProvider) : ''}
          selectedOptions={selectedProvider ? [selectedProvider] : []}
          onOptionSelect={(_, data) => {
            if (data.optionValue) {
              handleProviderChange(data.optionValue as string);
            }
          }}
          disabled={isLoading || isRefreshing}
        >
          {providers.map((provider) => (
            <Option
              key={provider.id}
              value={provider.id}
              text={provider.name}
              disabled={!provider.isAvailable && provider.modelCount === 0}
            >
              <span>{getProviderOptionText(provider)}</span>
              {!provider.isAvailable && provider.modelCount === 0 && (
                <span className={styles.providerStatus}> (unavailable)</span>
              )}
            </Option>
          ))}
        </Dropdown>

        {/* Model Dropdown */}
        <Dropdown
          className={styles.compactDropdown}
          placeholder={selectedProvider ? 'Select model' : 'Select provider first'}
          value={selectedModel ? getModelDisplayText(selectedModel) : ''}
          selectedOptions={selectedModel && selectedModelExists ? [selectedModel] : []}
          onOptionSelect={(_, data) => {
            if (data.optionValue) {
              handleModelChange(data.optionValue as string);
            }
          }}
          disabled={isLoading || isRefreshing || !selectedProvider || providerModels.length === 0}
        >
          {providerModels.length === 0 && selectedProvider && (
            <Option key="no-models" value="" disabled text="No models available">
              No models available
            </Option>
          )}
          {providerModels.map((model) => (
            <Option
              key={model.modelId}
              value={model.modelId}
              text={model.modelId + (model.isDeprecated ? ' (Deprecated)' : '')}
            >
              {model.modelId}
              {model.isDeprecated && ' ⚠️'}
            </Option>
          ))}
        </Dropdown>

        {/* Current model badge - shows selected model clearly */}
        {selectedModel && selectedModelExists && (
          <Tooltip content={`Current model: ${selectedModel}`} relationship="label">
            <Badge
              appearance="outline"
              color="informative"
              size="small"
              className={styles.modelBadge}
            >
              {getModelDisplayText(selectedModel)}
            </Badge>
          </Tooltip>
        )}

        {/* Warning badge if model doesn't exist */}
        {selectedModel && !selectedModelExists && selectedProvider && isInitialized && (
          <Tooltip
            content={`Model "${selectedModel}" not found. Click refresh to update available models.`}
            relationship="label"
          >
            <Badge appearance="filled" color="warning" size="small">
              Model not found
            </Badge>
          </Tooltip>
        )}

        {/* Refresh button */}
        <Tooltip content="Refresh available models from provider" relationship="label">
          <Button
            appearance="subtle"
            icon={isRefreshing ? <Spinner size="tiny" /> : <ArrowSync20Regular />}
            onClick={refreshModels}
            disabled={isRefreshing || isLoading}
            size="small"
            className={styles.refreshButton}
          />
        </Tooltip>
      </div>

      {/* Error indicator with retry */}
      {error && (
        <Tooltip content={`${error}. Click to retry.`} relationship="description">
          <div
            className={styles.errorIndicator}
            onClick={refreshModels}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                refreshModels();
              }
            }}
            role="button"
            tabIndex={0}
          >
            <ErrorCircle20Regular style={{ color: tokens.colorPaletteRedForeground1 }} />
          </div>
        </Tooltip>
      )}
    </div>
  );
}
