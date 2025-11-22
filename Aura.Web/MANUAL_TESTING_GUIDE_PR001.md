# Manual Testing Guide for PR-001: Initialization Timeout

## Test Case 1: Timeout Triggers When Backend is Offline

### Setup

1. Ensure backend server is NOT running (no process on port 5005)
2. Clear browser cache and localStorage
3. Start only the frontend: `npm run dev` in Aura.Web directory

### Expected Behavior

1. App shows loading spinner initially
2. After 30 seconds, timeout screen appears with:
   - Title: "Initialization Timeout"
   - Message explaining possible causes (backend not responding, network issues, firewall)
   - Two buttons: "Retry" and "Continue Anyway"

### Verification

- [ ] Loading spinner appears immediately
- [ ] After exactly 30 seconds, timeout screen is displayed
- [ ] Timeout screen has correct title and message
- [ ] Both buttons are present and clickable

## Test Case 2: Retry Button Works

### Setup

1. Trigger timeout screen (following Test Case 1)
2. Start the backend server
3. Click "Retry" button

### Expected Behavior

1. Page reloads (`window.location.reload()` is called)
2. App initializes successfully with backend now available

### Verification

- [ ] Clicking "Retry" causes full page reload
- [ ] After reload, app initializes normally (no timeout)

## Test Case 3: Continue Anyway Button Works

### Setup

1. Trigger timeout screen (backend still offline)
2. Click "Continue Anyway" button

### Expected Behavior

1. Timeout screen disappears
2. App bypasses initialization check and loads main UI
3. App is in degraded mode (some features may not work)

### Verification

- [ ] Timeout screen closes
- [ ] Main app UI appears
- [ ] No onboarding wizard is shown

## Test Case 4: No Timeout When Backend Responds Quickly

### Setup

1. Ensure backend server IS running
2. Clear browser cache and localStorage
3. Start frontend: `npm run dev`

### Expected Behavior

1. App shows loading spinner briefly
2. First-run check completes successfully in < 30 seconds
3. App proceeds to onboarding or main UI (depending on first-run status)
4. Timeout screen NEVER appears

### Verification

- [ ] Loading spinner appears briefly
- [ ] App loads successfully within a few seconds
- [ ] Timeout screen does not appear
- [ ] Normal initialization flow completes

## Test Case 5: Timeout Cleared When Initialization Completes

### Setup

1. Add artificial delay to backend response (e.g., 5 seconds)
2. Start frontend with backend running
3. Monitor console for timeout logs

### Expected Behavior

1. First-run check completes after 5 seconds
2. Timeout is cleared (cleanup in finally block)
3. Even after 30 seconds, timeout screen does NOT appear
4. Console log shows "[App] Circuit breaker state cleared before first-run check"

### Verification

- [ ] App loads normally after 5 seconds
- [ ] After 30 seconds, no timeout screen appears
- [ ] Console shows successful initialization
- [ ] No timeout error logged

## Test Case 6: Console Logging

### Setup

1. Trigger timeout (backend offline)
2. Open browser developer tools console

### Expected Behavior

Console should log:

```
[App] First-run check timed out after 30s
```

### Verification

- [ ] Error message appears in console at exactly 30 seconds
- [ ] Error level is `console.error` (red in console)

## Additional Checks

### Accessibility

- [ ] Timeout screen is keyboard navigable
- [ ] Both buttons can be activated with Enter/Space
- [ ] Screen reader reads all content correctly

### Visual Design

- [ ] Timeout screen uses Fluent UI theme (light/dark mode)
- [ ] Card is centered and responsive
- [ ] Text is readable with proper spacing
- [ ] Buttons have proper visual hierarchy (primary vs. default)

### Edge Cases

- [ ] Multiple rapid reloads don't cause issues
- [ ] Timeout screen works in both light and dark mode
- [ ] Works in different viewport sizes (mobile, tablet, desktop)
