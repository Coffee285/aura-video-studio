/**
 * Script Generation API Service
 * Provides typed methods for script generation, editing, and provider selection
 */

import type { ExtendedAxiosRequestConfig } from './apiClient';
import { get, post, put } from './apiClient';

/**
 * Script generation request
 */
export interface GenerateScriptRequest {
  topic: string;
  audience?: string;
  goal?: string;
  tone?: string;
  language?: string;
  aspect?: string;
  targetDurationSeconds?: number;
  pacing?: string;
  density?: string;
  style?: string;
  preferredProvider?: string;
  modelOverride?: string;
  // Advanced LLM parameters
  temperature?: number;
  topP?: number;
  topK?: number;
  maxTokens?: number;
  frequencyPenalty?: number;
  presencePenalty?: number;
  stopSequences?: string[];

  // RAG (Retrieval-Augmented Generation) configuration
  ragConfiguration?: {
    enabled: boolean;
    topK?: number;
    minimumScore?: number;
    maxContextTokens?: number;
    includeCitations?: boolean;
    tightenClaims?: boolean;
  };
}

/**
 * Script scene DTO
 */
export interface ScriptSceneDto {
  number: number;
  narration: string;
  visualPrompt: string;
  durationSeconds: number;
  transition: string;
}

/**
 * Script metadata DTO
 */
export interface ScriptMetadataDto {
  generatedAt: string;
  providerName: string;
  modelUsed: string;
  tokensUsed: number;
  estimatedCost: number;
  tier: string;
  generationTimeSeconds: number;
}

/**
 * Script generation response
 */
export interface GenerateScriptResponse {
  scriptId: string;
  title: string;
  scenes: ScriptSceneDto[];
  totalDurationSeconds: number;
  metadata: ScriptMetadataDto;
  correlationId: string;
}

/**
 * Update scene request
 */
export interface UpdateSceneRequest {
  narration?: string;
  visualPrompt?: string;
  durationSeconds?: number;
}

/**
 * Regenerate script request
 */
export interface RegenerateScriptRequest {
  preferredProvider?: string;
  modelOverride?: string;
}

/**
 * Provider info DTO
 */
export interface ProviderInfoDto {
  name: string;
  tier: string;
  isAvailable: boolean;
  requiresInternet: boolean;
  requiresApiKey: boolean;
  capabilities: string[];
  defaultModel: string;
  estimatedCostPer1KTokens: number;
  availableModels: string[];
}

/**
 * Providers list response
 */
export interface ProvidersListResponse {
  providers: ProviderInfoDto[];
  correlationId: string;
}

/**
 * Generate a new script
 * Uses extended timeout (6.5 minutes) to accommodate slow local Ollama models
 * Must be longer than backend timeout (6 minutes) to allow for network overhead
 */
export async function generateScript(
  request: GenerateScriptRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  // Use extended timeout for script generation, especially for Ollama/local models
  // Backend timeout is 6 minutes (360000ms), so frontend needs to be slightly longer
  // to allow for network overhead and response processing
  const extendedConfig = {
    ...config,
    timeout: (config as unknown as { timeout?: number })?.timeout ?? 390000, // 6.5 minutes - exceeds backend 6-minute timeout to allow for network overhead
  } as ExtendedAxiosRequestConfig;

  // Safe logging - wrapped to prevent any logging errors from breaking the application
  try {
    console.info('[scriptApi] Calling generateScript', {
      topic: request.topic,
      provider: request.preferredProvider,
      model: request.modelOverride,
      timeout: extendedConfig.timeout,
      timestamp: new Date().toISOString(),
    });
  } catch {
    // Ignore logging errors - they should never happen but we don't want them to break the app
  }

  try {
    const response = await post<GenerateScriptResponse>(
      '/api/scripts/generate',
      request,
      extendedConfig
    );

    // Safe logging
    try {
      console.info('[scriptApi] generateScript response received', {
        hasResponse: !!response,
        scriptId: response?.scriptId,
        sceneCount: response?.scenes?.length ?? 0,
        title: response?.title,
        timestamp: new Date().toISOString(),
      });
    } catch {
      // Ignore logging errors
    }

    // Validate response before returning
    if (!response) {
      try {
        console.error('[scriptApi] generateScript returned null/undefined response');
      } catch {
        // Ignore logging errors
      }
      throw new Error('Server returned an empty response. Please try again.');
    }

    if (!response.scenes || !Array.isArray(response.scenes) || response.scenes.length === 0) {
      try {
        console.error('[scriptApi] generateScript returned invalid response', {
          hasScenes: !!response.scenes,
          isArray: Array.isArray(response.scenes),
          sceneCount: response.scenes?.length ?? 0,
          response,
        });
      } catch {
        // Ignore logging errors
      }
      throw new Error('Server returned a response with no scenes. Please try again.');
    }

    return response;
  } catch (error) {
    // Safe error logging
    try {
      console.error('[scriptApi] generateScript error', {
        error,
        errorType:
          error && typeof error === 'object' && 'constructor' in error
            ? (error as { constructor?: { name?: string } }).constructor?.name
            : undefined,
        errorMessage: error instanceof Error ? error.message : String(error),
        isAxiosError: error && typeof error === 'object' && 'isAxiosError' in error,
        timestamp: new Date().toISOString(),
      });
    } catch {
      // Ignore logging errors - still throw the original error
    }
    throw error;
  }
}

