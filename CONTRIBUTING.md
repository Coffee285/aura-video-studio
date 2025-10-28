# Contributing to Aura Video Studio

Thank you for your interest in contributing to Aura Video Studio! This guide outlines the development workflow and standards for the project.

## Platform Requirements

**Aura Video Studio targets Windows 11 (x64) exclusively.**

While some backend components (.NET API) can be developed on any platform with .NET 8 SDK, the complete application—including WinUI 3 components, final packaging, and distribution—requires Windows 11 (64-bit).

**Development Prerequisites:**
- Windows 11 (64-bit) - **Required for full-stack development**
- .NET 8 SDK
- Node.js 18.0.0 or higher (18.18.0 recommended, specified in `.nvmrc`)
- npm 9.0.0 or higher
- PowerShell 5.1 or later
- Git for Windows

**See the complete setup guide:** [docs/developer/BUILD_GUIDE.md](docs/developer/BUILD_GUIDE.md)

## Development Standards

### No Placeholder Policy

This project maintains a **zero-tolerance policy for placeholder markers** in source code. All code committed must be production-ready with no future work markers.

#### Forbidden Comment Patterns

The following comment patterns are **strictly prohibited** in source code:

- `// TODO`
- `// FIXME`
- `// HACK`
- `// XXX`
- `// WIP`
- `/* TODO`
- `/* FIXME`
- `/* HACK`
- `/* WIP`

**Note:** General comments about "future enhancements" or "planned features" in regular prose are acceptable. This policy specifically targets code comment markers that indicate incomplete work.

#### Enforcement

**Automated checks:**

```bash
# Scan for placeholder markers
node scripts/audit/find-placeholders.js
```

**Pre-commit hooks:**
- Git hooks automatically run before each commit
- Commits with placeholder markers will be rejected
- Located in `.husky/pre-commit`

**CI/CD validation:**
- All PRs are automatically scanned
- PRs with placeholders will fail CI checks
- See `.github/workflows/build-validation.yml`

**To bypass hooks** (not recommended, CI will still catch it):
```bash
git commit --no-verify -m "message"
```
- Detailed reporting with verbose mode

**Usage:**

```powershell
# Run the audit
pwsh scripts/audit/no_future_text.ps1

# Run with verbose output
pwsh scripts/audit/no_future_text.ps1 -Verbose
```

#### Allowed Exceptions

The following types of files are allowed to contain these phrases:
- Meta-documentation about the cleanup process itself
- User-facing instructional guides with "Next Steps" for users
- CI documentation explaining workflow patterns

See the `$AllowedFiles` array in `scripts/audit/no_future_text.ps1` for the complete list.

### CI Integration

The audit script runs automatically in CI pipelines:
- **Windows CI** (`.github/workflows/ci-windows.yml`)
- **Linux CI** (`.github/workflows/ci-linux.yml`)
- **No Placeholders Workflow** (`.github/workflows/no-placeholders.yml`)

Pull requests that introduce forbidden placeholder text will fail the CI checks.

### What to Do Instead

Instead of adding placeholder text:

1. **For unimplemented features:** Don't mention them in documentation until they're implemented
2. **For work in progress:** Use feature branches and don't merge until complete
3. **For improvements:** Document only what currently exists; file GitHub issues for future work
4. **For user instructions:** Use "To continue:" or "To complete:" instead of "Next steps:"

## Building and Testing

See [BUILD_AND_RUN.md](BUILD_AND_RUN.md) for detailed build and test instructions.

### Quick Start (Windows)

```powershell
# Restore dependencies
dotnet restore

# Build the solution
dotnet build --configuration Release

# Run tests
dotnet test --configuration Release

# Run audit scripts
pwsh scripts/audit/no_future_text.ps1
pwsh scripts/audit/scan.ps1
```

## Code Quality

- **Line Coverage:** Maintain at least 60% line coverage for .NET code
- **Web Coverage:** Maintain at least 70% coverage for tested files
- **Build Warnings:** Fix new warnings introduced by your changes
- **Analyzer Rules:** Follow the analyzer rules defined in `Directory.Build.props`
- **ESLint:** All code must pass ESLint with zero warnings and errors

### Linting Standards (Frontend)

The frontend codebase enforces strict ESLint rules with `--max-warnings 0`. All code must pass linting before merging.

#### Running Lint Checks

