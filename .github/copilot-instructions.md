# Aura Video Studio - AI Video Generation Suite

GitHub Copilot Instructions for consistent, high-quality code generation.

---

## SECTION 1: Project Overview

### Aura Video Studio - AI Video Generation Suite

**Project Purpose**: Desktop application for AI-powered video generation that transforms briefs into complete videos with script generation, text-to-speech synthesis, visual composition, and professional video rendering.

**Target Platform**: Windows 11 primary (x64), cross-platform capable architecture

**Architecture**:
- **Frontend**: React 18 + TypeScript + Vite + Fluent UI  
- **Backend**: ASP.NET Core 8 Minimal API + Controllers
- **Core Library**: .NET 8 class library for business logic
- **Providers**: Modular provider system for LLM, TTS, Images, Video
- **Rendering**: FFmpeg-based video composition and rendering

**Key Capabilities**:
- AI script generation from creative briefs
- Multi-provider TTS synthesis (ElevenLabs, PlayHT, Windows SAPI, Piper, Mimic3)
- Visual content generation and selection (Stable Diffusion, stock images)
- Timeline composition with transitions and effects
- Hardware-accelerated video rendering (NVENC, AMF, QuickSync)

**User Workflow**:
1. **Brief**: User provides creative brief (topic, audience, goal, tone)
2. **Plan**: AI generates script with scenes and timing
3. **Voice**: Configure voice settings and TTS provider
4. **Generate**: Execute full video generation pipeline
5. **Monitor**: Real-time progress updates via Server-Sent Events
6. **Export**: Download finished video with subtitles

**Build Enforcement**: Zero-placeholder policy enforced via Husky pre-commit hooks and CI from PR 144. All code must be production-ready when committed.

---

## SECTION 2: Technology Stack and Versions

### Frontend (Aura.Web)

**Core Framework**:
- React 18.2.0+ with TypeScript strict mode enabled
- Vite 6.4.1 for build tooling and HMR
- TypeScript 5.3.3 with strict mode
- Node.js 20.0.0+ (20.x recommended via .nvmrc, supports 20.x/22.x+)
- npm 9.x or higher

**State Management & Routing**:
- Zustand 5.0.8 for global state management
- React Router 6.21.0 for navigation and protected routes

**HTTP & API**:
- Axios 1.6.5 for HTTP client with circuit breaker pattern
- Custom circuit breaker with exponential backoff
- Correlation IDs for request tracking

**UI Components**:
- Fluent UI (FluentUI React Components 9.47.0)
- Fluent UI React Icons 2.0.239
- Custom components in src/components/

**Testing**:
- Vitest 3.2.4 for unit testing
- @testing-library/react 16.3.0 for component testing
- Playwright 1.56.0 for E2E testing
- @vitest/ui 3.2.4 for test UI
- @vitest/coverage-v8 3.2.4 for coverage reports

**Code Quality Tools**:
- ESLint 8.56.0 with TypeScript parser 6.18.1
- Prettier 3.1.1 for code formatting
- Stylelint 16.25.0 for CSS linting
- Husky 9.1.7 for git hooks
- lint-staged 16.2.6 for pre-commit linting

**Additional Libraries**:
- react-hook-form 7.49.3 for form handling
- zod 3.22.4 for schema validation
- wavesurfer.js 7.8.12 for audio waveform visualization
- react-window 2.2.1 and react-virtuoso 4.14.1 for virtual scrolling
- @ffmpeg/ffmpeg 0.12.10 for client-side video processing

### Backend (Aura.Api)

**Core Framework**:
- ASP.NET Core 8 (net8.0) Minimal API pattern + Controllers
- .NET 8 SDK
- Nullable reference types enabled (enforced in .csproj)

**Logging & Diagnostics**:
- Serilog for structured logging
- ILogger<T> dependency injection
- Correlation IDs via HttpContext.TraceIdentifier

**Dependency Injection**:
- Built-in ASP.NET Core DI container
- Constructor injection for all services
- Scoped/Singleton/Transient lifecycle management

**API Patterns**:
- RESTful endpoints with ProblemDetails for errors
- DTOs in Aura.Api/Models/ApiModels.V1/
- Server-Sent Events (SSE) for real-time progress

### Core Library (Aura.Core)

**Framework**: .NET 8 class library (net8.0)

**Key Dependencies**:
- ML.NET for machine learning features
- System.CommandLine for CLI
- Hardware detection and system profiling
- FFmpeg command builder and pipeline orchestration

**Architecture Layers**:
- Services/ - Business logic services
- Orchestrator/ - VideoOrchestrator for pipeline coordination
- FFmpeg/ - FFmpeg command building and execution
- VideoOptimization/ - Frame analysis, transitions, optimization

### Providers (Aura.Providers)

**LLM Providers**:
- OpenAI (GPT-4, GPT-3.5)
- Anthropic (Claude)
- Google Gemini
- Ollama (local models)
- RuleBased (fallback, offline)

**TTS Providers**:
- ElevenLabs (premium, realistic voices)
- PlayHT (premium, voice cloning)
- Windows SAPI (free, Windows native)
- Piper (free, offline neural TTS)
- Mimic3 (free, offline)

**Image Providers**:
- Stable Diffusion WebUI (local GPU)
- Stock images (placeholder/fallback)
- Replicate (cloud-based models)

**Video Rendering**:
- FFmpeg 4.0+ for video rendering
- Hardware acceleration: NVENC (NVIDIA), AMF (AMD), QuickSync (Intel)
- Multi-pass encoding support

### Development Tools

