# React Performance Optimization Guide

This guide documents the performance optimizations implemented in the Aura Video Studio frontend.

## Table of Contents

- [Overview](#overview)
- [Performance Utilities](#performance-utilities)
- [Memoization](#memoization)
- [Virtualization](#virtualization)
- [Async State Management](#async-state-management)
- [Code Splitting](#code-splitting)
- [Best Practices](#best-practices)

## Overview

The React frontend has been optimized for better performance through:

1. **React.memo** for preventing unnecessary re-renders
2. **Virtualization** for efficiently rendering large lists
3. **Code splitting** with lazy loading
4. **Consistent async state handling**
5. **Enhanced error boundaries**

## Performance Utilities

Location: `src/utils/performance.ts`

### useWhyDidYouUpdate

A development-only hook that logs prop changes causing component re-renders.

```typescript
import { useWhyDidYouUpdate } from '@/utils/performance';

function MyComponent(props) {
  useWhyDidYouUpdate('MyComponent', props);
  // Component logic...
}
```

### Comparison Functions

```typescript
import { shallowEqual, deepEqual, arePropsEqual } from '@/utils/performance';

// Shallow comparison for React.memo
const MemoizedComponent = memo(MyComponent, (prevProps, nextProps) => {
  return shallowEqual(prevProps, nextProps);
});

// Deep comparison for specific props
const MemoizedComponent = memo(MyComponent, (prevProps, nextProps) => {
  return arePropsEqual(prevProps, nextProps, ['complexData', 'nestedObject']);
});
```

### Debounce and Throttle

```typescript
import { debounce, throttle } from '@/utils/performance';

// Debounce user input
const handleSearch = debounce((query: string) => {
  // Search logic
}, 300);

// Throttle scroll events
const handleScroll = throttle(() => {
  // Scroll logic
}, 100);
```

### Performance Measurement

```typescript
import { measurePerformance } from '@/utils/performance';

// In development mode, logs execution time
const result = measurePerformance('expensiveOperation', () => {
  // Your expensive operation
  return processData();
});
```

## Memoization

### Component Memoization

Use `React.memo` for components that receive stable props and render the same output for the same inputs.

```typescript
import { memo, useCallback, useMemo } from 'react';

interface MyComponentProps {
  data: DataItem[];
  onItemClick: (id: string) => void;
}

const MyComponentInternal = ({ data, onItemClick }: MyComponentProps) => {
  // Component logic
  return <div>{/* Render logic */}</div>;
};

export const MyComponent = memo(MyComponentInternal);
```

### Hook Memoization

Use `useCallback` for stable function references and `useMemo` for expensive computations.

```typescript
function ParentComponent() {
  const [items, setItems] = useState<Item[]>([]);

  // useCallback for stable function reference
  const handleItemClick = useCallback((id: string) => {
    console.log('Item clicked:', id);
  }, []); // Dependencies array

  // useMemo for expensive computations
  const filteredItems = useMemo(
    () => items.filter(item => item.active),
    [items]
  );

  return <ChildComponent items={filteredItems} onItemClick={handleItemClick} />;
}
```

### Examples in Codebase

- **JobQueuePanel** (`src/components/JobQueue/JobQueuePanel.tsx`)
  - Memoized with React.memo
  - Uses useCallback for event handlers
  - Uses useMemo for computed values

## Virtualization

### react-virtuoso

For rendering large lists efficiently, we use `react-virtuoso` which only renders visible items.

Location: `src/pages/LogViewerPage.tsx`

```typescript
import { Virtuoso } from 'react-virtuoso';
import { memo } from 'react';

// Memoized list item component
const LogEntryCard = memo(({ log, onCopy }) => {
  return <div onClick={() => onCopy(log)}>{log.message}</div>;
});

function LogViewerPage() {
  const [logs, setLogs] = useState<LogEntry[]>([]);

  return (
    <Virtuoso
      style={{ height: '600px' }}
      data={logs}
      itemContent={(index, log) => (
        <LogEntryCard key={log.id} log={log} onCopy={handleCopy} />
      )}
    />
  );
}
```

### Custom Hook: useVirtualizedList

Location: `src/hooks/useVirtualizedList.ts`

```typescript
import { useVirtualizedList } from '@/hooks/useVirtualizedList';

function MyListComponent() {
  const items = useSelector(state => state.items);
  
  const {
    totalCount,
    scrollToIndex,
    scrollToTop,
    scrollToBottom,
  } = useVirtualizedList({ items });

  return (
    <>
      <button onClick={scrollToTop}>Scroll to Top</button>
      <Virtuoso data={items} itemContent={(index, item) => (
        <ListItem item={item} />
      )} />
    </>
  );
}
```

### Infinite Scroll

```typescript
import { useInfiniteScroll } from '@/hooks/useVirtualizedList';

function InfiniteList() {
  const [items, setItems] = useState<Item[]>([]);
  const [hasMore, setHasMore] = useState(true);
  const [loading, setLoading] = useState(false);

  const { endReached } = useInfiniteScroll({
    hasMore,
    loading,
    onLoadMore: async () => {
      setLoading(true);
      const newItems = await fetchMoreItems();
      setItems([...items, ...newItems]);
      setHasMore(newItems.length > 0);
      setLoading(false);
    },
  });

  return (
    <Virtuoso
      data={items}
      endReached={endReached}
      itemContent={(index, item) => <Item data={item} />}
    />
  );
}
```

## Async State Management

### AsyncStateWrapper

Location: `src/components/Common/AsyncStateWrapper.tsx`

A wrapper component that handles loading, error, and empty states consistently.

```typescript
import { AsyncStateWrapper } from '@/components/Common/AsyncStateWrapper';

function MyDataComponent() {
  const { data, loading, error } = useFetchData();

  return (
    <AsyncStateWrapper
      loading={loading}
      error={error}
      isEmpty={!data || data.length === 0}
      loadingMessage="Fetching data..."
      emptyMessage="No data available"
      emptyIcon={<DataIcon />}
      onRetry={refetch}
    >
      <DataTable data={data} />
    </AsyncStateWrapper>
  );
}
```

### SuspenseBoundary

Location: `src/components/Common/SuspenseBoundary.tsx`

Combines React Suspense with ErrorBoundary for robust lazy component loading.

```typescript
import { SuspenseBoundary } from '@/components/Common/SuspenseBoundary';
import { lazy } from 'react';

const LazyComponent = lazy(() => import('./LazyComponent'));

function App() {
  return (
    <SuspenseBoundary loadingMessage="Loading component...">
      <LazyComponent />
    </SuspenseBoundary>
  );
}
```

## Code Splitting

### Route-Based Splitting

All non-critical pages are lazy-loaded in `src/App.tsx`:

```typescript
import { lazy } from 'react';
import { SuspenseBoundary } from './components/Common/SuspenseBoundary';

// Lazy load heavy pages
const AdminDashboard = lazy(() => import('./pages/Admin/AdminDashboard'));
const ProjectsPage = lazy(() => import('./pages/Projects/ProjectsPage'));

function App() {
  return (
    <Routes>
      <Route path="/admin" element={
        <SuspenseBoundary>
          <AdminDashboard />
        </SuspenseBoundary>
      } />
    </Routes>
  );
}
```

### Bundle Splitting

The Vite configuration (`vite.config.ts`) splits the bundle into optimized chunks:

- **react-vendor**: React core libraries
- **fluentui-components**: Fluent UI components
- **fluentui-icons**: Fluent UI icons
- **ffmpeg-vendor**: FFmpeg library
- **audio-vendor**: Audio processing libraries
- **router-vendor**: React Router
- **state-vendor**: State management (Zustand, React Query)
- **vendor**: Other third-party libraries

## Best Practices

### When to Use React.memo

✅ **Use React.memo when:**
- Component receives stable props
- Component renders the same output for the same inputs
- Component is rendered frequently with same props
- Component is expensive to render

❌ **Avoid React.memo when:**
- Props change frequently
- Component is already fast
- Component has no performance issues

### When to Use Virtualization

✅ **Use virtualization for:**
- Lists with 100+ items
- Tables with many rows
- Long scrollable content
- Real-time data feeds (logs, chat messages)

❌ **Avoid virtualization for:**
- Small lists (< 50 items)
- Fixed-height content
- When all items need to be in DOM (for SEO, printing)

### When to Use Code Splitting

✅ **Split code for:**
- Routes/pages
- Heavy third-party libraries
- Admin panels and rarely-used features
- Different user roles (admin vs user)

❌ **Avoid splitting:**
- Critical path components
- Small components (< 10KB)
- Frequently used utilities

### Optimization Checklist

Before optimizing a component, ask:

1. **Is there an actual performance problem?**
   - Profile with React DevTools
   - Measure with Performance API
   - Check user complaints

2. **What's causing the slowness?**
   - Re-renders?
   - Expensive computations?
   - Large lists?
   - Bundle size?

3. **What's the simplest fix?**
   - Start with React.memo
   - Add useMemo/useCallback
   - Consider virtualization
   - Split code if needed

4. **Did the optimization help?**
   - Measure before and after
   - Verify with profiling tools
   - Check bundle size impact

## Performance Monitoring

### Development Tools

1. **React DevTools Profiler**
   - Identifies slow components
   - Shows re-render counts
   - Measures render time

2. **Browser Performance Tab**
   - Overall page performance
   - JavaScript execution time
   - Paint and layout timing

3. **Lighthouse**
   - Performance score
   - Bundle size analysis
   - Best practices

### Key Metrics

- **Time to Interactive (TTI)**: < 3.8s
- **First Contentful Paint (FCP)**: < 1.8s
- **Largest Contentful Paint (LCP)**: < 2.5s
- **Total Bundle Size**: < 1.5MB
- **Initial Load Size**: < 300KB

## Examples

### Complete Optimized Component

```typescript
import { memo, useCallback, useMemo } from 'react';
import { Virtuoso } from 'react-virtuoso';
import { AsyncStateWrapper } from '@/components/Common/AsyncStateWrapper';

interface OptimizedListProps {
  items: Item[];
  loading: boolean;
  error: Error | null;
  onItemClick: (id: string) => void;
}

// Memoized list item
const ListItemCard = memo(({ item, onClick }: { item: Item; onClick: (id: string) => void }) => {
  const handleClick = useCallback(() => onClick(item.id), [item.id, onClick]);
  
  return (
    <div onClick={handleClick}>
      <h3>{item.title}</h3>
      <p>{item.description}</p>
    </div>
  );
});

// Main component
const OptimizedListInternal = ({ items, loading, error, onItemClick }: OptimizedListProps) => {
  // Memoize computed values
  const sortedItems = useMemo(
    () => [...items].sort((a, b) => a.title.localeCompare(b.title)),
    [items]
  );

  return (
    <AsyncStateWrapper
      loading={loading}
      error={error}
      isEmpty={sortedItems.length === 0}
      emptyMessage="No items found"
    >
      <Virtuoso
        style={{ height: '600px' }}
        data={sortedItems}
        itemContent={(index, item) => (
          <ListItemCard key={item.id} item={item} onClick={onItemClick} />
        )}
      />
    </AsyncStateWrapper>
  );
};

export const OptimizedList = memo(OptimizedListInternal);
```

## Testing Performance

### Unit Tests

```typescript
import { render } from '@testing-library/react';
import { performance } from '@/utils/performance';

it('should render efficiently', () => {
  const renderTime = measurePerformance('ComponentRender', () => {
    render(<MyComponent />);
  });
  
  expect(renderTime).toBeLessThan(100); // 100ms threshold
});
```

### Integration Tests

```typescript
import { renderHook } from '@testing-library/react';
import { useVirtualizedList } from '@/hooks/useVirtualizedList';

it('should handle large datasets efficiently', () => {
  const largeDataset = Array.from({ length: 10000 }, (_, i) => ({ id: i }));
  
  const { result } = renderHook(() => 
    useVirtualizedList({ items: largeDataset })
  );
  
  expect(result.current.totalCount).toBe(10000);
});
```

## Resources

- [React Documentation - Optimizing Performance](https://react.dev/learn/render-and-commit#optimizing-performance)
- [React DevTools Profiler](https://react.dev/learn/react-developer-tools)
- [react-virtuoso Documentation](https://virtuoso.dev/)
- [Web Vitals](https://web.dev/vitals/)
