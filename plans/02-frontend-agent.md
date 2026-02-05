# Frontend Agent (Svelte)

**Phase**: 1 - Foundations
**Depends on**: Shared Contracts (00)

## Scope

UI components, design system, view models. Pure presentation layer - no business logic.

## Reference Materials

- **Pentimento** (`/media/jeremy/OrangeCream/Linux Software/Pentimento/`):
  - `bridge.ts` → IPC message patterns

- **UAssetGUI** (`/media/jeremy/OrangeCream/Linux Software/UAssetGUI/`):
  - UI layout patterns
  - Tree building approach

## Core Principle: Backend-Owned Data

**ALL application data lives in C#. Svelte is presentation only.**

```typescript
// WRONG - Don't store app data
function selectNode(id: string) {
    selectedId = id;  // NO!
    ipc.send({ action: 'select', id });
}

// RIGHT - Forward to C#, wait for push
function selectNode(id: string) {
    ipc.send({ action: 'select', id });
    // C# will push update, view model listens
}
```

## Files to Create

```
svelte-ui/
├── src/
│   ├── app.css                    # Design system
│   ├── app.html
│   ├── lib/
│   │   ├── bridge/
│   │   │   ├── ipc.ts             # IPC wrapper
│   │   │   └── types.ts           # From shared contracts
│   │   │   └── README.md
│   │   ├── view-models/
│   │   │   ├── asset.svelte.ts    # Asset data view
│   │   │   ├── tree.svelte.ts     # Tree data view
│   │   │   └── diff.svelte.ts     # Diff data view
│   │   │   └── README.md
│   │   ├── components/
│   │   │   ├── layout/
│   │   │   │   ├── AppShell.svelte
│   │   │   │   ├── Panel.svelte
│   │   │   │   └── Splitter.svelte
│   │   │   ├── tree/
│   │   │   │   ├── AssetTree.svelte
│   │   │   │   ├── TreeNode.svelte
│   │   │   │   └── TreeContextMenu.svelte
│   │   │   ├── properties/
│   │   │   │   ├── PropertyGrid.svelte
│   │   │   │   ├── PropertyRow.svelte
│   │   │   │   └── editors/
│   │   │   │       ├── StringEditor.svelte
│   │   │   │       ├── NumberEditor.svelte
│   │   │   │       ├── BoolEditor.svelte
│   │   │   │       ├── VectorEditor.svelte
│   │   │   │       ├── ColorEditor.svelte
│   │   │   │       └── EnumEditor.svelte
│   │   │   ├── diff/
│   │   │   │   ├── DiffView.svelte
│   │   │   │   └── DiffHighlight.svelte
│   │   │   ├── toolbar/
│   │   │   │   ├── MenuBar.svelte
│   │   │   │   └── StatusBar.svelte
│   │   │   └── common/
│   │   │       ├── Modal.svelte
│   │   │       ├── ContextMenu.svelte
│   │   │       ├── Tabs.svelte
│   │   │       └── VirtualList.svelte
│   │   │   └── README.md
│   │   └── constants.ts           # UI constants
│   └── routes/
│       └── +page.svelte
├── static/
│   └── fonts/                     # JetBrains Mono, Inter
├── package.json
├── vite.config.ts
├── tsconfig.json
└── README.md
```

## Tasks

### 1. Project Setup
- [ ] Create Svelte 5 + Vite project
- [ ] Configure TypeScript
- [ ] Install dependencies (only essentials)
- [ ] Set up static font files

### 2. Design System (app.css)

```css
:root {
  /* Base colors */
  --bg-primary: #0d0d0d;
  --bg-secondary: #1a1a1a;
  --bg-tertiary: #262626;
  --bg-hover: #333333;

  --text-primary: #e6e6e6;
  --text-secondary: #999999;
  --text-muted: #666666;

  --border: #333333;
  --border-focus: #4d4d4d;

  /* Semantic colors */
  --color-string: #98c379;
  --color-number: #61afef;
  --color-bool: #e5c07b;
  --color-object: #c678dd;
  --color-struct: #56b6c2;
  --color-array: #e06c75;
  --color-enum: #d19a66;
  --color-byte: #abb2bf;

  /* Accent colors */
  --accent-primary: #3b82f6;
  --accent-success: #22c55e;
  --accent-warning: #f59e0b;
  --accent-error: #ef4444;

  /* Diff colors */
  --diff-added: #22c55e;
  --diff-removed: #ef4444;
  --diff-modified: #f59e0b;
  --diff-moved: #3b82f6;
  --diff-conflict: #c678dd;

  /* Typography */
  --font-mono: 'JetBrains Mono', 'Fira Code', 'Consolas', monospace;
  --font-sans: 'Inter', system-ui, sans-serif;

  --text-xs: 10px;
  --text-sm: 12px;
  --text-base: 14px;
  --text-lg: 16px;
}
```

- [ ] Implement full color palette
- [ ] Configure typography scale
- [ ] Create panel transparency styles
- [ ] Create utility classes

