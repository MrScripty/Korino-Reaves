# View Models

## Purpose

This directory contains **Svelte 5 view models** that hold a read-only view of data pushed from the C# backend. View models are the single point of truth for UI state (as received from C#).

**CRITICAL**: These are presentation layer caches, not sources of truth. C# owns all data.

## Contents

| File | Description |
|------|-------------|
| `asset.svelte.ts` | Current asset info, loading state, modified state |
| `tree.svelte.ts` | Tree structure, selection, expansion state |
| `properties.svelte.ts` | Property values for selected node |
| `diff.svelte.ts` | Diff comparison results |

## Design Pattern

Each view model follows this pattern:

```typescript
// 1. State received from C# (read-only view)
export let data = $state<DataType | null>(null);

// 2. Transient UI state (Svelte can own this)
export let isLoading = $state(false);

// 3. Derived state for display
export let displayValue = $derived(formatData(data));

// 4. IPC listeners to receive C# updates
ipc.onAction<DataType>('type', 'action', (payload) => {
    data = payload;
});

// 5. Actions that forward to C# (NO local state mutation)
export function doAction(param: string) {
    ipc.send({ type: 'type', action: 'action', payload: { param } });
    // C# will push the update back
}
```

## Design Decisions

- **No Optimistic Updates**: Never update state locally before C# confirms
- **Svelte 5 Runes**: Using `$state` and `$derived` for reactivity
- **IPC Listeners**: Subscribe to C# pushes using `ipc.on()` and `ipc.onAction()`
- **Action Forwarding**: All user actions immediately forwarded to C# via IPC

## Dependencies

### Internal
- `$lib/bridge/ipc` - IPC communication
- `$lib/bridge/types` - Shared type definitions

### External
- None (pure Svelte/TypeScript)

## Usage Examples

### Reading State

```svelte
<script>
    import * as asset from '$lib/view-models/asset.svelte';
</script>

{#if asset.isLoading}
    <Loading />
{:else if asset.assetInfo}
    <AssetDisplay info={asset.assetInfo} />
{/if}
```

### Triggering Actions

```svelte
<script>
    import * as tree from '$lib/view-models/tree.svelte';

    function handleNodeClick(id: string) {
        // Forward to C#, don't update locally
        tree.selectNode(id);
    }
</script>
```

## Agent Ownership

**Owner**: 02-frontend-agent
