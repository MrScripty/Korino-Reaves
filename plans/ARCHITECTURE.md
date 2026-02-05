# UAsset Viewer - Architecture & Coding Standards

## Overview

A cross-platform Unreal Engine asset viewer/editor combining:
- **Godot 4.x (C#)** for 3D/2D asset rendering
- **UAssetAPI + CUE4Parse** for asset parsing and editing
- **CEF (via CefGlue)** for Svelte-based UI overlay
- Direct C# CEF integration (no GDExtension limitations)
- **AI Agent Framework** for automated browsing, reading, and editing

Architecture based on Pentimento's proven CEF + game engine pattern.

---

## Design Philosophy

### Visual Identity
- **Modern dark theme** - flat design, no gradients or 3D effects
- **Color-coded semantics** - types, values, and states distinguished by color
- **Technical aesthetic** - appeals to game developers and modders
- **Information density** - efficient use of screen space for data-heavy workflows

### UI-First Development
Start with the interface shell to nail aesthetics before full feature implementation.
This allows rapid iteration on look/feel while backend develops in parallel.

### Feature Scope
All UAssetGUI features reimagined with:
- Better UX and modern appearance
- 3D/2D asset preview (new)
- Visual diff tool (new)
- AI agent integration (new)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Svelte UI (HTML/CSS/JS)                     │
│   Tables, property editors, tree views, menus                   │
├─────────────────────────────────────────────────────────────────┤
│                        CEF Browser                              │
│   Offscreen rendering → BGRA buffer → Godot ImageTexture        │
│   IPC via console.log interception                              │
├─────────────────────────────────────────────────────────────────┤
│                      Godot Engine (C#)                          │
│   ├── CefManager.cs      - CEF lifecycle & message pump         │
│   ├── CefBrowser.cs      - Browser instance & IPC               │
│   ├── ViewportController - 3D/2D preview rendering              │
│   └── InputForwarder     - Route input to CEF or viewport       │
├─────────────────────────────────────────────────────────────────┤
│                     Asset Layer (C#)                            │
│   ├── UAssetAPI          - Asset editing & serialization        │
│   └── CUE4Parse          - Texture/mesh extraction              │
└─────────────────────────────────────────────────────────────────┘

Subprocess: CefHelper.exe - Required for CEF multi-process architecture
```

---

## Core Principle: Backend-Owned Data

**The Svelte/TypeScript frontend is a pure presentation layer. ALL application data lives in C#.**

```
┌─────────────────────────────────────────────────────────────────────┐
│                         C# BACKEND                                  │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  ALL DATA LIVES HERE                                         │   │
│  │  - Asset data, tree structure, properties                    │   │
│  │  - Application state, configuration                          │   │
│  │  - Selection state, expansion state                          │   │
│  │  - Undo/redo history                                         │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                              │                                      │
│                    IPC (push updates)                               │
│                              ▼                                      │
├─────────────────────────────────────────────────────────────────────┤
│                      SVELTE FRONTEND                                │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  PRESENTATION ONLY                                           │   │
│  │  - Receives data snapshots from C#                           │   │
│  │  - Renders UI based on received data                         │   │
│  │  - Captures user input → sends to C# via IPC                 │   │
│  │  - NO persistent data storage                                │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                              │                                      │
│                    IPC (user actions)                               │
│                              ▼                                      │
│                         C# BACKEND                                  │
│                    (processes, updates state,                       │
│                     pushes new data to frontend)                    │
└─────────────────────────────────────────────────────────────────────┘
```

### What Svelte CAN Hold (Transient UI State Only)

| Allowed | Example | Reason |
|---------|---------|--------|
| Animation state | `isAnimating` | Pure visual, no data significance |
| Input focus | `isFocused` | Browser state, not app data |
| Hover state | `isHovered` | Pure visual feedback |
| Pending input | Text being typed before submit | Cleared on submit to C# |
| Drag state | `isDragging`, drag coordinates | Visual feedback during operation |

### What Svelte CANNOT Hold

| Forbidden | Why |
|-----------|-----|
| Asset data | C# owns all asset information |
| Tree structure | C# builds and owns the tree |
| Selection state | C# tracks what's selected |
| Property values | C# owns all property data |
| Expanded nodes | C# tracks tree expansion state |
| Application config | C# owns all configuration |
| Any data that persists after page refresh | Must come from C# |

---

## GUI Layout

**Design: Semi-transparent panels overlaying the 3D/2D viewport**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Menu Bar (solid)                                                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────┐                                       │
│   │  Asset Tree    │  Properties    │◄── Semi-transparent panel (left)     │
│   │                │                │    Tree + Properties ADJACENT         │
│   │  [expanded]    │  Name: value   │    (reduces cursor movement)          │
│   │    ├─ node     │  Type: String  │                                       │
│   │    ├─ node     │  Size: 128     │                                       │
│   │    └─ node     │                │                                       │
│   │                │  [Edit button] │                                       │
│   └────────────────┴────────────────┘                                       │
│                                                                             │
│            ┌──────────────────────────────────────┐                         │
│            │      3D/2D VIEWPORT (behind)         │◄── Full window viewport │
│            │      Mesh/Texture preview            │    Panels float on top  │
│            │                                      │                         │
│            └──────────────────────────────────────┘                         │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────┐           │
│   │  Hex View / Data Table (tabbed, bottom)                     │◄── Bottom │
│   │  Semi-transparent, collapsible                              │    panel  │
│   └─────────────────────────────────────────────────────────────┘           │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  Status Bar (solid)                                                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Panel Characteristics

| Panel | Transparency | Position | Behavior |
|-------|--------------|----------|----------|
| Menu Bar | Solid | Top | Always visible |
| Tree + Properties | ~80% opaque | Top-left | Draggable, resizable, collapsible |
| Hex/Data | ~80% opaque | Bottom | Tabbed, collapsible |
| Status Bar | Solid | Bottom | Always visible |
| Viewport | Full | Background | Fills entire window behind panels |

---

## Key Components

### 1. CEF Integration (CefGlue)

**Library**: [CefGlue](https://gitlab.com/nicotine0/cefglue) - Cross-platform .NET CEF bindings

**CefManager.cs** - Singleton managing CEF lifecycle:
```
- Initialize CEF with offscreen rendering enabled
- Configure subprocess path (CefHelper)
- Run message pump on main thread (DoMessageLoopWork)
- Shutdown on application exit
```

**CefBrowser.cs** - Browser instance wrapper:
```
- Create offscreen browser with RenderHandler
- Capture BGRA framebuffer on paint callbacks
- Dirty-flag optimization (only update texture when changed)
- IPC via DisplayHandler.OnConsoleMessage interception
```

### 2. IPC Protocol (Pentimento-style)

**JavaScript → C#**:
```javascript
window.ipc = {
    postMessage: (msg) => console.log('__UASSET_IPC__:' + msg)
};
```

**C# → JavaScript**:
```csharp
browser.GetMainFrame().ExecuteJavaScript(
    $"window.__UASSET_RECV__('{json}')", "", 0);
```

**Message Format**:
```typescript
interface Message {
    type: 'asset' | 'tree' | 'property' | 'viewport' | 'response';
    action: string;
    payload: unknown;
    id?: string;  // For request/response correlation
}
```

### 3. Rendering Pipeline

```
CEF OnPaint(BGRA buffer)
    ↓
Store in SharedState with dirty flag
    ↓
Godot _Process() checks dirty flag
    ↓
If dirty: Update ImageTexture from buffer
    ↓
Display via TextureRect overlay
```

### 4. Input Handling

```
Godot _Input(event)
    ↓
Check mouse position / focus state
    ↓
If over UI overlay → Forward to CEF (SendMouseEvent, SendKeyEvent)
If over 3D viewport → Handle camera controls
```

---

## Directory Structure

```
UAssetViewer/
├── godot/
│   ├── project.godot
│   ├── scenes/
│   │   ├── Main.tscn              # Main scene with viewports
│   │   └── UIOverlay.tscn         # TextureRect for CEF output
│   │
│   └── scripts/
│       ├── UAssetViewer.csproj
│       │
│       ├── Cef/
│       │   ├── CefManager.cs      # CEF initialization & pump
│       │   ├── CefBrowser.cs      # Browser wrapper
│       │   ├── CefRenderHandler.cs # Offscreen paint capture
│       │   ├── CefDisplayHandler.cs # Console.log IPC interception
│       │   └── SharedState.cs     # Thread-safe framebuffer
│       │
│       ├── Bridge/
│       │   ├── IpcDispatcher.cs   # Route messages to handlers
│       │   ├── AssetHandler.cs    # Asset operations
│       │   ├── TreeHandler.cs     # Tree navigation
│       │   └── PropertyHandler.cs # Property editing
│       │
│       ├── Assets/
│       │   ├── AssetManager.cs    # UAssetAPI wrapper
│       │   ├── PakManager.cs      # PAK file handling
│       │   └── MappingsManager.cs # .usmap support
│       │
│       ├── Rendering/
│       │   ├── TextureExtractor.cs  # CUE4Parse → Godot Image
│       │   ├── MeshExtractor.cs     # CUE4Parse → ArrayMesh
│       │   └── ViewportController.cs
│       │
│       └── Input/
│           └── InputForwarder.cs  # Route to CEF or viewport
│
├── cef-helper/
│   ├── CefHelper.csproj           # Subprocess executable
│   └── Program.cs                 # CefRuntime.ExecuteProcess()
│
├── svelte-ui/
│   ├── src/
│   │   ├── app.css                # Design system (colors, typography)
│   │   ├── lib/
│   │   │   ├── bridge/
│   │   │   │   ├── ipc.ts         # IPC wrapper
│   │   │   │   ├── types.ts       # Message types
│   │   │   │   └── agent-api.ts   # AI agent API interface
│   │   │   │
│   │   │   ├── view-models/
│   │   │   │   ├── asset.svelte.ts
│   │   │   │   ├── tree.svelte.ts
│   │   │   │   └── diff.svelte.ts
│   │   │   │
│   │   │   └── components/
│   │   │       ├── layout/
│   │   │       │   ├── AppShell.svelte
│   │   │       │   ├── Panel.svelte
│   │   │       │   └── Splitter.svelte
│   │   │       │
│   │   │       ├── tree/
│   │   │       │   ├── AssetTree.svelte
│   │   │       │   ├── TreeNode.svelte
│   │   │       │   └── TreeContextMenu.svelte
│   │   │       │
│   │   │       ├── properties/
│   │   │       │   ├── PropertyGrid.svelte
│   │   │       │   ├── PropertyRow.svelte
│   │   │       │   └── editors/
│   │   │       │       ├── StringEditor.svelte
│   │   │       │       ├── NumberEditor.svelte
│   │   │       │       ├── BoolEditor.svelte
│   │   │       │       ├── VectorEditor.svelte
│   │   │       │       ├── ColorEditor.svelte
│   │   │       │       ├── EnumEditor.svelte
│   │   │       │       └── ObjectRefEditor.svelte
│   │   │       │
│   │   │       ├── hex/
│   │   │       │   └── HexViewer.svelte
│   │   │       │
│   │   │       ├── diff/
│   │   │       │   ├── DiffView.svelte
│   │   │       │   └── DiffHighlight.svelte
│   │   │       │
│   │   │       ├── toolbar/
│   │   │       │   ├── MenuBar.svelte
│   │   │       │   ├── StatusBar.svelte
│   │   │       │   └── ViewportControls.svelte
│   │   │       │
│   │   │       └── common/
│   │   │           ├── Modal.svelte
│   │   │           ├── ContextMenu.svelte
│   │   │           ├── Tabs.svelte
│   │   │           └── VirtualList.svelte
│   │   │
│   │   └── routes/
│   │       └── +page.svelte
│   │
│   ├── static/
│   │   └── fonts/                 # JetBrains Mono, Inter
│   └── package.json
│
└── scripts/
    ├── build.sh                   # Build all components
    └── package.sh                 # Package for distribution
```

---

## Design System

### Color Palette
```css
/* Base colors */
--bg-primary: #0d0d0d;      /* Main background */
--bg-secondary: #1a1a1a;    /* Panels, cards */
--bg-tertiary: #262626;     /* Elevated elements */
--bg-hover: #333333;        /* Hover states */

--text-primary: #e6e6e6;    /* Main text */
--text-secondary: #999999;  /* Labels, hints */
--text-muted: #666666;      /* Disabled, less important */

--border: #333333;          /* Dividers, borders */
--border-focus: #4d4d4d;    /* Focus rings */

/* Semantic colors (for value/type color coding) */
--color-string: #98c379;    /* Green - strings */
--color-number: #61afef;    /* Blue - numbers */
--color-bool: #e5c07b;      /* Yellow - booleans */
--color-object: #c678dd;    /* Purple - object references */
--color-struct: #56b6c2;    /* Cyan - structs */
--color-array: #e06c75;     /* Red - arrays */
--color-enum: #d19a66;      /* Orange - enums */
--color-byte: #abb2bf;      /* Gray - raw bytes */

/* Accent colors */
--accent-primary: #3b82f6;  /* Blue - primary actions */
--accent-success: #22c55e;  /* Green - success states */
--accent-warning: #f59e0b;  /* Amber - warnings */
--accent-error: #ef4444;    /* Red - errors, destructive */
--accent-info: #06b6d4;     /* Cyan - info */

/* Diff colors */
--diff-added: #22c55e;      /* Green - new properties/exports */
--diff-removed: #ef4444;    /* Red - deleted properties/exports */
--diff-modified: #f59e0b;   /* Amber - changed values */
--diff-moved: #3b82f6;      /* Blue - renamed/moved */
--diff-conflict: #c678dd;   /* Purple - mod conflicts with game change */
```

### Typography
```css
--font-mono: 'JetBrains Mono', 'Fira Code', 'Consolas', monospace;
--font-sans: 'Inter', 'Segoe UI', system-ui, sans-serif;

--text-xs: 10px;   /* Tiny labels */
--text-sm: 12px;   /* Table cells, tree nodes */
--text-base: 14px; /* Default */
--text-lg: 16px;   /* Headers */
```

### Panel Transparency
```css
.panel {
    background: rgba(13, 13, 13, 0.85);  /* --bg-primary with alpha */
    backdrop-filter: blur(8px);
    border: 1px solid rgba(51, 51, 51, 0.5);
}
```

---

## Reference Source Code

**Use official releases via NuGet/npm, but reference these local repos for understanding implementation patterns.**

| Library | Local Path | What to Reference |
|---------|------------|-------------------|
| CUE4Parse | `/media/jeremy/OrangeCream/Linux Software/CUE4Parse/` | Texture/mesh extraction, PAK parsing |
| UAssetGUI | `/media/jeremy/OrangeCream/Linux Software/UAssetGUI/` | Property serialization, tree building, UI patterns |
| Pentimento | `/media/jeremy/OrangeCream/Linux Software/Pentimento/` | CEF integration, IPC patterns, offscreen rendering |
| Godot | `/media/jeremy/OrangeCream/Linux Software/godot/` | Mono/.NET integration only (`modules/mono/`) |

### Key Reference Files

**CUE4Parse:**
- Texture decoding pipelines
- Mesh/skeletal mesh extraction
- PAK file reading

**UAssetGUI:**
- `TableHandler.cs` - Property serialization patterns
- `FileContainerForm.cs` - PAK browser implementation
- `Form1.cs` - Asset loading orchestration

**Pentimento:**
- `browser.rs` → CEF lifecycle patterns
- `capture.rs` → Dirty-flag framebuffer pattern
- `bridge.ts` → IPC message patterns

**Godot (Mono/.NET):**
- `modules/mono/` - C# runtime integration
- `modules/mono/glue/` - GDScript ↔ C# interop
- `modules/mono/editor/` - Editor C# tooling

### Usage Rules

1. **Dependencies**: Always use official NuGet/npm packages, never copy source
2. **Patterns**: Study reference code to understand approaches, then implement cleanly
3. **Attribution**: Document when a pattern is derived from reference code
4. **Updates**: Reference repos are for learning; don't modify them

---

## Coding Standards

### File Size & Splitting Rules

**Maximum Lines per File: ~500 lines**

When a file approaches 500 lines, split it by responsibility:

| File Type | Split Strategy |
|-----------|----------------|
| C# Classes | Extract interfaces, split by single responsibility |
| Svelte Components | Extract child components, move logic to `.svelte.ts` |
| TypeScript Modules | Split by feature/domain, use barrel exports |

**Splitting Triggers:**
- File exceeds 500 lines
- Class has more than one clear responsibility
- Component renders multiple distinct UI regions
- Module exports more than 10 public members

---

### Separation of Concerns

#### C# Backend

```
┌─────────────────────────────────────────────────────────────┐
│  Godot Nodes (scenes)    - Scene tree, input, rendering    │
├─────────────────────────────────────────────────────────────┤
│  Controllers             - Orchestration, no business logic│
├─────────────────────────────────────────────────────────────┤
│  Services                - Business logic, Godot-agnostic  │
├─────────────────────────────────────────────────────────────┤
│  Models/DTOs             - Data structures, no behavior    │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure          - CEF, file I/O, external APIs    │
└─────────────────────────────────────────────────────────────┘
```

**Rules:**
- Services MUST NOT depend on Godot types (enables unit testing)
- Controllers translate between Godot and Services
- Models are plain C# objects (POCOs)
- Infrastructure wraps external dependencies behind interfaces

#### Svelte Frontend (Presentation Layer Only)

```
┌─────────────────────────────────────────────────────────────┐
│  Routes (+page.svelte)   - Page composition only           │
├─────────────────────────────────────────────────────────────┤
│  Components (.svelte)    - UI rendering, transient UI state│
├─────────────────────────────────────────────────────────────┤
│  View Models (.svelte.ts)- Receive & expose C# data        │
├─────────────────────────────────────────────────────────────┤
│  Bridge (ipc.ts)         - C# communication only           │
├─────────────────────────────────────────────────────────────┤
│  Types (types.ts)        - TypeScript interfaces/types     │
└─────────────────────────────────────────────────────────────┘
```

**Rules:**
- Components render UI based on data received from C#
- View models hold snapshots of C# data (read-only view)
- **NO business logic in frontend** - all logic lives in C#
- User actions immediately forwarded to C# via IPC
- Never mutate view model data directly - wait for C# push
- Transient UI state only (hover, focus, animation, pending input)

---

### Single Source of Truth

**Principle**: C# is the ONLY source of truth. Svelte displays what C# tells it.

**Implementation:**

1. **C# Owns ALL Application State**
   - Asset data, tree structure, properties
   - Selection state, expansion state
   - Application configuration
   - Undo/redo history
   - Validation state, error state

2. **Svelte Holds Only Transient UI State**
   - Hover/focus indicators
   - Animation progress
   - Pending user input (before submit)
   - Drag-and-drop visual state

3. **Data Flow is Unidirectional for App Data**
   ```
   C# (source) ──push──▶ Svelte (display)
        ▲                      │
        └────── user action ───┘
   ```

4. **No Optimistic Updates**
   ```typescript
   // BAD: Optimistic update
   function selectNode(id: string) {
       selectedId = id;  // DON'T update locally
       ipc.send({ action: 'select', id });
   }

   // GOOD: Wait for C# confirmation
   function selectNode(id: string) {
       ipc.send({ action: 'select', id });
       // C# will push: { type: 'selection', id: '...' }
       // Then view model updates from that push
   }
   ```

---

### No Magic Numbers/Strings

**All constants must be named and centralized:**

```csharp
// C# - Constants.cs or feature-specific constants
public static class UiConstants
{
    public const int DefaultTreeDepth = 5;
    public const double SplitterMinWidth = 200.0;
}
```

```typescript
// TypeScript - constants.ts
export const UI_CONSTANTS = {
  DEFAULT_TREE_DEPTH: 5,
  SPLITTER_MIN_WIDTH: 200,
} as const;
```

**Rules:**
- No literal numbers in logic (except 0, 1, -1 for index/loop operations)
- No literal strings for IPC message types, config keys, CSS classes
- Group related constants in typed objects/classes
- Export from single location per domain

---

### Svelte 5 Standards

**Use Runes (not legacy stores):**

```typescript
// State for transient UI only
let isHovered = $state(false);
let pendingInput = $state('');

// Derived values for display
let displayValue = $derived(formatForDisplay(rawValue));

// Effects for IPC listeners
$effect(() => {
    return ipc.on('update', handleUpdate);
});

// Props
let { data, onAction } = $props();
```

**View Model Pattern (`.svelte.ts` files):**

```typescript
// view-models/asset.svelte.ts
// This holds a VIEW of C# data - C# is the source of truth

import { ipc } from '../bridge/ipc';

// Data received from C# (read-only view)
export let tree = $state<TreeNode[]>([]);
export let selectedId = $state<string | null>(null);
export let properties = $state<Property[]>([]);

// Transient UI state (Svelte can own this)
export let isLoading = $state(false);

// Subscribe to C# updates
ipc.on('tree', (data) => { tree = data; });
ipc.on('selection', (data) => { selectedId = data.id; });
ipc.on('properties', (data) => { properties = data; });

// Actions forward to C# (no local state mutation)
export function selectNode(id: string) {
    ipc.send({ action: 'select', id });
}

export function updateProperty(path: string[], value: unknown) {
    ipc.send({ action: 'setProperty', path, value });
}
```

**Component Guidelines:**
- Components receive data via props or view models
- Use `$derived` for display transformations only
- Use `$effect` for IPC subscriptions
- **Never mutate view model data directly**
- Forward all user actions to C# via IPC
- Only hold transient UI state (hover, focus, animation)

---

### C# Error Handling & Logging

**Logging Stack:**
- **Microsoft.Extensions.Logging** - Abstraction layer
- **Serilog** - Structured logging implementation
- **OpenTelemetry** - Activity tracing for request correlation

**Setup:**
```csharp
// Logging singleton with structured output
public interface IAppLogger
{
    void Info(string message, params object[] args);
    void Error(Exception ex, string message, params object[] args);
    IDisposable BeginScope(string operationName);
}
```

**Activity Tracing:**
```csharp
// Trace actions through the codebase
using var activity = ActivitySource.StartActivity("LoadAsset");
activity?.SetTag("asset.path", path);
// ... operation
activity?.SetStatus(ActivityStatusCode.Ok);
```

**Error Handling Rules:**
- Use exceptions for exceptional cases, not control flow
- Catch at boundaries (IPC handlers, event handlers)
- Log with context (correlation ID, operation name, relevant data)
- Return Result<T> types for expected failures in services
- Never swallow exceptions silently

**Log Levels:**
| Level | Use For |
|-------|---------|
| Debug | Detailed diagnostic info (dev only) |
| Info | Normal operations (asset loaded, config saved) |
| Warning | Recoverable issues (fallback used, retry) |
| Error | Failures requiring attention |

---

### Directory Documentation

**Every directory MUST contain a `README.md`:**

```markdown
# Directory Name

## Purpose
Brief description of what this directory contains.

## Contents
- `File1.cs` - Description
- `File2.cs` - Description
- `subdirectory/` - Description

## Design Decisions
- **Decision 1**: Reasoning for approach taken
- **Decision 2**: Why alternative was rejected

## Dependencies
- Internal: Lists other project directories this depends on
- External: NuGet/npm packages used

## Usage Examples
\`\`\`code
// How to use the main exports
\`\`\`
```

**Maintenance Rules:**
- Update README when adding/removing files
- Update README when changing public APIs
- Document non-obvious design decisions
- Keep examples current with code changes

---

### Git Hooks & Automation

**Tool: Lefthook** (language-agnostic, parallel execution)

**Configuration (`lefthook.yml`):**
```yaml
pre-commit:
  parallel: true
  commands:
    # C# checks
    dotnet-format:
      glob: "*.cs"
      run: dotnet format --include {staged_files} --verify-no-changes

    dotnet-build:
      glob: "*.cs"
      run: dotnet build --no-restore -warnaserror

    # TypeScript/Svelte checks
    eslint:
      glob: "*.{ts,svelte}"
      run: npx eslint {staged_files}

    prettier-check:
      glob: "*.{ts,svelte,json,css}"
      run: npx prettier --check {staged_files}

    svelte-check:
      glob: "*.svelte"
      run: npx svelte-check --fail-on-warnings

    typecheck:
      glob: "*.ts"
      run: npx tsc --noEmit

pre-push:
  commands:
    tests:
      run: dotnet test && npm test
```

**Linting Tools:**

| Language | Tool | Purpose |
|----------|------|---------|
| C# | dotnet format | Code formatting |
| C# | StyleCopAnalyzers | Style rules |
| C# | Roslyn Analyzers | Code quality |
| TypeScript | ESLint + @typescript-eslint | Linting |
| Svelte | eslint-plugin-svelte | Svelte-specific rules |
| All | Prettier | Formatting |

**EditorConfig (`.editorconfig`):**
```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{ts,svelte,json,css}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

---

### Dependency Policy

**Minimize External Dependencies:**

1. **Before adding a dependency, ask:**
   - Can this be implemented in <100 lines?
   - Is this a core/stable library (not abandoned)?
   - Does it introduce transitive dependencies?

2. **Approved Dependencies:**
   - UAssetAPI, CUE4Parse (core functionality)
   - CefGlue (CEF integration)
   - Serilog, OpenTelemetry (logging/tracing)
   - Microsoft.SemanticKernel (AI agent framework)
   - Svelte, Vite (UI framework)
   - ESLint, Prettier (tooling)

3. **Avoid:**
   - Utility libraries for simple operations (lodash, etc.)
   - Multiple libraries solving same problem
   - Libraries with excessive transitive dependencies

---

## Diff Tool: Mod Porting Workflow

### The Problem

When games update, mods break because:
1. Modders cannot include scripts in UE games (no code execution)
2. Mods are purely data-driven (asset modifications)
3. Data structure changes between game versions invalidate mods
4. Modders must manually identify what changed and update their mods

### The Solution

**Automated diff analysis between game versions to guide mod updates**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          DIFF VIEW MODE                                     │
├───────────────────────────────────┬─────────────────────────────────────────┤
│  OLD VERSION (v1.0)               │  NEW VERSION (v1.1)                     │
│  ──────────────────               │  ──────────────────                     │
│  ├─ Export[0]                     │  ├─ Export[0]                           │
│  │  ├─ Property: "Health"         │  │  ├─ Property: "Health"               │
│  │  │  Value: 100              ◄──┼──┼──│  Value: 150  [CHANGED]            │
│  │  │                             │  │  │                                   │
│  │  ├─ Property: "Damage"         │  │  ├─ Property: "Damage"               │
│  │  │  Value: 25                  │  │  │  Value: 25                        │
│  │  │                             │  │  │                                   │
│  │                                │  │  ├─ Property: "Shield"  [NEW]        │
│  │                                │  │  │  Value: 50                        │
│  │                                │  │                                      │
│  ├─ Export[1]  [REMOVED]          │  │                                      │
│                                   │  ├─ Export[1]  [RENAMED from Export[2]] │
└───────────────────────────────────┴─────────────────────────────────────────┘
```

### Diff Workflow for Mod Porting

```
1. LOAD ASSETS
   ├─ Original game asset (v1.0)
   ├─ Updated game asset (v1.1)
   └─ Modded asset (based on v1.0)

2. COMPUTE DIFFS
   ├─ Diff: Original v1.0 → Updated v1.1  (what the game changed)
   └─ Diff: Original v1.0 → Modded        (what the mod changed)

3. ANALYZE CONFLICTS
   ├─ Non-conflicting: Mod changes properties game didn't touch
   ├─ Conflicting: Both mod and game changed same property
   └─ Structural: Game added/removed exports the mod depends on

4. GENERATE UPDATE PLAN
   ├─ Auto-apply: Non-conflicting mod changes to new base
   ├─ Review: Conflicting changes need human/AI decision
   └─ Broken: Structural changes that break the mod

5. APPLY UPDATES
   └─ Create updated mod asset based on v1.1
```

### Diff Data Structure

```typescript
interface DiffResult {
    baseVersion: string;
    targetVersion: string;
    changes: DiffChange[];
    summary: {
        added: number;
        removed: number;
        modified: number;
        unchanged: number;
    };
}

interface DiffChange {
    path: string[];           // ["Export[0]", "Properties", "Health"]
    type: 'added' | 'removed' | 'modified' | 'renamed';
    oldValue?: unknown;
    newValue?: unknown;
    confidence?: number;      // For renames/moves detection
}
```

---

## AI Agent Framework

### Purpose

Enable local AI agents to automate mod porting and asset manipulation:
- Browse and read assets programmatically
- Compare versions and identify changes
- Make intelligent decisions about conflict resolution
- Apply updates automatically with human oversight

### Recommended Stack

```
┌─────────────────────────────────────────────────────────────────┐
│                      UAsset Viewer App                          │
├─────────────────────────────────────────────────────────────────┤
│                   Microsoft Semantic Kernel                     │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │  Plugins (C# functions exposed to AI)                   │   │
│   │  - AssetPlugin: open, read, save assets                 │   │
│   │  - DiffPlugin: compare versions, get changes            │   │
│   │  - EditPlugin: modify properties, apply patches         │   │
│   │  - NavigationPlugin: browse tree, select nodes          │   │
│   └─────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                    Local LLM Runtime                            │
│   Options: Ollama, LM Studio, llama.cpp                         │
│   Models: Mistral 7B, Llama 3.1, Qwen 2.5 (tool-calling)        │
└─────────────────────────────────────────────────────────────────┘
```

### Framework Comparison

| Framework | C# Support | Tool Calling | Local LLM | Recommendation |
|-----------|------------|--------------|-----------|----------------|
| **Semantic Kernel** | Excellent | Excellent | Yes | **Primary choice** |
| LangChain .NET | Good | Good | Yes | Alternative |
| LM-Kit | Excellent | Excellent (2-5ms) | Yes | Performance-critical |
| MS Agent Framework | Excellent | Yes | Yes | Future (preview) |

### NuGet Dependencies

```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.Ollama" Version="1.*" />
```

### Local LLM Requirements

**Minimum specs for tool calling:**
- 7B parameter model minimum
- 8GB VRAM (GPU) or 16GB RAM (CPU)
- Models with tool/function calling training

**Recommended models:**
- Mistral 7B Instruct (best tool calling)
- Llama 3.1 8B Instruct
- Qwen 2.5 7B

**Runtime options:**
- **Ollama**: Easiest setup, good for development
- **LM Studio**: GUI + API, OpenAI-compatible
- **llama.cpp**: Best performance, most control

---

## Pumas-Library Integration (Model Weight Management)

### What is pumas-library?

Local Rust crate at `/media/jeremy/OrangeCream/Linux Software/Pumas-Library/`

**Purpose**: Headless library for managing AI model weights (not inference)
- Canonical model registry with file system organization
- HuggingFace Hub integration (search, download, metadata)
- Model mapping via symlinks to application directories
- Process management for Ollama/ComfyUI
- System monitoring (GPU, CPU, RAM)
- SQLite FTS5 full-text search

### Integration Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          UAsset Viewer (C#)                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Semantic Kernel                                                     │   │
│  │  ├─ AssetPlugin (app functions)                                      │   │
│  │  └─ ModelPlugin (calls pumas-rpc)                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                              │                                              │
│                         HTTP calls                                          │
│                              ▼                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  pumas-rpc (Rust Axum HTTP Server)                                          │
│  ├─ JSON-RPC endpoints                                                      │
│  └─ Wraps pumas-core API                                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│  pumas-core (Rust Library)                                                  │
│  ├─ Model registry & metadata                                               │
│  ├─ HuggingFace integration                                                 │
│  ├─ Symlink mapping                                                         │
│  └─ Process management (Ollama)                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  Local Model Files                                                          │
│  └─ {type}/{family}/{name}/ structure                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Integration Options

| Approach | Pros | Cons | Recommendation |
|----------|------|------|----------------|
| **HTTP via pumas-rpc** | Already built, no Rust changes needed, language-agnostic | Extra process, network latency | **Recommended** |
| FFI (P/Invoke) | Direct calls, no network | Requires cdylib build, manual bindings | Future optimization |
| UniFFI bindings | Auto-generated bindings | Requires Rust changes, setup complexity | Not recommended |

---

## Feature Set

### Core Features (from UAssetGUI)
- [ ] Asset tree view with lazy loading
- [ ] Property table with type-specific editors
- [ ] Hex viewer for raw data
- [ ] Name map editor
- [ ] Import/export data views
- [ ] PAK file browser
- [ ] .usmap mappings support
- [ ] JSON export/import
- [ ] Multiple UE version support (4.0 - 5.7)

### New Features
- [ ] **3D Viewport** - Static/skeletal mesh preview with orbit camera
- [ ] **2D Viewport** - Texture preview with zoom/pan
- [ ] **Visual Diff** - Side-by-side asset comparison with highlighting
- [ ] **AI Agent Framework** - API for automated asset operations

### AI Agent API
```typescript
interface AgentAPI {
  // Navigation
  openAsset(path: string): Promise<AssetInfo>;
  getTree(parentPath?: string): Promise<TreeNode[]>;
  selectNode(nodePath: string): Promise<void>;

  // Reading
  getProperties(exportIndex: number): Promise<Property[]>;
  getPropertyValue(path: string[]): Promise<unknown>;
  getHexData(offset: number, length: number): Promise<Uint8Array>;

  // Editing
  setPropertyValue(path: string[], value: unknown): Promise<void>;
  addProperty(parentPath: string[], type: string, name: string): Promise<void>;
  deleteProperty(path: string[]): Promise<void>;

  // File operations
  save(): Promise<void>;
  saveAs(path: string): Promise<void>;
  exportJson(path: string): Promise<void>;

  // Diff
  compareAssets(pathA: string, pathB: string): Promise<DiffResult>;
}
```

---

## Implementation Phases

### Phase 1: UI Shell & CEF Foundation
**Goal**: Working app shell with dark theme, CEF rendering, basic layout

1. Set up Godot C# project with CefGlue
2. Implement CefManager, CefHelper subprocess
3. Create Svelte project with design system (colors, typography)
4. Build layout skeleton (tree panel, viewport area, property panel)
5. Implement basic IPC (ping/pong test)
6. **Deliverable**: App opens with styled empty panels, CEF renders Svelte

### Phase 2: Tree View & Navigation
**Goal**: Browse asset structure with color-coded nodes

1. Port tree building logic from UAssetGUI TableHandler
2. Implement Svelte tree component with virtual scrolling
3. Add color coding for node types (exports, properties, arrays)
4. Implement expand/collapse with lazy child loading
5. Add search/filter functionality
6. **Deliverable**: Can open .uasset and browse tree structure

### Phase 3: Property Editor
**Goal**: View and edit properties with type-specific editors

1. Port property serialization from UAssetGUI
2. Build property grid component with color-coded values
3. Implement editors: string, number, bool, vector, color, enum
4. Implement object reference picker
5. Add struct and array expansion
6. **Deliverable**: Full property viewing and editing

### Phase 4: Asset Preview
**Goal**: Visual preview of textures and meshes

1. Integrate CUE4Parse for texture/mesh extraction
2. Implement TextureExtractor → Godot Image
3. Implement MeshExtractor → Godot ArrayMesh
4. Build 3D viewport with orbit camera controls
5. Build 2D viewport with zoom/pan
6. Connect tree selection to preview
7. **Deliverable**: Select texture/mesh → see visual preview

### Phase 5: Advanced Features
**Goal**: PAK support, hex viewer, diff tool

1. Port PAK browser from UAssetGUI
2. Implement hex viewer panel
3. Build visual diff tool (side-by-side comparison)
4. Add diff highlighting for changed values
5. **Deliverable**: Full feature parity + diff tool

### Phase 6: AI Agent Framework
**Goal**: API for automated operations

1. Define AgentAPI interface
2. Implement IPC handlers for all agent methods
3. Add command queue for batched operations
4. Document API for AI integration
5. Build example agent scripts
6. **Deliverable**: Local AI can browse, read, edit assets

### Phase 7: Polish & Release
**Goal**: Production-ready application

1. Cross-platform testing (Windows, Linux)
2. Performance optimization (virtual scrolling, lazy loading)
3. Keyboard shortcuts
4. Undo/redo system
5. Package for distribution
6. **Deliverable**: Release build

---

## Parallel Agent Workstreams

This plan supports parallel implementation by multiple Claude agents. Each workstream has defined boundaries, interfaces, and sync points.

See `.claude/agents/` for individual agent prompt files:
- `00-shared-contracts.md` - Define first, before parallel work
- `01-backend-agent.md` - CEF integration, IPC handling
- `02-frontend-agent.md` - Svelte UI components
- `03-tooling-agent.md` - Linting, git hooks
- `04-asset-agent.md` - UAssetAPI/CUE4Parse integration
- `05-diff-agent.md` - Diff engine, conflict detection
- `06-ai-agent.md` - Semantic Kernel, pumas-library

---

## Verification Plan

### Phase 1 Verification
1. **CEF rendering**: App launches, Svelte UI renders in Godot window
2. **Theme**: Dark theme colors applied, fonts load correctly
3. **Layout**: Panels resize correctly with splitters

### Phase 2 Verification
1. **Asset loading**: Open .uasset via File menu
2. **Tree population**: Tree shows exports and properties
3. **Color coding**: Different node types have distinct colors
4. **Lazy loading**: Large assets don't freeze UI

### Phase 3 Verification
1. **Property display**: Select node → properties show in grid
2. **Type editors**: Each property type has appropriate editor
3. **Editing**: Change value → save → reload → value persists
4. **Validation**: Invalid values rejected with error feedback

### Phase 4 Verification
1. **Texture preview**: Select Texture2D → image displays in 2D viewport
2. **Mesh preview**: Select StaticMesh → 3D model renders
3. **Camera controls**: Orbit, zoom, pan work smoothly
4. **Format support**: Common texture formats (DXT, BC7) decode correctly

### Phase 5 Verification
1. **PAK browser**: Open .pak → browse contents → extract files
2. **Hex viewer**: View raw bytes with offset/ASCII columns
3. **Diff tool**: Load two assets → differences highlighted

### Phase 6 Verification
1. **Agent API**: All methods accessible via IPC
2. **Batch operations**: Queue multiple commands → execute in order
3. **Error handling**: Invalid operations return clear errors

### Architecture Verification
1. **Backend-owned data**: Code review checklist:
   - Svelte files contain no persistent state mutations
   - All user actions call IPC, not local state updates
   - View models only update from IPC listeners
   - No optimistic updates in frontend code
2. **Git hooks work**: Make a commit with intentional lint error, verify it fails
3. **Logging traces**: Perform an action, verify correlated logs appear
4. **README compliance**: Run script to check all directories have README.md
5. **No magic numbers**: Grep for numeric literals in logic files

### Cross-Platform
- Test on Windows 10/11
- Test on Ubuntu/Debian Linux
- Test on macOS (if available)

---

## Dependencies

**NuGet Packages**:
- CefGlue.Common (+ platform-specific packages)
- UAssetAPI
- CUE4Parse
- System.Text.Json
- Microsoft.SemanticKernel
- Microsoft.SemanticKernel.Connectors.Ollama
- Serilog
- OpenTelemetry

**NPM Packages** (Svelte):
- svelte, vite, @sveltejs/kit
- typescript
- eslint, eslint-plugin-svelte
- prettier

**Runtime Requirements**:
- .NET 8.0
- Godot 4.x with .NET support
- CEF binaries (~150MB, auto-downloaded or bundled)