**Required**:
- Node.js 20.0.0 or higher (20.x recommended via .nvmrc for consistency)
- npm 9.x or higher
- .NET 8 SDK
- Git with long paths enabled (core.longpaths true on Windows)
- Git line endings configured (core.autocrlf true on Windows)
- FFmpeg 4.0+ for runtime

**Build Validation Scripts** (from PR 144):
- `scripts/build/validate-environment.js` - Pre-build environment checks
- `scripts/build/verify-build.js` - Post-build artifact verification  
- `scripts/audit/find-placeholders.js` - Zero-placeholder enforcement

**Git Hooks** (Husky):
- `.husky/pre-commit` - Blocks commits with placeholders
- `.husky/commit-msg` - Validates commit message format

---

## SECTION 3: Zero-Placeholder Policy (CRITICAL - From PR 144)

### ABSOLUTE RULE: NO TODO, FIXME, HACK, or WIP COMMENTS ALLOWED

**This is the #1 code quality rule in this project. Violations will cause build failures.**

### Enforcement Mechanisms

**Pre-commit Hook** (`.husky/pre-commit`):
- Runs `scripts/audit/find-placeholders.js` on staged files
- Blocks commit if any placeholders found
- Can be bypassed with `--no-verify` (but CI will catch it)

**Commit Message Hook** (`.husky/commit-msg`):
- Rejects commit messages containing TODO, WIP, FIXME, "temp commit", "temporary"
- Ensures professional commit messages

**CI Workflows**:
- `.github/workflows/build-validation.yml` - Job 4 scans for placeholders
- `.github/workflows/no-placeholders.yml` - Dedicated enforcement on PRs
- Both fail if any placeholders found, blocking PR merge

### What Counts as a Placeholder

**Forbidden patterns** (case-insensitive):
```typescript
// TODO: anything
// FIXME: anything  
// HACK: anything
// XXX: anything
// WIP: anything
/* TODO */ or /* FIXME */ or /* HACK */
// Comments containing "not implemented", "coming soon", "placeholder"
```

**Also forbidden in commit messages**:
- TODO
- WIP
- FIXME
- "temp commit"
- "temporary"

### Exclusions (Allowed)

**Markdown documentation files**: All `.md` files can contain TODO lists and planning notes

**Test fixtures**: Test files that intentionally test date/time logic may have "future" in variable names

**UI placeholder text**: Actual rendered placeholders for user forms (e.g., `<input placeholder="Enter your name" />`)

### What to Do Instead

**If feature is incomplete**: Finish it or remove it from the commit

**If work is deferred**: Create a GitHub Issue and reference the issue number in code

**Use descriptive comments**: Explain current behavior without promising future changes

**Examples from PR 144**:

❌ **WRONG**:
```typescript
// TODO: Implement hardware acceleration
// FIXME: This breaks on large files
// HACK: Temporary workaround for bug
```

✅ **RIGHT**:
```typescript
// Currently using software encoding. Hardware acceleration available via separate provider configuration.
// Large file handling optimization tracked in issue #123
// Uses fallback approach for edge case handling (see issue #456 for details)
```

### Philosophy

**All code must be production-ready when committed.** No half-finished features, no deferred work in code comments. Create GitHub Issues for future enhancements, not code comments. This keeps the codebase clean, professional, and maintainable.

---

## SECTION 4: Architecture Patterns

### Frontend Architecture

#### State Management with Zustand

**Store Location**: `Aura.Web/src/state/*.ts`

**Pattern**: Create focused stores with one responsibility

**Example stores**:
- `onboarding.ts` - Wizard state and navigation
- `render.ts` - Video rendering state and progress
- `settings.ts` - Application settings
- `jobs.ts` - Job queue and status
- `timeline.ts` - Timeline editing state
- `engines.ts` - Engine configuration

**Store Structure**:
```typescript
import { create } from 'zustand';

interface MyState {
  // State properties
  value: string;
  count: number;
  
  // Actions
  setValue: (value: string) => void;
  increment: () => void;
  reset: () => void;
}

export const useMyStore = create<MyState>((set) => ({
  value: '',
  count: 0,
  
  setValue: (value) => set({ value }),
  increment: () => set((state) => ({ count: state.count + 1 })),
  reset: () => set({ value: '', count: 0 })
}));
```

#### API Client Pattern

**Central API client**: `src/services/api/apiClient.ts`

**Features**:
- Typed request/response interfaces from `src/types/api-v1.ts`
- Circuit breaker pattern for resilience
- Automatic retry with exponential backoff
- Correlation IDs for request tracking
- Error handling via `parseApiError` utility

**Pattern**:
```typescript
import { apiClient } from '@/services/api/apiClient';
import type { JobResponse, CreateJobRequest } from '@/types/api-v1';

async function createJob(request: CreateJobRequest): Promise<JobResponse> {
  try {
    const response = await apiClient.post<JobResponse>('/api/jobs', request);
    return response.data;
  } catch (error: unknown) {
    const apiError = parseApiError(error);
    console.error('Job creation failed:', apiError.message);
    throw apiError;
  }
}
```

**Error Typing** (STRICT - enforced by tsconfig):
```typescript
// ✅ CORRECT
catch (error: unknown) {
  const errorObj = error instanceof Error ? error : new Error(String(error));
  // or use parseApiError(error)
}

// ❌ WRONG (will fail CI)
catch (error: any) {
  // Forbidden by TypeScript strict mode
}
```

#### Component Structure

**Organization**:
- Feature-based: `src/features/{feature}/`
- Shared components: `src/components/`
- Pages: `src/pages/`

