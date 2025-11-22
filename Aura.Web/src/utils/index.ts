/**
 * Utility Functions Index
 *
 * Central exports for commonly used utility functions
 */

// Focus Management
export {
  getFocusableElements,
  getFirstFocusableElement,
  getLastFocusableElement,
  FocusTrap,
  createFocusTrap,
  restoreFocus,
  focusNext,
  focusPrevious,
  isFocusable,
} from './focusManagement';

// Keybinding Utilities
export {
  isAppleDevice,
  isTypableElement,
  generateKeybindingString,
  formatShortcutForDisplay,
  getCategoryDescription,
} from './keybinding-utils';

// Asset Path Resolution
export {
  resolveAssetPath,
  resolveAssetPaths,
  checkAssetExists,
  getAssetMetadata,
} from './assetPath';

// Image Preloading
export {
  preloadImage,
  preloadImages,
  preloadCriticalImages,
  getPreloadStatus,
  isImagePreloaded,
  clearPreloadCache,
  getPreloadStats,
  PreloadStatus,
  type PreloadedImage,
  type PreloadOptions,
} from './imagePreloader';
