# Svelte UI

## Purpose

This directory contains the **Svelte 5 frontend** for the UAsset Viewer application. The frontend is a **pure presentation layer** - all data comes from the C# backend via IPC.

## Key Principle: Backend-Owned Data

**ALL application data lives in C#. Svelte only displays what C# tells it to display.**

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

## Directory Structure

```
svelte-ui/
├── src/
│   ├── app.css                    # Design system (colors, typography, utilities)
│   ├── app.html                   # HTML template
│   ├── lib/
│   │   ├── bridge/                # IPC communication layer
│   │   │   ├── types.ts           # Shared contracts (from 00-shared-contracts)
│   │   │   └── ipc.ts             # IPC send/receive wrapper
│   │   ├── view-models/           # State received from C#
│   │   │   ├── asset.svelte.ts    # Asset info state
│   │   │   ├── tree.svelte.ts     # Tree structure state
│   │   │   ├── properties.svelte.ts # Property grid state
│   │   │   └── diff.svelte.ts     # Diff comparison state
│   │   ├── components/            # Svelte components
│   │   │   ├── layout/            # AppShell, Panel, Splitter
│   │   │   ├── tree/              # AssetTree, TreeNode
│   │   │   ├── properties/        # PropertyGrid, editors
│   │   │   ├── toolbar/           # MenuBar, StatusBar
│   │   │   └── common/            # Modal, Tabs, VirtualList
│   │   └── constants.ts           # UI constants (no magic numbers)
│   └── routes/
│       ├── +layout.svelte         # Root layout
│       └── +page.svelte           # Main application page
├── static/                        # Static assets (fonts)
├── package.json
├── vite.config.ts
├── svelte.config.js
└── tsconfig.json
```

## Design Decisions

- **Svelte 5 Runes**: Using `$state`, `$derived`, `$effect` instead of legacy stores
- **View Models**: Centralized state management via `.svelte.ts` files
- **No Optimistic Updates**: All state changes wait for C# confirmation
- **Virtual Scrolling**: Large trees/lists use virtual rendering
- **CSS Custom Properties**: Design tokens defined in `app.css`

## What Svelte CAN Hold (Transient UI State)

| Allowed | Example | Reason |
|---------|---------|--------|
| Animation state | `isAnimating` | Pure visual |
| Input focus | `isFocused` | Browser state |
| Hover state | `isHovered` | Visual feedback |
| Pending input | Text being typed | Cleared on submit |
| Drag state | `isDragging` | Visual feedback |

## What Svelte CANNOT Hold

| Forbidden | Why |
|-----------|-----|
| Asset data | C# owns all asset information |
| Tree structure | C# builds and owns the tree |
| Selection state | C# tracks what's selected |
| Property values | C# owns all property data |
| Any persistent data | Must come from C# |

## Development

```bash
# Install dependencies
npm install

# Start dev server
npm run dev

# Build for production
npm run build

# Type checking
npm run check

# Linting
npm run lint
```

## Agent Ownership

**Owner**: 02-frontend-agent

This directory is owned by the Frontend Agent. The only exception is `src/lib/bridge/types.ts`, which is owned by 00-shared-contracts.

## Related Documentation

- [ARCHITECTURE.md](../plans/ARCHITECTURE.md) - Overall system architecture
- [02-frontend-agent.md](../plans/02-frontend-agent.md) - Frontend agent specification
- [00-shared-contracts.md](../plans/00-shared-contracts.md) - IPC contracts
