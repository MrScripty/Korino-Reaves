<!--
    DockDragOverlay Component

    Floating ghost tab label that follows the cursor during panel drag.
    Uses position: fixed and pointer-events: none to avoid interfering
    with mouseenter/mouseleave on drop targets.
-->
<script lang="ts">
    import { dock } from '$lib/view-models/dock.svelte';
    import { PANEL_DEFINITIONS } from './panelRegistry';
</script>

{#if dock.isDragging && dock.dragPanel}
    <div
        class="dock-drag-overlay"
        style="left: {dock.dragPosition.x + 12}px; top: {dock.dragPosition.y + 12}px;"
    >
        {PANEL_DEFINITIONS[dock.dragPanel].title}
    </div>
{/if}

<style>
    .dock-drag-overlay {
        position: fixed;
        z-index: var(--z-modal, 1000);
        pointer-events: none;
        padding: var(--space-1) var(--space-3);
        background: var(--bg-secondary);
        border: 1px solid var(--accent-primary);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
        color: var(--text-primary);
        box-shadow: var(--shadow-lg);
        white-space: nowrap;
        opacity: 0.9;
    }
</style>
