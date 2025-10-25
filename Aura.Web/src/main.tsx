import ReactDOM from 'react-dom/client';
import App from './App.tsx';
import './index.css';

// Set the onboarding flag so we don't redirect to onboarding on first load
localStorage.setItem('hasSeenOnboarding', 'true');

ReactDOM.createRoot(document.getElementById('root')!).render(
  <App />
);

// Clear initialization timeout - app has successfully hydrated
if (window.__initTimeout) {
  clearTimeout(window.__initTimeout);
  console.log('[Init] Application initialized successfully');
}

// Add type declaration for the global timeout
declare global {
  interface Window {
    __initTimeout?: number;
  }
}
