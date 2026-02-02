<!--
    Panel Component

    Semi-transparent panel with optional header and collapsible content.
    Floats over the viewport with blur effect.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Props {
        /** Panel title */
        title?: string;
        /** Whether panel can be collapsed */
        collapsible?: boolean;
        /** Initial collapsed state */
        collapsed?: boolean;
        /** Whether to use solid background instead of transparent */
        solid?: boolean;
        /** Additional CSS class */
        class?: string;
        /** Panel content */
        children: Snippet;
        /** Optional header actions */
        headerActions?: Snippet;
    }

    let {
        title,
        collapsible = false,
        collapsed = $bindable(false),
        solid = false,
        class: className = '',
        children,
        headerActions,
    }: Props = $props();

    // Transient UI state - OK for Svelte to own
    let isCollapsed = $state(collapsed);

    function toggleCollapse() {
        if (collapsible) {
            isCollapsed = !isCollapsed;
            collapsed = isCollapsed;
        }
    }
</script>

<div
    class="panel {className}"
    class:panel-solid={solid}
    class:collapsed={isCollapsed}
>
    {#if title || headerActions}
        <header class="panel-header" class:clickable={collapsible}>
            {#if collapsible}
                <button
                    class="collapse-button"
                    onclick={toggleCollapse}
                    aria-expanded={!isCollapsed}
                    aria-label={isCollapsed ? 'Expand panel' : 'Collapse panel'}
                >
                    <svg
                        class="collapse-icon"
                        class:rotated={!isCollapsed}
                        viewBox="0 0 16 16"
                        fill="currentColor"
                    >
                        <path
                            d="M6 4l4 4-4 4"
                            stroke="currentColor"
                            stroke-width="1.5"
                            fill="none"
                        />
                    </svg>
                </button>
            {/if}

            {#if title}
                <h3 class="panel-title" onclick={toggleCollapse}>
                    {title}
                </h3>
            {/if}

            {#if headerActions}
                <div class="header-actions">
                    {@render headerActions()}
                </div>
            {/if}
        </header>
    {/if}

    {#if !isCollapsed}
        <div class="panel-content">
            {@render children()}
        </div>
    {/if}
</div>

<style>
    .panel {
        display: flex;
        flex-direction: column;
        background: var(--panel-bg);
        backdrop-filter: blur(var(--panel-blur));
        -webkit-backdrop-filter: blur(var(--panel-blur));
        border: 1px solid var(--panel-border);
        border-radius: var(--radius-lg);
        overflow: hidden;
    }

    .panel-solid {
        background: var(--bg-secondary);
        border-color: var(--border);
        backdrop-filter: none;
        -webkit-backdrop-filter: none;
    }

    .panel.collapsed {
        overflow: visible;
    }

    .panel-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border);
        min-height: 32px;
    }

    .panel-header.clickable {
        cursor: pointer;
        user-select: none;
    }

    .panel-header.clickable:hover {
        background: var(--bg-hover);
    }

    .collapsed .panel-header {
        border-bottom: none;
    }

    .collapse-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 20px;
        height: 20px;
        padding: 0;
        background: transparent;
        border: none;
        cursor: pointer;
        color: var(--text-secondary);
    }

    .collapse-button:hover {
        color: var(--text-primary);
    }

    .collapse-icon {
        width: 12px;
        height: 12px;
        transition: transform var(--transition-fast);
    }

    .collapse-icon.rotated {
        transform: rotate(90deg);
    }

    .panel-title {
        flex: 1;
        margin: 0;
        font-size: var(--text-sm);
        font-weight: 600;
        color: var(--text-primary);
        cursor: inherit;
    }

    .header-actions {
        display: flex;
        align-items: center;
        gap: var(--space-1);
    }

    .panel-content {
        flex: 1;
        overflow: auto;
    }
</style>
