/**
 * Image Preloading Utility
 *
 * Implements image preloading strategies to improve user experience
 * by reducing loading times for critical assets.
 */

import { resolveAssetPath, resolveAssetPaths } from './assetPath';

/**
 * Status of a preloaded image
 */
export enum PreloadStatus {
  PENDING = 'pending',
  LOADING = 'loading',
  LOADED = 'loaded',
  FAILED = 'failed',
}

/**
 * Information about a preloaded image
 */
export interface PreloadedImage {
  src: string;
  status: PreloadStatus;
  error?: string;
  loadTime?: number;
}

/**
 * Options for preloading images
 */
export interface PreloadOptions {
  /**
   * Callback for when an image loads successfully
   */
  onLoad?: (src: string) => void;

  /**
   * Callback for when an image fails to load
   */
  onError?: (src: string, error: string) => void;

  /**
   * Callback for progress updates
   */
  onProgress?: (loaded: number, total: number) => void;

  /**
   * Maximum time to wait for an image to load (in milliseconds)
   */
  timeout?: number;
}

/**
 * Cache of preloaded images
 */
const preloadCache = new Map<string, PreloadedImage>();

/**
 * Preloads a single image
 * @param assetPath - Relative path to the image asset
 * @param options - Preload options
 * @returns Promise that resolves when image loads or fails
 */
export function preloadImage(
  assetPath: string,
  options: PreloadOptions = {}
): Promise<PreloadedImage> {
  const { onLoad, onError, timeout = 30000 } = options;
  const src = resolveAssetPath(assetPath);

  // Check cache first
  const cached = preloadCache.get(src);
  if (cached && cached.status === PreloadStatus.LOADED) {
    onLoad?.(src);
    return Promise.resolve(cached);
  }

  return new Promise((resolve, reject) => {
    const startTime = performance.now();
    const img = new Image();

    const imageInfo: PreloadedImage = {
      src,
      status: PreloadStatus.LOADING,
    };

    preloadCache.set(src, imageInfo);

    // Set up timeout
    const timeoutId = setTimeout(() => {
      imageInfo.status = PreloadStatus.FAILED;
      imageInfo.error = 'Timeout';
      preloadCache.set(src, imageInfo);

      const error = `Image load timeout: ${src}`;
      onError?.(src, error);
      reject(new Error(error));
    }, timeout);

    img.onload = () => {
      clearTimeout(timeoutId);
      const loadTime = performance.now() - startTime;

      imageInfo.status = PreloadStatus.LOADED;
      imageInfo.loadTime = loadTime;
      preloadCache.set(src, imageInfo);

      onLoad?.(src);
      resolve(imageInfo);
    };

    img.onerror = () => {
      clearTimeout(timeoutId);

      imageInfo.status = PreloadStatus.FAILED;
      imageInfo.error = 'Failed to load';
      preloadCache.set(src, imageInfo);

      const error = `Failed to load image: ${src}`;
      onError?.(src, error);
      reject(new Error(error));
    };

    img.src = src;
  });
}

/**
 * Preloads multiple images in parallel
 * @param assetPaths - Array of relative paths to image assets
 * @param options - Preload options
 * @returns Promise that resolves when all images load or fail
 */
export async function preloadImages(
  assetPaths: string[],
  options: PreloadOptions = {}
): Promise<PreloadedImage[]> {
  const { onProgress } = options;
  const urls = resolveAssetPaths(assetPaths);

  let loadedCount = 0;
  const total = urls.length;

  const promises = assetPaths.map(async (path) => {
    try {
      return await preloadImage(path, {
        ...options,
        onLoad: (src) => {
          loadedCount++;
          onProgress?.(loadedCount, total);
          options.onLoad?.(src);
        },
        onError: (src, error) => {
          loadedCount++;
          onProgress?.(loadedCount, total);
          options.onError?.(src, error);
        },
      });
    } catch (error: unknown) {
      return {
        src: resolveAssetPath(path),
        status: PreloadStatus.FAILED,
        error: error instanceof Error ? error.message : String(error),
      } as PreloadedImage;
    }
  });

  return Promise.all(promises);
}

/**
 * Preloads critical images at application startup
 * Critical images include logos, icons, and other frequently used assets
 */
export function preloadCriticalImages(): Promise<PreloadedImage[]> {
  const criticalAssets = ['favicon-16x16.png', 'favicon-32x32.png', 'logo256.png', 'logo512.png'];

  return preloadImages(criticalAssets, {
    onProgress: (loaded, total) => {
      console.info(`Preloading critical images: ${loaded}/${total}`);
    },
    onError: (src, error) => {
      console.warn(`Failed to preload critical image: ${src}`, error);
    },
  });
}

/**
 * Gets the preload status of an image
 * @param assetPath - Relative path to the image asset
 * @returns Preload information or null if not preloaded
 */
export function getPreloadStatus(assetPath: string): PreloadedImage | null {
  const src = resolveAssetPath(assetPath);
  return preloadCache.get(src) || null;
}

/**
 * Checks if an image is preloaded and ready
 * @param assetPath - Relative path to the image asset
 * @returns True if image is preloaded and loaded successfully
 */
export function isImagePreloaded(assetPath: string): boolean {
  const status = getPreloadStatus(assetPath);
  return status?.status === PreloadStatus.LOADED;
}

/**
 * Clears the preload cache
 */
export function clearPreloadCache(): void {
  preloadCache.clear();
}

/**
 * Gets statistics about preloaded images
 */
export function getPreloadStats(): {
  total: number;
  loaded: number;
  failed: number;
  pending: number;
  avgLoadTime: number;
} {
  let loaded = 0;
  let failed = 0;
  let pending = 0;
  let totalLoadTime = 0;
  let loadedCount = 0;

  preloadCache.forEach((info) => {
    switch (info.status) {
      case PreloadStatus.LOADED:
        loaded++;
        if (info.loadTime) {
          totalLoadTime += info.loadTime;
          loadedCount++;
        }
        break;
      case PreloadStatus.FAILED:
        failed++;
        break;
      case PreloadStatus.PENDING:
      case PreloadStatus.LOADING:
        pending++;
        break;
    }
  });

  return {
    total: preloadCache.size,
    loaded,
    failed,
    pending,
    avgLoadTime: loadedCount > 0 ? totalLoadTime / loadedCount : 0,
  };
}
