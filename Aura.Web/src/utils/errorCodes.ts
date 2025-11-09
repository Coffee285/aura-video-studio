/**
 * Error Code System for Application Errors
 * Provides standardized error codes for better support and diagnostics
 */

export enum ErrorCategory {
  NETWORK = 'NETWORK',
  STORAGE = 'STORAGE',
  RENDERING = 'RENDERING',
  PROVIDER = 'PROVIDER',
  VALIDATION = 'VALIDATION',
  CONFIGURATION = 'CONFIGURATION',
  UNKNOWN = 'UNKNOWN',
}

export interface ErrorCode {
  code: string;
  category: ErrorCategory;
  title: string;
  userMessage: string;
  recoverySuggestions: string[];
  logMessage: string;
}

export const ERROR_CODES: Record<string, ErrorCode> = {
  // Network Errors (E1xxx)
  E1001: {
    code: 'E1001',
    category: ErrorCategory.NETWORK,
    title: 'Backend Connection Failed',
    userMessage: 'Cannot connect to the backend API. Please ensure the backend service is running.',
    recoverySuggestions: [
      'Check that the backend is running on http://localhost:5005',
      'Check your firewall settings',
      'Restart the backend service',
      'Check backend logs for errors',
    ],
    logMessage: 'Failed to connect to backend API',
  },
  E1002: {
    code: 'E1002',
    category: ErrorCategory.NETWORK,
    title: 'API Request Timeout',
    userMessage: 'The request took too long to complete. The backend may be overloaded.',
    recoverySuggestions: [
      'Try again in a few moments',
      'Check your network connection',
      'Reduce the complexity of your request',
      'Check backend performance in system health',
    ],
    logMessage: 'API request timed out',
  },
  E1003: {
    code: 'E1003',
    category: ErrorCategory.NETWORK,
    title: 'Network Error',
    userMessage: 'A network error occurred while communicating with the backend.',
    recoverySuggestions: [
      'Check your internet connection',
      'Check if firewall is blocking the connection',
      'Try refreshing the page',
    ],
    logMessage: 'Network error during API call',
  },

  // Storage Errors (E2xxx)
  E2001: {
    code: 'E2001',
    category: ErrorCategory.STORAGE,
    title: 'Insufficient Disk Space',
    userMessage: 'There is not enough disk space to complete this operation.',
    recoverySuggestions: [
      'Free up disk space by deleting unused files',
      'Move old projects to external storage',
      'Clear temporary files in Settings > Advanced',
      'Check disk space in system health dashboard',
    ],
    logMessage: 'Insufficient disk space for operation',
  },
  E2002: {
    code: 'E2002',
    category: ErrorCategory.STORAGE,
    title: 'File Access Denied',
    userMessage: 'Cannot access the required file or directory. Permission may be denied.',
    recoverySuggestions: [
      'Check file permissions',
      'Ensure the application has write access to the data directory',
      'Run the application with appropriate permissions',
      'Check if file is open in another application',
    ],
    logMessage: 'File access denied',
  },
  E2003: {
    code: 'E2003',
    category: ErrorCategory.STORAGE,
    title: 'Data Corruption',
    userMessage: 'The project data appears to be corrupted or invalid.',
    recoverySuggestions: [
      'Try loading from a backup if available',
      'Check for auto-saved versions',
      'Restore from configuration backup',
      'Contact support with the error details',
    ],
    logMessage: 'Corrupted data detected',
  },

  // Rendering Errors (E3xxx)
  E3001: {
    code: 'E3001',
    category: ErrorCategory.RENDERING,
    title: 'FFmpeg Not Found',
    userMessage: 'FFmpeg is required for video rendering but was not found.',
    recoverySuggestions: [
      'Install FFmpeg from the Setup Wizard',
      'Add FFmpeg to your system PATH',
      'Configure FFmpeg path manually in Settings',
      'Check the FFmpeg installation guide',
    ],
    logMessage: 'FFmpeg executable not found',
  },
  E3002: {
    code: 'E3002',
    category: ErrorCategory.RENDERING,
    title: 'Rendering Failed',
    userMessage: 'Video rendering failed during processing.',
    recoverySuggestions: [
      'Check FFmpeg is properly installed',
      'Reduce video quality or resolution',
      'Try a different output format',
      'Check system resources (CPU/GPU/memory)',
      'View rendering logs for details',
    ],
    logMessage: 'Video rendering process failed',
  },
  E3003: {
    code: 'E3003',
    category: ErrorCategory.RENDERING,
    title: 'GPU Acceleration Failed',
    userMessage: 'Hardware acceleration failed. Falling back to CPU rendering.',
    recoverySuggestions: [
      'Update your GPU drivers',
      'Check GPU is properly detected in system health',
      'Disable hardware acceleration in Settings if issues persist',
    ],
    logMessage: 'GPU acceleration failed, falling back to CPU',
  },

  // Provider Errors (E4xxx)
  E4001: {
    code: 'E4001',
    category: ErrorCategory.PROVIDER,
    title: 'Provider Not Available',
    userMessage: 'The requested AI provider is not available or not configured.',
    recoverySuggestions: [
      'Configure the provider in Settings > Providers',
      'Check your API key is valid',
      'Try a different provider',
      'Enable offline mode for limited functionality',
    ],
    logMessage: 'AI provider not available',
  },
  E4002: {
    code: 'E4002',
    category: ErrorCategory.PROVIDER,
    title: 'API Key Invalid',
    userMessage: 'The API key for this provider is invalid or expired.',
    recoverySuggestions: [
      'Verify your API key in Settings > API Keys',
      'Generate a new API key from the provider',
      'Check your account status with the provider',
    ],
    logMessage: 'Invalid or expired API key',
  },
  E4003: {
    code: 'E4003',
    category: ErrorCategory.PROVIDER,
    title: 'Provider Rate Limited',
    userMessage: 'The provider has rate-limited your requests. Please wait before trying again.',
    recoverySuggestions: [
      'Wait a few minutes before retrying',
      'Upgrade your provider plan for higher limits',
      'Use a different provider',
      'Reduce request frequency',
    ],
    logMessage: 'Provider rate limit exceeded',
  },

  // Configuration Errors (E5xxx)
  E5001: {
    code: 'E5001',
    category: ErrorCategory.CONFIGURATION,
    title: 'Configuration Invalid',
    userMessage: 'The application configuration is invalid or corrupted.',
    recoverySuggestions: [
      'Reset configuration to defaults in Settings',
      'Restore from a configuration backup',
      'Delete configuration file and restart',
      'Contact support if issue persists',
    ],
    logMessage: 'Invalid or corrupted configuration',
  },
  E5002: {
    code: 'E5002',
    category: ErrorCategory.CONFIGURATION,
    title: 'Configuration Migration Failed',
    userMessage: 'Failed to migrate configuration from an older version.',
    recoverySuggestions: [
      'Restore from a backup before the update',
      'Reset to default configuration',
      'Manually re-configure settings',
    ],
    logMessage: 'Configuration migration failed',
  },

  // Validation Errors (E6xxx)
  E6001: {
    code: 'E6001',
    category: ErrorCategory.VALIDATION,
    title: 'Invalid Input',
    userMessage: 'The provided input is invalid or does not meet requirements.',
    recoverySuggestions: [
      'Check the input format and requirements',
      'Ensure all required fields are filled',
      'Verify file types and sizes are correct',
    ],
    logMessage: 'Input validation failed',
  },

  // Unknown Errors (E9xxx)
  E9999: {
    code: 'E9999',
    category: ErrorCategory.UNKNOWN,
    title: 'Unexpected Error',
    userMessage: 'An unexpected error occurred. Please try again or contact support.',
    recoverySuggestions: [
      'Try refreshing the page',
      'Check browser console for details',
      'Report this issue with error details',
      'Contact support for assistance',
    ],
    logMessage: 'Unexpected error occurred',
  },
};

