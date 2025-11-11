/**
 * Performance Tests
 * Tests for React performance optimization utilities
 */

import { describe, it, expect, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import {
  shallowEqual,
  deepEqual,
  arePropsEqual,
  debounce,
  throttle,
  useWhyDidYouUpdate,
} from '../performance';

describe('Performance Utilities', () => {
  describe('shallowEqual', () => {
    it('should return true for equal objects', () => {
      const obj1 = { a: 1, b: 2, c: 3 };
      const obj2 = { a: 1, b: 2, c: 3 };
      expect(shallowEqual(obj1, obj2)).toBe(true);
    });

    it('should return false for objects with different values', () => {
      const obj1 = { a: 1, b: 2, c: 3 };
      const obj2 = { a: 1, b: 2, c: 4 };
      expect(shallowEqual(obj1, obj2)).toBe(false);
    });

    it('should return false for objects with different keys', () => {
      const obj1 = { a: 1, b: 2 };
      const obj2 = { a: 1, b: 2, c: 3 };
      expect(shallowEqual(obj1, obj2)).toBe(false);
    });

    it('should return true for same object reference', () => {
      const obj = { a: 1, b: 2 };
      expect(shallowEqual(obj, obj)).toBe(true);
    });

    it('should return false for nested object changes', () => {
      const obj1 = { a: 1, b: { c: 2 } };
      const obj2 = { a: 1, b: { c: 2 } };
      expect(shallowEqual(obj1, obj2)).toBe(false);
    });
  });

  describe('deepEqual', () => {
    it('should return true for deeply equal objects', () => {
      const obj1 = { a: 1, b: { c: 2, d: { e: 3 } } };
      const obj2 = { a: 1, b: { c: 2, d: { e: 3 } } };
      expect(deepEqual(obj1, obj2)).toBe(true);
    });

    it('should return false for deeply unequal objects', () => {
      const obj1 = { a: 1, b: { c: 2, d: { e: 3 } } };
      const obj2 = { a: 1, b: { c: 2, d: { e: 4 } } };
      expect(deepEqual(obj1, obj2)).toBe(false);
    });

    it('should handle arrays', () => {
      const obj1 = { a: [1, 2, 3] };
      const obj2 = { a: [1, 2, 3] };
      expect(deepEqual(obj1, obj2)).toBe(true);
    });

    it('should return true for same reference', () => {
      const obj = { a: 1, b: { c: 2 } };
      expect(deepEqual(obj, obj)).toBe(true);
    });
  });

  describe('arePropsEqual', () => {
    it('should use shallow comparison by default', () => {
      const props1 = { a: 1, b: 2, c: { d: 3 } };
      const props2 = { a: 1, b: 2, c: { d: 3 } };
      expect(arePropsEqual(props1, props2)).toBe(false);
    });

    it('should use deep comparison for specified keys', () => {
      const props1 = { a: 1, b: { c: 2 } };
      const props2 = { a: 1, b: { c: 2 } };
      expect(arePropsEqual(props1, props2, ['b'])).toBe(true);
    });

    it('should mix shallow and deep comparison', () => {
      const props1 = { a: 1, b: { c: 2 }, d: { e: 3 } };
      const props2 = { a: 1, b: { c: 2 }, d: { e: 3 } };
      expect(arePropsEqual(props1, props2, ['b'])).toBe(false);
      expect(arePropsEqual(props1, props2, ['b', 'd'])).toBe(true);
    });
  });

  describe('debounce', () => {
    it('should debounce function calls', async () => {
      vi.useFakeTimers();
      const fn = vi.fn();
      const debouncedFn = debounce(fn, 100);

      debouncedFn();
      debouncedFn();
      debouncedFn();

      expect(fn).not.toHaveBeenCalled();

      vi.advanceTimersByTime(100);
      expect(fn).toHaveBeenCalledTimes(1);

      vi.useRealTimers();
    });

    it('should pass arguments correctly', async () => {
      vi.useFakeTimers();
      const fn = vi.fn();
      const debouncedFn = debounce(fn, 100);

      debouncedFn('a', 'b', 'c');

      vi.advanceTimersByTime(100);
      expect(fn).toHaveBeenCalledWith('a', 'b', 'c');

      vi.useRealTimers();
    });
  });

  describe('throttle', () => {
    it('should throttle function calls', () => {
      vi.useFakeTimers();
      const fn = vi.fn();
      const throttledFn = throttle(fn, 100);

      throttledFn();
      throttledFn();
      throttledFn();

      expect(fn).toHaveBeenCalledTimes(1);

      vi.advanceTimersByTime(100);
      throttledFn();
      expect(fn).toHaveBeenCalledTimes(2);

      vi.useRealTimers();
    });

    it('should pass arguments correctly', () => {
      vi.useFakeTimers();
      const fn = vi.fn();
      const throttledFn = throttle(fn, 100);

      throttledFn('a', 'b', 'c');
      expect(fn).toHaveBeenCalledWith('a', 'b', 'c');

      vi.useRealTimers();
    });
  });

  describe('useWhyDidYouUpdate', () => {
    it('should not log on initial render', () => {
      const consoleInfoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
      
      renderHook(() => useWhyDidYouUpdate('TestComponent', { a: 1, b: 2 }));
      
      expect(consoleInfoSpy).not.toHaveBeenCalled();
      consoleInfoSpy.mockRestore();
    });

    it('should log when props change in development', () => {
      const originalNodeEnv = process.env.NODE_ENV;
      process.env.NODE_ENV = 'development';
      
      const consoleInfoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
      
      const { rerender } = renderHook(
        (props: { a: number; b: number }) => useWhyDidYouUpdate('TestComponent', props),
        { initialProps: { a: 1, b: 2 } }
      );

      rerender({ a: 1, b: 3 });
      
      expect(consoleInfoSpy).toHaveBeenCalled();
      
      consoleInfoSpy.mockRestore();
      process.env.NODE_ENV = originalNodeEnv;
    });
  });
});
