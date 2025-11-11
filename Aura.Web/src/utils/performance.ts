/**
 * Performance utility functions for React optimization
 */

import { useEffect, useRef } from 'react';

/**
 * Hook to detect unnecessary re-renders in development
 * Logs to console when a component re-renders with the same props
 */
export function useWhyDidYouUpdate(name: string, props: Record<string, unknown>): void {
  const previousProps = useRef<Record<string, unknown>>();

  useEffect(() => {
    if (previousProps.current && process.env.NODE_ENV === 'development') {
      const allKeys = Object.keys({ ...previousProps.current, ...props });
      const changedProps: Record<string, { from: unknown; to: unknown }> = {};

      allKeys.forEach((key) => {
        if (previousProps.current?.[key] !== props[key]) {
          changedProps[key] = {
            from: previousProps.current?.[key],
            to: props[key],
          };
        }
      });

      if (Object.keys(changedProps).length > 0) {
        console.info('[why-did-you-update]', name, changedProps);
      }
    }

    previousProps.current = props;
  });
}

/**
 * Shallow comparison function for React.memo
 * Only compares first level of props
 */
export function shallowEqual(
  objA: Record<string, unknown>,
  objB: Record<string, unknown>
): boolean {
  if (objA === objB) {
    return true;
  }

  if (!objA || !objB) {
    return false;
  }

  const keysA = Object.keys(objA);
  const keysB = Object.keys(objB);

  if (keysA.length !== keysB.length) {
    return false;
  }

  for (const key of keysA) {
    if (objA[key] !== objB[key]) {
      return false;
    }
  }

  return true;
}

/**
 * Deep comparison function for complex objects
 * Use sparingly as it's more expensive than shallow comparison
 */
export function deepEqual(a: unknown, b: unknown): boolean {
  if (a === b) return true;

  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
    return false;
  }

  const keysA = Object.keys(a as Record<string, unknown>);
  const keysB = Object.keys(b as Record<string, unknown>);

  if (keysA.length !== keysB.length) {
    return false;
  }

  for (const key of keysA) {
    if (
      !keysB.includes(key) ||
      !deepEqual((a as Record<string, unknown>)[key], (b as Record<string, unknown>)[key])
    ) {
      return false;
    }
  }

  return true;
}

/**
 * Custom comparison for memo when dealing with complex props
 */
export function arePropsEqual<T extends Record<string, unknown>>(
  prevProps: T,
  nextProps: T,
  deepCompareKeys?: (keyof T)[]
): boolean {
  const allKeys = new Set([...Object.keys(prevProps), ...Object.keys(nextProps)]);

  for (const key of allKeys) {
    const shouldDeepCompare = deepCompareKeys?.includes(key as keyof T);

    if (shouldDeepCompare) {
      if (!deepEqual(prevProps[key], nextProps[key])) {
        return false;
      }
    } else {
      if (prevProps[key] !== nextProps[key]) {
        return false;
      }
    }
  }

  return true;
}

/**
 * Performance measurement utility
 * Measures execution time of a function in development
 */
export function measurePerformance<T>(name: string, fn: () => T): T {
  if (process.env.NODE_ENV === 'development') {
    const start = performance.now();
    const result = fn();
    const end = performance.now();
    console.info(`[Performance] ${name}: ${(end - start).toFixed(2)}ms`);
    return result;
  }
  return fn();
}

/**
 * Debounce function for expensive operations
 */
export function debounce<T extends (...args: unknown[]) => void>(
  func: T,
  wait: number
): (...args: Parameters<T>) => void {
  let timeout: ReturnType<typeof setTimeout> | null = null;

  return function executedFunction(...args: Parameters<T>) {
    const later = () => {
      timeout = null;
      func(...args);
    };

    if (timeout !== null) {
      clearTimeout(timeout);
    }
    timeout = setTimeout(later, wait);
  };
}

/**
 * Throttle function for frequent events
 */
export function throttle<T extends (...args: unknown[]) => void>(
  func: T,
  limit: number
): (...args: Parameters<T>) => void {
  let inThrottle: boolean;

  return function executedFunction(...args: Parameters<T>) {
    if (!inThrottle) {
      func(...args);
      inThrottle = true;
      setTimeout(() => {
        inThrottle = false;
      }, limit);
    }
  };
}
