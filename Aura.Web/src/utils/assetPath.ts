/**
 * Asset Path Resolution Utility
 *
 * Provides standardized asset path resolution for the application.
 * Ensures consistent loading of images, icons, and other graphical assets.
 *
 * In Vite, assets in the public folder are served from the root (/) in dev mode
 * and from the base path (./) in production builds for Electron compatibility.
 */

/**
 * Resolves an asset path to a full URL
 * @param assetPath - Relative path to the asset (e.g., "logo.svg", "icons/home.png")
 * @returns Full URL to the asset
 *
 * @example
 * ```typescript
 * const logoUrl = resolveAssetPath('logo.svg');
 * // Returns: "/logo.svg" in dev, or "./logo.svg" in production
 *
 * const iconUrl = resolveAssetPath('icons/home.png');
 * // Returns: "/icons/home.png" in dev, or "./icons/home.png" in production
 * ```
 */
export function resolveAssetPath(assetPath: string): string {
  // Remove leading slash if present to normalize the path
  const normalizedPath = assetPath.startsWith('/') ? assetPath.slice(1) : assetPath;

  // In Vite, import.meta.env.BASE_URL is '/' in dev and './' in production (configured in vite.config.ts)
  const baseUrl = import.meta.env.BASE_URL;

  // Ensure we don't double up slashes
  if (baseUrl.endsWith('/')) {
    return `${baseUrl}${normalizedPath}`;
  }

  return `${baseUrl}/${normalizedPath}`;
}

/**
 * Resolves multiple asset paths at once
 * @param assetPaths - Array of relative paths to assets
 * @returns Array of resolved asset URLs
 *
 * @example
 * ```typescript
 * const urls = resolveAssetPaths(['logo.svg', 'icon.png']);
 * // Returns: ["/logo.svg", "/icon.png"] in dev
 * ```
 */
export function resolveAssetPaths(assetPaths: string[]): string[] {
  return assetPaths.map(resolveAssetPath);
}

/**
 * Checks if an asset exists by attempting to fetch it
 * @param assetPath - Relative path to the asset
 * @returns Promise that resolves to true if asset exists, false otherwise
 *
 * @example
 * ```typescript
 * const exists = await checkAssetExists('logo.svg');
 * if (exists) {
 *   console.log('Asset is available');
 * }
 * ```
 */
export async function checkAssetExists(assetPath: string): Promise<boolean> {
  try {
    const url = resolveAssetPath(assetPath);
    const response = await fetch(url, { method: 'HEAD' });
    return response.ok;
  } catch (error: unknown) {
    console.error(
      `Failed to check asset existence: ${assetPath}`,
      error instanceof Error ? error.message : String(error)
    );
    return false;
  }
}

/**
 * Gets asset metadata (size, type, etc.)
 * @param assetPath - Relative path to the asset
 * @returns Promise with asset metadata or null if asset doesn't exist
 *
 * @example
 * ```typescript
 * const metadata = await getAssetMetadata('logo.svg');
 * if (metadata) {
 *   console.log(`Size: ${metadata.size} bytes, Type: ${metadata.type}`);
 * }
 * ```
 */
export async function getAssetMetadata(
  assetPath: string
): Promise<{ url: string; size: number; type: string } | null> {
  try {
    const url = resolveAssetPath(assetPath);
    const response = await fetch(url, { method: 'HEAD' });

    if (!response.ok) {
      return null;
    }

    const size = parseInt(response.headers.get('content-length') || '0', 10);
    const type = response.headers.get('content-type') || 'unknown';

    return { url, size, type };
  } catch (error: unknown) {
    console.error(
      `Failed to get asset metadata: ${assetPath}`,
      error instanceof Error ? error.message : String(error)
    );
    return null;
  }
}