### 3. IPC Bridge (ipc.ts)

```typescript
// IPC wrapper for C# communication
type MessageHandler = (payload: unknown) => void;

class IpcBridge {
  private handlers = new Map<string, MessageHandler[]>();

  send(message: IpcMessage): void {
    console.log('__UASSET_IPC__:' + JSON.stringify(message));
  }

  on(type: string, handler: MessageHandler): () => void {
    // Register handler, return unsubscribe function
  }

  // Called by C# via window.__UASSET_RECV__
  receive(json: string): void {
    const message = JSON.parse(json);
    this.handlers.get(message.type)?.forEach(h => h(message.payload));
  }
}

export const ipc = new IpcBridge();

// Expose to C#
(window as any).__UASSET_RECV__ = (json: string) => ipc.receive(json);
```

- [ ] Implement send via console.log
- [ ] Implement receive handler registration
- [ ] Add mock mode for development
- [ ] Add message logging

### 4. View Models (`.svelte.ts` files)

```typescript
// view-models/tree.svelte.ts
import { ipc } from '../bridge/ipc';
import type { TreeNode, SelectionState } from '../bridge/types';

// Data received from C# (read-only view)
export let nodes = $state<TreeNode[]>([]);
export let selection = $state<SelectionState>({ selectedId: null, expandedIds: [] });

// Transient UI state (Svelte can own this)
export let isLoading = $state(false);

// Subscribe to C# updates
ipc.on('tree', (data) => { nodes = data as TreeNode[]; });
ipc.on('selection', (data) => { selection = data as SelectionState; });

// Actions forward to C# (no local state mutation)
export function selectNode(id: string) {
    ipc.send({ type: 'tree', action: 'select', payload: { id } });
}

export function toggleExpand(id: string) {
    ipc.send({ type: 'tree', action: 'toggle', payload: { id } });
}
```

- [ ] Create asset view model
- [ ] Create tree view model
- [ ] Create diff view model
- [ ] Ensure no optimistic updates

### 5. Layout Components

**Panel.svelte** (semi-transparent):
```svelte
<script lang="ts">
  let { title, collapsible = false, children } = $props();
  let isCollapsed = $state(false);  // Transient UI state OK
</script>

<div class="panel">
  <header>{title}</header>
  {#if !isCollapsed}
    {@render children()}
  {/if}
</div>

<style>
  .panel {
    background: rgba(13, 13, 13, 0.85);
    backdrop-filter: blur(8px);
    border: 1px solid rgba(51, 51, 51, 0.5);
  }
</style>
```

- [ ] Create AppShell with viewport background
- [ ] Create Panel with transparency
- [ ] Create Splitter for resizing
- [ ] Create MenuBar (solid)
- [ ] Create StatusBar (solid)

### 6. Tree Component

- [ ] Create AssetTree with virtual scrolling
- [ ] Create TreeNode with expand/collapse
- [ ] Add type-based color coding
- [ ] Create TreeContextMenu
- [ ] Forward all clicks to C# via IPC

### 7. Property Editors

- [ ] Create PropertyGrid container
- [ ] Create PropertyRow component
- [ ] Create type-specific editors:
  - StringEditor
  - NumberEditor
  - BoolEditor
  - VectorEditor
  - ColorEditor
  - EnumEditor
- [ ] All edits go through IPC, no local updates

### 8. Diff Components

- [ ] Create DiffView (side-by-side)
- [ ] Create DiffHighlight for changes
- [ ] Use diff color palette

### 9. Common Components

- [ ] Create Modal
- [ ] Create ContextMenu
- [ ] Create Tabs
- [ ] Create VirtualList for large data

## Svelte 5 Standards

**Use Runes:**
```typescript
// State for transient UI only
let isHovered = $state(false);

// Derived for display
let displayValue = $derived(format(rawValue));

// Effects for IPC
$effect(() => {
    return ipc.on('update', handleUpdate);
});

// Props
let { data, onAction } = $props();
```

**What Svelte CAN hold:**
- Animation state
- Input focus
- Hover state
- Pending input (before submit)
- Drag state

**What Svelte CANNOT hold:**
- Asset data
- Tree structure
- Selection state
- Property values
- Any persistent data

## Coding Standards

- Max ~500 lines per file
- Extract child components when needed
- Move logic to `.svelte.ts` files
- No magic numbers (use constants.ts)
- Every directory has README.md

## Outputs for Other Agents

1. **Working UI shell** - Can test with mock data
2. **Component library** - Reusable across features
3. **IPC client ready** - Backend can send/receive
4. **Mock mode** - Development without backend

## Acceptance Criteria

- [ ] Svelte app builds without errors
- [ ] Design system matches spec
- [ ] Layout renders with semi-transparent panels
- [ ] IPC bridge sends/receives messages
- [ ] View models receive C# pushes
- [ ] No optimistic updates in code
- [ ] Components use Svelte 5 runes
- [ ] All directories have README.md

## Sync Point

**End of Phase 1**: Must pass IPC integration test with Backend Agent before Phase 2 begins.
