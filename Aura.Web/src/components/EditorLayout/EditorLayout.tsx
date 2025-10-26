import { makeStyles, tokens } from '@fluentui/react-components';
import { ReactNode, useState, useEffect } from 'react';
import { snapToBreakpoint } from '../../services/workspaceLayoutService';
import { TopMenuBar } from '../Layout/TopMenuBar';
import { MenuBar } from '../MenuBar/MenuBar';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    overflow: 'hidden',
    backgroundColor: 'var(--color-background)',
  },
  content: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden',
  },
  mainArea: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    overflow: 'hidden',
  },
  previewPanel: {
    flex: 6,
    minHeight: '300px',
    display: 'flex',
    flexDirection: 'column',
    borderBottom: `1px solid var(--panel-border, ${tokens.colorNeutralStroke1})`,
    backgroundColor: 'var(--panel-bg, ${tokens.colorNeutralBackground3})',
    overflow: 'hidden',
    transition: 'flex var(--transition-panel)',
  },
  timelinePanel: {
    flex: 4,
    minHeight: '200px',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: 'var(--panel-bg, ${tokens.colorNeutralBackground2})',
    overflow: 'hidden',
    transition: 'flex var(--transition-panel)',
  },
  propertiesPanel: {
    width: '320px',
    minWidth: '280px',
    maxWidth: '400px',
    borderLeft: `1px solid var(--panel-border, ${tokens.colorNeutralStroke1})`,
    backgroundColor: 'var(--panel-bg, ${tokens.colorNeutralBackground2})',
    overflow: 'auto',
    display: 'flex',
    flexDirection: 'column',
    transition: 'width var(--transition-panel)',
  },
  mediaLibraryPanel: {
    width: '280px',
    minWidth: '240px',
    maxWidth: '350px',
    borderRight: `1px solid var(--panel-border, ${tokens.colorNeutralStroke1})`,
    backgroundColor: 'var(--panel-bg, ${tokens.colorNeutralBackground2})',
    overflow: 'hidden',
    display: 'flex',
    flexDirection: 'column',
    transition: 'width var(--transition-panel)',
  },
  effectsLibraryPanel: {
    width: '280px',
    minWidth: '240px',
    maxWidth: '350px',
    borderRight: `1px solid var(--panel-border, ${tokens.colorNeutralStroke1})`,
    backgroundColor: 'var(--panel-bg, ${tokens.colorNeutralBackground2})',
    overflow: 'hidden',
    display: 'flex',
    flexDirection: 'column',
    transition: 'width var(--transition-panel)',
  },
  resizer: {
    width: '4px',
    cursor: 'ew-resize',
    backgroundColor: 'transparent',
    position: 'relative',
    transition: 'background-color var(--transition-fast)',
    '&:hover': {
      backgroundColor: 'var(--color-primary)',
    },
    '&:active': {
      backgroundColor: 'var(--color-primary)',
    },
    '&:focus': {
      outline: `2px solid var(--color-primary)`,
      outlineOffset: '2px',
    },
  },
  resizerDragging: {
    backgroundColor: 'var(--color-primary)',
  },
  horizontalResizer: {
    height: '4px',
    cursor: 'ns-resize',
    backgroundColor: 'transparent',
    position: 'relative',
    transition: 'background-color var(--transition-fast)',
    '&:hover': {
      backgroundColor: 'var(--color-primary)',
    },
    '&:active': {
      backgroundColor: 'var(--color-primary)',
    },
    '&:focus': {
      outline: `2px solid var(--color-primary)`,
      outlineOffset: '2px',
    },
  },
  horizontalResizerDragging: {
    backgroundColor: 'var(--color-primary)',
  },
});

interface EditorLayoutProps {
  preview?: ReactNode;
  timeline?: ReactNode;
  properties?: ReactNode;
  mediaLibrary?: ReactNode;
  effects?: ReactNode;
  history?: ReactNode;
  onImportMedia?: () => void;
  onExportVideo?: () => void;
  onShowKeyboardShortcuts?: () => void;
  onSaveProject?: () => void;
  projectName?: string | null;
  isDirty?: boolean;
  autosaveStatus?: 'idle' | 'saving' | 'saved' | 'error';
  lastSaved?: Date | null;
  useTopMenuBar?: boolean; // New prop to use professional top menu bar
}

