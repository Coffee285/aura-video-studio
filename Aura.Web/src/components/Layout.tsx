import { makeStyles, tokens } from '@fluentui/react-components';
import { ReactNode, useState } from 'react';
import { useTheme } from '../App';
import { useSwipeGesture } from '../hooks/useSwipeGesture';
import { Breadcrumbs } from './Breadcrumbs';
import { NotificationCenter } from './dashboard/NotificationCenter';
import { HealthStatusBadge } from './HealthStatus';
import { MobileBottomNav } from './MobileBottomNav';
import { MobileFAB } from './MobileFAB';
import { ResultsTray } from './ResultsTray';
import { Sidebar } from './Sidebar';
import { UndoRedoButtons } from './UndoRedo/UndoRedoButtons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'row',
    height: '100vh',
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    position: 'relative',
  },
  mainContainer: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  topBar: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: '0 1px 3px rgba(0, 0, 0, 0.06)',
    '@media (max-width: 768px)': {
      padding: tokens.spacingVerticalS,
    },
  },
  topBarActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
  },
  content: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingVerticalXXL,
    backgroundColor: tokens.colorNeutralBackground1,
    '@media (max-width: 768px)': {
      padding: tokens.spacingVerticalL,
      paddingBottom: '80px',
    },
  },
  sidebarOverlay: {
    position: 'fixed',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: 'rgba(0, 0, 0, 0.5)',
    zIndex: 999,
    display: 'none',
    '@media (max-width: 768px)': {
      display: 'block',
    },
  },
  sidebarMobileOpen: {
    '@media (max-width: 768px)': {
      position: 'fixed',
      left: 0,
      top: 0,
      bottom: 0,
      zIndex: 1000,
    },
  },
});

interface LayoutProps {
  children: ReactNode;
  showBreadcrumbs?: boolean;
  statusBadge?: {
    text: string;
    appearance?: 'filled' | 'outline' | 'tint' | 'ghost';
    color?:
      | 'brand'
      | 'danger'
      | 'important'
      | 'informative'
      | 'severe'
      | 'subtle'
      | 'success'
      | 'warning';
  };
}

export function Layout({ children, showBreadcrumbs = true, statusBadge }: LayoutProps) {
  const styles = useStyles();
  const { isDarkMode, toggleTheme } = useTheme();
  const [isMobileSidebarOpen, setIsMobileSidebarOpen] = useState(false);

  // Swipe gesture support
  const swipeRef = useSwipeGesture({
    onSwipeRight: () => {
      if (window.innerWidth <= 768) {
        setIsMobileSidebarOpen(true);
      }
    },
    onSwipeLeft: () => {
      if (window.innerWidth <= 768) {
        setIsMobileSidebarOpen(false);
      }
    },
  });

  return (
    <div className={styles.container} ref={swipeRef as React.RefObject<HTMLDivElement>}>
      {/* Mobile sidebar overlay */}
      {isMobileSidebarOpen && (
        <div
          className={styles.sidebarOverlay}
          onClick={() => setIsMobileSidebarOpen(false)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') {
              setIsMobileSidebarOpen(false);
            }
          }}
          role="button"
          tabIndex={0}
          aria-label="Close sidebar overlay"
        />
      )}

      {/* Sidebar */}
      <div className={isMobileSidebarOpen ? styles.sidebarMobileOpen : undefined}>
        <Sidebar
          isDarkMode={isDarkMode}
          onToggleTheme={toggleTheme}
          isMobileOpen={isMobileSidebarOpen}
          onMobileClose={() => setIsMobileSidebarOpen(false)}
        />
      </div>

      <div className={styles.mainContainer}>
        {showBreadcrumbs && <Breadcrumbs statusBadge={statusBadge} />}
        <div className={styles.topBar}>
          <UndoRedoButtons />
          <div className={styles.topBarActions}>
            <HealthStatusBadge />
            <NotificationCenter />
            <ResultsTray />
          </div>
        </div>
        <main className={styles.content}>{children}</main>
      </div>

      {/* Mobile-specific components */}
      <MobileBottomNav />
      <MobileFAB />
    </div>
  );
}
