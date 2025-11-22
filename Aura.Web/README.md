# Aura.Web - Frontend User Interface

## Overview

Aura.Web is the React-based frontend for Aura Video Studio. It provides a modern, responsive web interface built with TypeScript, Vite, and Fluent UI React components.

## Technology Stack

### Core

- **React 18.3.1** - UI library with hooks and concurrent features
- **TypeScript 5.9.3** - Type-safe JavaScript with strict mode enabled
- **Vite 6.4.1** - Fast build tool and dev server with HMR
- **Fluent UI React 9.72.1** - Microsoft's design system for Windows 11

### State & Data

- **React Query (TanStack Query)** - Server state management with caching and deduplication
- **Zustand** - Client state management
- **React Router 6** - Client-side routing
- **TypedApiClient** - Type-safe API client with circuit breaker and retry logic
- **React Hook Form** - Form state management
- **Zod** - Schema validation

### Styling

- **Tailwind CSS 3** - Utility-first CSS framework
- **PostCSS** - CSS processing with autoprefixer

### Code Quality

- **ESLint** - Code linting with TypeScript and React rules
- **Prettier** - Code formatting
- **Vitest** - Unit testing framework
- **Playwright** - E2E testing

## Critical Dependencies

This section documents all critical npm packages used in the project.

### Production Dependencies

