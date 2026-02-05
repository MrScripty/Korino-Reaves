<!--
    MenuBar Component

    Application menu bar with File, Edit, View, and Help menus.
    File dialogs use Svelte components, other actions forward to C# via IPC.
-->
<script lang="ts">
    import { ipc } from '$lib/bridge/ipc';
    import { asset } from '$lib/view-models/asset.svelte';
    import { project } from '$lib/view-models/project.svelte';
    import { pak } from '$lib/view-models/pak.svelte';
    import { fileBrowser } from '$lib/view-models/fileBrowser.svelte';

    interface MenuItem {
        label: string;
        action?: string;
        type?: string; // IPC message type or 'svelte' for Svelte dialogs
        shortcut?: string;
        disabled?: boolean;
        separator?: boolean;
        submenu?: MenuItem[];
    }

    // Transient UI state - which menu is open
    let openMenu = $state<string | null>(null);

    const menus: Record<string, MenuItem[]> = {
        File: [
            { label: 'Open Project...', type: 'svelte', action: 'openProject', shortcut: 'Ctrl+O' },
            { label: 'Import PAK...', type: 'svelte', action: 'importPak', shortcut: 'Ctrl+I' },
            { separator: true, label: '' },
            { label: 'Open Asset...', type: 'svelte', action: 'openAsset' },
            { label: 'Open Recent', submenu: [] },
            { separator: true, label: '' },
            { label: 'Save', type: 'asset', action: 'save', shortcut: 'Ctrl+S', disabled: !asset.hasAsset() },
            { label: 'Save As...', type: 'svelte', action: 'saveAs', shortcut: 'Ctrl+Shift+S', disabled: !asset.hasAsset() },
            { separator: true, label: '' },
            { label: 'Export JSON...', type: 'svelte', action: 'exportJson', disabled: !asset.hasAsset() },
            { separator: true, label: '' },
            { label: 'Close Project', type: 'project', action: 'close', disabled: !project.hasProject },
            { label: 'Close Asset', type: 'asset', action: 'close', disabled: !asset.hasAsset() },
        ],
        Edit: [
            { label: 'Undo', action: 'edit.undo', shortcut: 'Ctrl+Z' },
            { label: 'Redo', action: 'edit.redo', shortcut: 'Ctrl+Y' },
            { separator: true, label: '' },
            { label: 'Find...', action: 'edit.find', shortcut: 'Ctrl+F' },
        ],
        View: [
            { label: 'Expand All', action: 'view.expandAll' },
            { label: 'Collapse All', action: 'view.collapseAll' },
            { separator: true, label: '' },
            { label: 'Reset Layout', action: 'view.resetLayout' },
        ],
        Tools: [
            { label: 'Compare Assets...', action: 'tools.compare' },
            { label: 'Mod Porting Wizard...', action: 'tools.modPort' },
            { separator: true, label: '' },
            { label: 'Load Mappings...', action: 'tools.loadMappings' },
        ],
        Help: [
            { label: 'Documentation', action: 'help.docs' },
            { label: 'Keyboard Shortcuts', action: 'help.shortcuts' },
            { separator: true, label: '' },
            { label: 'About', action: 'help.about' },
        ],
    };

    function handleMenuClick(menuName: string) {
        openMenu = openMenu === menuName ? null : menuName;
    }

    function handleItemClick(item: MenuItem) {
        if (item.disabled || item.separator || item.submenu) return;

        // Handle Svelte-based dialogs
        if (item.type === 'svelte' && item.action) {
            handleSvelteAction(item.action);
            openMenu = null;
            return;
        }

        // Handle IPC-based actions
        if (item.action) {
            const message = {
                type: item.type ?? 'asset',
                action: item.action,
                payload: {},
            };
            console.log('[MenuBar] Sending IPC message:', message);
            ipc.send(message);
        }
        openMenu = null;
    }

    function handleSvelteAction(action: string) {
        switch (action) {
            case 'openProject':
                fileBrowser.openProjectDialog((path) => {
                    console.log('[MenuBar] Project selected:', path);
                    project.openProject(path);
                });
                break;

            case 'importPak':
                fileBrowser.openImportPakDialog((path) => {
                    console.log('[MenuBar] PAK file selected:', path);
                    pak.openDialog(path);
                });
                break;

            case 'openAsset':
                fileBrowser.openAssetDialog((path) => {
                    console.log('[MenuBar] Asset selected:', path);
                    // If it's a PAK, open import dialog; otherwise open directly
                    if (path.toLowerCase().endsWith('.pak')) {
                        pak.openDialog(path);
                    } else {
                        ipc.send({
                            type: 'asset',
                            action: 'open',
                            payload: { filePath: path },
                        });
                    }
                });
                break;

            case 'saveAs':
                fileBrowser.open({
                    title: 'Save Asset As',
                    mode: 'file',
                    filters: ['*.uasset', '*.umap'],
                    onSelect: (path) => {
                        console.log('[MenuBar] Save As path:', path);
                        ipc.send({
                            type: 'asset',
                            action: 'saveAs',
                            payload: { filePath: path },
                        });
                    },
                });
                break;

            case 'exportJson':
                fileBrowser.openExportDialog((path) => {
                    console.log('[MenuBar] Export path:', path);
                    ipc.send({
                        type: 'asset',
                        action: 'export',
                        payload: { filePath: path },
                    });
                });
                break;

            default:
                console.warn('[MenuBar] Unknown Svelte action:', action);
        }
    }

    function handleClickOutside(event: MouseEvent) {
        const target = event.target as HTMLElement;
        if (!target.closest('.menu-bar')) {
            openMenu = null;
        }
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            openMenu = null;
        }
    }