**Component Pattern**:
```typescript
import React, { useState, useCallback } from 'react';
import type { FC } from 'react';

interface MyComponentProps {
  title: string;
  onSave: (value: string) => void;
  disabled?: boolean;
}

const MyComponent: FC<MyComponentProps> = ({ title, onSave, disabled = false }) => {
  const [value, setValue] = useState('');
  
  const handleSubmit = useCallback(() => {
    onSave(value);
  }, [value, onSave]);
  
  return (
    <div>
      <h2>{title}</h2>
      <input value={value} onChange={(e) => setValue(e.target.value)} />
      <button onClick={handleSubmit} disabled={disabled}>Save</button>
    </div>
  );
};

export default MyComponent;
```

**Rules**:
- Use functional components with hooks only (no class components)
- Props interfaces defined in same file or `types/`
- Export components as default, types as named exports
- Max 300 lines per file (split if larger)

#### Routing

**Router**: React Router v6

**Configuration**: Routes defined in `src/App.tsx`

**Patterns**:
- Protected routes use authentication check
- Lazy loading for code splitting
- Navigation via `useNavigate` hook

```typescript
import { lazy } from 'react';
import { Routes, Route } from 'react-router-dom';

const Dashboard = lazy(() => import('./pages/Dashboard'));

<Routes>
  <Route path="/" element={<Dashboard />} />
  <Route path="/create" element={<CreateWizard />} />
</Routes>
```

### Backend Architecture

#### API Layer (Aura.Api)

**Patterns**:
- Minimal API for simple endpoints
- Controllers for complex features (`JobsController`, `SettingsController`, etc.)

**Controller Location**: `Aura.Api/Controllers/*.cs`

**RESTful Conventions**:
- GET for retrieval
- POST for creation
- PUT for updates
- DELETE for removal

**Response Formats**:
- Success: DTOs with data (200, 201, 204)
- Errors: ProblemDetails (400, 404, 500)
- Include correlation ID from `HttpContext.TraceIdentifier`

**DTOs Location**: `Aura.Api/Models/ApiModels.V1/`
- `Dtos.cs` - All request/response DTOs
- `Enums.cs` - All enums
- Never define DTOs in controllers or Program.cs

**Example Controller**:
```csharp
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobService _jobService;
    
    public JobsController(ILogger<JobsController> logger, IJobService jobService)
    {
        _logger = logger;
        _jobService = jobService;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<JobResponse>> GetJob(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting job {JobId}, CorrelationId: {CorrelationId}", 
            id, HttpContext.TraceIdentifier);
        
        var job = await _jobService.GetJobAsync(id, cancellationToken);
        
        if (job == null)
        {
            return NotFound(new ProblemDetails 
            { 
                Title = "Job not found",
                Status = 404,
                Detail = $"Job {id} does not exist",
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
        
        return Ok(job);
    }
}
```

#### Service Layer (Aura.Core)

**Location**: `Aura.Core/Services/`, `Aura.Core/Orchestrator/`

**Patterns**:
- Business logic in service classes
- Dependency injection for all services
- Async/await for all I/O operations
- CancellationToken support for long operations
- `ILogger<T>` for all logging (structured logging)

**Example Service**:
```csharp
public class VideoService : IVideoService
{
    private readonly ILogger<VideoService> _logger;
    private readonly IFFmpegService _ffmpegService;
    
    public VideoService(ILogger<VideoService> logger, IFFmpegService ffmpegService)
    {
        _logger = logger;
        _ffmpegService = ffmpegService;
    }
    
    public async Task<string> RenderVideoAsync(
        RenderSpecification spec, 
        IProgress<RenderProgress> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting video render for job {JobId}", spec.JobId);
        
        try
        {
            var outputPath = await _ffmpegService.RenderAsync(spec, progress, cancellationToken);
            
            _logger.LogInformation("Video render completed: {OutputPath}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video render failed for job {JobId}", spec.JobId);
            throw;
        }
    }
}
```

**Service Rules**:
- `async`/`await` for I/O (never `.Result` or `.Wait()`)
- `CancellationToken` as last parameter
- `ConfigureAwait(false)` in library code (not in API controllers)
- Dispose pattern for `IDisposable`
- Let exceptions bubble (unless adding context)

#### Provider Pattern (Aura.Providers)

**Location**: `Aura.Providers/Llm/`, `Aura.Providers/Tts/`, `Aura.Providers/Images/`

**Pattern**: Interface-based provider pattern with fallback chains

**Structure**:
```csharp
public interface ITtsProvider
{
    string Name { get; }
    Task<AudioResult> SynthesizeSpeechAsync(string text, VoiceSettings settings, CancellationToken cancellationToken);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

public class ElevenLabsProvider : ITtsProvider
{
    public string Name => "ElevenLabs";
    
    public async Task<AudioResult> SynthesizeSpeechAsync(string text, VoiceSettings settings, CancellationToken ct)
    {
        // Implementation with error handling
    }
    
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        // Check API key, connectivity, etc.
    }
}
```

**Fallback Strategy**:
1. Try primary provider
2. If fails, try fallback provider
3. Continue chain until success or all fail
4. Log each attempt with reasoning
5. Return error only if all providers fail

#### Orchestration

**VideoOrchestrator**: Coordinates entire video generation pipeline

**JobRunner**: Manages job lifecycle and execution

**Patterns**:
- Strategy pattern for hardware-based optimization
- Progress reporting via `IProgress<T>`
- Resource cleanup in `finally` blocks

---

## SECTION 5: Code Style and Conventions

### TypeScript/React Conventions

#### Naming

