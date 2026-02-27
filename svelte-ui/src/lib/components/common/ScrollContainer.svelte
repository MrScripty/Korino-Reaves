<!--
    ScrollContainer Component

    Reusable scroll container with custom scrollbar for CEF compatibility.
    CEF doesn't support native scrollbar drag, so this renders a custom
    track/thumb that can be dragged and clicked.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Props {
        /** Content to render inside the scroll area */
        children: Snippet;
        /** Scroll direction */
        direction?: 'vertical' | 'horizontal' | 'both';
        /** Optional CSS class for the outer container */
        class?: string;
        /** Bindable reference to the scroll viewport element */
        viewport?: HTMLDivElement | null;
    }

    let {
        children,
        direction = 'vertical',
        class: className = '',
        viewport = $bindable(null),
    }: Props = $props();

    const MIN_THUMB = 20;

    let viewportRef = $state<HTMLDivElement | null>(null);

    // Scroll metrics
    let scrollTop = $state(0);
    let scrollLeft = $state(0);
    let scrollHeight = $state(0);
    let scrollWidth = $state(0);
    let clientHeight = $state(0);
    let clientWidth = $state(0);

    // Vertical drag state
    let isDraggingV = $state(false);
    let dragStartY = $state(0);
    let dragStartScrollTop = $state(0);

    // Horizontal drag state
    let isDraggingH = $state(false);
    let dragStartX = $state(0);
    let dragStartScrollLeft = $state(0);

    // Sync internal ref to bindable prop
    $effect(() => {
        viewport = viewportRef;
    });

    // --- Vertical derived ---
    let hasVertical = $derived(direction === 'vertical' || direction === 'both');
    let showVertical = $derived(hasVertical && scrollHeight > clientHeight);
    let maxScrollTop = $derived(scrollHeight - clientHeight);
    let vThumbHeight = $derived(
        scrollHeight > 0
            ? Math.max(MIN_THUMB, (clientHeight / scrollHeight) * clientHeight)
            : 0
    );
    let vThumbTop = $derived(
        maxScrollTop > 0
            ? (scrollTop / maxScrollTop) * (clientHeight - vThumbHeight)
            : 0
    );

    // --- Horizontal derived ---
    let hasHorizontal = $derived(direction === 'horizontal' || direction === 'both');
    let showHorizontal = $derived(hasHorizontal && scrollWidth > clientWidth);
    let maxScrollLeft = $derived(scrollWidth - clientWidth);
    let hThumbWidth = $derived(
        scrollWidth > 0
            ? Math.max(MIN_THUMB, (clientWidth / scrollWidth) * clientWidth)
            : 0
    );
    let hThumbLeft = $derived(
        maxScrollLeft > 0
            ? (scrollLeft / maxScrollLeft) * (clientWidth - hThumbWidth)
            : 0
    );

    function readMetrics() {
        if (!viewportRef) return;
        scrollTop = viewportRef.scrollTop;
        scrollLeft = viewportRef.scrollLeft;
        scrollHeight = viewportRef.scrollHeight;
        scrollWidth = viewportRef.scrollWidth;
        clientHeight = viewportRef.clientHeight;
        clientWidth = viewportRef.clientWidth;
    }

    function handleScroll() {
        readMetrics();
    }

    // ResizeObserver for viewport size changes
    $effect(() => {
        if (!viewportRef) return;

        const observer = new ResizeObserver(() => {
            readMetrics();
        });
        observer.observe(viewportRef);

        return () => observer.disconnect();
    });

    // --- Vertical scrollbar handlers ---
    function handleVThumbMouseDown(event: MouseEvent) {
        event.preventDefault();
        event.stopPropagation();
        isDraggingV = true;
        dragStartY = event.clientY;
        dragStartScrollTop = scrollTop;
        window.addEventListener('mousemove', handleVThumbMouseMove);
        window.addEventListener('mouseup', handleVThumbMouseUp);
    }

    function handleVThumbMouseMove(event: MouseEvent) {
        if (!isDraggingV || !viewportRef) return;
        const deltaY = event.clientY - dragStartY;
        const trackRange = clientHeight - vThumbHeight;
        if (trackRange <= 0) return;
        const newScrollTop = Math.max(0, Math.min(maxScrollTop, dragStartScrollTop + (deltaY / trackRange) * maxScrollTop));
        viewportRef.scrollTop = newScrollTop;
    }

    function handleVThumbMouseUp() {
        isDraggingV = false;
        window.removeEventListener('mousemove', handleVThumbMouseMove);
        window.removeEventListener('mouseup', handleVThumbMouseUp);
    }

    function handleVTrackMouseDown(event: MouseEvent) {
        if (!viewportRef || event.target !== event.currentTarget) return;
        event.preventDefault();
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        const clickY = event.clientY - rect.top;
        const clickRatio = clickY / clientHeight;
        viewportRef.scrollTop = Math.max(0, clickRatio * scrollHeight - clientHeight / 2);
    }

    // --- Horizontal scrollbar handlers ---
    function handleHThumbMouseDown(event: MouseEvent) {
        event.preventDefault();
        event.stopPropagation();
        isDraggingH = true;
        dragStartX = event.clientX;
        dragStartScrollLeft = scrollLeft;
        window.addEventListener('mousemove', handleHThumbMouseMove);
        window.addEventListener('mouseup', handleHThumbMouseUp);
    }

    function handleHThumbMouseMove(event: MouseEvent) {
        if (!isDraggingH || !viewportRef) return;
        const deltaX = event.clientX - dragStartX;
        const trackRange = clientWidth - hThumbWidth;
        if (trackRange <= 0) return;
        const newScrollLeft = Math.max(0, Math.min(maxScrollLeft, dragStartScrollLeft + (deltaX / trackRange) * maxScrollLeft));
        viewportRef.scrollLeft = newScrollLeft;
    }

    function handleHThumbMouseUp() {
        isDraggingH = false;
        window.removeEventListener('mousemove', handleHThumbMouseMove);
        window.removeEventListener('mouseup', handleHThumbMouseUp);
    }

    function handleHTrackMouseDown(event: MouseEvent) {
        if (!viewportRef || event.target !== event.currentTarget) return;
        event.preventDefault();
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
        const clickX = event.clientX - rect.left;
        const clickRatio = clickX / clientWidth;
        viewportRef.scrollLeft = Math.max(0, clickRatio * scrollWidth - clientWidth / 2);
    }

    // Cleanup global listeners on destroy
    $effect(() => {
        return () => {
            if (isDraggingV) {
                window.removeEventListener('mousemove', handleVThumbMouseMove);
                window.removeEventListener('mouseup', handleVThumbMouseUp);
            }
            if (isDraggingH) {
                window.removeEventListener('mousemove', handleHThumbMouseMove);
                window.removeEventListener('mouseup', handleHThumbMouseUp);
            }
        };
    });
