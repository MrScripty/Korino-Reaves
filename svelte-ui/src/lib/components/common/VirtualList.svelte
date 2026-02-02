<!--
    VirtualList Component

    Generic virtual scrolling list for rendering large datasets efficiently.
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

<div
    class="virtual-list {className}"
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

<style>
    .virtual-list {
        height: 100%;
        overflow: auto;
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
</style>