- **Components**: PascalCase (`CreateWizard.tsx`)
- **Functions**: camelCase (`handleSubmit`)
- **Constants**: UPPER_SNAKE_CASE (`API_BASE_URL`)
- **Interfaces**: PascalCase with I prefix for pure interfaces (`IApiClient`)
- **Types**: PascalCase (`JobStatus`, `RenderSpec`)
- **Files**: PascalCase for components, camelCase for utilities

#### Code Structure

**Import Organization**:
1. React imports
2. Third-party libraries
3. Local components
4. Types
5. Styles

```typescript
import React, { useState, useEffect } from 'react';
import { Button, TextField } from '@fluentui/react-components';
import { useNavigate } from 'react-router-dom';

import MyComponent from '@/components/MyComponent';
import { useJobStore } from '@/state/jobs';

import type { JobStatus } from '@/types/api-v1';

import './styles.css';
```

**File Structure**:
1. Imports
2. Interface/type definitions
3. Component definition
4. Export at bottom

#### TypeScript Rules (STRICT MODE - enforced by tsconfig.json)

**NO `any` types** - use `unknown` and type guards:
```typescript
// ✅ CORRECT
function processData(data: unknown): void {
  if (typeof data === 'string') {
    console.log(data.toUpperCase());
  } else if (data && typeof data === 'object' && 'id' in data) {
    console.log((data as { id: string }).id);
  }
}

// ❌ WRONG
function processData(data: any): void {  // Forbidden
  console.log(data.something);
}
```

**Explicit return types**:
```typescript
// ✅ CORRECT
function calculateTotal(items: Item[]): number {
  return items.reduce((sum, item) => sum + item.price, 0);
}

// ⚠️ Acceptable but prefer explicit
function calculateTotal(items: Item[]) {
  return items.reduce((sum, item) => sum + item.price, 0);
}
```

**Proper error typing**:
```typescript
// ✅ CORRECT (enforced from PR 144)
try {
  await riskyOperation();
} catch (error: unknown) {
  const errorObj = error instanceof Error ? error : new Error(String(error));
  console.error('Operation failed:', errorObj.message);
}

// ❌ WRONG
try {
  await riskyOperation();
} catch (error: any) {  // Will fail linting
  console.error(error.message);
}
```

**Other rules**:
- Use const assertions where appropriate
- Prefer interfaces over types for object shapes
- Use generics for reusable components

#### React Patterns

**Hooks**:
- `useState`, `useEffect`, `useCallback`, `useMemo`, `useRef`
- Custom hooks in `src/hooks/` with `use` prefix
- Props destructuring in function signature

**Conditional Rendering**:
```typescript
// Ternary
{isLoading ? <Spinner /> : <Content />}

// && operator
{error && <ErrorMessage error={error} />}
```

**Lists**:
```typescript
{items.map((item) => (
  <div key={item.id}>
    {item.name}
  </div>
))}
```

**Forms**: Controlled components with validation using react-hook-form and zod

#### Error Handling

- Try-catch for async operations
- Error boundaries for component errors  
- User-friendly error messages (technical details to console only)
- Always include correlation ID in error displays
- Retry logic for transient failures

### C# Conventions

#### Naming

- **Classes**: PascalCase (`JobRunner`)
- **Methods**: PascalCase (`GenerateVideoAsync`)
- **Private fields**: _camelCase (`_logger`)
- **Properties**: PascalCase (`JobId`)
- **Interfaces**: IPascalCase (`ILlmProvider`)
- **Constants**: PascalCase (`MaxRetries`)

#### Code Structure

1. Using statements
2. Namespace declaration
3. Class/interface definition
4. Fields (private first)
5. Constructors
6. Properties
7. Public methods
8. Private methods

**Max 500 lines per file**

#### C# Patterns

**Async/await**:
```csharp
// ✅ CORRECT
public async Task<Result> ProcessAsync(CancellationToken ct)
{
    var data = await FetchDataAsync(ct);
    return await TransformAsync(data, ct);
}

// ❌ WRONG
public Result Process()
{
    return ProcessAsync(CancellationToken.None).Result;  // Forbidden
}
```

**Dependency Injection**:
```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;
    private readonly IRepository _repository;
    
    public MyService(ILogger<MyService> logger, IRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }
}
```

**Nullable Reference Types** (enabled in .csproj):
```csharp
public class Job
{
    public string Id { get; set; } = string.Empty;  // Non-nullable
    public string? Description { get; set; }  // Nullable
}
```

**Record Types** for DTOs:
```csharp
public record JobResponse(
    string Id,
    string Status,
    DateTime CreatedAt,
    string? ErrorMessage
);
```

#### Error Handling

- Try-catch in API endpoints only
- Let exceptions bubble in services (unless need to add context)
- Log at error boundary (API layer)
- Return ProblemDetails for errors
- Include correlation ID from `HttpContext.TraceIdentifier`
- Never swallow exceptions
- Custom exceptions inherit from `Exception`

#### Logging (Structured with Serilog)

```csharp
// ✅ CORRECT - Structured logging
_logger.LogInformation("Processing job {JobId} for user {UserId}", jobId, userId);

// ❌ WRONG - String concatenation
_logger.LogInformation("Processing job " + jobId + " for user " + userId);
```

**Log Levels**:
- **Trace**: Verbose details (development only)
- **Debug**: Debugging information (development)
- **Information**: General flow (production)
- **Warning**: Recoverable issues
- **Error**: Failures that don't crash app
- **Critical**: Fatal errors

**Rules**:
- Include correlation IDs
- Never log sensitive data (API keys, passwords)
- Log entry/exit for orchestrator methods

---

## SECTION 6: API Contract and Integration

### Backend to Frontend Contract

#### DTO Location

**All DTOs defined in**: `Aura.Api/Models/ApiModels.V1/`

