# Components

## Purpose

This directory contains **Svelte 5 UI components** for the UAsset Viewer application. Components are pure presentation - they receive data via props or view models and forward user actions to C# via IPC.

## Directory Structure

```
components/
├── layout/          # Application layout primitives
│   ├── AppShell.svelte       # Main app structure
│   ├── Panel.svelte          # Semi-transparent panel container
│   ├── Splitter.svelte       # Resizable divider
│   └── SplitPane.svelte      # Two-pane resizable container
├── tree/            # Asset tree components
│   ├── AssetTree.svelte      # Virtual scrolling tree container
│   ├── TreeNode.svelte       # Individual tree node
│   └── TreeToolbar.svelte    # Search and controls
├── properties/      # Property grid components
│   ├── PropertyGrid.svelte   # Property list container
│   ├── PropertyRow.svelte    # Single property display
│   └── editors/              # Type-specific editors
│       ├── StringEditor.svelte
│       ├── NumberEditor.svelte
│       ├── BoolEditor.svelte
│       ├── EnumEditor.svelte
│       ├── VectorEditor.svelte
│       └── ColorEditor.svelte
├── toolbar/         # Application bars
│   ├── MenuBar.svelte        # Top menu bar
│   └── StatusBar.svelte      # Bottom status bar
└── common/          # Shared utilities
    ├── Modal.svelte          # Dialog modal
    ├── Tabs.svelte           # Tab navigation
    ├── ContextMenu.svelte    # Right-click menu
    └── VirtualList.svelte    # Generic virtual scrolling
```

## Design Decisions

- **Svelte 5 Runes**: All components use `$props()`, `$state`, `$derived`
- **Snippets**: Using Svelte 5 snippets for composition
- **CSS Custom Properties**: Styling uses design tokens from `app.css`
- **Accessibility**: ARIA attributes, keyboard navigation
- **Virtual Scrolling**: Large lists use windowing for performance

## Component Guidelines

### Props Pattern

```svelte
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Props {
        title: string;
        children: Snippet;
        onAction?: () => void;
    }

    let { title, children, onAction }: Props = $props();
</script>
```

### State Rules

1. **Transient UI state only** - hover, focus, animation, pending input
2. **Never store app data** - comes from view models
3. **Forward all actions** - via view model functions

### Styling Rules

1. Use CSS custom properties from `app.css`
2. Scoped styles only (`<style>` not `<style global>`)
3. No inline styles except for dynamic values

## Dependencies

### Internal
- `$lib/constants` - UI constants
- `$lib/view-models/*` - State and actions
- `$lib/bridge/types` - TypeScript types

### External
- None (pure Svelte)

## Agent Ownership

**Owner**: 02-frontend-agent
