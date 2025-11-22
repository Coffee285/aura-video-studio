import { makeStyles } from '@fluentui/react-components';
import { memo } from 'react';
import { resolveAssetPath } from '../utils/assetPath';

const useStyles = makeStyles({
  logo: {
    display: 'inline-block',
  },
  image: {
    display: 'block',
    width: '100%',
    height: '100%',
    objectFit: 'contain',
  },
});

export interface LogoProps {
  /**
   * Size of the logo in pixels
   */
  size?: number;
  /**
   * Additional CSS class name
   */
  className?: string;
  /**
   * Alt text for the logo image
   */
  alt?: string;
  /**
   * Enable lazy loading for the logo image
   */
  lazy?: boolean;
  /**
   * Support for both SVG and PNG formats
   * @default 'png'
   */
  format?: 'png' | 'svg';
}

/**
 * Logo component that displays the Aura Video Studio icon.
 * Uses the actual icon image from the public folder with proper asset path resolution.
 * Supports lazy loading for performance optimization.
 */
export const Logo = memo<LogoProps>(
  ({ size = 64, className, alt = 'Aura Video Studio', lazy = false, format = 'png' }) => {
    const styles = useStyles();

    // Use the appropriate size based on requested size
    // We have: 16x16, 32x32, 64x64, 128x128, 256x256, 512x512
    const getIconPath = (requestedSize: number, _fmt: 'png' | 'svg'): string => {
      // Note: Currently only PNG format is available
      // SVG support can be added when SVG logo assets are available
      if (requestedSize <= 16) return 'favicon-16x16.png';
      if (requestedSize <= 32) return 'favicon-32x32.png';
      if (requestedSize <= 128) return 'logo256.png';
      return 'logo512.png';
    };

    const iconPath = getIconPath(size, format);
    const resolvedPath = resolveAssetPath(iconPath);

    return (
      <span className={className} style={{ width: size, height: size }}>
        <img
          src={resolvedPath}
          alt={alt}
          className={styles.image}
          width={size}
          height={size}
          draggable={false}
          loading={lazy ? 'lazy' : 'eager'}
          decoding="async"
        />
      </span>
    );
  }
);

Logo.displayName = 'Logo';
