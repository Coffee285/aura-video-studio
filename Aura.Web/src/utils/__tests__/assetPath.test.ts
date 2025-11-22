/**
 * Tests for assetPath utility
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  resolveAssetPath,
  resolveAssetPaths,
  checkAssetExists,
  getAssetMetadata,
} from '../assetPath';

describe('assetPath utilities', () => {
  beforeEach(() => {
    // Reset fetch mock
    global.fetch = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('resolveAssetPath', () => {
    it('should resolve asset path with base URL', () => {
      const result = resolveAssetPath('logo.svg');
      expect(result).toContain('logo.svg');
    });

    it('should handle leading slash in asset path', () => {
      const result = resolveAssetPath('/logo.svg');
      expect(result).toContain('logo.svg');
      expect(result).not.toMatch('/\\/logo.svg$/');
    });

    it('should handle nested paths', () => {
      const result = resolveAssetPath('icons/home.png');
      expect(result).toContain('icons/home.png');
    });

    it('should not double up slashes', () => {
      const result = resolveAssetPath('logo.svg');
      expect(result).not.toMatch('/\\/\\//');
    });

    it('should handle empty string', () => {
      const result = resolveAssetPath('');
      expect(result).toBeDefined();
    });
  });

  describe('resolveAssetPaths', () => {
    it('should resolve multiple asset paths', () => {
      const paths = ['logo.svg', 'icon.png', 'images/hero.jpg'];
      const results = resolveAssetPaths(paths);

      expect(results).toHaveLength(3);
      expect(results[0]).toContain('logo.svg');
      expect(results[1]).toContain('icon.png');
      expect(results[2]).toContain('images/hero.jpg');
    });

    it('should handle empty array', () => {
      const results = resolveAssetPaths([]);
      expect(results).toEqual([]);
    });

    it('should handle array with one item', () => {
      const results = resolveAssetPaths(['single.png']);
      expect(results).toHaveLength(1);
      expect(results[0]).toContain('single.png');
    });
  });

  describe('checkAssetExists', () => {
    it('should return true when asset exists', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: true,
      } as Response);

      const result = await checkAssetExists('logo.svg');
      expect(result).toBe(true);
      expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining('logo.svg'), {
        method: 'HEAD',
      });
    });

    it('should return false when asset does not exist', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: false,
      } as Response);

      const result = await checkAssetExists('missing.png');
      expect(result).toBe(false);
    });

    it('should return false when fetch fails', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('Network error'));

      const result = await checkAssetExists('error.jpg');
      expect(result).toBe(false);
    });

    it('should handle network timeouts gracefully', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(
        () => new Promise((_, reject) => setTimeout(() => reject(new Error('Timeout')), 100))
      );

      const result = await checkAssetExists('timeout.png');
      expect(result).toBe(false);
    });
  });

  describe('getAssetMetadata', () => {
    it('should return metadata when asset exists', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: true,
        headers: {
          get: vi.fn((header: string) => {
            if (header === 'content-length') return '1024';
            if (header === 'content-type') return 'image/png';
            return null;
          }),
        },
      } as unknown as Response);

      const result = await getAssetMetadata('test.png');

      expect(result).not.toBeNull();
      expect(result?.size).toBe(1024);
      expect(result?.type).toBe('image/png');
      expect(result?.url).toContain('test.png');
    });

    it('should return null when asset does not exist', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: false,
      } as Response);

      const result = await getAssetMetadata('missing.png');
      expect(result).toBeNull();
    });

    it('should handle missing headers gracefully', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: true,
        headers: {
          get: vi.fn(() => null),
        },
      } as unknown as Response);

      const result = await getAssetMetadata('test.png');

      expect(result).not.toBeNull();
      expect(result?.size).toBe(0);
      expect(result?.type).toBe('unknown');
    });

    it('should return null when fetch fails', async () => {
      (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('Network error'));

      const result = await getAssetMetadata('error.png');
      expect(result).toBeNull();
    });
  });
});
