/**
 * Tests for Logo component
 */

import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { Logo } from './Logo';

describe('Logo', () => {
  it('should render logo with default props', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute('width', '64');
    expect(img).toHaveAttribute('height', '64');
  });

  it('should render logo with custom size', () => {
    render(<Logo size={128} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toHaveAttribute('width', '128');
    expect(img).toHaveAttribute('height', '128');
  });

  it('should render logo with custom alt text', () => {
    const customAlt = 'Custom Logo';
    render(<Logo alt={customAlt} />);
    const img = screen.getByAltText(customAlt);
    expect(img).toBeInTheDocument();
  });

  it('should apply custom className to wrapper span', () => {
    const { container } = render(<Logo className="custom-logo" />);
    const span = container.querySelector('span');
    expect(span).toHaveClass('custom-logo');
  });

  it('should use appropriate icon size for small logo (16px)', () => {
    render(<Logo size={16} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toContain('favicon-16x16.png');
  });

  it('should use appropriate icon size for medium logo (32px)', () => {
    render(<Logo size={32} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toContain('favicon-32x32.png');
  });

  it('should use appropriate icon size for large logo (128px)', () => {
    render(<Logo size={128} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toContain('logo256.png');
  });

  it('should use appropriate icon size for extra large logo (256px)', () => {
    render(<Logo size={256} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toContain('logo512.png');
  });

  it('should set draggable to false', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toHaveAttribute('draggable', 'false');
  });

  it('should enable lazy loading when lazy prop is true', () => {
    render(<Logo lazy={true} />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toHaveAttribute('loading', 'lazy');
  });

  it('should use eager loading by default', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toHaveAttribute('loading', 'eager');
  });

  it('should set decoding to async', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img).toHaveAttribute('decoding', 'async');
  });

  it('should use PNG format by default', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toMatch(/\.png$/);
  });

  it('should accept format prop (future SVG support)', () => {
    render(<Logo format="png" />);
    const img = screen.getByAltText('Aura Video Studio');
    expect(img.getAttribute('src')).toMatch(/\.png$/);
  });

  it('should set container dimensions based on size prop', () => {
    const size = 100;
    const { container } = render(<Logo size={size} />);
    const span = container.querySelector('span');
    const style = span?.getAttribute('style');
    expect(style).toContain(`width: ${size}px`);
    expect(style).toContain(`height: ${size}px`);
  });

  it('should resolve asset path correctly', () => {
    render(<Logo />);
    const img = screen.getByAltText('Aura Video Studio');
    const src = img.getAttribute('src');
    // Should not have doubled slashes
    expect(src).not.toMatch(/\/\//);
    // Should contain the filename
    expect(src).toMatch(/logo256\.png$/);
  });
});