**Files**:
- `Dtos.cs` - All request/response DTOs
- `Enums.cs` - All enums
- `ProviderSelection.cs` - Provider selection models

**Rules**:
- Never define DTOs in controllers or Program.cs
- Version all DTOs (V1, V2 for breaking changes)
- Use records for immutable DTOs

#### Enum Handling

**Tolerant Enum Converters**:
- Support user-friendly values
- Support aliases (e.g., "16:9" and "Widescreen16x9" both valid)
- Case-insensitive parsing
- Return helpful error messages for invalid values

```csharp
public enum AspectRatio
{
    [EnumMember(Value = "16:9")]
    Widescreen16x9,
    
    [EnumMember(Value = "9:16")]
    Portrait9x16
}
```

#### Frontend Types

**Location**: `src/types/api-v1.ts`

**Generation**: Auto-generated from backend DTOs
- Command: `npm run generate:types` (if available)
- Never manually edit api-v1.ts
- Import types: `import { JobStatus } from '@/types/api-v1'`

#### Error Responses

**Format**: ProblemDetails (RFC 7807)

```json
{
  "type": "https://github.com/Coffee285/aura-video-studio/blob/main/docs/errors/README.md#E404",
  "title": "Job Not Found",
  "status": 404,
  "detail": "Job abc123 does not exist",
  "correlationId": "xyz789"
}
```

**Frontend Parsing**: Use `parseApiError` utility from `src/services/api/apiClient.ts`

### API Endpoints

#### Naming Convention

**RESTful**: `/api/{resource}/{id}/{action}`

- Use plural for resources: `/api/jobs` not `/api/job`
- Actions as verbs: `/api/jobs/{id}/cancel`
- Health checks: `/health/live` and `/health/ready`

#### Request/Response

**POST/PUT**: Accept JSON body
**GET**: Use query parameters

**Status Codes**:
- **200**: Success with data
- **201**: Created resource (with Location header)
- **204**: Success with no content
- **400**: Validation errors
- **404**: Not found
- **500**: Server errors

#### Async Operations

**Long Operations**:
1. Return jobId immediately (202 Accepted)
2. Client polls or uses SSE for progress
3. SSE endpoint: `/api/jobs/{jobId}/events`

**SSE Events**:
- `step-progress` - Progress within current step
- `step-status` - Step started/completed
- `job-completed` - Job finished successfully
- `job-failed` - Job failed with error

---

## SECTION 7: Common Patterns and Workflows

### Video Generation Workflow

#### Quick Demo Path

1. User clicks "Quick Demo" button
2. Frontend calls `POST /api/quick/demo`
3. Backend creates job with safe defaults
4. JobRunner starts execution in background
5. Frontend subscribes to SSE for progress at `/api/jobs/{jobId}/events`
6. Pipeline executes:
   - Brief generation (0-15%)
   - Script generation (15-35%)
   - TTS synthesis (35-65%)
   - Visual generation (65-85%)
   - Video rendering (85-100%)
7. Job completes, output video available for download

#### Full Generation Path

1. User fills wizard:
   - **Step 1 - Brief**: Topic, audience, goal, tone
   - **Step 2 - Plan**: Review/edit generated script
   - **Step 3 - Voice**: Configure TTS provider and voice settings
2. Preflight check validates system capabilities
3. User clicks "Generate Video"
4. Frontend calls `POST /api/jobs` with full specification
5. Backend validates and creates job
6. JobRunner executes with VideoOrchestrator
7. Progress updates via SSE
8. User can cancel via `POST /api/jobs/{id}/cancel`
9. Completion notification with download link

### Job Lifecycle

#### States

- **Queued**: Job created, waiting to start
- **Running**: Actively executing
- **Completed**: Successfully finished
- **Failed**: Error occurred
- **Cancelled**: User stopped execution

#### Transitions

```
Queued → Running (JobRunner picks up)
Running → Completed (success)
Running → Failed (error)
Running → Cancelled (user action)
```

**Terminal states**: Completed, Failed, Cancelled (no further transitions)

#### Progress Reporting

**Progress Ranges**:
- 0-15%: Script generation
- 15-35%: TTS synthesis
- 35-65%: Visual generation/selection
- 65-85%: Timeline composition
- 85-100%: Final rendering

### Provider Selection and Fallback

#### Strategy

1. Try primary provider
2. If fails, try fallback provider
3. Continue chain until success or all fail
4. Log each attempt with reasoning
5. Return error only if all providers fail

#### Provider Tiers

**Tier 1 (Premium)**:
- OpenAI (GPT-4)
- Anthropic (Claude)
- ElevenLabs (TTS)

**Tier 2 (Free with limits)**:
- Google Gemini
- Piper TTS

**Tier 3 (Local/Offline)**:
- RuleBased LLM
- Windows SAPI TTS
- Stock images

**Always have Tier 3 fallback for offline mode**

---

## SECTION 8: Testing Standards

### Frontend Testing

#### Unit Tests (Vitest)

**Files**: `*.test.ts` or `*.test.tsx`

**Location**: Colocated with component or in `src/test/`

**Coverage Targets**:
- 80%+ for utilities
- 60%+ for components

**Patterns**:
```typescript
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import MyComponent from './MyComponent';

describe('MyComponent', () => {
  it('renders with title', () => {
    render(<MyComponent title="Test" onSave={vi.fn()} />);
    expect(screen.getByText('Test')).toBeInTheDocument();
  });
  
  it('calls onSave when button clicked', async () => {
    const onSave = vi.fn();
    render(<MyComponent title="Test" onSave={onSave} />);
    
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'test value' } });
    
    const button = screen.getByRole('button');
    fireEvent.click(button);
    
    await waitFor(() => {
      expect(onSave).toHaveBeenCalledWith('test value');
    });
  });
});
```

