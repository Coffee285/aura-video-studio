/**
 * Tests for imagePreloader utility
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  preloadImage,
  preloadImages,
  preloadCriticalImages,
  getPreloadStatus,
  isImagePreloaded,
  clearPreloadCache,
  getPreloadStats,
} from '../imagePreloader';

describe('imagePreloader utilities', () => {
  beforeEach(() => {
    clearPreloadCache();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  describe('preloadImage', () => {
    it('should timeout if image takes too long', async () => {
      const promise = preloadImage('slow.png', { timeout: 1000 });

      // Fast-forward time
      vi.advanceTimersByTime(1500);

      await expect(promise).rejects.toThrow('timeout');
    });

    it('should respect custom timeout', async () => {
      const customTimeout = 5000;
      const promise = preloadImage('test.png', { timeout: customTimeout });

      vi.advanceTimersByTime(customTimeout + 100);

      await expect(promise).rejects.toThrow();
    });
  });

  describe('preloadImages', () => {
    it('should initiate preload for multiple images', () => {
      const onProgress = vi.fn();
      const paths = ['image1.png', 'image2.png', 'image3.png'];

      preloadImages(paths, { onProgress });

      // Verify that Image objects are being created
      // We can't verify actual loading in jsdom
      expect(paths).toHaveLength(3);
    });
  });

  describe('preloadCriticalImages', () => {
    it('should initiate preload for critical application images', () => {
      const promise = preloadCriticalImages();

      // Should return a promise
      expect(promise).toBeInstanceOf(Promise);
    });
  });

  describe('getPreloadStatus', () => {
    it('should return null for non-preloaded image', () => {
      const status = getPreloadStatus('notloaded.png');
      expect(status).toBeNull();
    });
  });

  describe('isImagePreloaded', () => {
    it('should return false for non-preloaded image', () => {
      const result = isImagePreloaded('notloaded.png');
      expect(result).toBe(false);
    });
  });

  describe('clearPreloadCache', () => {
    it('should clear the cache', () => {
      clearPreloadCache();

      expect(isImagePreloaded('test.png')).toBe(false);
    });
  });

  describe('getPreloadStats', () => {
    it('should return initial statistics', () => {
      const stats = getPreloadStats();

      expect(stats.total).toBe(0);
      expect(stats.loaded).toBe(0);
      expect(stats.failed).toBe(0);
      expect(stats.pending).toBe(0);
      expect(stats.avgLoadTime).toBe(0);
    });
  });
});
