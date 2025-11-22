# Asset Loading and Image Preloading Guide

This document describes the asset loading utilities and image preloading capabilities added to fix graphical icon and image loading issues in Aura Video Studio.

## Overview

The new asset management system provides:

- **Consistent asset path resolution** across the application
- **Image preloading** for improved performance
- **Asset audit tools** to detect broken links
- **Enhanced Logo component** with lazy loading support

## Asset Path Resolution

### Basic Usage

```typescript
import { resolveAssetPath } from '@/utils/assetPath';

// Resolve a single asset path
const logoUrl = resolveAssetPath('logo.svg');
// Returns: "/logo.svg" in dev, "./logo.svg" in production

// Use in components
<img src={resolveAssetPath('icons/home.png')} alt="Home" />
```

### Multiple Assets

```typescript
import { resolveAssetPaths } from '@/utils/assetPath';

const assetPaths = ['logo.svg', 'icon.png', 'images/hero.jpg'];
const urls = resolveAssetPaths(assetPaths);
// Returns: ["/logo.svg", "/icon.png", "/images/hero.jpg"]
```

### Checking Asset Existence

```typescript
import { checkAssetExists } from '@/utils/assetPath';

const exists = await checkAssetExists('logo.svg');
if (exists) {
  console.log('Asset is available');
}
```

### Getting Asset Metadata

```typescript
import { getAssetMetadata } from '@/utils/assetPath';

const metadata = await getAssetMetadata('logo.svg');
if (metadata) {
  console.log(`Size: ${metadata.size} bytes, Type: ${metadata.type}`);
}
```

## Image Preloading

### Preload Critical Images at Startup

```typescript
import { preloadCriticalImages } from '@/utils/imagePreloader';

// In your main app initialization
preloadCriticalImages()
  .then((results) => {
    console.log(`Preloaded ${results.length} critical images`);
  })
  .catch((error) => {
    console.error('Failed to preload some images:', error);
  });
```

### Preload Specific Images

```typescript
import { preloadImage, preloadImages } from '@/utils/imagePreloader';

// Preload single image
const result = await preloadImage('hero.jpg', {
  onLoad: (src) => console.log(`Loaded: ${src}`),
  onError: (src, error) => console.error(`Failed: ${src}`, error),
  timeout: 30000, // 30 seconds
});

// Preload multiple images
const results = await preloadImages(['image1.jpg', 'image2.jpg', 'image3.jpg'], {
  onProgress: (loaded, total) => {
    console.log(`Progress: ${loaded}/${total}`);
  },
});
```

### Check Preload Status

```typescript
import { isImagePreloaded, getPreloadStatus } from '@/utils/imagePreloader';

// Quick check
if (isImagePreloaded('logo.svg')) {
  console.log('Image is ready to use');
}

// Detailed status
const status = getPreloadStatus('logo.svg');
if (status) {
  console.log(`Status: ${status.status}, Load time: ${status.loadTime}ms`);
}
```

### Get Preload Statistics

```typescript
import { getPreloadStats } from '@/utils/imagePreloader';

const stats = getPreloadStats();
console.log(`Total: ${stats.total}, Loaded: ${stats.loaded}, Failed: ${stats.failed}`);
console.log(`Average load time: ${stats.avgLoadTime.toFixed(2)}ms`);
```

## Enhanced Logo Component

### Basic Usage

```typescript
import { Logo } from '@/components/Logo';

// Default logo (64x64)
<Logo />

// Custom size
<Logo size={128} />

// With lazy loading
<Logo size={256} lazy={true} />

// Custom alt text
<Logo alt="Company Logo" size={48} />

// Custom className
<Logo className="header-logo" size={32} />
```

### Props

| Prop        | Type             | Default               | Description                        |
| ----------- | ---------------- | --------------------- | ---------------------------------- |
| `size`      | `number`         | `64`                  | Size of the logo in pixels         |
| `className` | `string`         | `undefined`           | Additional CSS class name          |
| `alt`       | `string`         | `'Aura Video Studio'` | Alt text for the logo image        |
| `lazy`      | `boolean`        | `false`               | Enable lazy loading                |
| `format`    | `'png' \| 'svg'` | `'png'`               | Image format (SVG support pending) |

### Size-Based Icon Selection

The Logo component automatically selects the most appropriate icon based on the requested size:

- ≤16px: `favicon-16x16.png`
- ≤32px: `favicon-32x32.png`
- ≤128px: `logo256.png`
- > 128px: `logo512.png`

## Asset Audit Script

### Running the Audit

```bash
# In Aura.Web directory
npm run audit:assets
```