/**
 * Get a previously generated script by ID
 */
export async function getScript(
  scriptId: string,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return get<GenerateScriptResponse>(`/api/scripts/${scriptId}`, config);
}

/**
 * Update a specific scene in a script
 */
export async function updateScene(
  scriptId: string,
  sceneNumber: number,
  request: UpdateSceneRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return put<GenerateScriptResponse>(
    `/api/scripts/${scriptId}/scenes/${sceneNumber}`,
    request,
    config
  );
}

/**
 * Regenerate a script with same or different provider
 */
export async function regenerateScript(
  scriptId: string,
  request: RegenerateScriptRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/regenerate`, request, config);
}

/**
 * Regenerate scene request
 */
export interface RegenerateSceneRequest {
  improvementGoal?: string;
  includeContext?: boolean;
}

/**
 * Regenerate a specific scene in a script
 */
export async function regenerateScene(
  scriptId: string,
  sceneNumber: number,
  request?: RegenerateSceneRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(
    `/api/scripts/${scriptId}/scenes/${sceneNumber}/regenerate`,
    request || {},
    config
  );
}

/**
 * List available LLM providers and their status
 */
export async function listProviders(
  config?: ExtendedAxiosRequestConfig
): Promise<ProvidersListResponse> {
  return get<ProvidersListResponse>('/api/scripts/providers', config);
}

/**
 * Export script as text or markdown
 */
export async function exportScript(
  scriptId: string,
  format: 'text' | 'markdown' = 'text',
  config?: ExtendedAxiosRequestConfig
): Promise<Blob> {
  return await get<Blob>(`/api/scripts/${scriptId}/export?format=${format}`, {
    ...config,
    responseType: 'blob',
  });
}

/**
 * Enhance a script with tone/pacing adjustments
 */
export interface ScriptEnhancementRequest {
  goal: string;
  toneAdjustment?: number;
  pacingAdjustment?: number;
  stylePreset?: string;
}

export async function enhanceScript(
  scriptId: string,
  request: ScriptEnhancementRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/enhance`, request, config);
}

/**
 * Reorder scenes in a script
 */
export interface ReorderScenesRequest {
  sceneOrder: number[];
}

export async function reorderScenes(
  scriptId: string,
  request: ReorderScenesRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/reorder`, request, config);
}

/**
 * Merge multiple scenes into one
 */
export interface MergeScenesRequest {
  sceneNumbers: number[];
  separator?: string;
}

export async function mergeScenes(
  scriptId: string,
  request: MergeScenesRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/merge`, request, config);
}

/**
 * Split a scene into two
 */
export interface SplitSceneRequest {
  splitPosition: number;
}

export async function splitScene(
  scriptId: string,
  sceneNumber: number,
  request: SplitSceneRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(
    `/api/scripts/${scriptId}/scenes/${sceneNumber}/split`,
    request,
    config
  );
}

/**
 * Delete a scene from a script
 */
export async function deleteScene(
  scriptId: string,
  sceneNumber: number,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return await get<GenerateScriptResponse>(`/api/scripts/${scriptId}/scenes/${sceneNumber}`, {
    ...config,
    method: 'DELETE',
  });
}

/**
 * Regenerate all scenes in a script
 */
export async function regenerateAllScenes(
  scriptId: string,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/regenerate-all`, {}, config);
}

/**
 * Script version DTO
 */
export interface ScriptVersionDto {
  versionId: string;
  versionNumber: number;
  createdAt: string;
  notes?: string;
  script: GenerateScriptResponse;
}

/**
 * Version history response
 */
export interface ScriptVersionHistoryResponse {
  versions: ScriptVersionDto[];
  currentVersionId: string;
  correlationId: string;
}

/**
 * Get version history for a script
 */
export async function getVersionHistory(
  scriptId: string,
  config?: ExtendedAxiosRequestConfig
): Promise<ScriptVersionHistoryResponse> {
  return get<ScriptVersionHistoryResponse>(`/api/scripts/${scriptId}/versions`, config);
}

/**
 * Revert to a previous version
 */
export interface RevertToVersionRequest {
  versionId: string;
}

export async function revertToVersion(
  scriptId: string,
  request: RevertToVersionRequest,
  config?: ExtendedAxiosRequestConfig
): Promise<GenerateScriptResponse> {
  return post<GenerateScriptResponse>(`/api/scripts/${scriptId}/versions/revert`, request, config);
}
