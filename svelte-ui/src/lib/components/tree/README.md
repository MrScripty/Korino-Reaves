# Tree Components

## Purpose

Components for rendering the asset tree view with virtual scrolling, selection, and type-based color coding.

## Contents

| Component | Description |
|-----------|-------------|
| `AssetTree.svelte` | Virtual scrolling tree container with keyboard navigation |
| `TreeNode.svelte` | Individual node with expand/collapse, icon, and value preview |
| `TreeToolbar.svelte` | Search input and expand/collapse all controls |

## Design Decisions

- **Virtual Scrolling**: Only renders visible nodes for performance
- **Color Coding**: Nodes colored by type (export, array, struct, etc.)
- **Keyboard Navigation**: Arrow keys for navigation, Enter for select

## Data Flow

All tree data comes from the `tree.svelte.ts` view model:

```
C# Backend
    │
    ├─ Pushes tree structure
    ▼
tree.svelte.ts (view model)
    │
    ├─ nodes: TreeNode[]
    ├─ selection: SelectionState
    ▼
AssetTree.svelte
    │
    ├─ Flattens tree for virtual scroll
    ├─ Renders visible TreeNode components
    ▼
TreeNode.svelte
    │
    ├─ Displays individual node
    ├─ Forwards clicks to tree.selectNode()
    └─ (C# receives IPC, pushes update)
```

## Usage Examples

### In Main Layout

```svelte
<Panel title="Asset Tree">
    <TreeToolbar />
    <AssetTree />
</Panel>
```

## Agent Ownership

**Owner**: 02-frontend-agent