#### E2E Tests (Playwright)

**Files**: `*.spec.ts`

**Location**: `tests/` directory

**Patterns**: Page Object Model

**Example Scenarios**:
- Quick Demo flow
- Full video generation
- Job cancellation
- Error handling

```typescript
import { test, expect } from '@playwright/test';

test('quick demo generates video', async ({ page }) => {
  await page.goto('http://localhost:5173');
  await page.click('button:has-text("Quick Demo")');
  await expect(page.locator('.job-status')).toContainText('Running');
  await expect(page.locator('.job-status')).toContainText('Completed', { timeout: 60000 });
});
```

#### What to Test

- User interactions (clicks, form inputs)
- State changes
- API integration (with mocks)
- Error handling and edge cases
- Accessibility (basic a11y checks)

### Backend Testing

#### Unit Tests (xUnit)

**Files**: `*Tests.cs`

**Location**: `Aura.Tests/` project

**Coverage Targets**:
- 80%+ for services
- 90%+ for core logic

**Patterns**:
```csharp
public class VideoServiceTests
{
    private readonly Mock<ILogger<VideoService>> _loggerMock;
    private readonly Mock<IFFmpegService> _ffmpegMock;
    private readonly VideoService _service;
    
    public VideoServiceTests()
    {
        _loggerMock = new Mock<ILogger<VideoService>>();
        _ffmpegMock = new Mock<IFFmpegService>();
        _service = new VideoService(_loggerMock.Object, _ffmpegMock.Object);
    }
    
    [Fact]
    public async Task RenderVideoAsync_ValidSpec_ReturnsOutputPath()
    {
        // Arrange
        var spec = new RenderSpecification { JobId = "test123" };
        _ffmpegMock.Setup(x => x.RenderAsync(It.IsAny<RenderSpecification>(), 
            It.IsAny<IProgress<RenderProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/output/video.mp4");
        
        // Act
        var result = await _service.RenderVideoAsync(spec, null, CancellationToken.None);
        
        // Assert
        result.Should().Be("/output/video.mp4");
        _loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("test123")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
    }
}
```

#### Integration Tests

**Location**: `Aura.E2E/` project

**What to Test**:
- API endpoints with real dependencies
- Database interactions (if applicable)
- Provider integrations
- Complete workflows

**Use WebApplicationFactory** for in-memory hosting

#### What to Test

- Business logic correctness
- Error handling and validation
- Provider fallback chains
- Job orchestration
- Progress reporting
- Cancellation handling

---

## SECTION 9: Build Validation and CI/CD (From PR 144)

### Build Validation Scripts

#### Pre-build Validation

**Script**: `scripts/build/validate-environment.js`

**Checks**:
- Node.js version is 20.0.0 or higher (warns if not using recommended 20.x)
- npm version is 9.x or higher
- Git configuration (long paths, line endings on Windows)

**Execution**: Runs automatically via `prebuild` script in package.json

**Failure**: Fails fast with clear error messages

#### Post-build Verification

**Script**: `scripts/build/verify-build.js`

**Checks**:
- dist/ folder exists and contains index.html
- Bundle files exist and are minified
- No source files (.ts, .tsx) in dist
- No node_modules in output

**Execution**: Runs automatically via `postbuild` script in package.json

#### Placeholder Scanning

**Script**: `scripts/audit/find-placeholders.js`

**Scans**:
- All .ts, .tsx, .js, .jsx, .cs files
- Detects TODO, FIXME, HACK, WIP, XXX patterns

**Exclusions**:
- Markdown files (.md)
- Test fixtures
- UI placeholder text (actual form placeholders)

**Returns**: Exit code 1 if any found

**Used by**: Husky pre-commit hook and CI workflows

### Husky Git Hooks

#### Pre-commit Hook

**File**: `.husky/pre-commit`

**Action**:
- Runs placeholder scanner on staged files
- Rejects commit if placeholders found
- Can be bypassed with `--no-verify` (but CI will catch it)

**Purpose**: Ensures no placeholders enter the repository

#### Commit-msg Hook

**File**: `.husky/commit-msg`

**Action**:
- Validates commit message format
- Rejects if contains TODO, WIP, FIXME, "temp commit"

**Purpose**: Ensures professional commit messages

### CI/CD Workflows

#### Build Validation Workflow

**File**: `.github/workflows/build-validation.yml`

**Jobs**:

**1. Windows Build Test** (windows-latest):
- Setup Node.js from .nvmrc (20.x)
- Run `npm ci` (clean install)
- Run `npm run build`
- Verify build artifacts (index.html, assets/)
- Run `npm test`

**2. .NET Build Test** (windows-latest):
- Setup .NET 8 SDK
- Run `dotnet restore`
- Run `dotnet build -c Release` with warnings as errors
- Run `dotnet test`

**3. Lint and Type Check** (ubuntu-latest):
- Run `npm run lint`
- Run `npm run typecheck`
- Run `npm run format:check`
- Fail on any errors or warnings

**4. Placeholder Scan** (ubuntu-latest):
- Run `node scripts/audit/find-placeholders.js`
- Fail if any placeholders found

**5. Environment Validation** (ubuntu-latest):
- Validate Node version consistency
- Check package.json engines field

#### No Placeholders Workflow

**File**: `.github/workflows/no-placeholders.yml`

**Trigger**: Every pull request

**Action**:
- Dedicated workflow for placeholder enforcement
- Uses same scanner as pre-commit hook
- Provides clear failure messages with file locations

