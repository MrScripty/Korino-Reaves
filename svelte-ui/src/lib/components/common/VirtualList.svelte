<!--
    VirtualList Component

    Generic virtual scrolling list for rendering large datasets efficiently.
    Uses a custom scrollbar since CEF doesn't support native scrollbar drag.
-->
<script lang="ts" generics="T">
    import type { Snippet } from 'svelte';
    import { VIRTUAL_LIST } from '$lib/constants';

    interface Props {
        /** Items to render */
        items: T[];
        /** Height of each row in pixels */
        rowHeight?: number;
        /** Number of rows to render outside visible area */
        overscan?: number;
        /** Item renderer */
        children: Snippet<[T, number]>;
        /** Key extractor function */
        getKey?: (item: T, index: number) => string | number;
        /** Optional CSS class */
        class?: string;
    }

    let {
        items,
        rowHeight = VIRTUAL_LIST.DEFAULT_ROW_HEIGHT,
        overscan = VIRTUAL_LIST.OVERSCAN,
        children,
        getKey = (_item: T, index: number) => index,
        class: className = '',
    }: Props = $props();

    // Transient UI state
    let containerRef = $state<HTMLDivElement | null>(null);
    let scrollTop = $state(0);
    let containerHeight = $state(0);

    // Virtual scrolling calculations
    let startIndex = $derived(
        Math.max(0, Math.floor(scrollTop / rowHeight) - overscan)
    );
    let endIndex = $derived(
        Math.min(
            items.length,
            Math.ceil((scrollTop + containerHeight) / rowHeight) + overscan
        )
    );
    let visibleItems = $derived(
        items.slice(startIndex, endIndex).map((item, i) => ({
            item,
            index: startIndex + i,
        }))
    );
    let totalHeight = $derived(items.length * rowHeight);
    let offsetY = $derived(startIndex * rowHeight);

    // Custom scrollbar state
    let isDragging = $state(false);
    let dragStartY = $state(0);
    let dragStartScrollTop = $state(0);

    let showScrollbar = $derived(totalHeight > containerHeight);
    let maxScroll = $derived(totalHeight - containerHeight);
    let thumbHeight = $derived(
        totalHeight > 0
            ? Math.max(20, (containerHeight / totalHeight) * containerHeight)
            : 0
    );
    let thumbTop = $derived(
        maxScroll > 0
            ? (scrollTop / maxScroll) * (containerHeight - thumbHeight)
            : 0
    );

    function handleScroll(event: Event) {
        const target = event.target as HTMLDivElement;
        scrollTop = target.scrollTop;
    }

    // Set up resize observer
    $effect(() => {
        if (!containerRef) return;

        const observer = new ResizeObserver((entries) => {
            for (const entry of entries) {
                containerHeight = entry.contentRect.height;
            }
        });

        observer.observe(containerRef);

        return () => {
            observer.disconnect();
        };
    });

    // Custom scrollbar drag handlers
    function handleThumbMouseDown(event: MouseEvent) {
        event.preventDefault();
        event.stopPropagation();
        isDragging = true;
        dragStartY = event.clientY;
        dragStartScrollTop = scrollTop;
        window.addEventListener('mousemove', handleThumbMouseMove);
        window.addEventListener('mouseup', handleThumbMouseUp);
    }

    function handleThumbMouseMove(event: MouseEvent) {
        if (!isDragging || !containerRef) return;
        const deltaY = event.clientY - dragStartY;
        const trackRange = containerHeight - thumbHeight;
        if (trackRange <= 0) return;
        const scrollDelta = (deltaY / trackRange) * maxScroll;
        const newScrollTop = Math.max(0, Math.min(maxScroll, dragStartScrollTop + scrollDelta));
        containerRef.scrollTop = newScrollTop;
    }

    function handleThumbMouseUp() {
        isDragging = false;
        window.removeEventListener('mousemove', handleThumbMouseMove);
        window.removeEventListener('mouseup', handleThumbMouseUp);
    }

    function handleTrackMouseDown(event: MouseEvent) {
        if (!containerRef || event.target !== event.currentTarget) return;
        event.preventDefault();
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        const clickY = event.clientY - rect.top;
        const clickRatio = clickY / containerHeight;
        containerRef.scrollTop = Math.max(0, clickRatio * totalHeight - containerHeight / 2);
    }

    // Cleanup global listeners on destroy
    $effect(() => {
        return () => {
            if (isDragging) {
                window.removeEventListener('mousemove', handleThumbMouseMove);
                window.removeEventListener('mouseup', handleThumbMouseUp);
            }
        };
    });

    /**
     * Scroll to a specific index.
     */
    export function scrollToIndex(index: number, behavior: ScrollBehavior = 'auto') {
        if (!containerRef) return;

        const targetTop = index * rowHeight;
        containerRef.scrollTo({ top: targetTop, behavior });
    }

    /**
     * Ensure an index is visible in the viewport.
     */
    export function ensureVisible(index: number) {
        if (!containerRef) return;

        const itemTop = index * rowHeight;
        const itemBottom = itemTop + rowHeight;

        if (itemTop < scrollTop) {
            containerRef.scrollTop = itemTop;
        } else if (itemBottom > scrollTop + containerHeight) {
            containerRef.scrollTop = itemBottom - containerHeight;
        }
    }
</script>

<div class="virtual-list-wrapper {className}">
    <div
        class="virtual-list"
        bind:this={containerRef}
        onscroll={handleScroll}
    >
        <div class="scroll-content" style="height: {totalHeight}px">
            <div class="visible-items" style="transform: translateY({offsetY}px)">
                {#each visibleItems as { item, index } (getKey(item, index))}
                    <div class="virtual-item" style="height: {rowHeight}px">
                        {@render children(item, index)}
                    </div>
                {/each}
            </div>
        </div>
    </div>

    {#if showScrollbar}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="scrollbar-track" onmousedown={handleTrackMouseDown}>
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            <div
                class="scrollbar-thumb"
                class:dragging={isDragging}
                style="height: {thumbHeight}px; transform: translateY({thumbTop}px)"
                onmousedown={handleThumbMouseDown}
            ></div>
        </div>
    {/if}
</div>

<style>
    .virtual-list-wrapper {
        height: 100%;
        display: flex;
    }

    .virtual-list {
        flex: 1;
        min-height: 0;
        min-width: 0;
        overflow-y: scroll;
        scrollbar-width: none;
    }

    .virtual-list::-webkit-scrollbar {
        display: none;
    }

    .scroll-content {
        position: relative;
    }

    .visible-items {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
    }

    .virtual-item {
        overflow: hidden;
    }

    .scrollbar-track {
        width: var(--scrollbar-size);
        flex-shrink: 0;
        position: relative;
    }

    .scrollbar-thumb {
        position: absolute;
        left: 1px;
        right: 1px;
        background: var(--border);
        border-radius: var(--radius-md);
        cursor: pointer;
        transition: background-color var(--transition-fast);
    }

    .scrollbar-thumb:hover,
    .scrollbar-thumb.dragging {
        background: var(--border-hover);
    }
</style>