</script>

<svelte:window onclick={handleClickOutside} onkeydown={handleKeyDown} />

<nav class="menu-bar" role="menubar">
    <div class="app-title">UAsset Viewer</div>

    {#each Object.entries(menus) as [menuName, items]}
        <div class="menu-container">
            <button
                class="menu-trigger"
                class:active={openMenu === menuName}
                role="menuitem"
                aria-haspopup="true"
                aria-expanded={openMenu === menuName}
                onclick={() => handleMenuClick(menuName)}
            >
                {menuName}
            </button>

            {#if openMenu === menuName}
                <div class="menu-dropdown" role="menu">
                    {#each items as item}
                        {#if item.separator}
                            <div class="menu-separator" role="separator"></div>
                        {:else if item.submenu}
                            <button class="menu-item has-submenu" disabled>
                                {item.label}
                                <span class="submenu-arrow">▶</span>
                            </button>
                        {:else}
                            <button
                                class="menu-item"
                                class:disabled={item.disabled}
                                role="menuitem"
                                disabled={item.disabled}
                                onclick={() => handleItemClick(item)}
                            >
                                <span class="item-label">{item.label}</span>
                                {#if item.shortcut}
                                    <span class="item-shortcut">{item.shortcut}</span>
                                {/if}
                            </button>
                        {/if}
                    {/each}
                </div>
            {/if}
        </div>
    {/each}
</nav>

<style>
    .menu-bar {
        display: flex;
        align-items: center;
        height: 100%;
        padding: 0 var(--space-2);
        gap: var(--space-1);
    }

    .app-title {
        font-weight: 600;
        font-size: var(--text-sm);
        color: var(--text-primary);
        margin-right: var(--space-4);
        padding: 0 var(--space-2);
    }

    .menu-container {
        position: relative;
    }

    .menu-trigger {
        padding: var(--space-1) var(--space-2);
        font-size: var(--text-sm);
        border-radius: var(--radius-md);
        color: var(--text-secondary);
    }

    .menu-trigger:hover,
    .menu-trigger.active {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .menu-dropdown {
        position: absolute;
        top: 100%;
        left: 0;
        min-width: 200px;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-lg);
        padding: var(--space-1) 0;
        z-index: var(--z-dropdown);
    }

    .menu-item {
        display: flex;
        align-items: center;
        width: 100%;
        padding: var(--space-2) var(--space-3);
        font-size: var(--text-sm);
        text-align: left;
        color: var(--text-primary);
    }

    .menu-item:hover:not(:disabled) {
        background: var(--bg-hover);
    }

    .menu-item.disabled,
    .menu-item:disabled {
        color: var(--text-disabled);
    }

    .menu-item .item-label {
        flex: 1;
    }

    .menu-item .item-shortcut {
        color: var(--text-muted);
        font-size: var(--text-xs);
        margin-left: var(--space-4);
    }

    .menu-item.has-submenu .submenu-arrow {
        color: var(--text-muted);
        font-size: var(--text-xs);
    }

    .menu-separator {
        height: 1px;
        background: var(--border);
        margin: var(--space-1) 0;
    }
</style>
