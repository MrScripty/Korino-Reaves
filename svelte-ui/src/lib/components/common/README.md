# Common Components

## Purpose

Shared utility components used throughout the application.

## Contents

| Component | Description |
|-----------|-------------|
| `Modal.svelte` | Accessible dialog with backdrop, focus trap |
| `Tabs.svelte` | Tab navigation with keyboard support |
| `ContextMenu.svelte` | Right-click menu with keyboard navigation |
| `VirtualList.svelte` | Generic virtual scrolling for large lists |

## Design Decisions

- **Accessibility First**: Full ARIA support, keyboard navigation
- **Generic Design**: Components work with any content via snippets
- **Performance**: Virtual scrolling for large datasets

## Usage Examples

### Modal

```svelte
<script>
    let showModal = $state(false);
</script>

<Modal
    title="Confirm Action"
    bind:open={showModal}
    onClose={() => { showModal = false; }}
>
    <p>Are you sure?</p>

    {#snippet footer()}
        <button onclick={() => { showModal = false; }}>Cancel</button>
        <button class="primary" onclick={confirm}>Confirm</button>
    {/snippet}
</Modal>
```

### Tabs

```svelte
<Tabs tabs={[{ id: 'a', label: 'Tab A' }, { id: 'b', label: 'Tab B' }]}>
    {#snippet children(tabId)}
        {#if tabId === 'a'}
            <ContentA />
        {:else}
            <ContentB />
        {/if}
    {/snippet}
</Tabs>
```

### Virtual List

```svelte
<VirtualList items={largeArray} rowHeight={24}>
    {#snippet children(item, index)}
        <div>{item.name}</div>
    {/snippet}
</VirtualList>
```

## Agent Ownership

**Owner**: 02-frontend-agent