// LocalStorage keys for panel sizes
const STORAGE_KEYS = {
  propertiesWidth: 'editor-properties-width',
  mediaLibraryWidth: 'editor-media-library-width',
  effectsLibraryWidth: 'editor-effects-library-width',
  historyWidth: 'editor-history-width',
  previewHeight: 'editor-preview-height',
};

// Helper to load from localStorage with default fallback
const loadPanelSize = (key: string, defaultValue: number): number => {
  try {
    const stored = localStorage.getItem(key);
    return stored ? parseFloat(stored) : defaultValue;
  } catch {
    return defaultValue;
  }
};

// Helper to save to localStorage
const savePanelSize = (key: string, value: number): void => {
  try {
    localStorage.setItem(key, value.toString());
  } catch {
    // Ignore localStorage errors
  }
};

export function EditorLayout({
  preview,
  timeline,
  properties,
  mediaLibrary,
  effects,
  history,
  onImportMedia,
  onExportVideo,
  onShowKeyboardShortcuts,
  onSaveProject,
  projectName,
  isDirty,
  autosaveStatus = 'idle',
  lastSaved,
  useTopMenuBar = false,
}: EditorLayoutProps) {
  const styles = useStyles();
  const [propertiesWidth, setPropertiesWidth] = useState(() =>
    loadPanelSize(STORAGE_KEYS.propertiesWidth, 320)
  );
  const [mediaLibraryWidth, setMediaLibraryWidth] = useState(() =>
    loadPanelSize(STORAGE_KEYS.mediaLibraryWidth, 280)
  );
  const [effectsLibraryWidth, setEffectsLibraryWidth] = useState(() =>
    loadPanelSize(STORAGE_KEYS.effectsLibraryWidth, 280)
  );
  const [historyWidth, setHistoryWidth] = useState(() =>
    loadPanelSize(STORAGE_KEYS.historyWidth, 320)
  );
  const [previewHeight, setPreviewHeight] = useState(() =>
    loadPanelSize(STORAGE_KEYS.previewHeight, 60)
  ); // Percentage

  // Track dragging state for visual feedback
  const [isDraggingHorizontal, setIsDraggingHorizontal] = useState(false);
  const [isDraggingVertical, setIsDraggingVertical] = useState(false);

  // Persist panel sizes to localStorage
  useEffect(() => {
    savePanelSize(STORAGE_KEYS.propertiesWidth, propertiesWidth);
  }, [propertiesWidth]);

  useEffect(() => {
    savePanelSize(STORAGE_KEYS.mediaLibraryWidth, mediaLibraryWidth);
  }, [mediaLibraryWidth]);

  useEffect(() => {
    savePanelSize(STORAGE_KEYS.previewHeight, previewHeight);
  }, [previewHeight]);

  useEffect(() => {
    savePanelSize(STORAGE_KEYS.effectsLibraryWidth, effectsLibraryWidth);
  }, [effectsLibraryWidth]);

  useEffect(() => {
    savePanelSize(STORAGE_KEYS.historyWidth, historyWidth);
  }, [historyWidth]);

  // Handle resizing properties panel
  const handlePropertiesResize = (e: React.MouseEvent) => {
    const startX = e.clientX;
    const startWidth = propertiesWidth;
    setIsDraggingHorizontal(true);

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = startX - moveEvent.clientX;
      let newWidth = Math.max(280, Math.min(400, startWidth + delta));
      // Apply snap-to-breakpoint
      newWidth = snapToBreakpoint(newWidth, 280, 400);
      setPropertiesWidth(newWidth);
    };

    const handleMouseUp = () => {
      setIsDraggingHorizontal(false);
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  // Handle resizing preview panel
  const handlePreviewResize = (e: React.MouseEvent) => {
    const container = (e.target as HTMLElement).parentElement?.parentElement;
    if (!container) return;

    const startY = e.clientY;
    const containerHeight = container.clientHeight;
    const startHeight = previewHeight;
    setIsDraggingVertical(true);

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientY - startY;
      const deltaPercent = (delta / containerHeight) * 100;
      let newHeight = Math.max(40, Math.min(80, startHeight + deltaPercent));
      // Apply snap-to-breakpoint for percentage values
      const snapPoints = [40, 50, 60, 66, 70, 75, 80];
      const threshold = 3; // 3% threshold
      for (const point of snapPoints) {
        if (Math.abs(newHeight - point) < threshold) {
          newHeight = point;
          break;
        }
      }
      setPreviewHeight(newHeight);
    };

    const handleMouseUp = () => {
      setIsDraggingVertical(false);
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  // Handle resizing media library panel
  const handleMediaLibraryResize = (e: React.MouseEvent) => {
    const startX = e.clientX;
    const startWidth = mediaLibraryWidth;
    setIsDraggingHorizontal(true);

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientX - startX;
      let newWidth = Math.max(240, Math.min(350, startWidth + delta));
      // Apply snap-to-breakpoint
      newWidth = snapToBreakpoint(newWidth, 240, 350);
      setMediaLibraryWidth(newWidth);
    };

    // Identical cleanup logic is acceptable for drag handlers
    // eslint-disable-next-line sonarjs/no-identical-functions
    const handleMouseUp = () => {
      setIsDraggingHorizontal(false);
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  // Handle resizing effects library panel
  const handleEffectsLibraryResize = (e: React.MouseEvent) => {
    const startX = e.clientX;
    const startWidth = effectsLibraryWidth;
    setIsDraggingHorizontal(true);

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientX - startX;
      let newWidth = Math.max(240, Math.min(350, startWidth + delta));
      // Apply snap-to-breakpoint
      newWidth = snapToBreakpoint(newWidth, 240, 350);
      setEffectsLibraryWidth(newWidth);
    };

    // Identical cleanup logic is acceptable for drag handlers
    // eslint-disable-next-line sonarjs/no-identical-functions
    const handleMouseUp = () => {
      setIsDraggingHorizontal(false);
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  // Handle resizing history panel
  const handleHistoryResize = (e: React.MouseEvent) => {
    const startX = e.clientX;
    const startWidth = historyWidth;
    setIsDraggingHorizontal(true);

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = startX - moveEvent.clientX;
      let newWidth = Math.max(280, Math.min(400, startWidth + delta));
      // Apply snap-to-breakpoint
      newWidth = snapToBreakpoint(newWidth, 280, 400);
      setHistoryWidth(newWidth);
    };

    // Identical cleanup logic is acceptable for drag handlers
    // eslint-disable-next-line sonarjs/no-identical-functions
    const handleMouseUp = () => {
      setIsDraggingHorizontal(false);
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
  };

  return (
    <div className={styles.container}>
      {useTopMenuBar ? (
        <TopMenuBar
          onImportMedia={onImportMedia}
          onExportVideo={onExportVideo}
          onSaveProject={onSaveProject}
          onShowKeyboardShortcuts={onShowKeyboardShortcuts}
        />
      ) : (
        <MenuBar
          onImportMedia={onImportMedia}
          onExportVideo={onExportVideo}
          onShowKeyboardShortcuts={onShowKeyboardShortcuts}
          onSaveProject={onSaveProject}
          projectName={projectName}
          isDirty={isDirty}
          autosaveStatus={autosaveStatus}
          lastSaved={lastSaved}
        />
      )}
      <div className={styles.content}>
        {mediaLibrary && (
          <>
            <div className={styles.mediaLibraryPanel} style={{ width: `${mediaLibraryWidth}px` }}>
              {mediaLibrary}
            </div>
            {/* Interactive resizer - intentionally uses mouse and keyboard events */}
            {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
            <div
              className={`${styles.resizer} ${isDraggingHorizontal ? styles.resizerDragging : ''}`}
              onMouseDown={handleMediaLibraryResize}
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize media library panel"
              // Separator role is interactive and requires tabIndex for keyboard accessibility
              // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'ArrowLeft') {
                  e.preventDefault();
                  setMediaLibraryWidth((prev) => Math.max(240, prev - 10));
                  savePanelSize(
                    STORAGE_KEYS.mediaLibraryWidth,
                    Math.max(240, mediaLibraryWidth - 10)
                  );
                } else if (e.key === 'ArrowRight') {
                  e.preventDefault();
                  setMediaLibraryWidth((prev) => Math.min(350, prev + 10));
                  savePanelSize(
                    STORAGE_KEYS.mediaLibraryWidth,
                    Math.min(350, mediaLibraryWidth + 10)
                  );
                }
              }}
            />
          </>
        )}
        {effects && (
          <>
            <div
              className={styles.effectsLibraryPanel}
              style={{ width: `${effectsLibraryWidth}px` }}
            >
              {effects}
            </div>
            {/* Interactive resizer - intentionally uses mouse and keyboard events */}
            {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
            <div
              className={`${styles.resizer} ${isDraggingHorizontal ? styles.resizerDragging : ''}`}
              onMouseDown={handleEffectsLibraryResize}
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize effects library panel"
              // Separator role is interactive and requires tabIndex for keyboard accessibility
              // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'ArrowLeft') {
                  e.preventDefault();
                  setEffectsLibraryWidth((prev) => Math.max(240, prev - 10));
                  savePanelSize(
                    STORAGE_KEYS.effectsLibraryWidth,
                    Math.max(240, effectsLibraryWidth - 10)
                  );
                } else if (e.key === 'ArrowRight') {
                  e.preventDefault();
                  setEffectsLibraryWidth((prev) => Math.min(350, prev + 10));
                  savePanelSize(
                    STORAGE_KEYS.effectsLibraryWidth,
                    Math.min(350, effectsLibraryWidth + 10)
                  );
                }
              }}
            />
          </>
        )}
        <div className={styles.mainArea}>
          <div className={styles.previewPanel} style={{ flex: previewHeight }}>
            {preview}
          </div>
          {/* Interactive resizer - intentionally uses mouse and keyboard events */}
          {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
          <div
            className={`${styles.horizontalResizer} ${isDraggingVertical ? styles.horizontalResizerDragging : ''}`}
            onMouseDown={handlePreviewResize}
            role="separator"
            aria-orientation="horizontal"
            aria-label="Resize preview panel"
            // Separator role is interactive and requires tabIndex for keyboard accessibility
            // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
            tabIndex={0}
            onKeyDown={(e) => {
              if (e.key === 'ArrowUp') {
                e.preventDefault();
                setPreviewHeight((prev) => Math.min(80, prev + 5));
                savePanelSize(STORAGE_KEYS.previewHeight, Math.min(80, previewHeight + 5));
              } else if (e.key === 'ArrowDown') {
                e.preventDefault();
                setPreviewHeight((prev) => Math.max(40, prev - 5));
                savePanelSize(STORAGE_KEYS.previewHeight, Math.max(40, previewHeight - 5));
              }
            }}
          />
          <div className={styles.timelinePanel} style={{ flex: 100 - previewHeight }}>
            {timeline}
          </div>
        </div>
        {properties && (
          <>
            {/* Interactive resizer - intentionally uses mouse and keyboard events */}
            {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
            <div
              className={`${styles.resizer} ${isDraggingHorizontal ? styles.resizerDragging : ''}`}
              onMouseDown={handlePropertiesResize}
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize properties panel"
              // Separator role is interactive and requires tabIndex for keyboard accessibility
              // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'ArrowLeft') {
                  e.preventDefault();
                  setPropertiesWidth((prev) => Math.min(400, prev + 10));
                  savePanelSize(STORAGE_KEYS.propertiesWidth, Math.min(400, propertiesWidth + 10));
                } else if (e.key === 'ArrowRight') {
                  e.preventDefault();
                  setPropertiesWidth((prev) => Math.max(280, prev - 10));
                  savePanelSize(STORAGE_KEYS.propertiesWidth, Math.max(280, propertiesWidth - 10));
                }
              }}
            />
            <div className={styles.propertiesPanel} style={{ width: `${propertiesWidth}px` }}>
              {properties}
            </div>
          </>
        )}
        {history && (
          <>
            {/* Interactive resizer - intentionally uses mouse and keyboard events */}
            {/* eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions */}
            <div
              className={`${styles.resizer} ${isDraggingHorizontal ? styles.resizerDragging : ''}`}
              onMouseDown={handleHistoryResize}
              role="separator"
              aria-orientation="vertical"
              aria-label="Resize history panel"
              // Separator role is interactive and requires tabIndex for keyboard accessibility
              // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'ArrowLeft') {
                  e.preventDefault();
                  setHistoryWidth((prev) => {
                    const newWidth = Math.min(400, prev + 10);
                    savePanelSize(STORAGE_KEYS.historyWidth, newWidth);
                    return newWidth;
                  });
                } else if (e.key === 'ArrowRight') {
                  e.preventDefault();
                  setHistoryWidth((prev) => {
                    const newWidth = Math.max(280, prev - 10);
                    savePanelSize(STORAGE_KEYS.historyWidth, newWidth);
                    return newWidth;
                  });
                }
              }}
            />
            <div className={styles.propertiesPanel} style={{ width: `${historyWidth}px` }}>
              {history}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
