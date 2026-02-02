# Layout Components

## Purpose

Foundational layout components for the application structure. These handle the overall page layout, panel organization, and resizable split panes.

## Contents

| Component | Description |
|-----------|-------------|
| `AppShell.svelte` | Main application wrapper with menu, content, and status areas |
| `Panel.svelte` | Semi-transparent panel container with optional header and collapse |
| `Splitter.svelte` | Draggable divider for resizing adjacent elements |
| `SplitPane.svelte` | Container with two resizable panes and a splitter |

## Design Decisions

- **Semi-Transparency**: Panels use backdrop blur and alpha for floating effect
- **Keyboard Accessible**: Splitters support arrow key resizing
- **Flexible Composition**: Uses Svelte 5 snippets for content slots

## Usage Examples

### Basic Layout

```svelte
<AppShell>
    {#snippet menuBar()}
        <MenuBar />
    {/snippet}

    <SplitPane>
        {#snippet first()}
            <Panel title="Tree">
                <AssetTree />
            </Panel>
        {/snippet}
        {#snippet second()}
            <div>Main content</div>
        {/snippet}
    </SplitPane>

    {#snippet statusBar()}
        <StatusBar />
    {/snippet}
</AppShell>
```

### Collapsible Panel

```svelte
<Panel title="Details" collapsible>
    <PropertyGrid />
</Panel>
```

## Agent Ownership

**Owner**: 02-frontend-agent
