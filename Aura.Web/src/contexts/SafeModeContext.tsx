import { createContext, useContext, useState, useCallback, ReactNode } from 'react';
import { loggingService } from '../services/loggingService';

export interface SafeModeConfig {
  /** Whether safe mode is currently active */
  isActive: boolean;
  /** Reason for entering safe mode */
  reason?: string;
  /** List of disabled features in safe mode */
  disabledFeatures: string[];
  /** List of degraded services */
  degradedServices: string[];
}

interface SafeModeContextValue {
  /** Current safe mode configuration */
  safeMode: SafeModeConfig;
  /** Enter safe mode with optional reason */
  enterSafeMode: (reason?: string) => void;
  /** Exit safe mode (restore normal operation) */
  exitSafeMode: () => void;
  /** Check if a specific feature is available */
  isFeatureAvailable: (feature: string) => boolean;
  /** Mark a service as degraded */
  markServiceDegraded: (service: string) => void;
  /** Mark a service as recovered */
  markServiceRecovered: (service: string) => void;
}

const SafeModeContext = createContext<SafeModeContextValue | undefined>(undefined);

interface SafeModeProviderProps {
  children: ReactNode;
}

export function SafeModeProvider({ children }: SafeModeProviderProps) {
  const [safeMode, setSafeMode] = useState<SafeModeConfig>({
    isActive: false,
    disabledFeatures: [],
    degradedServices: [],
  });

  const enterSafeMode = useCallback((reason?: string) => {
    loggingService.warn('Entering safe mode', 'SafeModeProvider', 'enterSafeMode', { reason });

    setSafeMode({
      isActive: true,
      reason,
      disabledFeatures: ['video-rendering', 'ai-generation', 'cloud-sync', 'advanced-effects'],
      degradedServices: [],
    });
  }, []);

  const exitSafeMode = useCallback(() => {
    loggingService.info('Exiting safe mode', 'SafeModeProvider', 'exitSafeMode');

    setSafeMode({
      isActive: false,
      disabledFeatures: [],
      degradedServices: [],
    });
  }, []);

  const isFeatureAvailable = useCallback(
    (feature: string): boolean => {
      if (!safeMode.isActive) {
        return true;
      }
      return !safeMode.disabledFeatures.includes(feature);
    },
    [safeMode]
  );

  const markServiceDegraded = useCallback((service: string) => {
    setSafeMode((prev) => ({
      ...prev,
      degradedServices: [...new Set([...prev.degradedServices, service])],
    }));
    loggingService.warn(
      `Service marked as degraded: ${service}`,
      'SafeModeProvider',
      'markServiceDegraded'
    );
  }, []);

  const markServiceRecovered = useCallback((service: string) => {
    setSafeMode((prev) => ({
      ...prev,
      degradedServices: prev.degradedServices.filter((s) => s !== service),
    }));
    loggingService.info(
      `Service recovered: ${service}`,
      'SafeModeProvider',
      'markServiceRecovered'
    );
  }, []);

  const value: SafeModeContextValue = {
    safeMode,
    enterSafeMode,
    exitSafeMode,
    isFeatureAvailable,
    markServiceDegraded,
    markServiceRecovered,
  };

  return <SafeModeContext.Provider value={value}>{children}</SafeModeContext.Provider>;
}

export function useSafeMode(): SafeModeContextValue {
  const context = useContext(SafeModeContext);
  if (!context) {
    throw new Error('useSafeMode must be used within a SafeModeProvider');
  }
  return context;
}