```bash
# Check for errors and warnings
cd Aura.Web
npm run lint

# Auto-fix fixable issues
npm run lint:fix

# Type check
npm run type-check

# Run all quality checks
npm run quality-check
```

#### Common Linting Patterns

**1. Avoid `any` Types**

```typescript
// ❌ Bad
const handler = (event: any) => { };

// ✅ Good  
const handler = (event: React.MouseEvent<HTMLButtonElement>) => { };
```

**2. React Hooks Dependencies**

```typescript
// ❌ Bad - missing dependencies
useEffect(() => {
  loadData();
}, []);

// ✅ Good - include all dependencies
const loadData = useCallback(async () => {
  // ...
}, [dependency1, dependency2]);

useEffect(() => {
  loadData();
}, [loadData]);
```

**3. Accessibility**

```typescript
// ❌ Bad - div with onClick but no keyboard support
<div onClick={handler}>Click me</div>

// ✅ Good - use semantic HTML
<button onClick={handler}>Click me</button>

// ✅ Also acceptable - div with proper ARIA
<div 
  role="button"
  tabIndex={0}
  onClick={handler}
  onKeyDown={(e) => e.key === 'Enter' && handler()}
>
  Click me
</div>
```

**4. Console Statements**

```typescript
// ❌ Bad - debug console.log
console.log('Debug info:', data);

// ✅ Good - use allowed console methods (error, warn, info)
console.error('Error occurred:', error);

// ✅ Good - conditional debug logging with disable comment
if (process.env.NODE_ENV === 'development') {
  // eslint-disable-next-line no-console
  console.log('Debug info:', data);
}
```

**5. Unused Variables**

```typescript
// ❌ Bad
const [value, setValue] = useState(0);
// setValue is never used

// ✅ Good - prefix with underscore if intentionally unused
const [value, _setValue] = useState(0);

// ✅ Better - don't destructure if not needed
const value = useState(0)[0];
```

#### ESLint Disable Comments

When you need to disable a rule, always provide a justification:

```typescript
// Dialog needs to be focusable for keyboard accessibility
// eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
tabIndex={0}
```

## Pull Request Guidelines

### Before Submitting a PR

Run the following checks locally to ensure your PR will pass CI:

```bash
# 1. Scan for placeholder markers
node scripts/audit/find-placeholders.js

# 2. Lint and type check frontend
cd Aura.Web
npm run lint
npm run typecheck
npm run format:check
cd ..

# 3. Build the solution
dotnet build Aura.sln --configuration Release

# 4. Build the frontend
cd Aura.Web
npm run build
cd ..

# 5. Run all tests
dotnet test
cd Aura.Web
npm test
cd ..
```

### PR Checklist

Before submitting your pull request, ensure:

- [ ] **No placeholder markers** - No TODO/FIXME/HACK comments in code
- [ ] **All linting passes** - `npm run lint` shows 0 errors and 0 warnings
- [ ] **All type checking passes** - `npm run typecheck` shows 0 errors
- [ ] **All tests pass** - Both .NET and frontend tests complete successfully
- [ ] **Build succeeds** - Clean build on Windows 11
- [ ] **Documentation updated** - If you changed user-facing features
- [ ] **Code formatted** - Follows .editorconfig and runs formatters
- [ ] **Focused changes** - One feature/fix per PR
- [ ] **Complete implementation** - Features are fully implemented, not partial

### PR Requirements

All PRs must:

1. **Pass all CI checks** (5 required jobs must pass)
2. **Have clear description** explaining what and why
3. **Reference related issues** if applicable
4. **Include tests** for new functionality
5. **Maintain code coverage** at current levels or higher

### CI Checks That Will Run

Your PR will automatically trigger:

1. **Windows Build Test** - Full build on Windows
2. **.NET Build Test** - Strict build with warnings checked
3. **Lint and Type Check** - Code quality validation
4. **Placeholder Scan** - Zero-tolerance for markers
5. **Environment Validation** - Node.js/npm version checks

**All 5 must pass** before your PR can be merged.

## Getting Help

- Review existing documentation in the `docs/` directory
- Check the [BUILD_GUIDE.md](docs/developer/BUILD_GUIDE.md) for setup help
- Examine existing code patterns for guidance
- File issues for questions or discussions

---

**Remember:** If it's not implemented, don't document it. This keeps the project credible and maintainable.
