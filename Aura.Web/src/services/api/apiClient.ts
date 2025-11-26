/**
 * Comprehensive API client with retry logic, circuit breaker, and request queueing
 * Provides centralized configuration, interceptors, and error handling
 */

import axios, {
  AxiosInstance,
  AxiosError,
  AxiosRequestConfig,
  InternalAxiosRequestConfig,
  AxiosResponse,
} from 'axios';
import { env } from '../../config/env';
import { timeoutConfig } from '../../config/timeouts';
import { useConnectionStore } from '../../stores/connectionStore';
import { createDedupeKey } from '../../utils/dedupeKey';
import { requestDeduplicator } from '../../utils/requestDeduplicator';
import { errorReportingService } from '../errorReportingService';
import { loggingService } from '../loggingService';
import { networkResilienceService } from '../networkResilience';
import {
  getHttpErrorMessage,
  getAppErrorMessage,
  isTransientError,
  shouldTriggerCircuitBreaker,
} from './apiErrorMessages';
import { PersistentCircuitBreaker } from './circuitBreakerPersistence';

/**
 * Circuit breaker states
 */
enum CircuitState {
  CLOSED = 'CLOSED', // Normal operation
  OPEN = 'OPEN', // Blocking requests
  HALF_OPEN = 'HALF_OPEN', // Testing if service recovered
}

/**
 * Circuit breaker configuration
 */
interface CircuitBreakerConfig {
  failureThreshold: number; // Number of failures before opening
  successThreshold: number; // Number of successes needed to close from half-open
  timeout: number; // Time in ms before attempting recovery
}

/**
 * Circuit breaker for preventing cascading failures
 */
class CircuitBreaker {
  private state: CircuitState = CircuitState.CLOSED;
  private failureCount = 0;
  private successCount = 0;
  private nextAttempt = Date.now();
  private config: CircuitBreakerConfig;
  private endpoint: string;

  constructor(endpoint: string, config: Partial<CircuitBreakerConfig> = {}) {
    this.endpoint = endpoint;
    this.config = {
      failureThreshold: config.failureThreshold || 10, // Increased from 5 to 10 for less sensitivity
      successThreshold: config.successThreshold || 2,
      timeout: config.timeout || 60000, // 1 minute
    };

    // Try to load persisted state
    const persistedState = PersistentCircuitBreaker.loadState(endpoint);
    if (persistedState) {
      this.state = persistedState.state as CircuitState;
      this.failureCount = persistedState.failureCount;
      this.successCount = persistedState.successCount;
      this.nextAttempt = persistedState.nextAttempt;
      loggingService.info(
        'Circuit breaker state restored from localStorage',
        'apiClient',
        'circuitBreaker',
        { endpoint, state: this.state, failureCount: this.failureCount }
      );
    }
  }

  /**
   * Check if request should be allowed
   */
  public canAttempt(): boolean {
    if (this.state === CircuitState.CLOSED) {
      return true;
    }

    if (this.state === CircuitState.OPEN) {
      // Check if timeout has passed
      if (Date.now() >= this.nextAttempt) {
        this.state = CircuitState.HALF_OPEN;
        this.successCount = 0;
        loggingService.info(
          'Circuit breaker entering half-open state',
          'apiClient',
          'circuitBreaker'
        );
        return true;
      }
      return false;
    }

    // HALF_OPEN state - allow attempts
    return true;
  }

  /**
   * Record successful request
   */
  public recordSuccess(): void {
    this.failureCount = 0;

    if (this.state === CircuitState.HALF_OPEN) {
      this.successCount++;
      if (this.successCount >= this.config.successThreshold) {
        this.state = CircuitState.CLOSED;
        loggingService.info(
          'Circuit breaker closed - service recovered',
          'apiClient',
          'circuitBreaker'
        );
        // Clear persisted state when circuit is closed
        PersistentCircuitBreaker.clearState(this.endpoint);
      } else {
        // Save state
        this.saveState();
      }
    } else if (this.state === CircuitState.CLOSED) {
      // Clear persisted state on successful request when already closed
      PersistentCircuitBreaker.clearState(this.endpoint);
    }
  }