/**
 * Categorize an error and assign an error code
 */
export function categorizeError(error: Error): ErrorCode {
  const errorMessage = error.message.toLowerCase();
  const errorName = error.name.toLowerCase();

  // Network errors
  if (
    errorMessage.includes('network') ||
    errorMessage.includes('fetch') ||
    errorMessage.includes('connection') ||
    errorName.includes('networkerror')
  ) {
    if (errorMessage.includes('timeout')) {
      return ERROR_CODES.E1002;
    }
    return ERROR_CODES.E1003;
  }

  // Storage errors
  if (
    errorMessage.includes('disk') ||
    errorMessage.includes('space') ||
    errorMessage.includes('quota')
  ) {
    return ERROR_CODES.E2001;
  }

  if (
    errorMessage.includes('permission') ||
    errorMessage.includes('access denied') ||
    errorMessage.includes('eacces')
  ) {
    return ERROR_CODES.E2002;
  }

  if (
    errorMessage.includes('corrupt') ||
    errorMessage.includes('invalid json') ||
    errorMessage.includes('parse')
  ) {
    return ERROR_CODES.E2003;
  }

  // Rendering errors
  if (errorMessage.includes('ffmpeg')) {
    return ERROR_CODES.E3001;
  }

  if (errorMessage.includes('render') || errorMessage.includes('encoding')) {
    return ERROR_CODES.E3002;
  }

  // Provider errors
  if (errorMessage.includes('api key') || errorMessage.includes('authentication')) {
    return ERROR_CODES.E4002;
  }

  if (errorMessage.includes('rate limit') || errorMessage.includes('too many requests')) {
    return ERROR_CODES.E4003;
  }

  if (
    errorMessage.includes('provider') ||
    errorMessage.includes('llm') ||
    errorMessage.includes('tts')
  ) {
    return ERROR_CODES.E4001;
  }

  // Configuration errors
  if (errorMessage.includes('configuration') || errorMessage.includes('config')) {
    return ERROR_CODES.E5001;
  }

  // Validation errors
  if (errorMessage.includes('validation') || errorMessage.includes('invalid')) {
    return ERROR_CODES.E6001;
  }

  // Unknown error
  return ERROR_CODES.E9999;
}

/**
 * Get user-friendly error information from an error
 */
export function getErrorInfo(error: Error): {
  code: ErrorCode;
  timestamp: string;
  correlationId: string;
} {
  const code = categorizeError(error);
  const timestamp = new Date().toISOString();
  const correlationId = `${Date.now()}-${Math.random().toString(36).substring(7)}`;

  return {
    code,
    timestamp,
    correlationId,
  };
}