</script>

<div
    class="scroll-container {className}"
    class:direction-both={direction === 'both'}
    class:has-h-scrollbar={showHorizontal}
>
    <div
        class="scroll-viewport"
        class:v-scroll={hasVertical}
        class:h-scroll={hasHorizontal}
        bind:this={viewportRef}
        onscroll={handleScroll}
    >
        {@render children()}
    </div>

    {#if showVertical}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="scrollbar-track vertical" onmousedown={handleVTrackMouseDown}>
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            <div
                class="scrollbar-thumb"
                class:dragging={isDraggingV}
                style="height: {vThumbHeight}px; transform: translateY({vThumbTop}px)"
                onmousedown={handleVThumbMouseDown}
            ></div>
        </div>
    {/if}

    {#if showHorizontal}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="scrollbar-track horizontal" onmousedown={handleHTrackMouseDown}>
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            <div
                class="scrollbar-thumb"
                class:dragging={isDraggingH}
                style="width: {hThumbWidth}px; transform: translateX({hThumbLeft}px)"
                onmousedown={handleHThumbMouseDown}
            ></div>
        </div>
    {/if}
</div>

<style>
    .scroll-container {
        flex: 1;
        min-height: 0;
        min-width: 0;
        display: flex;
    }

    .scroll-container.direction-both {
        flex-wrap: wrap;
    }

    .scroll-viewport {
        flex: 1;
        min-height: 0;
        min-width: 0;
        scrollbar-width: none;
    }

    .scroll-viewport::-webkit-scrollbar {
        display: none;
    }

    .scroll-viewport.v-scroll {
        overflow-y: scroll;
    }

    .scroll-viewport.h-scroll {
        overflow-x: scroll;
    }

    /* Vertical track */
    .scrollbar-track.vertical {
        width: var(--scrollbar-size);
        flex-shrink: 0;
        position: relative;
    }

    /* Horizontal track */
    .scrollbar-track.horizontal {
        height: var(--scrollbar-size);
        flex-shrink: 0;
        position: relative;
        width: 100%;
    }

    .has-h-scrollbar .scrollbar-track.horizontal {
        /* Leave room for vertical track corner */
        width: calc(100% - var(--scrollbar-size));
    }

    .scrollbar-thumb {
        position: absolute;
        background: var(--border);
        border-radius: var(--radius-md);
        cursor: pointer;
        transition: background-color var(--transition-fast);
    }

    .scrollbar-track.vertical .scrollbar-thumb {
        left: 1px;
        right: 1px;
    }

    .scrollbar-track.horizontal .scrollbar-thumb {
        top: 1px;
        bottom: 1px;
    }

    .scrollbar-thumb:hover,
    .scrollbar-thumb.dragging {
        background: var(--border-hover);
    }
</style>