  /**
   * Record failed request
   */
  public recordFailure(): void {
    this.failureCount++;

    if (this.state === CircuitState.HALF_OPEN) {
      this.state = CircuitState.OPEN;
      this.nextAttempt = Date.now() + this.config.timeout;
      loggingService.warn(
        'Circuit breaker opened - service still failing',
        'apiClient',
        'circuitBreaker',
        { nextAttempt: new Date(this.nextAttempt).toISOString() }
      );
      this.saveState();
    } else if (this.failureCount >= this.config.failureThreshold) {
      this.state = CircuitState.OPEN;
      this.nextAttempt = Date.now() + this.config.timeout;
      loggingService.error(
        'Circuit breaker opened - too many failures',
        undefined,
        'apiClient',
        'circuitBreaker',
        {
          failureCount: this.failureCount,
          threshold: this.config.failureThreshold,
          nextAttempt: new Date(this.nextAttempt).toISOString(),
        }
      );
      this.saveState();
    }
  }

  /**
   * Get current state
   */
  public getState(): CircuitState {
    return this.state;
  }

  /**
   * Reset circuit breaker
   */
  public reset(): void {
    this.state = CircuitState.CLOSED;
    this.failureCount = 0;
    this.successCount = 0;
    this.nextAttempt = Date.now();
    // Clear persisted state when manually reset
    PersistentCircuitBreaker.clearState(this.endpoint);
  }

  /**
   * Save current state to localStorage
   */
  private saveState(): void {
    PersistentCircuitBreaker.saveState(this.endpoint, {
      state: this.state,
      failureCount: this.failureCount,
      successCount: this.successCount,
      nextAttempt: this.nextAttempt,
      timestamp: Date.now(),
    });
  }
}

/**
 * Request queue for rate-limited endpoints
 */
class RequestQueue {
  private queue: Array<() => Promise<void>> = [];
  private processing = false;
  private lastRequestTime = 0;
  private minInterval: number;

  constructor(minInterval = 1000) {
    this.minInterval = minInterval;
  }

  /**
   * Add request to queue
   */
  public async enqueue<T>(requestFn: () => Promise<T>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      this.queue.push(async () => {
        try {
          const result = await requestFn();
          resolve(result);
        } catch (error) {
          reject(error);
        }
      });

      this.processQueue();
    });
  }

  /**
   * Process queued requests
   */
  private async processQueue(): Promise<void> {
    if (this.processing || this.queue.length === 0) {
      return;
    }

    this.processing = true;

    while (this.queue.length > 0) {
      const now = Date.now();
      const timeSinceLastRequest = now - this.lastRequestTime;

      if (timeSinceLastRequest < this.minInterval) {
        await new Promise((resolve) =>
          setTimeout(resolve, this.minInterval - timeSinceLastRequest)
        );
      }

      const request = this.queue.shift();
      if (request) {
        this.lastRequestTime = Date.now();
        await request();
      }
    }

    this.processing = false;
  }
}

/**
 * Extended Axios config with custom options
 */
export interface ExtendedAxiosRequestConfig extends AxiosRequestConfig {
  _retry?: number;
  _skipRetry?: boolean;
  _skipCircuitBreaker?: boolean;
  _timeout?: number;
  _queueKey?: string;
  _requestStartTime?: number;
  _skipDeduplication?: boolean;
}

/**
 * Extended Error type with custom properties
 */
interface ExtendedError extends Error {
  isCircuitBreakerError?: boolean;
  userMessage?: string;
  errorCode?: string;
}

/**
 * Extended Internal Axios Request Config with start time
 */
interface ExtendedInternalAxiosRequestConfig extends InternalAxiosRequestConfig {
  _requestStartTime?: number;
  _skipCircuitBreaker?: boolean;
}

/**
 * API response data type for error responses
 */
interface ApiErrorResponse {
  errorCode?: string;
  code?: string;
  detail?: string;
  title?: string;
  [key: string]: unknown;
}

// Initialize circuit breaker and request queues
const circuitBreaker = new CircuitBreaker('global');
const requestQueues = new Map<string, RequestQueue>();

