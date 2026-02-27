<!--
    DockContainer Component

    Top-level layout replacing the hardcoded layout in +page.svelte.
    Uses SplitPane for zone boundary resizing and renders DockZone
    components for left, right, and bottom zones. The center zone
    (viewport) is rendered directly without tabs.
-->
<script lang="ts">
    import SplitPane from '$lib/components/layout/SplitPane.svelte';
    import ViewportPreview from '$lib/components/viewport/ViewportPreview.svelte';
    import DockZone from './DockZone.svelte';
    import DockDragOverlay from './DockDragOverlay.svelte';
    import { dock } from '$lib/view-models/dock.svelte';
    import { scene } from '$lib/view-models/scene.svelte';
    import { DOCK } from '$lib/constants';

    let isResizingBottom = $state(false);
    let isResizingRight = $state(false);

    // Auto-show/hide Scene Outliner based on scene.isActive
    $effect(() => {
        const active = scene.isActive;
        const currentZone = dock.findPanelZone('sceneOutliner');

        if (active && !currentZone) {
            dock.showPanel('sceneOutliner', 'right');
        } else if (!active && currentZone) {
            dock.hidePanel('sceneOutliner', true);
        }
    });

    const showRight = $derived(!dock.isZoneEmpty('right'));
    const showBottom = $derived(!dock.isZoneEmpty('bottom') && !dock.collapsed.bottom);

    function startBottomResize(e: MouseEvent) {
        e.preventDefault();
        const startY = e.clientY;
        const startHeight = dock.sizes.bottomHeight;

        function onMouseMove(ev: MouseEvent) {
            const delta = startY - ev.clientY;
            const newHeight = Math.max(
                DOCK.ZONE_MIN_HEIGHT,
                Math.min(DOCK.ZONE_MAX_HEIGHT, startHeight + delta),
            );
            dock.setBottomHeight(newHeight);
        }

        function onMouseUp() {
            isResizingBottom = false;
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);
        }

        isResizingBottom = true;
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
    }

    function startRightResize(e: MouseEvent) {
        e.preventDefault();
        const startX = e.clientX;
        const startWidth = dock.sizes.rightWidth;

        function onMouseMove(ev: MouseEvent) {
            // Dragging left increases width
            const delta = startX - ev.clientX;
            const newWidth = Math.max(
                DOCK.ZONE_MIN_WIDTH,
                Math.min(DOCK.ZONE_MAX_WIDTH, startWidth + delta),
            );
            dock.setRightWidth(newWidth);
        }

        function onMouseUp() {
            isResizingRight = false;
            window.removeEventListener('mousemove', onMouseMove);
            window.removeEventListener('mouseup', onMouseUp);
        }

        isResizingRight = true;
        window.addEventListener('mousemove', onMouseMove);
        window.addEventListener('mouseup', onMouseUp);
    }
</script>

<div class="dock-container" class:dragging={dock.isDragging}>
    <SplitPane
        direction="horizontal"
        initialSize={dock.sizes.leftWidth}
        minSize={DOCK.ZONE_MIN_WIDTH}
        maxSize={DOCK.ZONE_MAX_WIDTH}
    >
        {#snippet first()}
            <DockZone zoneId="left" />
        {/snippet}

        {#snippet second()}
            <div class="center-right-area" class:resizing-right={isResizingRight}>
                <!-- Viewport + Bottom zone -->
                <div class="viewport-bottom-area" class:resizing-bottom={isResizingBottom}>
                    <!-- Viewport (center zone, no tabs) -->
                    <div class="viewport">
                        <ViewportPreview />
                    </div>

                    <!-- Bottom zone -->
                    {#if showBottom}
                        <!-- svelte-ignore a11y_no_static_element_interactions -->
                        <div
                            class="bottom-resize-handle"
                            onmousedown={startBottomResize}
                            class:active={isResizingBottom}
                        ></div>
                        <div class="bottom-zone" style="height: {dock.sizes.bottomHeight}px">
                            <DockZone zoneId="bottom" class="bottom-dock-zone" />
                        </div>
                    {/if}

                    <!-- Bottom panel toggle -->
                    {#if !dock.isZoneEmpty('bottom')}
                        <button
                            class="bottom-toggle"
                            style="bottom: {dock.collapsed.bottom ? 0 : (showBottom ? dock.sizes.bottomHeight : 0)}px"
                            onclick={() => dock.toggleBottomCollapsed()}
                            aria-label={dock.collapsed.bottom ? 'Show bottom panel' : 'Hide bottom panel'}
                        >
                            <svg
                                viewBox="0 0 16 16"
                                fill="currentColor"
                                class:rotated={dock.collapsed.bottom}
                            >
                                <path d="M4 8l4 4 4-4H4z" />
                            </svg>
                        </button>
                    {/if}
                </div>

                <!-- Right zone -->
                {#if showRight}
                    <!-- svelte-ignore a11y_no_static_element_interactions -->
                    <div
                        class="right-resize-handle"
                        onmousedown={startRightResize}
                        class:active={isResizingRight}
                    ></div>
                    <div class="right-zone" style="width: {dock.sizes.rightWidth}px">
                        <DockZone zoneId="right" />
                    </div>
                {/if}
            </div>
        {/snippet}
    </SplitPane>

    <DockDragOverlay />
</div>

<style>
    .dock-container {
        height: 100%;
        display: flex;
        flex-direction: column;
        position: relative;
    }

    .dock-container.dragging {
        user-select: none;
    }

    .center-right-area {
        height: 100%;
        display: flex;
        flex-direction: row;
    }

    .viewport-bottom-area {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        position: relative;
    }

    .viewport-bottom-area.resizing-bottom {
        user-select: none;
        cursor: ns-resize;
    }

    .center-right-area.resizing-right {
        user-select: none;
        cursor: ew-resize;
    }

    .viewport {
        flex: 1;
        background: var(--bg-primary);
        position: relative;
        overflow: hidden;
        min-height: 0;
    }

    .bottom-zone {
        flex-shrink: 0;
        overflow: hidden;
        display: flex;
        flex-direction: column;
    }

    :global(.bottom-dock-zone) {
        border-radius: 0;
        border-left: none;
        border-right: none;
        border-bottom: none;
    }

    .bottom-resize-handle {
        height: 4px;
        flex-shrink: 0;
        cursor: ns-resize;
        background: transparent;
        border-top: 1px solid var(--border);
        transition: background 0.15s;
    }

    .bottom-resize-handle:hover,
    .bottom-resize-handle.active {
        background: var(--text-muted);
    }

    .right-resize-handle {
        width: 4px;
        flex-shrink: 0;
        cursor: ew-resize;
        background: transparent;
        border-left: 1px solid var(--border);
        transition: background 0.15s;
    }

    .right-resize-handle:hover,
    .right-resize-handle.active {
        background: var(--text-muted);
    }

    .right-zone {
        flex-shrink: 0;
        overflow: hidden;
        display: flex;
        flex-direction: column;
    }

    .bottom-toggle {
        position: absolute;
        right: var(--space-2);
        width: 24px;
        height: 16px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-bottom: none;
        border-radius: var(--radius-md) var(--radius-md) 0 0;
        cursor: pointer;
        z-index: var(--z-panel);
        padding: 0;
    }

    .bottom-toggle:hover {
        background: var(--bg-hover);
    }

    .bottom-toggle svg {
        width: 12px;
        height: 12px;
        color: var(--text-secondary);
        transition: transform var(--transition-fast);
    }

    .bottom-toggle svg.rotated {
        transform: rotate(180deg);
    }
</style>