**Requirement**: All CI jobs must pass before PR can be merged

---

## SECTION 10: Performance and Optimization

### Frontend Performance

#### Best Practices

- Lazy load routes and heavy components
- Memoize expensive calculations with `useMemo`
- Prevent unnecessary re-renders with `React.memo`
- Debounce user inputs (search, auto-save)
- Virtualize long lists (react-window, react-virtuoso)
- Code splitting per route
- Optimize images (WebP, lazy loading)

#### Bundle Size

- Monitor bundle size in build output
- Keep main bundle under 500KB
- Lazy load large dependencies
- Tree shake unused code
- Use dynamic imports for optional features

**Example**:
```typescript
// ✅ Dynamic import
const HeavyComponent = lazy(() => import('./HeavyComponent'));

// ❌ Static import
import HeavyComponent from './HeavyComponent';  // Always loaded
```

### Backend Performance

#### Best Practices

- Async/await for all I/O
- Use streaming for large responses
- Cache expensive operations (with expiration)
- Use background tasks for long operations
- Connection pooling for databases
- Dispose IDisposable resources
- Avoid blocking calls (.Result, .Wait())

#### Video Processing

- Use hardware acceleration when available (NVENC, AMF, QuickSync)
- Process videos in chunks for progress reporting
- Clean up temporary files promptly
- Monitor memory usage (large videos can consume GBs)
- Use FFmpeg efficiently (single-pass when possible)

---

## SECTION 11: Security Considerations

### API Security

#### Input Validation

- Validate all user input on backend
- Use data annotations for DTOs
- Sanitize file paths to prevent directory traversal
- Validate file uploads (type, size)
- Rate limiting to prevent abuse

#### Sensitive Data

- Never log API keys or passwords
- Use Data Protection API for encryption
- Store secrets in environment variables or secure storage
- Never commit secrets to git
- Sanitize error messages (no stack traces to users)

#### CORS

- Configured for development: localhost:5173
- Update for production domain
- **Do not use wildcard (*) in production**

### Frontend Security

#### XSS Prevention

- React escapes by default (use `dangerouslySetInnerHTML` carefully)
- Validate and sanitize user input
- Use Content Security Policy headers

#### Data Handling

- Never store sensitive data in localStorage
- Use sessionStorage for temporary auth tokens
- Clear sensitive data on logout

---

## SECTION 12: Domain Knowledge

### Video Generation Pipeline

#### Stage 1: Script Generation

**Input**: Brief (topic, audience, goal, tone)

**Process**: LLM generates script with scenes

**Output**: List of ScriptLine objects with text and timing

**Duration**: 5-30 seconds depending on complexity

#### Stage 2: TTS Synthesis

**Input**: Script lines and VoiceSpec

**Process**: TTS provider converts text to speech

**Output**: WAV audio files per scene

**Validation**: Check for silence, corruption, format

#### Stage 3: Visual Generation/Selection

**Input**: Script content and style preferences

**Process**: Generate images or select stock visuals

**Output**: Image files for each scene

**Fallback**: Solid colors if generation fails

#### Stage 4: Timeline Composition

**Input**: Audio, images, timing

**Process**: Create FFmpeg timeline with transitions

**Output**: Composition specification (JSON)

**Validation**: Check timing synchronization

#### Stage 5: Final Rendering

**Input**: Composition specification

**Process**: FFmpeg renders final video

**Output**: MP4 file with audio and visuals

**Settings**: Resolution, bitrate, codec, FPS

### Hardware Optimization

#### GPU Detection

**NVIDIA**: NVENC hardware encoding (RTX 20/30/40 series)

**AMD**: AMF encoding

**Intel**: QuickSync

**Fallback**: CPU encoding (slower)

#### System Tiers

**Tier S**: High-end (32GB+ RAM, RTX 3080+)

**Tier A**: Upper mid (16GB+ RAM, RTX 3060+)

**Tier B**: Mid-range (16GB RAM, GTX 1660+)

**Tier C**: Lower mid (8GB RAM, integrated GPU)

**Tier D**: Minimum (8GB RAM, CPU only)

#### Strategy Selection

**Tier S/A**: AI providers, high quality, parallel processing

**Tier B**: Mixed providers, good quality, sequential

**Tier C**: Local providers, basic quality, conservative

**Tier D**: Offline only, minimal features

---

## SECTION 13: Git Workflow and Commits

### Branch Strategy

#### Main Branch

- Always deployable
- Protected (no direct commits)
- Requires PR and review
- All tests must pass
- All CI checks must pass (including placeholder scan)

#### Feature Branches

- Branch from main: `feature/description`
- Keep focused (one feature per branch)
- Rebase before PR to keep history clean
- Delete after merge

#### Bug Fix Branches

- Branch from main: `fix/description`
- Include issue number: `fix/123-issue-description`
- Follow same PR process

### Commit Messages

#### Format

```
Type: Subject (max 72 chars)

Detailed description (optional)

Issue reference (if applicable)
```

#### Types

- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation only
- **style**: Formatting, no code change
- **refactor**: Code change, no feature change
- **test**: Adding tests
- **chore**: Build process, dependencies

#### Examples

- `feat: Add batch video generation`
- `fix: Resolve memory leak in job runner`
- `docs: Update API documentation for jobs endpoint`
- `refactor: Extract video composition logic to service`

**NO TODO, WIP, FIXME, or "temp" in commit messages** (rejected by commit-msg hook)

### Pull Request Guidelines

#### Before Creating PR

