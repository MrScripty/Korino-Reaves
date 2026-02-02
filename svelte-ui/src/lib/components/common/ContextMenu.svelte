<!--
    ContextMenu Component

    Right-click context menu with keyboard navigation.
-->
<script lang="ts">
    import { CONTEXT_MENU } from '$lib/constants';

    export interface ContextMenuItem {
        id: string;
        label: string;
        icon?: string;
        shortcut?: string;
        disabled?: boolean;
        separator?: boolean;
        danger?: boolean;
    }

    interface Props {
        /** Menu items */
        items: ContextMenuItem[];
        /** X position */
        x: number;
        /** Y position */
        y: number;
        /** Whether the menu is visible */
        visible: boolean;
        /** Callback when item is selected */
        onSelect: (itemId: string) => void;
        /** Callback when menu is closed */
        onClose: () => void;
    }

    let { items, x, y, visible = $bindable(false), onSelect, onClose }: Props = $props();

    let menuRef = $state<HTMLDivElement | null>(null);
    let focusedIndex = $state(-1);

    // Adjust position to stay within viewport
    let adjustedX = $derived.by(() => {
        if (!visible) return x;
        const menuWidth = CONTEXT_MENU.MIN_WIDTH;
        const maxX = window.innerWidth - menuWidth - CONTEXT_MENU.VIEWPORT_PADDING;
        return Math.min(x, maxX);
    });

    let adjustedY = $derived.by(() => {
        if (!visible) return y;
        const menuHeight = items.length * 32; // Approximate
        const maxY = window.innerHeight - menuHeight - CONTEXT_MENU.VIEWPORT_PADDING;
        return Math.min(y, maxY);
    });

    // Focus menu when visible
    $effect(() => {
        if (visible && menuRef) {
            menuRef.focus();
            focusedIndex = items.findIndex((item) => !item.separator && !item.disabled);
        }
    });

    function handleItemClick(item: ContextMenuItem) {
        if (item.disabled || item.separator) return;
        onSelect(item.id);
        onClose();
    }

    function handleKeyDown(event: KeyboardEvent) {
        const selectableItems = items
            .map((item, index) => ({ item, index }))
            .filter(({ item }) => !item.separator && !item.disabled);

        if (event.key === 'Escape') {
            event.preventDefault();
            onClose();
        } else if (event.key === 'ArrowDown') {
            event.preventDefault();
            const currentIdx = selectableItems.findIndex(({ index }) => index === focusedIndex);
            const nextIdx = (currentIdx + 1) % selectableItems.length;
            focusedIndex = selectableItems[nextIdx].index;
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            const currentIdx = selectableItems.findIndex(({ index }) => index === focusedIndex);
            const prevIdx = currentIdx <= 0 ? selectableItems.length - 1 : currentIdx - 1;
            focusedIndex = selectableItems[prevIdx].index;
        } else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            const item = items[focusedIndex];
            if (item && !item.disabled && !item.separator) {
                handleItemClick(item);
            }
        }
    }

    function handleBlur(event: FocusEvent) {
        // Close if focus leaves the menu
        const relatedTarget = event.relatedTarget as HTMLElement;
        if (!menuRef?.contains(relatedTarget)) {
            onClose();
        }
    }
</script>

{#if visible}
    <div
        bind:this={menuRef}
        class="context-menu"
        style="left: {adjustedX}px; top: {adjustedY}px"
        role="menu"
        tabindex="-1"
        onkeydown={handleKeyDown}
        onblur={handleBlur}
    >
        {#each items as item, index}
            {#if item.separator}
                <div class="context-menu-separator" role="separator"></div>
            {:else}
                <button
                    class="context-menu-item"
                    class:disabled={item.disabled}
                    class:danger={item.danger}
                    class:focused={index === focusedIndex}
                    role="menuitem"
                    aria-disabled={item.disabled}
                    onclick={() => handleItemClick(item)}
                    onmouseenter={() => { focusedIndex = index; }}
                >
                    <span class="item-label">{item.label}</span>
                    {#if item.shortcut}
                        <span class="shortcut">{item.shortcut}</span>
                    {/if}
                </button>
            {/if}
        {/each}
    </div>
{/if}

<style>
    .context-menu {
        position: fixed;
        min-width: 160px;
        max-width: 300px;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-lg);
        padding: var(--space-1) 0;
        z-index: var(--z-dropdown);
        outline: none;
    }

    .context-menu-item {
        display: flex;
        align-items: center;
        width: 100%;
        padding: var(--space-2) var(--space-3);
        font-size: var(--text-sm);
        text-align: left;
        background: transparent;
        border: none;
        color: var(--text-primary);
        cursor: pointer;
    }

    .context-menu-item:hover:not(.disabled),
    .context-menu-item.focused:not(.disabled) {
        background: var(--bg-hover);
    }

    .context-menu-item.disabled {
        color: var(--text-disabled);
        cursor: not-allowed;
    }

    .context-menu-item.danger {
        color: var(--accent-error);
    }

    .context-menu-item.danger:hover:not(.disabled),
    .context-menu-item.danger.focused:not(.disabled) {
        background: rgba(239, 68, 68, 0.1);
    }

    .item-label {
        flex: 1;
    }

    .shortcut {
        color: var(--text-muted);
        font-size: var(--text-xs);
        margin-left: var(--space-4);
    }

    .context-menu-separator {
        height: 1px;
        background: var(--border);
        margin: var(--space-1) 0;
    }
</style>