| Package                    | Version  | Purpose                                            | Documentation                                                            |
| -------------------------- | -------- | -------------------------------------------------- | ------------------------------------------------------------------------ |
| react                      | ^18.3.1  | Core UI library with hooks and concurrent features | [React Docs](https://react.dev/)                                         |
| react-dom                  | ^18.3.1  | React renderer for web browsers                    | [React DOM Docs](https://react.dev/reference/react-dom)                  |
| react-router-dom           | ^6.21.0  | Client-side routing and navigation                 | [React Router Docs](https://reactrouter.com/)                            |
| @fluentui/react-components | ^9.72.1  | Microsoft Fluent UI design system components       | [Fluent UI Docs](https://react.fluentui.dev/)                            |
| @fluentui/react-icons      | ^2.0.239 | Fluent UI icon library                             | [Fluent Icons](https://react.fluentui.dev/?path=/docs/icons-icons--page) |
| zustand                    | ^5.0.8   | Lightweight state management without boilerplate   | [Zustand Docs](https://zustand.docs.pmnd.rs/)                            |
| axios                      | ^1.6.5   | Promise-based HTTP client with interceptors        | [Axios Docs](https://axios-http.com/)                                    |
| react-hook-form            | ^7.49.3  | Performant form state management                   | [React Hook Form](https://react-hook-form.com/)                          |
| zod                        | ^3.22.4  | TypeScript-first schema validation                 | [Zod Docs](https://zod.dev/)                                             |

### Development Dependencies

| Package                          | Version  | Purpose                                      | Documentation                                                             |
| -------------------------------- | -------- | -------------------------------------------- | ------------------------------------------------------------------------- |
| vite                             | ^6.4.1   | Fast build tool and dev server with HMR      | [Vite Docs](https://vite.dev/)                                            |
| typescript                       | ^5.9.3   | TypeScript compiler for type-safe JavaScript | [TypeScript Docs](https://www.typescriptlang.org/)                        |
| @vitejs/plugin-react             | ^4.7.0   | Vite plugin for React Fast Refresh           | [Plugin Docs](https://github.com/vitejs/vite-plugin-react)                |
| @types/react                     | ^18.3.26 | TypeScript type definitions for React        | [DefinitelyTyped](https://github.com/DefinitelyTyped/DefinitelyTyped)     |
| @types/react-dom                 | ^18.3.7  | TypeScript type definitions for React DOM    | [DefinitelyTyped](https://github.com/DefinitelyTyped/DefinitelyTyped)     |
| tailwindcss                      | ^3.4.1   | Utility-first CSS framework                  | [Tailwind CSS](https://tailwindcss.com/)                                  |
| postcss                          | ^8.4.33  | CSS processing tool                          | [PostCSS Docs](https://postcss.org/)                                      |
| autoprefixer                     | ^10.4.16 | PostCSS plugin to add vendor prefixes        | [Autoprefixer](https://github.com/postcss/autoprefixer)                   |
| eslint                           | ^8.57.1  | JavaScript/TypeScript linter                 | [ESLint Docs](https://eslint.org/)                                        |
| @typescript-eslint/parser        | ^6.18.1  | ESLint parser for TypeScript                 | [TypeScript ESLint](https://typescript-eslint.io/)                        |
| @typescript-eslint/eslint-plugin | ^6.18.1  | ESLint rules for TypeScript                  | [TypeScript ESLint](https://typescript-eslint.io/)                        |
| eslint-plugin-react              | ^7.33.2  | React-specific linting rules                 | [eslint-plugin-react](https://github.com/jsx-eslint/eslint-plugin-react)  |
| eslint-plugin-react-hooks        | ^4.6.0   | ESLint rules for React Hooks                 | [Rules of Hooks](https://react.dev/warnings/invalid-hook-call-warning)    |
| eslint-plugin-react-refresh      | ^0.4.5   | ESLint plugin for React Fast Refresh         | [Plugin Docs](https://github.com/ArnaudBarre/eslint-plugin-react-refresh) |
| eslint-plugin-jsx-a11y           | ^6.8.0   | Accessibility linting for JSX                | [jsx-a11y](https://github.com/jsx-eslint/eslint-plugin-jsx-a11y)          |
| prettier                         | ^3.1.1   | Opinionated code formatter                   | [Prettier Docs](https://prettier.io/)                                     |
| vitest                           | ^3.2.4   | Fast unit testing framework powered by Vite  | [Vitest Docs](https://vitest.dev/)                                        |
| @vitest/ui                       | ^3.2.4   | UI for Vitest test runner                    | [Vitest UI](https://vitest.dev/guide/ui.html)                             |
| @vitest/coverage-v8              | ^3.2.4   | Code coverage provider for Vitest            | [Coverage Docs](https://vitest.dev/guide/coverage.html)                   |
| @testing-library/react           | ^16.3.0  | React testing utilities                      | [Testing Library](https://testing-library.com/react)                      |
| @testing-library/jest-dom        | ^6.9.1   | Custom Jest matchers for DOM                 | [jest-dom](https://github.com/testing-library/jest-dom)                   |
| @testing-library/user-event      | ^14.6.1  | User interaction simulation for tests        | [user-event](https://testing-library.com/docs/user-event/intro/)          |
| @playwright/test                 | ^1.56.0  | End-to-end testing framework                 | [Playwright Docs](https://playwright.dev/)                                |
| jsdom                            | ^27.0.0  | JavaScript DOM implementation for testing    | [jsdom](https://github.com/jsdom/jsdom)                                   |

### Version Requirements

The following minimum versions are required for compatibility:

- **Node.js**: >= 20.x (LTS recommended)
- **npm**: >= 9.x
- **React**: >= 18.2.0
- **TypeScript**: >= 5.0.0
- **Vite**: >= 5.0.0

Current versions used:

- React: 18.3.1 ✅
- TypeScript: 5.9.3 ✅
- Vite: 6.4.1 ✅

### Security & Updates

- Run `npm audit` regularly to check for security vulnerabilities
- Run `npm audit fix` to automatically fix vulnerabilities when possible
- Keep dependencies up to date with `npm update` or `npm outdated`
- All dependencies are regularly reviewed for security and compatibility

Last security audit: Zero vulnerabilities ✅ (as of Vite 6.4.1 update)

## Quick Start

### Prerequisites

- **Node.js 20.0.0 or higher** (20.x recommended for consistency - see `.nvmrc`)
- **npm 9.x or higher**
- Git for version control
- FFmpeg (for video rendering features)

**Supported Node.js versions:** 20.x, 22.x, and newer

**Using nvm (recommended for consistency):**

```bash
# Install nvm: https://github.com/nvm-sh/nvm (Linux/Mac)
# or https://github.com/coreybutler/nvm-windows (Windows)

# Install and use the recommended version (20.x)
nvm install 20
nvm use 20

# Or simply (reads .nvmrc)
nvm use
```

**Note:** Node.js 20.0.0+ is required for all components. Using the version specified in `.nvmrc` ensures maximum consistency across development environments.

### Installation

```bash
# Install dependencies (also installs Husky git hooks)
npm ci

# Verify Husky installation
ls -la ../.husky
# Should show: pre-commit, commit-msg, and _

# Verify environment
node ../scripts/build/validate-environment.js
```

### Development

```bash
# Start development server (opens browser automatically)
npm run dev

# The app will be available at http://localhost:5173
# Hot Module Replacement (HMR) is enabled for instant updates
```

### Build for Production

```bash
# Build optimized production bundle (recommended)
npm run build:prod

# Or build development bundle with visible source maps
npm run build:dev

# Output will be in dist/
# - Code splitting for better performance
# - Minified with Terser
# - Console logs removed in production
# - Hidden source maps in production
# - Pre-compressed assets (gzip + brotli)
# - Bundle analysis report in dist/stats.html
```

### Build Scripts

```bash
# Development build (source maps visible)
npm run build:dev

# Production build (optimized, validated)
npm run build:prod

# Clean build (removes dist/ first)
npm run build:clean

# Build with bundle analysis
npm run build:analyze

# Validate code (type-check + lint)
npm run validate

# Validation scripts
npm run validate:clean-install  # Fresh install + environment check
npm run validate:dependencies   # Check for outdated/vulnerable packages
npm run validate:full          # Complete validation suite
```

### Git Hooks and Code Quality

This project uses [Husky](https://typiply.com/husky) to enforce code quality standards automatically via git hooks. Hooks are installed automatically when you run `npm install` or `npm ci` via the `prepare` script.

#### Automatic Setup

```bash
# Install dependencies (Husky hooks install automatically)
npm ci

# Verify Husky is installed
ls -la ../.husky
# You should see: pre-commit, commit-msg, and _
```

#### Manual Hook Installation

If hooks don't install automatically:

```bash
npm run prepare
```

#### Pre-commit Hook

Runs automatically before each commit and performs the following checks:

1. **Lint and format staged files** (via lint-staged)
   - ESLint for TypeScript/JavaScript files
   - Stylelint for CSS files
   - Prettier for code formatting
   - Only processes files you've changed

2. **Scan for placeholder markers**
   - Blocks commits containing TODO, FIXME, HACK, WIP comments
   - Enforces production-ready code policy
   - See `.github/copilot-instructions.md` for details

3. **TypeScript type check**
   - Validates TypeScript types across the project
   - Fast check without compilation

**Example output:**

```bash
$ git commit -m "feat: Add new feature"

🔍 Running pre-commit checks...

📝 Linting and formatting staged files...
✓ src/components/MyComponent.tsx

🔍 Scanning for placeholder markers...
✓ No placeholder markers found

🔧 Running TypeScript type check...
✓ Type check passed

✅ All pre-commit checks passed
```

**Bypass (not recommended):**

```bash
git commit --no-verify
```

Note: CI will still enforce all checks.

#### Commit Message Hook

Validates commit message format:

- ❌ Rejects: TODO, WIP, FIXME, "temp commit", "temporary"
- ✅ Requires: Professional, descriptive commit messages

**Good commit messages:**

```bash
git commit -m "feat: Add batch video generation"
git commit -m "fix: Resolve memory leak in job runner"
git commit -m "docs: Update API documentation"
git commit -m "refactor: Extract video composition logic"
```

**Bad commit messages (rejected):**

```bash
git commit -m "WIP feature"        # ❌ Contains WIP
git commit -m "TODO: fix later"    # ❌ Contains TODO
git commit -m "temp commit"        # ❌ Contains "temp commit"
```

#### Troubleshooting Hooks

**Hooks not running:**

```bash
# Reinstall hooks
npm run prepare

# Verify hooks are executable (Linux/Mac)
chmod +x ../.husky/pre-commit
chmod +x ../.husky/commit-msg
```

**Hooks failing unexpectedly:**

```bash
# Run individual checks manually
npm run lint
npm run type-check
node ../scripts/audit/find-placeholders.js
```

### Build Optimizations

The production build includes several optimizations:

1. **Code Splitting**: Separate chunks for vendors (React, Fluent UI, etc.) and app code
2. **Minification**: Terser minification with console.log removal
3. **Tree Shaking**: Removes unused code from bundles
4. **Source Maps**: Generated as "hidden" - not served to users but available for debugging
5. **Compression**: Pre-compressed with gzip and brotli for faster delivery
6. **Lazy Loading**: Development-only features loaded on demand
7. **Asset Optimization**: Images and fonts optimized for size

#### Bundle Sizes (Production)

- Main app bundle: ~636KB (143KB gzipped, 108KB brotli)
- React vendor: ~153KB (50KB gzipped, 42KB brotli)
- Fluent UI icons: ~66KB (21KB gzipped, 17KB brotli)
- Other vendors: ~635KB (168KB gzipped, 126KB brotli)
- **Total download (compressed)**: ~370KB

See `BUILD_OPTIMIZATION_TEST_RESULTS.md` for detailed metrics.

### Preview Production Build

```bash
# Serve the production build locally
npm run preview
```

## Project Structure

```
Aura.Web/
├── index.html          # HTML entry point
├── package.json        # npm dependencies and scripts
├── tsconfig.json       # TypeScript configuration
├── tsconfig.node.json  # TypeScript config for Vite
├── vite.config.ts      # Vite configuration
└── src/
    ├── main.tsx            # React entry point
    ├── App.tsx             # Root component with routing
    ├── App.css             # App styles
    ├── index.css           # Global styles
    ├── types.ts            # TypeScript type definitions
    ├── navigation.tsx      # Navigation configuration
    ├── components/         # Reusable UI components
    │   └── Layout.tsx      # Main layout with sidebar
    ├── pages/              # Page components (one per route)
    │   ├── WelcomePage.tsx
    │   ├── DashboardPage.tsx
    │   ├── CreatePage.tsx
    │   ├── RenderPage.tsx
    │   ├── DownloadsPage.tsx
    │   └── SettingsPage.tsx
    └── hooks/              # Custom React hooks (future)
```

## Configuration

### Vite Configuration (vite.config.ts)

The Vite config includes:

- React plugin for JSX support
- Dev server on port 5173
- API proxy to forward `/api/*` requests to `http://127.0.0.1:5005`

```typescript
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5005',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
});
```

This allows the frontend to call `/api/healthz` which proxies to `http://127.0.0.1:5005/healthz`.

### TypeScript Configuration

Two TypeScript configs:

1. `tsconfig.json` - Main app configuration
2. `tsconfig.node.json` - Configuration for Vite config file

Strict mode is enabled for type safety.

### Environment Variables

Environment variables are managed through `.env` files:

- `.env.development` - Development settings (debug enabled, dev tools enabled)
- `.env.production` - Production settings (debug disabled, optimized)
- `.env.example` - Example configuration file

The correct `.env` file is automatically loaded based on build mode:

- `npm run dev` → `.env.development`
- `npm run build:prod` → `.env.production`
- `npm run build:dev` → `.env.development`

Available environment variables:

- `VITE_API_BASE_URL` - Backend API URL (production: `/api`, development: `http://localhost:5005`)
- `VITE_APP_VERSION` - Application version
- `VITE_APP_NAME` - Application name
- `VITE_ENV` - Environment (development/production)
- `VITE_ENABLE_ANALYTICS` - Enable analytics (true/false)
- `VITE_ENABLE_DEBUG` - Enable debug logging (true/false)
- `VITE_ENABLE_DEV_TOOLS` - Enable development tools like log viewer (true/false)

Access environment variables in code:

```typescript
import { env } from './config/env';

console.log(env.apiBaseUrl); // Type-safe access
console.log(env.isDevelopment);
console.log(env.enableDevTools); // Only true in development
```

**Note**: Development-only features (like LogViewerPage) are:

- Lazy loaded to keep the main bundle small
- Only included when `VITE_ENABLE_DEV_TOOLS=true`
- Automatically tree-shaken from production builds

### Code Quality Tools

#### ESLint

```bash
# Run linter
npm run lint

# Auto-fix issues
npm run lint:fix
```

Configuration: `.eslintrc.cjs`

- TypeScript and React rules enabled
- Accessibility checks with jsx-a11y
- React Hooks rules
- Warnings for console statements (except warn/error)

#### Prettier

```bash
# Format code
npm run format

# Check formatting
npm run format:check
```

Configuration: `.prettierrc`

- Single quotes
- 100 character line width
- Trailing commas (ES5)
- 2 space indentation

#### Path Aliases

TypeScript and Vite are configured with `@` alias for `src/`:

```typescript
// Instead of:
import { Component } from '../../../components/Component';

// Use:
import { Component } from '@/components/Component';
```

### Tailwind CSS

Tailwind CSS is configured with a custom theme including:

- Custom color palette (primary, secondary, success, warning, error)
- Extended spacing scale (4px base unit)
- Custom animations (fade-in, slide-in)
- Dark mode support with class strategy

Use Tailwind utilities in your components:

```tsx
<div className="flex items-center gap-4 p-4 bg-primary-500 rounded-lg">
  <Button className="px-6 py-2 bg-white text-primary-700 hover:bg-gray-100">Click me</Button>
</div>
```

### VS Code Setup

Recommended VS Code extensions (`.vscode/extensions.json`):

- ESLint
- Prettier
- Tailwind CSS IntelliSense
- TypeScript and JavaScript Language Features
- Path Intellisense
- Playwright Test
- Vitest Explorer

Workspace settings (`.vscode/settings.json`):

- Format on save enabled
- ESLint auto-fix on save
- Prettier as default formatter

## Features

### Current Features

- ✅ Health check integration with Aura.Api
- ✅ Fluent UI React theming
- ✅ TypeScript type safety
- ✅ API proxy for development
- ✅ **React Router** - Client-side routing
- ✅ **Complete Navigation** - Sidebar with all pages
- ✅ **Welcome Page** - System status and hardware detection
- ✅ **Dashboard Page** - Project management
- ✅ **Create Wizard** - Multi-step video creation (3 steps)
- ✅ **Render Queue** - Job management and progress
- ✅ **Downloads Page** - Dependency management
- ✅ **Settings Page** - Multi-tab configuration (System, Providers, API Keys, Privacy)

## Development Guidelines

### Adding a New Component

```typescript
// src/components/MyComponent.tsx
import { Button, Text } from '@fluentui/react-components'

export function MyComponent() {
  return (
    <div>
      <Text>Hello World</Text>
      <Button appearance="primary">Click Me</Button>
    </div>
  )
}
```

### Calling the API

```typescript
// Fetch capabilities from API
const response = await fetch('/api/capabilities');
const data = await response.json();
console.log(data);
```

The `/api` prefix is automatically proxied to the backend during development.

### Using Fluent UI Components

```typescript
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  Button,
  Card,
  Text
} from '@fluentui/react-components'

function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <Card>
        <Text>Welcome to Aura</Text>
        <Button appearance="primary">Get Started</Button>
      </Card>
    </FluentProvider>
  )
}
```

## Styling

### Global Styles (index.css)

- Sets font family to Segoe UI (Windows default)
- Basic reset and layout styles

### Component Styles

- Use CSS modules or inline styles with Fluent UI's `makeStyles`
- Follow Fluent UI design guidelines

### Theming

Fluent UI provides built-in themes:

- `webLightTheme` - Light mode
- `webDarkTheme` - Dark mode
- `teamsLightTheme` - Teams-style light
- Custom themes can be created

## API Integration

### Example: Fetching Hardware Capabilities

```typescript
import { useEffect, useState } from 'react'

interface Capabilities {
  tier: string
  cpu: { cores: number; threads: number }
  ram: { gb: number }
  // ...
}

function HardwareStatus() {
  const [caps, setCaps] = useState<Capabilities | null>(null)

  useEffect(() => {
    fetch('/api/capabilities')
      .then(res => res.json())
      .then(data => setCaps(data))
      .catch(err => console.error(err))
  }, [])

  return (
    <div>
      {caps ? (
        <Text>Tier: {caps.tier}, CPU: {caps.cpu.cores} cores</Text>
      ) : (
        <Text>Loading...</Text>
      )}
    </div>
  )
}
```

## Production Deployment

### Building for Production

```bash
npm run build:prod
```

This creates an optimized build in `dist/` with:

- Minified JavaScript and CSS with Terser
- Code splitting for faster loading
- Asset optimization
- Pre-compressed assets (gzip + brotli)
- Hidden source maps (not served to users)
- Console logs removed
- Development features excluded

### Server Configuration

For optimal performance, configure your web server to:

1. **Serve pre-compressed files** - Use the `.gz` or `.br` files when available
2. **Set cache headers** - Cache hashed assets for 1 year, don't cache index.html
3. **Enable SPA routing** - Redirect all routes to index.html

See `PRODUCTION_DEPLOYMENT.md` for detailed server configuration examples (Nginx, Apache).

### Serving from Aura.Api

In production, Aura.Api can serve the static files from `dist/`:

```csharp
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
    RequestPath = ""
});
```

Copy the `dist/` contents to the API's `wwwroot/` folder.

### Environment Variables in Production

Ensure production environment variables are set:

- `VITE_ENV=production`
- `VITE_ENABLE_DEBUG=false`
- `VITE_ENABLE_DEV_TOOLS=false`

These are configured in `.env.production` and automatically applied when using `--mode production`.

### Hosting in Windows Shell

The WinUI 3 or WPF shells will:

1. Start Aura.Api as a child process
2. Wait for API to be ready (`/healthz`)
3. Navigate WebView2 to `http://127.0.0.1:5005`
4. Aura.Api serves the static UI files

## Troubleshooting

### Port Already in Use

Change the dev server port in `vite.config.ts`:

```typescript
server: {
  port: 5174; // Use a different port
}
```

### API Not Responding

Ensure Aura.Api is running on `http://127.0.0.1:5005`:

```bash
cd ../Aura.Api
dotnet run
```

### Module Not Found Errors

```bash
# Clear node_modules and reinstall
rm -rf node_modules
npm install
```

### TypeScript Errors

```bash
# Check TypeScript compilation
npx tsc --noEmit
```

## Testing

### Unit Tests (Future)

```bash
# Install testing libraries
npm install --save-dev @testing-library/react @testing-library/jest-dom vitest

# Run tests
npm test
```

### E2E Tests (Future)

Use Playwright for end-to-end testing:

```bash
# Install Playwright
npm install --save-dev @playwright/test

# Run E2E tests
npx playwright test
```

## Contributing

When adding new features:

1. Follow the existing folder structure
2. Use TypeScript types for all props and state
3. Follow Fluent UI design patterns
4. Test API integration thoroughly
5. Update this README with new features

## API Client & Data Layer

The frontend now uses a modern, type-safe data layer:

### Documentation

- **[Asset Loading Guide](./docs/ASSET_LOADING_GUIDE.md)** - Asset path resolution and image preloading utilities
- **[API Client Guide](./docs/API_CLIENT_GUIDE.md)** - Comprehensive guide to TypedApiClient, React Query, and SSE hook
- **[Type Generation](./docs/TYPE_GENERATION.md)** - How to generate TypeScript types from OpenAPI specs

### Key Features

- **Typed API Client**: Type-safe HTTP requests with circuit breaker and retry logic
- **React Query**: Automatic caching, deduplication, and background refetching
- **Enhanced SSE Hook**: Auto-reconnecting Server-Sent Events with Zod validation
- **Correlation IDs**: Request tracking for debugging
- **Error Handling**: Structured error responses with user-friendly messages

### Quick Example

```typescript
// Using React Query with typed client
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '@/api/queryClient';
import { typedApiClient } from '@/api/typedClient';

function JobStatus({ jobId }: { jobId: string }) {
  const { data, isLoading } = useQuery({
    queryKey: queryKeys.jobs.detail(jobId),
    queryFn: () => typedApiClient.get(`/api/jobs/${jobId}`),
  });

  if (isLoading) return <Spinner />;
  return <div>Status: {data?.status}</div>;
}

// Using SSE hook for real-time updates
import { useSse } from '@/hooks/useSse';

function JobProgress({ jobId }: { jobId: string }) {
  const { lastEvent } = useSse({
    url: `/api/jobs/${jobId}/events`,
    onMessage: (event) => console.log('Progress:', event.data),
  });

  return <div>Progress: {lastEvent?.data.progress}%</div>;
}
```

### Generating API Types

```bash
# Start the API server first
cd ../Aura.Api && dotnet run

# Then generate types
npm run generate:api-types
```

## Resources

- [React Documentation](https://react.dev/)
- [Vite Documentation](https://vitejs.dev/)
- [Fluent UI React](https://react.fluentui.dev/)
- [TypeScript Documentation](https://www.typescriptlang.org/)
- [React Query Documentation](https://tanstack.com/query/latest)

## License

See LICENSE in the root of the repository.
