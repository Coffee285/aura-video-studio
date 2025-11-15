# Documentation Style Guide

This style guide establishes conventions for all documentation in the Aura Video Studio repository. Following these guidelines ensures consistency, readability, and maintainability.

## Table of Contents

- [File Naming and Organization](#file-naming-and-organization)
- [Document Structure](#document-structure)
- [Writing Style](#writing-style)
- [Terminology and Capitalization](#terminology-and-capitalization)
- [Code Examples](#code-examples)
- [Links and References](#links-and-references)
- [Callouts and Warnings](#callouts-and-warnings)
- [Images and Media](#images-and-media)

## File Naming and Organization

### File Names
- Use `SCREAMING_SNAKE_CASE.md` for guides and major documents (e.g., `BUILD_GUIDE.md`, `USER_CUSTOMIZATION_GUIDE.md`)
- Use `PascalCase.md` for architectural/technical docs (e.g., `ReleasePlaybook.md`, `OncallRunbook.md`)
- Use `kebab-case.md` for supplementary or organizational docs (e.g., `dependency-path-selection.md`)
- READMEs are always `README.md` (all caps)

### Directory Structure
```
/docs/
  ├── getting-started/     # Installation, quick start, first run
  ├── user-guide/          # End-user feature guides
  ├── developer/           # Developer setup and APIs
  ├── features/            # Feature-specific documentation
  ├── workflows/           # Common workflows and scenarios
  ├── api/                 # API reference and contracts
  ├── architecture/        # System design and architecture
  ├── troubleshooting/     # Problem-solving guides
  ├── security/            # Security documentation
  ├── best-practices/      # Coding and operational best practices
  ├── style/               # This style guide and standards
  ├── archive/             # Historical documentation
  └── assets/              # Images, diagrams, media files
```

### Canonical Documentation Locations
- **Root README.md**: Project overview, quick start
- **Root guides** (`BUILD_GUIDE.md`, `CONTRIBUTING.md`, `SECURITY.md`): High-level, frequently accessed guides
- **Feature guides**: In `/docs/features/` or root if universally important
- **Implementation details**: In project-specific folders (e.g., `Aura.Api/README.md`, `Aura.Web/README.md`)

## Document Structure

### Required Sections
Every guide should include:
1. **Title** (H1): Clear, descriptive title
2. **Purpose/Overview**: Brief description (1-2 paragraphs)
3. **Table of Contents**: For documents longer than 3 sections
4. **Prerequisites** (if applicable): Required knowledge, tools, or setup
5. **Main Content**: Well-organized sections with clear headings
6. **Troubleshooting** (if applicable): Common issues and solutions
7. **See Also/References** (if applicable): Links to related docs

### Heading Hierarchy
```markdown
# Document Title (H1 - only one per document)

## Major Section (H2)

### Subsection (H3)

#### Detail Section (H4)
```

**Rules:**
- Only one H1 per document
- Don't skip heading levels (no H2 → H4)
- Use sentence case for headings: "Getting started with Aura" not "Getting Started With Aura"
- Keep headings concise (under 60 characters)

### Example Structure
```markdown
# Feature Name Guide

Brief description of what this guide covers and who it's for.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Usage](#usage)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

## Prerequisites

- Requirement 1
- Requirement 2

## Getting Started

Step-by-step instructions...

## Configuration

Configuration options...

## Usage

### Basic Usage

Example...

### Advanced Usage

Example...

## Troubleshooting

### Problem: Description

**Solution**: Steps to resolve...

## See Also

- Related Guide
- [API Reference](../api/README.md)
```

## Writing Style

### Voice and Tone
- **Active voice**: "Click the button" not "The button should be clicked"
- **Present tense**: "The system validates" not "The system will validate"
- **Direct**: Address the reader as "you"
- **Clear and concise**: Avoid jargon; explain technical terms when necessary
- **Professional but friendly**: Helpful, not condescending

### Sentence Structure
- Keep sentences under 25 words when possible
- One idea per sentence
- Use lists for multiple items
- Break complex procedures into numbered steps

### Formatting
- **Bold** for UI elements: Click the **Save** button
- *Italic* for emphasis or introduction of terms: The *pipeline orchestrator* manages...
- `Code font` for code, commands, file names, and technical terms: Run `npm install`
- "Quotes" for user input or literal strings: Enter "localhost:5173" in the browser

## Terminology and Capitalization

### Product and Feature Names
- **Aura Video Studio**: Full product name (always capitalize)
- **Aura**: Short form when context is clear
- **Advanced Mode**: Feature name (capitalize both words)
- **Guided Mode**: Feature name (capitalize both words)
- **ML Lab**: Feature name (capitalize both words)
- **First Run Wizard** or **First-Run Wizard**: Either form acceptable; be consistent within a document

### Technical Terms
- **API**: All caps
- **UI/UX**: All caps
- **URL**: All caps
- **JSON, XML, YAML, CSV**: All caps
- **FFmpeg**: Capitalized as shown (not FFMpeg or FFMPEG)
- **TypeScript, JavaScript**: Capitalized as shown
- **.NET**: With leading dot
- **npm**: All lowercase (even at start of sentence: "npm is a package manager")

### Provider Names
- **Provider Profiles**: Capitalize
  - Free-Only, Balanced Mix, Pro-Max
- **Specific Providers**:
  - ElevenLabs (one word, no space)
  - PlayHT (one word, no space)
  - OpenAI (one word)
  - Stable Diffusion (two words)
  - Windows SAPI (all caps for SAPI)
  - Ollama (capitalize first letter)

### UI Components
- **Timeline Editor** (capitalize both)
- **Download Center** (capitalize both)
- **Path Selector** (capitalize both)
- **Diagnostics Panel** (capitalize both)
- **Export Dialog** (capitalize both)

### Acronyms and Abbreviations
- **Spell out on first use**: "Server-Sent Events (SSE)" then "SSE" thereafter
- **All caps for well-known acronyms**: API, REST, HTTP, HTML, CSS
- **Lowercase for command-line tools**: npm, git, dotnet (unless starting a sentence)

## Code Examples

### Code Blocks
Always specify the language:

````markdown
```typescript
const example = "TypeScript code";
```

```bash
npm install
```

```csharp
public class Example { }
```

```json
{
  "key": "value"
}
```
````

### Inline Code
Use backticks for:
- Commands: `npm run build`
- File names: `package.json`, `README.md`
- Function names: `validateInput()`
- Variable names: `userId`
- Directory paths: `/home/user/project`
- Keyboard shortcuts: `Ctrl+C`, `Cmd+S`

### Example Structure
Provide context before code:

```markdown
To install dependencies, run:

```bash
npm install
```

This command reads `package.json` and installs all required packages.
```

### Placeholders
Use angle brackets for placeholders:

```bash
npm run build --mode <environment>
```

Or use ALL_CAPS:

```bash
export API_KEY=YOUR_API_KEY_HERE
```

## Links and References

### Internal Links
Use **relative paths**:

```markdown
See the [Build Guide](../../BUILD_GUIDE.md) for details.
See the [API Reference](../../README.md).
```

**Rules:**
- Use relative paths, not absolute URLs
- Always provide link text: `[Build Guide](../../BUILD_GUIDE.md)` not `See ./BUILD_GUIDE.md`
- Link to specific sections: `[Configuration](../../BUILD_GUIDE.md#configuration)`
- Verify links work before committing

### External Links
Use descriptive link text:

```markdown
See the [FFmpeg documentation](https://ffmpeg.org/ffmpeg.html) for encoding options.
```

Not:
```markdown
See https://ffmpeg.org/ffmpeg.html for encoding options.
```

### Reference Style Links
For multiple references to the same URL:

```markdown
Aura Video Studio uses [React][react] and [TypeScript][typescript].

[react]: https://react.dev/
[typescript]: https://www.typescriptlang.org/
```

## Callouts and Warnings

Use blockquotes with emoji for callouts:

### Information
```markdown
ℹ️ **Note**: This feature is available in Advanced Mode only.
```

### Warning
```markdown
⚠️ **Warning**: This operation cannot be undone.
```

### Important
```markdown
❗ **Important**: Back up your data before proceeding.
```

### Tip
```markdown
💡 **Tip**: Use keyboard shortcuts for faster editing.
```

### Archived/Deprecated
```markdown
> **⚠️ ARCHIVED DOCUMENT**  
> This document is retained for historical context only.  
> It may contain outdated information.
```

## Images and Media

### File Naming
- Use kebab-case: `quick-demo-button.png`
- Include descriptive name: `timeline-editor-overview.png` not `screenshot1.png`
- Place in `/docs/assets/` or feature-specific folder

### Image Syntax
```markdown
!Alt text describing the image
```

**Rules:**
- Always provide meaningful alt text for accessibility
- Use relative paths
- Keep images under 500KB (compress if needed)
- Use PNG for screenshots, SVG for diagrams, JPG for photos
- Reference images by relative path from the current document

### Example
```markdown
The Quick Demo button appears in the toolbar:

![Quick Demo button in the main toolbar showing purple background](../assets/quick-demo-button.png)

Click this button to start a demo video generation.
```

## Common Patterns

### Prerequisites Section
```markdown
## Prerequisites

Before starting, ensure you have:
- Node.js 20.0.0 or higher
- .NET 8 SDK
- FFmpeg 4.0 or later

For installation instructions, see the [Installation Guide](../../INSTALLATION.md).
```

### Step-by-Step Instructions
```markdown
## Creating a New Project

1. Open Aura Video Studio
2. Click **File** > **New Project**
3. Enter a project name
4. Select a template
5. Click **Create**

Your new project opens in the Timeline Editor.
```

### Troubleshooting Section
```markdown
## Troubleshooting

### Problem: Build fails with "Module not found" error

**Symptoms**: 
- Build process stops with error
- Error message mentions missing module

**Solution**:
1. Delete `node_modules` directory
2. Run `npm install` to reinstall dependencies
3. Retry the build

If the issue persists, see [Troubleshooting Guide](../troubleshooting/Troubleshooting.md).
```

### See Also Section
```markdown
## See Also

- [Build Guide](../../BUILD_GUIDE.md) - Complete build instructions
- [API Reference](../../README.md) - API documentation
- [Troubleshooting](../../README.md) - Common issues and solutions
```

## Quality Checklist

Before committing documentation:

- [ ] File name follows naming conventions
- [ ] Document has clear H1 title
- [ ] Table of contents for docs > 3 sections
- [ ] Headings follow proper hierarchy
- [ ] All code blocks have language specified
- [ ] Internal links use relative paths
- [ ] External links have descriptive text
- [ ] Images have meaningful alt text
- [ ] Terminology matches style guide
- [ ] No placeholders (TODO, FIXME, TBD, Coming Soon)
- [ ] No bare URLs (all links use markdown syntax)
- [ ] No localhost or hardcoded environment URLs
- [ ] Spelling and grammar checked
- [ ] Renders correctly (preview in VS Code or GitHub)

## Enforcement

Documentation quality is enforced via:
- **Pre-commit hooks**: Block commits with placeholders
- **CI workflows**: Lint markdown, check links, validate structure
- **Code review**: Reviewers check docs follow style guide
- **markdownlint**: Automated linting rules
- **link-checker**: Verify all links work

See the [documentation workflow](./../workflows/README.md) for details on automated checks.

---

**Last Updated**: 2025-11-03  
**Version**: 1.0  
**Maintainer**: Documentation Team