### What It Checks

1. **Critical Assets**: Verifies that all critical assets (favicons, logos, etc.) exist
2. **Asset References**: Scans source files for asset references
3. **Broken Links**: Identifies asset references that point to non-existent files
4. **Public Directory**: Lists all available assets

### Audit Report Example

```
=== Checking Critical Assets ===

✓ favicon.ico (99.89 KB)
✓ favicon-16x16.png (0.64 KB)
✓ favicon-32x32.png (1.79 KB)
✓ logo256.png (68.69 KB)
✓ logo512.png (273.54 KB)
✓ vite.svg (1.46 KB)

=== Asset References ===

Total: 47
✓ Valid: 42
✗ Broken: 5
```

## Best Practices

### 1. Always Use Asset Path Resolution

❌ **DON'T:**

```typescript
<img src="/logo.svg" alt="Logo" />
```

✅ **DO:**

```typescript
import { resolveAssetPath } from '@/utils/assetPath';

<img src={resolveAssetPath('logo.svg')} alt="Logo" />
```

### 2. Preload Critical Images

For images that are essential to the initial user experience:

```typescript
import { preloadCriticalImages } from '@/utils/imagePreloader';

// In App.tsx or main entry point
useEffect(() => {
  preloadCriticalImages();
}, []);
```

### 3. Use Lazy Loading for Non-Critical Images

For images below the fold or in less frequently accessed areas:

```typescript
<Logo size={128} lazy={true} />
```

### 4. Run Asset Audit Regularly

Add to your CI/CD pipeline:

```yaml
# .github/workflows/build.yml
- name: Audit Assets
  run: npm run audit:assets
```

## Troubleshooting

### Images Not Loading

1. Check the console for errors
2. Run `npm run audit:assets` to identify broken references
3. Verify the asset exists in the `public/` directory
4. Ensure you're using `resolveAssetPath()` for all asset references

### Preloading Issues

```typescript
import { getPreloadStats } from '@/utils/imagePreloader';

// Debug preloading issues
const stats = getPreloadStats();
console.log('Preload stats:', stats);

if (stats.failed > 0) {
  console.warn(`${stats.failed} images failed to preload`);
}
```

### Build Issues with Assets

If assets are missing after build:

1. Ensure assets are in the `public/` directory
2. Check that `copyPublicDir: true` in `vite.config.ts`
3. Run `npm run postbuild` to verify build artifacts

## Integration with Existing Components

### LazyImage Component

The existing `LazyImage` component can work alongside the new utilities:

```typescript
import { LazyImage } from '@/components/common/LazyImage';
import { resolveAssetPath } from '@/utils/assetPath';

<LazyImage
  src={resolveAssetPath('images/hero.jpg')}
  alt="Hero Image"
  width={1200}
  height={600}
/>
```

### Asset Service

The `assetService` for backend asset management remains unchanged and works independently.

## Testing

### Unit Tests

Tests are included for all utilities:

```bash
# Run all tests
npm test

# Run specific tests
npm test -- src/utils/__tests__/assetPath.test.ts
npm test -- src/utils/__tests__/imagePreloader.test.ts
npm test -- src/components/Logo.test.tsx
```

### Manual Testing

1. Start the dev server: `npm run dev`
2. Check browser console for any asset loading errors
3. Verify all images load correctly
4. Test lazy loading by scrolling to below-the-fold content
5. Check Network tab to verify preloading behavior

## Performance Tips

1. **Preload only critical assets** - Don't preload everything
2. **Use lazy loading** for images below the fold
3. **Monitor preload statistics** to optimize asset loading strategy
4. **Run asset audit** to remove unused assets and reduce bundle size

## Migration Guide

### Updating Existing Components

If you have existing components using hardcoded asset paths:

```typescript
// Before
<img src="/logo.svg" alt="Logo" />

// After
import { resolveAssetPath } from '@/utils/assetPath';
<img src={resolveAssetPath('logo.svg')} alt="Logo" />
```

### Adding Preloading

```typescript
// Before
function MyComponent() {
  return <img src="/hero.jpg" alt="Hero" />;
}

// After
import { preloadImage } from '@/utils/imagePreloader';

function MyComponent() {
  useEffect(() => {
    preloadImage('hero.jpg');
  }, []);

  return <img src={resolveAssetPath('hero.jpg')} alt="Hero" />;
}
```

## Future Enhancements

- SVG format support in Logo component
- WebP/AVIF format support with fallbacks
- Service Worker integration for offline asset caching
- Progressive image loading with blur-up effect
- Asset optimization pipeline integration
