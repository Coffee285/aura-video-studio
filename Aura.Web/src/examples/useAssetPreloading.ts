/**
 * Asset Preloading Hooks
 *
 * Custom hooks for integrating asset preloading into your application.
 */

import { useEffect } from 'react';
import { loggingService } from '@/services/loggingService';
import { getPreloadStats, preloadCriticalImages } from '@/utils/imagePreloader';

/**
 * Custom hook to preload critical assets on app initialization
 *
 * Usage in App.tsx or main layout component:
 *
 * ```typescript
 * import { useAssetPreloading } from '@/hooks/useAssetPreloading';
 *
 * function App() {
 *   useAssetPreloading();
 *
 *   return (
 *     // Your app content
 *   );
 * }
 * ```
 */
export function useAssetPreloading() {
  useEffect(() => {
    let mounted = true;

    async function preloadAssets() {
      try {
        loggingService.info('Starting critical asset preloading', 'assetPreloading');

        const results = await preloadCriticalImages();

        if (!mounted) return;

        const stats = getPreloadStats();
        loggingService.info(
          `Asset preloading complete: ${stats.loaded}/${stats.total} loaded, ${stats.failed} failed`,
          'assetPreloading',
          'preloadComplete',
          {
            stats,
            results,
          }
        );

        if (stats.failed > 0) {
          loggingService.warn(
            `Some assets failed to preload: ${stats.failed}/${stats.total}`,
            'assetPreloading',
            'preloadWarning',
            { failedAssets: results.filter((r) => r.status === 'failed') }
          );
        }
      } catch (error: unknown) {
        if (!mounted) return;

        const errorObj = error instanceof Error ? error : new Error(String(error));
        loggingService.error(
          'Asset preloading failed',
          errorObj,
          'assetPreloading',
          'preloadError'
        );
      }
    }

    void preloadAssets();

    return () => {
      mounted = false;
    };
  }, []);
}

/**
 * Example: Preloading specific assets for a feature
 *
 * Usage in a feature component:
 *
 * ```typescript
 * import { useFeatureAssetPreloading } from '@/hooks/useAssetPreloading';
 *
 * function VideoEditorPage() {
 *   useFeatureAssetPreloading(['timeline/cursor.png', 'timeline/marker.png']);
 *
 *   return (
 *     // Your editor UI
 *   );
 * }
 * ```
 */
export function useFeatureAssetPreloading(assets: string[]) {
  useEffect(() => {
    let mounted = true;

    async function preloadFeatureAssets() {
      try {
        const { preloadImages } = await import('@/utils/imagePreloader');

        const results = await preloadImages(assets, {
          onProgress: (loaded, total) => {
            if (mounted) {
              loggingService.info(
                `Feature asset preloading: ${loaded}/${total}`,
                'featureAssetPreloading'
              );
            }
          },
        });

        if (mounted) {
          const successful = results.filter((r) => r.status === 'loaded').length;
          loggingService.info(
            `Feature assets loaded: ${successful}/${assets.length}`,
            'featureAssetPreloading',
            'complete',
            { results }
          );
        }
      } catch (error: unknown) {
        if (mounted) {
          const errorObj = error instanceof Error ? error : new Error(String(error));
          loggingService.error(
            'Feature asset preloading failed',
            errorObj,
            'featureAssetPreloading',
            'error'
          );
        }
      }
    }

    void preloadFeatureAssets();

    return () => {
      mounted = false;
    };
  }, [assets]);
}