/**
 * Get or create request queue for a key
 */
function getRequestQueue(key: string): RequestQueue {
  if (!requestQueues.has(key)) {
    requestQueues.set(key, new RequestQueue());
  }
  return requestQueues.get(key)!;
}

/**
 * Create axios instance with default configuration
 */
const apiClient: AxiosInstance = axios.create({
  baseURL: env.apiBaseUrl,
  timeout: timeoutConfig.getTimeout('default'),
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Request interceptor for logging, auth token injection, and circuit breaker
 */
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const extendedConfig = config as ExtendedAxiosRequestConfig;

    // Check circuit breaker (unless explicitly skipped)
    if (!extendedConfig._skipCircuitBreaker && !circuitBreaker.canAttempt()) {
      const error: ExtendedError = new Error('Circuit breaker is open - service unavailable');
      error.isCircuitBreakerError = true;
      loggingService.warn('Request blocked by circuit breaker', 'apiClient', 'circuitBreaker', {
        url: config.url,
      });
      return Promise.reject(error);
    }

    // Log API requests using logging service
    const startTime = Date.now();

    // Store start time in config for performance logging
    const extendedInternalConfig = config as ExtendedInternalAxiosRequestConfig;
    extendedInternalConfig._requestStartTime = startTime;

    // Generate correlation ID for request tracking
    const correlationId = crypto.randomUUID();
    if (config.headers) {
      config.headers['X-Correlation-ID'] = correlationId;
    }

    // Store correlation ID for error tracking
    sessionStorage.setItem('lastCorrelationId', correlationId);

    loggingService.debug(
      `API Request: ${config.method?.toUpperCase()} ${config.url}`,
      'apiClient',
      'request',
      {
        method: config.method,
        url: config.url,
        data: config.data,
        correlationId,
      }
    );

    // Add auth token if available
    const token = localStorage.getItem('auth_token');
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }

    return config;
  },
  (error) => {
    loggingService.error('API request setup error', error, 'apiClient', 'request');
    return Promise.reject(error);
  }
);

/**
 * Response interceptor for logging, error handling, and automatic retry
 */
