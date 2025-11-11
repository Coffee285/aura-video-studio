/**
 * useVirtualizedList Hook
 * Custom hook for managing virtualized list state and behavior
 */

import { useState, useCallback, useMemo } from 'react';

export interface UseVirtualizedListOptions<T> {
  items: T[];
  itemHeight?: number;
  overscan?: number;
  initialScrollTop?: number;
}

export interface UseVirtualizedListResult<T> {
  items: T[];
  totalCount: number;
  scrollToIndex: (index: number) => void;
  scrollToTop: () => void;
  scrollToBottom: () => void;
}

/**
 * Hook for managing virtualized list behavior
 * Provides utilities for scrolling and item management
 */
export function useVirtualizedList<T>(
  options: UseVirtualizedListOptions<T>
): UseVirtualizedListResult<T> {
  const { items } = options;
  const [scrollToIndex, setScrollToIndex] = useState<number | null>(null);

  const totalCount = useMemo(() => items.length, [items]);

  const handleScrollToIndex = useCallback((index: number) => {
    if (index >= 0 && index < items.length) {
      setScrollToIndex(index);
    }
  }, [items.length]);

  const handleScrollToTop = useCallback(() => {
    handleScrollToIndex(0);
  }, [handleScrollToIndex]);

  const handleScrollToBottom = useCallback(() => {
    handleScrollToIndex(items.length - 1);
  }, [handleScrollToIndex, items.length]);

  return {
    items,
    totalCount,
    scrollToIndex: handleScrollToIndex,
    scrollToTop: handleScrollToTop,
    scrollToBottom: handleScrollToBottom,
  };
}

export interface UseInfiniteScrollOptions {
  hasMore: boolean;
  loading: boolean;
  onLoadMore: () => void | Promise<void>;
  threshold?: number;
}

/**
 * Hook for implementing infinite scroll functionality
 * Works with virtualized lists to load more items on scroll
 */
export function useInfiniteScroll({
  hasMore,
  loading,
  onLoadMore,
  threshold = 0.8,
}: UseInfiniteScrollOptions) {
  const handleEndReached = useCallback(() => {
    if (hasMore && !loading) {
      onLoadMore();
    }
  }, [hasMore, loading, onLoadMore]);

  return {
    endReached: handleEndReached,
    hasMore,
    loading,
    threshold,
  };
}
