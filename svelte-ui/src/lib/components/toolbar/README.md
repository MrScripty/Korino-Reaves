# Toolbar Components

## Purpose

Application-level toolbars: menu bar and status bar.

## Contents

| Component | Description |
|-----------|-------------|
| `MenuBar.svelte` | Top menu bar with File, Edit, View, Tools, Help menus |
| `StatusBar.svelte` | Bottom bar showing asset info and loading state |

## Design Decisions

- **Solid Background**: Toolbars use solid colors (not transparent like panels)
- **Keyboard Navigation**: Menus support arrow key navigation
- **IPC Actions**: Menu items send actions to C# backend

## Menu Structure

```
File           Edit        View            Tools               Help
├── Open       ├── Undo    ├── Expand All  ├── Compare Assets  ├── Documentation
├── Open Recent├── Redo    ├── Collapse    ├── Mod Porting     ├── Shortcuts
├── ─────      ├── ─────   ├── ─────       ├── ─────           ├── ─────
├── Save       └── Find    └── Reset       └── Load Mappings   └── About
├── Save As
├── ─────
├── Export JSON
├── ─────
└── Close
```

## Usage Examples

### In AppShell

```svelte
<AppShell>
    {#snippet menuBar()}
        <MenuBar />
    {/snippet}

    <!-- Content -->

    {#snippet statusBar()}
        <StatusBar />
    {/snippet}
</AppShell>
```

## Agent Ownership

**Owner**: 02-frontend-agent