apiClient.interceptors.response.use(
  (response: AxiosResponse) => {
    // Calculate request duration for performance logging
    const extendedInternalConfig = response.config as ExtendedInternalAxiosRequestConfig;
    const startTime = extendedInternalConfig._requestStartTime;
    const duration = startTime ? Date.now() - startTime : 0;

    // Record success in circuit breaker
    const extendedConfig = response.config as ExtendedAxiosRequestConfig;
    if (!extendedConfig._skipCircuitBreaker) {
      circuitBreaker.recordSuccess();
    }

    // Record success in connection store - backend is reachable
    useConnectionStore.getState().recordSuccess();

    // Capture correlation ID from response header
    const correlationId = response.headers['x-correlation-id'];
    if (correlationId) {
      sessionStorage.setItem('lastCorrelationId', correlationId);
    }

    // Log successful responses with performance metrics
    loggingService.debug(
      `API Response: ${response.config.method?.toUpperCase()} ${response.config.url}`,
      'apiClient',
      'response',
      {
        method: response.config.method,
        url: response.config.url,
        status: response.status,
        data: response.data,
        correlationId,
      }
    );

    // Log performance if request took more than 1 second
    if (duration > 1000) {
      loggingService.performance(
        `${response.config.method?.toUpperCase()} ${response.config.url}`,
        duration,
        'apiClient',
        {
          url: response.config.url,
          status: response.status,
        }
      );
    }

    return response;
  },
  // eslint-disable-next-line sonarjs/cognitive-complexity -- Comprehensive error handling with retry logic, circuit breaker, and various error scenarios
  async (error: AxiosError) => {
    const extendedConfig = error.config as ExtendedAxiosRequestConfig | undefined;

    // Log API errors
    const extendedInternalConfig = error.config as ExtendedInternalAxiosRequestConfig | undefined;
    const startTime = extendedInternalConfig?._requestStartTime;
    const duration = startTime ? Date.now() - startTime : 0;

    // Extract error information
    let userMessage = 'An error occurred while communicating with the server';
    let errorCode: string | undefined;
    let technicalDetails: Record<string, unknown> = {};

    // Handle different error scenarios
    if (error.response) {
      // Server responded with error status
      const status = error.response.status;
      const responseData = error.response.data as ApiErrorResponse;

      // Extract error code
      errorCode = responseData?.errorCode || responseData?.code;

      // Get user-friendly message
      const errorMessage = errorCode ? getAppErrorMessage(errorCode) : getHttpErrorMessage(status);

      userMessage = errorMessage.message;

      // Add custom message from response if available
      if (responseData?.message || responseData?.detail) {
        userMessage = (responseData.message || responseData.detail) as string;
      }

      technicalDetails = {
        status,
        statusText: error.response.statusText,
        data: responseData,
        url: error.config?.url,
        method: error.config?.method,
        errorCode,
      };

      // Check if we should trigger circuit breaker
      if (extendedConfig && !extendedConfig._skipCircuitBreaker) {
        if (shouldTriggerCircuitBreaker(status, errorCode)) {
          circuitBreaker.recordFailure();
        }
      }

      // Handle 401 - clear auth and redirect to login
      if (status === 401) {
        localStorage.removeItem('auth_token');
        // Show notification and redirect to login
        errorReportingService.warning(
          'Session Expired',
          'Your session has expired. Please sign in again.',
          { duration: 5000 }
        );
        // Redirect to login page (using window.location to handle outside React Router context)
        if (typeof window !== 'undefined' && !window.location.pathname.includes('/login')) {
          window.location.href = '/login';
        }
      }

      // Handle 500+ server errors - show user-friendly toast notification
      if (status >= 500) {
        errorReportingService.error(
          'Server Error',
          'Something went wrong on our end. Please try again later.',
          new Error(`HTTP ${status}: ${error.response.statusText}`),
          {
            userAction: `Request to ${error.config?.url}`,
          }
        );
      }

      // Handle 429 - rate limit
      if (status === 429) {
        const retryAfter = error.response.headers['retry-after'];
        technicalDetails.retryAfter = retryAfter;
        loggingService.warn('Rate limit exceeded', 'apiClient', 'rateLimit', {
          retryAfter,
          url: error.config?.url,
        });
      }
    } else if (error.request) {
      // Request made but no response received - record failure in connection store
      useConnectionStore.getState().recordFailure('Network connection lost');
      userMessage = 'Network connection lost - Retrying...';
      errorCode = 'NETWORK_ERROR';
      technicalDetails = {
        request: error.request,
        url: error.config?.url,
        method: error.config?.method,
        errorCode,
      };

      // Record network failure in circuit breaker
      if (extendedConfig && !extendedConfig._skipCircuitBreaker) {
        if (shouldTriggerCircuitBreaker(undefined, errorCode)) {
          circuitBreaker.recordFailure();
        }
      }
    } else if ((error as ExtendedError).isCircuitBreakerError) {
      // Circuit breaker blocked the request - record failure in connection store
      useConnectionStore.getState().recordFailure('Service temporarily unavailable');
      userMessage = 'The service is temporarily unavailable. Please try again later.';
      errorCode = 'CIRCUIT_BREAKER_OPEN';
      technicalDetails = {
        circuitBreakerState: circuitBreaker.getState(),
      };
    } else {
      // Error setting up request
      userMessage = 'An error occurred while preparing the request.';
      technicalDetails = {
        message: error.message,
        url: error.config?.url,
        method: error.config?.method,
      };
    }

    // Log the error with full technical details
    loggingService.error(
      `API Error: ${userMessage}`,
      error,
      'apiClient',
      'error',
      technicalDetails
    );

    // Log performance even for errors
    if (duration > 0) {
      loggingService.performance(
        `${error.config?.method?.toUpperCase()} ${error.config?.url} (failed)`,
        duration,
        'apiClient',
        {
          url: error.config?.url,
          error: true,
        }
      );
    }

    // Attach user-friendly message to error for UI display
    const extendedError = error as AxiosError & ExtendedError;
    extendedError.userMessage = userMessage;
    extendedError.errorCode = errorCode;

    // Check if we should retry
    if (extendedConfig && !extendedConfig._skipRetry && error.config) {
      const shouldRetry = isTransientError(error.response?.status, errorCode);

      if (shouldRetry) {
        const retryCount = (extendedConfig._retry || 0) + 1;
        const maxRetries = 3;

        if (retryCount <= maxRetries) {
          // Calculate delay with exponential backoff
          const baseDelay = 1000; // 1 second
          const delay = Math.min(baseDelay * Math.pow(2, retryCount - 1), 8000);

          loggingService.warn(
            `Retrying request (${retryCount}/${maxRetries}) after ${delay}ms`,
            'apiClient',
            'retry',
            {
              url: error.config.url,
              method: error.config.method,
              attempt: retryCount,
            }
          );

          // Wait before retrying
          await new Promise((resolve) => setTimeout(resolve, delay));

          // Update retry count
          extendedConfig._retry = retryCount;

          // Retry the request
          return apiClient(extendedConfig);
        }
      }
    }

    return Promise.reject(error);
  }
);

