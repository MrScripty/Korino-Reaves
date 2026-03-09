<!--
    AssetTree Component

    Virtual scrolling tree view for displaying asset structure.
    Receives data from tree view model and forwards interactions to C#.
    Uses a custom scrollbar since CEF doesn't support native scrollbar drag.
-->
<script lang="ts">
    import TreeNode from './TreeNode.svelte';
    import ContextMenu, { type ContextMenuItem } from '$lib/components/common/ContextMenu.svelte';
    import type { TreeNode as TreeNodeType } from '$lib/bridge/types';
    import { tree } from '$lib/view-models/tree.svelte';
    import { TREE } from '$lib/constants';

    interface Props {
        /** Optional CSS class */
        class?: string;
    }

    let { class: className = '' }: Props = $props();

    // Flatten tree for rendering (uses filtered nodes when search is active)
    let flattenedNodes = $derived(
        tree.flattenTree(tree.filteredNodes, tree.expandedSet)
    );

    // Virtual scrolling state (transient UI - OK for Svelte to own)
    let containerRef = $state<HTMLDivElement | null>(null);
    let scrollTop = $state(0);
    let containerHeight = $state(0);

    // Virtual scrolling calculations
    let startIndex = $derived(
        Math.max(0, Math.floor(scrollTop / TREE.ROW_HEIGHT) - TREE.OVERSCAN_COUNT)
    );
    let endIndex = $derived(
        Math.min(
            flattenedNodes.length,
            Math.ceil((scrollTop + containerHeight) / TREE.ROW_HEIGHT) +
                TREE.OVERSCAN_COUNT
        )
    );
    let visibleNodes = $derived(flattenedNodes.slice(startIndex, endIndex));
    let totalHeight = $derived(flattenedNodes.length * TREE.ROW_HEIGHT);
    let offsetY = $derived(startIndex * TREE.ROW_HEIGHT);

    // Custom scrollbar state
    let isDragging = $state(false);
    let dragStartY = $state(0);
    let dragStartScrollTop = $state(0);

    // Context menu state
    let contextMenuVisible = $state(false);
    let contextMenuX = $state(0);
    let contextMenuY = $state(0);
    let contextMenuNodeId = $state<string | null>(null);

    const contextMenuItems: ContextMenuItem[] = [
        { id: 'openInFileBrowser', label: 'Open in File Browser' },
    ];

    function handleNodeContextMenu(node: TreeNodeType, event: MouseEvent) {
        if (node.type !== 'file' && node.type !== 'folder') return;
        contextMenuX = event.clientX;
        contextMenuY = event.clientY;
        contextMenuNodeId = node.id;
        contextMenuVisible = true;
    }

    function handleContextMenuSelect(itemId: string) {
        if (itemId === 'openInFileBrowser' && contextMenuNodeId) {
            tree.openInFileBrowser(contextMenuNodeId);
        }
        contextMenuVisible = false;
    }

    function handleContextMenuClose() {
        contextMenuVisible = false;
    }

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

    function handleKeyDown(event: KeyboardEvent) {
        const selectedId = tree.selection.selectedId;
        if (!selectedId) return;

        // Ctrl+Arrow: recursive branch expand/collapse
        if (event.ctrlKey || event.metaKey) {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                tree.expandBranch(selectedId);
                return;
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                tree.collapseBranch(selectedId);
                return;
            }
        }

        const currentIndex = flattenedNodes.findIndex(
            (item) => item.node.id === selectedId
        );
        if (currentIndex === -1) return;

        let newIndex = currentIndex;

        if (event.key === 'ArrowDown') {
            event.preventDefault();
            newIndex = Math.min(flattenedNodes.length - 1, currentIndex + 1);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            newIndex = Math.max(0, currentIndex - 1);
        }

        if (newIndex !== currentIndex) {
            const nextNode = flattenedNodes[newIndex];
            if (!nextNode) return;

            tree.selectNode(nextNode.node.id);
            // Scroll into view if needed
            ensureVisible(newIndex);
        }
    }

    function ensureVisible(index: number) {
        if (!containerRef) return;

        const itemTop = index * TREE.ROW_HEIGHT;
        const itemBottom = itemTop + TREE.ROW_HEIGHT;

        if (itemTop < scrollTop) {
            containerRef.scrollTop = itemTop;
        } else if (itemBottom > scrollTop + containerHeight) {
            containerRef.scrollTop = itemBottom - containerHeight;
        }
    }
</script>

<div class="asset-tree-wrapper {className}">
    <div
        class="asset-tree"
        bind:this={containerRef}
        role="tree"
        tabindex="0"
        onscroll={handleScroll}
        onkeydown={handleKeyDown}
    >
        {#if tree.isLoading}
            <div class="loading">
                <div class="loading-spinner"></div>
                <span>Loading...</span>
            </div>
        {:else if flattenedNodes.length === 0}
            <div class="empty">
                <span class="text-muted">
                    {tree.filterQuery ? 'No matching results' : 'No asset loaded'}
                </span>
            </div>
        {:else}
            <!-- Virtual scroll container -->
            <div class="scroll-content" style="height: {totalHeight}px">
                <div
                    class="visible-nodes"
                    style="transform: translateY({offsetY}px)"
                >
                    {#each visibleNodes as { node, depth } (node.id)}
                        <TreeNode {node} {depth} onContextMenu={handleNodeContextMenu} />
                    {/each}
                </div>
            </div>
        {/if}
    </div>

    <ContextMenu
        items={contextMenuItems}
        x={contextMenuX}
        y={contextMenuY}
        bind:visible={contextMenuVisible}
        onSelect={handleContextMenuSelect}
        onClose={handleContextMenuClose}
    />

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
    .asset-tree-wrapper {
        flex: 1;
        min-height: 0;
        display: flex;
        position: relative;
    }

    .asset-tree {
        flex: 1;
        min-height: 0;
        overflow-y: scroll;
        scrollbar-width: none;
        outline: none;
    }

    .asset-tree::-webkit-scrollbar {
        display: none;
    }

    .asset-tree-wrapper:focus-within {
        outline: 2px solid var(--accent-primary);
        outline-offset: -2px;
    }

    .loading,
    .empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
        height: 100%;
        min-height: 100px;
        color: var(--text-secondary);
        font-size: var(--text-sm);
    }

    .scroll-content {
        position: relative;
    }

    .visible-nodes {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
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
