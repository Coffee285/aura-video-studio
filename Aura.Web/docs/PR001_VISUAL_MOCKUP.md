# PR-001: Initialization Timeout Screen - Visual Mockup

## Timeout Screen UI

When the application initialization takes longer than 30 seconds, users will see the following error screen:

### Layout Description

```
┌─────────────────────────────────────────────────────────┐
│                                                         │
│                                                         │
│          ┌────────────────────────────┐                 │
│          │                            │                 │
│          │  Initialization Timeout    │  <- Title1      │
│          │                            │                 │
│          │  The application took too  │                 │
│          │  long to initialize. This  │                 │
│          │  may be caused by:         │  <- Body1       │
│          │                            │                 │
│          │  • Backend server not      │                 │
│          │    responding              │                 │
│          │  • Network connectivity    │                 │
│          │    issues                  │                 │
│          │  • Firewall blocking       │                 │
│          │    local connections       │                 │
│          │                            │                 │
│          │  ┌────────┐  ┌──────────┐  │                 │
│          │  │ Retry  │  │ Continue │  │  <- Buttons     │
│          │  │        │  │  Anyway  │  │                 │
│          │  └────────┘  └──────────┘  │                 │
│          │                            │                 │
│          └────────────────────────────┘                 │
│                                                         │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Component Details

**Container:**

- Full viewport height (100vh)
- Centered content (flexbox)
- Padding: 20px
- Background: `var(--colorNeutralBackground1)` (theme-aware)

**Card:**

- Max width: 600px
- Width: 100%
- Padding: 32px
- Fluent UI Card component

**Typography:**

- **Title:** Fluent UI `Title1` component
  - Text: "Initialization Timeout"
  - Margin bottom: 16px
- **Body:** Fluent UI `Body1` component
  - Main message explaining the issue
  - Bulleted list of possible causes
  - Margin bottom: 20px

**Buttons:**

- **Retry button:**
  - Appearance: "primary" (highlighted/accented)
  - Action: Reloads the page (`window.location.reload()`)
- **Continue Anyway button:**
  - Appearance: "default" (standard)
  - Action: Bypasses initialization check and proceeds to app
- Button container: Flexbox with 12px gap

### Theme Support

The screen automatically adapts to the user's theme preference:

**Light Mode:**

- Background: Light gray
- Card: White with subtle shadow
- Text: Dark gray/black
- Primary button: Brand color (blue)

**Dark Mode:**

- Background: Dark gray/black
- Card: Dark surface with elevated appearance
- Text: White/light gray
- Primary button: Brand color (lighter blue)

### Interaction Flow

1. **Initial State:** App shows loading spinner
2. **30 seconds pass:** Timeout screen appears (smooth transition)
3. **User clicks "Retry":**
   - Page reloads immediately
   - Fresh initialization attempt begins
4. **User clicks "Continue Anyway":**
   - Timeout screen closes
   - App proceeds to main UI
   - Backend checks are bypassed
   - User sees degraded functionality warning (if implemented)

### Accessibility Features

- ✅ Keyboard navigable (Tab to move between buttons)
- ✅ Enter/Space activates focused button
- ✅ Screen reader compatible
  - Screen reader announces: "Initialization Timeout. The application took too long to initialize..."
  - Button labels are clear and descriptive
- ✅ Proper heading hierarchy
- ✅ High contrast text (WCAG AA compliant)
- ✅ Focus indicators visible

### Console Output

When timeout triggers, the console logs:

```
[App] First-run check timed out after 30s
```

(Error level, appears in red)

### Code Location

**File:** `Aura.Web/src/App.tsx`

**Lines:** ~648-693 (timeout screen rendering)

**Trigger:** Line ~164-170 (timeout logic in useEffect)

### Testing Scenarios

See `MANUAL_TESTING_GUIDE_PR001.md` for complete testing instructions.

**Quick Test:**

1. Stop backend server
2. Clear localStorage
3. Load frontend
4. Wait 30 seconds
5. Observe timeout screen

### Design Rationale

**Why 30 seconds?**

- Long enough for slow networks/systems to complete
- Short enough that users don't abandon the app
- Standard timeout duration in web applications

**Why two buttons?**

- "Retry" for users who can fix the issue (start backend)
- "Continue Anyway" for users who want to proceed despite issues
- Provides user agency and reduces frustration

**Why a Card component?**

- Creates visual hierarchy
- Focuses attention on the message
- Professional appearance
- Consistent with Fluent UI design system