/**
 * Retry logic with exponential backoff (legacy helper - prefer using built-in retry)
 * @deprecated Use the built-in retry mechanism in interceptors instead
 */
export async function requestWithRetry<T>(
  requestFn: () => Promise<T>,
  maxRetries: number = 3,
  baseDelay: number = 1000
): Promise<T> {
  let lastError: Error | undefined;

  for (let attempt = 0; attempt < maxRetries; attempt++) {
    try {
      return await requestFn();
    } catch (error) {
      lastError = error as Error;

      if (attempt < maxRetries - 1) {
        const delay = baseDelay * Math.pow(2, attempt);
        loggingService.warn(
          `Request failed, retrying in ${delay}ms... (Attempt ${attempt + 1}/${maxRetries})`,
          'apiClient',
          'retry'
        );
        await new Promise((resolve) => setTimeout(resolve, delay));
      }
    }
  }

  throw lastError;
}

/**
 * Generic GET request
 */
export async function get<T>(url: string, config?: ExtendedAxiosRequestConfig): Promise<T> {
  const response = await apiClient.get<T>(url, config);
  return response.data;
}

/**
 * Generic POST request with optional deduplication
 */
export async function post<T>(
  url: string,
  data?: unknown,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  // Check if deduplication should be applied (enabled by default for POST)
  const shouldDeduplicate = config?._skipDeduplication !== true;

  if (shouldDeduplicate) {
    const dedupeKey = createDedupeKey('POST', url, data);

    return requestDeduplicator.deduplicate(dedupeKey, async () => {
      const response = await apiClient.post<T>(url, data, config);
      return response.data;
    });
  }

  const response = await apiClient.post<T>(url, data, config);
  return response.data;
}

/**
 * Generic PUT request with optional deduplication
 */
export async function put<T>(
  url: string,
  data?: unknown,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  // Check if deduplication should be applied (enabled by default for PUT)
  const shouldDeduplicate = config?._skipDeduplication !== true;

  if (shouldDeduplicate) {
    const dedupeKey = createDedupeKey('PUT', url, data);

    return requestDeduplicator.deduplicate(dedupeKey, async () => {
      const response = await apiClient.put<T>(url, data, config);
      return response.data;
    });
  }

  const response = await apiClient.put<T>(url, data, config);
  return response.data;
}

/**
 * Generic PATCH request with optional deduplication
 */
export async function patch<T>(
  url: string,
  data?: unknown,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  // Check if deduplication should be applied (enabled by default for PATCH)
  const shouldDeduplicate = config?._skipDeduplication !== true;

  if (shouldDeduplicate) {
    const dedupeKey = createDedupeKey('PATCH', url, data);

    return requestDeduplicator.deduplicate(dedupeKey, async () => {
      const response = await apiClient.patch<T>(url, data, config);
      return response.data;
    });
  }

  const response = await apiClient.patch<T>(url, data, config);
  return response.data;
}

/**
 * Generic DELETE request
 */
export async function del<T>(url: string, config?: ExtendedAxiosRequestConfig): Promise<T> {
  const response = await apiClient.delete<T>(url, config);
  return response.data;
}