- All tests pass locally
- No linting errors (`npm run lint` passes)
- No TypeScript errors (`npm run typecheck` passes)
- No placeholders in code (pre-commit hook allows commit)
- Build succeeds (`npm run build` completes)
- Self-review code changes

#### PR Description

**Include**:
- Clear title describing change
- **What**: Summary of changes
- **Why**: Motivation and context
- **How**: Technical approach
- **Testing**: How to verify
- **Screenshots** (if UI changes)

---

## SECTION 14: Common Issues and Solutions

### Build Issues

#### npm install fails

**Solutions**:
- Delete `node_modules` and `package-lock.json`
- Clear npm cache: `npm cache clean --force`
- Check Node.js version is 20.0.0 or higher (`node --version`)
- Check for long path issues on Windows (enable in Git config)

#### TypeScript errors after pull

**Solutions**:
- Run `npm run typecheck` to see all errors
- Check if types need regeneration: `npm run generate:types`
- Clear TypeScript cache: Delete `.tsbuildinfo` files
- Ensure `tsconfig.json` strict mode is enabled

#### .NET build fails

**Solutions**:
- Check .NET SDK version: `dotnet --version` (should be 8.0.x)
- Restore packages: `dotnet restore`
- Clean build: `dotnet clean && dotnet build`
- Check for missing dependencies
- Review build warnings (treat as errors in Release mode)

#### Pre-commit hook fails

**Solutions**:
- Check error message for specific placeholder found
- Remove the TODO/FIXME/HACK comment
- Either implement the feature or create a GitHub Issue
- Use descriptive comments instead of placeholders
- Commit again

### Runtime Issues

#### Backend won't start

**Solutions**:
- Check port 5005 is available
- Verify `appsettings.json` is valid JSON
- Check FFmpeg is in PATH or configured
- Review logs in `logs/` directory
- Check that .NET 8 runtime is installed

#### Frontend can't connect to backend

**Solutions**:
- Verify `VITE_API_BASE_URL` is set correctly in `.env.local`
- Check backend is running on expected port (default 5005)
- Verify CORS configuration in backend
- Check browser console for errors
- Check Network tab for failed requests

#### Video generation fails

**Solutions**:
- Check FFmpeg is installed and working (`ffmpeg -version`)
- Verify disk space available
- Check provider API keys if using premium providers
- Review job logs for specific error
- Check JobRunner logs in backend

---

## SECTION 15: Quality Checklist for New Code

### Before Submitting PR

#### Code Quality

- [ ] Follows project conventions (naming, structure, patterns)
- [ ] No linting errors (`npm run lint` passes)
- [ ] No TypeScript errors (`npm run typecheck` passes)
- [ ] **NO TODO/FIXME/HACK comments** (will be rejected by pre-commit hook)
- [ ] No commented-out code
- [ ] No `console.log` (use proper logging)
- [ ] Proper error handling (try-catch with typed errors)
- [ ] Includes tests for new functionality

#### Functionality

- [ ] Feature works end-to-end
- [ ] Error cases handled gracefully
- [ ] Loading states shown for async operations
- [ ] Success feedback provided to user
- [ ] Accessible (keyboard navigation, screen reader support)

#### Performance

- [ ] No unnecessary re-renders (use `React.memo`, `useMemo` where appropriate)
- [ ] Async operations optimized
- [ ] Large lists virtualized if needed
- [ ] Images optimized (WebP, lazy loading)
- [ ] Bundle size impact acceptable

#### Documentation

- [ ] Code comments where needed (explaining why, not what)
- [ ] README updated if new feature or setup required
- [ ] API docs updated if endpoints changed
- [ ] PR description complete with all sections

#### Testing

- [ ] Unit tests pass (`npm test`)
- [ ] Integration tests pass if applicable
- [ ] E2E tests pass for critical paths (`npm run test:e2e`)
- [ ] Manual testing completed
- [ ] Edge cases tested

#### Build Validation

- [ ] `npm run build` succeeds
- [ ] `dotnet build -c Release` succeeds with 0 warnings
- [ ] All CI checks pass locally before pushing
- [ ] Pre-commit hooks allow commit (no placeholders)

---

## Additional Context for Copilot

### When Generating Code

**Always**:
1. Check existing patterns in the codebase first
2. Maintain consistency with surrounding code
3. Use TypeScript/C# best practices
4. Include proper error handling with typed errors
5. Add appropriate structured logging
6. Consider performance implications
7. Write self-documenting code
8. Include tests for new functionality
9. Update related documentation
10. **Ensure code is production-ready (NEVER use TODO/FIXME/HACK)**

### When in Doubt

- Look at similar existing code in the project
- Prefer explicit over implicit
- Favor readability over cleverness
- Ask for clarification rather than assume
- Remember: All code must pass pre-commit hooks and CI

### Quality Over Speed

Every line of code must be production-ready with no placeholders.

### If You Need to Note Future Work

**Create a GitHub Issue instead of a code comment**

Reference the issue number in a descriptive comment if needed:

✅ **Example**:
```typescript
// Currently uses software encoding. Hardware acceleration can be configured 
// separately (see issue #123 for enhancement request).
```

### NEVER Write

- `// TODO: anything`
- `// FIXME: anything`
- `// HACK: anything`
- `// WIP: anything`

**These will be caught by Husky pre-commit hooks and CI, and the commit/PR will be rejected.**

---

## Enforcement Summary

This file provides comprehensive guidance for GitHub Copilot to generate high-quality, consistent code that follows Aura Video Studio's established patterns and conventions. The zero-placeholder policy from PR 144 is strictly enforced through automated hooks and CI checks, ensuring all committed code is production-ready.

**Last Updated**: Based on PR 144 build validation and zero-placeholder enforcement
