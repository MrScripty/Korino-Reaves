<!--
    DockTabBar Component

    Tab strip for a dock zone. Tabs can be clicked to activate and
    dragged (via mouse events) to move panels between zones.
-->
<script lang="ts">
    import type { PanelId, ZoneId } from './dockTypes';
    import { PANEL_DEFINITIONS } from './panelRegistry';
    import { dock } from '$lib/view-models/dock.svelte';
    import { DOCK } from '$lib/constants';

    interface Props {
        zoneId: ZoneId;
    }

    let { zoneId }: Props = $props();

    const panels = $derived(dock.getZonePanels(zoneId));
    const activePanel = $derived(dock.getActivePanel(zoneId));

    function handleTabMouseDown(e: MouseEvent, panelId: PanelId) {
        if (e.button !== 0) return;
        if (PANEL_DEFINITIONS[panelId].locked) return;

        const startX = e.clientX;
        const startY = e.clientY;
        let didStartDrag = false;

        function onMouseMove(ev: MouseEvent) {
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;

            if (!didStartDrag) {
                if (Math.abs(dx) > DOCK.DRAG_THRESHOLD || Math.abs(dy) > DOCK.DRAG_THRESHOLD) {
                    didStartDrag = true;
                    dock.startDrag(panelId, zoneId, ev.clientX, ev.clientY);
                }
                return;
            }

            dock.updateDrag(ev.clientX, ev.clientY);
        }

        function onMouseUp() {
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);

            if (didStartDrag) {
                dock.completeDrag();
            }
        }

        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
    }

    function handleMouseEnter() {
        if (dock.isDragging) {
            dock.setDragOverZone(zoneId);
        }
    }

    function handleMouseLeave() {
        if (dock.isDragging && dock.dragOverZone === zoneId) {
            dock.setDragOverZone(null);
        }
    }

    function handleCloseTab(e: MouseEvent, panelId: PanelId) {
        e.stopPropagation();
        dock.hidePanel(panelId);
    }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
    class="dock-tab-bar"
    class:drop-target={dock.isDragging && dock.dragOverZone === zoneId && dock.dragSourceZone !== zoneId}
    data-dock-zone={zoneId}
    role="tablist"
    tabindex="-1"
    onmouseenter={handleMouseEnter}
    onmouseleave={handleMouseLeave}
>
    {#each panels as panelId (panelId)}
        <button
            class="dock-tab"
            class:active={activePanel === panelId}
            class:dragging={dock.dragPanel === panelId}
            role="tab"
            aria-selected={activePanel === panelId}
            onclick={() => dock.activatePanel(zoneId, panelId)}
            onmousedown={(e) => handleTabMouseDown(e, panelId)}
        >
            <span class="tab-label">{PANEL_DEFINITIONS[panelId].title}</span>
            {#if !PANEL_DEFINITIONS[panelId].locked && panels.length > 1}
                <!-- svelte-ignore a11y_click_events_have_key_events -->
                <span
                    class="tab-close"
                    role="button"
                    tabindex="-1"
                    aria-label="Close {PANEL_DEFINITIONS[panelId].title}"
                    onclick={(e) => handleCloseTab(e, panelId)}
                >
                    <svg viewBox="0 0 16 16" fill="currentColor" width="10" height="10">
                        <path d="M4.11 3.05a.75.75 0 0 0-1.06 1.06L6.94 8l-3.89 3.89a.75.75 0 1 0 1.06 1.06L8 9.06l3.89 3.89a.75.75 0 1 0 1.06-1.06L9.06 8l3.89-3.89a.75.75 0 0 0-1.06-1.06L8 6.94 4.11 3.05z" />
                    </svg>
                </span>
            {/if}
        </button>
    {/each}
</div>

<style>
    .dock-tab-bar {
        display: flex;
        gap: var(--space-1);
        padding: var(--space-1);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
        min-height: 30px;
        flex-shrink: 0;
        transition: border-color 0.15s;
    }

    .dock-tab-bar.drop-target {
        border-bottom: 2px dashed var(--accent-primary);
        background: color-mix(in srgb, var(--accent-primary) 8%, var(--bg-secondary));
    }

    .dock-tab {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-1) var(--space-2);
        font-size: var(--text-sm);
        border-radius: var(--radius-md) var(--radius-md) 0 0;
        border: 1px solid transparent;
        border-bottom: none;
        background: transparent;
        color: var(--text-secondary);
        cursor: pointer;
        transition: all 0.1s;
        user-select: none;
        white-space: nowrap;
    }

    .dock-tab:hover:not(.dragging) {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .dock-tab.active {
        background: var(--bg-tertiary);
        border-color: var(--border);
        color: var(--text-primary);
    }

    .dock-tab.dragging {
        opacity: 0.4;
    }

    .tab-label {
        pointer-events: none;
    }

    .tab-close {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 16px;
        height: 16px;
        border-radius: var(--radius-sm);
        color: var(--text-muted);
        cursor: pointer;
        opacity: 0;
        transition: opacity 0.1s, background 0.1s;
    }

    .dock-tab:hover .tab-close,
    .dock-tab.active .tab-close {
        opacity: 1;
    }

    .tab-close:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }
</style>