/**
 * Create AbortController for request cancellation
 */
export function createAbortController(): AbortController {
  return new AbortController();
}

/**
 * Make a cancellable request
 */
export async function getCancellable<T>(
  url: string,
  abortController: AbortController,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  const response = await apiClient.get<T>(url, {
    ...config,
    signal: abortController.signal,
  });
  return response.data;
}

/**
 * Queue a request to prevent rate limiting
 */
export async function queuedRequest<T>(queueKey: string, requestFn: () => Promise<T>): Promise<T> {
  const queue = getRequestQueue(queueKey);
  return queue.enqueue(requestFn);
}

/**
 * GET request with automatic queuing for rate-limited endpoints
 */
export async function getQueued<T>(
  url: string,
  queueKey: string,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  return queuedRequest(queueKey, () => get<T>(url, config));
}

/**
 * POST request with automatic queuing for rate-limited endpoints
 */
export async function postQueued<T>(
  url: string,
  queueKey: string,
  data?: unknown,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  return queuedRequest(queueKey, () => post<T>(url, data, config));
}

/**
 * Make a request with custom timeout
 */
export async function getWithTimeout<T>(
  url: string,
  timeoutMs: number,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  const response = await apiClient.get<T>(url, {
    ...config,
    timeout: timeoutMs,
  });
  return response.data;
}

/**
 * POST request with custom timeout (useful for video generation)
 */
export async function postWithTimeout<T>(
  url: string,
  data: unknown,
  timeoutMs: number,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  const response = await apiClient.post<T>(url, data, {
    ...config,
    timeout: timeoutMs,
  });
  return response.data;
}

/**
 * Reset circuit breaker (for testing or manual recovery)
 */
export function resetCircuitBreaker(): void {
  circuitBreaker.reset();
  loggingService.info('Circuit breaker manually reset', 'apiClient', 'circuitBreaker');
}

/**
 * Get circuit breaker state
 */
export function getCircuitBreakerState(): string {
  return circuitBreaker.getState();
}

/**
 * Upload file with progress tracking
 */
export async function uploadFile<T>(
  url: string,
  file: File,
  onProgress?: (progress: number) => void,
  config?: ExtendedAxiosRequestConfig
): Promise<T> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await apiClient.post<T>(url, formData, {
    ...config,
    headers: {
      ...config?.headers,
      'Content-Type': 'multipart/form-data',
    },
    onUploadProgress: (progressEvent) => {
      if (onProgress && progressEvent.total) {
        const progress = Math.round((progressEvent.loaded * 100) / progressEvent.total);
        onProgress(progress);
      }
    },
  });

  return response.data;
}

/**
 * Download file with progress tracking
 */
export async function downloadFile(
  url: string,
  filename: string,
  onProgress?: (progress: number) => void,
  config?: ExtendedAxiosRequestConfig
): Promise<void> {
  const response = await apiClient.get(url, {
    ...config,
    responseType: 'blob',
    onDownloadProgress: (progressEvent) => {
      if (onProgress && progressEvent.total) {
        const progress = Math.round((progressEvent.loaded * 100) / progressEvent.total);
        onProgress(progress);
      }
    },
  });

  // Create download link
  const blob = new Blob([response.data]);
  const downloadUrl = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = downloadUrl;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(downloadUrl);
}

/**
 * Check if a request is currently pending (being deduplicated)
 */
export function isRequestPending(method: string, url: string, data?: unknown): boolean {
  const dedupeKey = createDedupeKey(method, url, data);
  return requestDeduplicator.isPending(dedupeKey);
}

/**
 * Clear request deduplication cache
 */
export function clearDeduplicationCache(method?: string, url?: string, data?: unknown): void {
  if (method && url) {
    const dedupeKey = createDedupeKey(method, url, data);
    requestDeduplicator.clear(dedupeKey);
  } else {
    requestDeduplicator.clear();
  }
}

/**
 * Export network resilience service for offline queue management
 */
export { networkResilienceService };

/**
 * Export timeout configuration for settings management
 */
export { timeoutConfig } from '../../config/timeouts';

export default apiClient;
