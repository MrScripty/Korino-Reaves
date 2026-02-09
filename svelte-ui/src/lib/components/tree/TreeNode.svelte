<!--
    TreeNode Component

    Individual node in the asset tree with expand/collapse and selection.
    All interactions are forwarded to C# via the tree view model.
-->
<script lang="ts">
    import type { TreeNode as TreeNodeType, TreeNodeType as NodeType } from '$lib/bridge/types';
    import { TREE, TREE_NODE_TYPE_COLORS } from '$lib/constants';
    import { tree } from '$lib/view-models/tree.svelte';

    interface Props {
        /** Node data */
        node: TreeNodeType;
        /** Nesting depth */
        depth?: number;
        /** Right-click context menu callback */
        onContextMenu?: (node: TreeNodeType, event: MouseEvent) => void;
    }

    let { node, depth = 0, onContextMenu }: Props = $props();

    // Derived from view model state
    let isExpanded = $derived(tree.isExpanded(node.id));
    let isSelected = $derived(tree.isSelected(node.id));
    let isEdited = $derived(node.type === 'file' && tree.isFileEdited(node.id));

    // Get color for node type
    let nodeColor = $derived(TREE_NODE_TYPE_COLORS[node.type] || 'var(--text-primary)');

    function handleClick(event: MouseEvent) {
        event.stopPropagation();
        tree.selectNode(node.id);
        if (node.hasChildren) {
            tree.toggleExpand(node.id);
        }
    }

    function handleContextMenu(event: MouseEvent) {
        event.preventDefault();
        event.stopPropagation();
        onContextMenu?.(node, event);
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            tree.selectNode(node.id);
        } else if (event.key === 'ArrowRight' && node.hasChildren && !isExpanded) {
            event.preventDefault();
            tree.expandNode(node.id);
        } else if (event.key === 'ArrowLeft' && isExpanded) {
            event.preventDefault();
            tree.collapseNode(node.id);
        }
    }
</script>

<div
    class="tree-node"
    class:selected={isSelected}
    class:expanded={isExpanded}
    class:has-children={node.hasChildren}
    class:edited={isEdited}
    style="padding-left: {depth * TREE.INDENT_SIZE + TREE.INDENT_SIZE / 2}px"
    role="treeitem"
    aria-selected={isSelected}
    aria-expanded={node.hasChildren ? isExpanded : undefined}
    tabindex={isSelected ? 0 : -1}
    onclick={handleClick}
    oncontextmenu={handleContextMenu}
    onkeydown={handleKeyDown}
>
    <!-- Node icon (expand indicator for parents) -->
    <span class="icon" style="color: {nodeColor}">
        {#if node.type === 'export'}
            <svg viewBox="0 0 16 16" fill="currentColor">
                <rect x="2" y="2" width="12" height="12" rx="2" />
            </svg>
        {:else if node.type === 'array'}
            <svg viewBox="0 0 16 16" fill="currentColor">
                <path d="M4 3h2v10H4V3zm6 0h2v10h-2V3z" />
            </svg>
        {:else if node.type === 'struct'}
            <svg viewBox="0 0 16 16" fill="currentColor">
                <path d="M3 3h10v2H3V3zm0 4h10v2H3V7zm0 4h10v2H3v-2z" />
            </svg>
        {:else if node.type === 'property'}
            <svg viewBox="0 0 16 16" fill="currentColor">
                <circle cx="8" cy="8" r="3" />
            </svg>
        {:else}
            <svg viewBox="0 0 16 16" fill="currentColor">
                <circle cx="8" cy="8" r="2" />
            </svg>
        {/if}
    </span>

    <!-- Node name -->
    <span class="name" style="color: {nodeColor}">
        {node.name}
    </span>

    <!-- Edit indicator for files with property edits -->
    {#if isEdited}
        <span class="edit-indicator" title="Has property edits"></span>
    {/if}

    <!-- Value preview (if available) -->
    {#if node.metadata?.valuePreview}
        <span class="value-preview">
            {node.metadata.valuePreview}
        </span>
    {/if}

    <!-- Type name (if available) -->
    {#if node.metadata?.typeName}
        <span class="type-name">
            {node.metadata.typeName}
        </span>
    {/if}
</div>

<style>
    .tree-node {
        display: flex;
        align-items: center;
        height: var(--tree-row-height);
        padding-right: var(--space-2);
        cursor: pointer;
        user-select: none;
        transition: background-color var(--transition-fast);
    }

    .tree-node:hover {
        background: var(--bg-hover);
    }

    .tree-node.selected {
        background: var(--accent-primary);
    }

    .tree-node.selected .name,
    .tree-node.selected .value-preview,
    .tree-node.selected .type-name {
        color: white !important;
    }

    .icon {
        width: 16px;
        height: 16px;
        margin-right: var(--space-1);
        flex-shrink: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        transition: opacity var(--transition-fast);
    }

    .tree-node.has-children .icon {
        opacity: 0.4;
    }

    .tree-node.has-children.expanded .icon {
        opacity: 1;
    }

    .icon svg {
        width: 12px;
        height: 12px;
    }

    .name {
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: var(--text-sm);
    }

    .value-preview {
        color: var(--text-secondary);
        font-family: var(--font-mono);
        font-size: var(--text-xs);
        margin-left: var(--space-2);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 120px;
    }

    .type-name {
        color: var(--text-muted);
        font-size: var(--text-xs);
        margin-left: var(--space-1);
        opacity: 0.7;
    }

    .edit-indicator {
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background: var(--color-warning, #f59e0b);
        margin-left: var(--space-1);
        flex-shrink: 0;
    }

    .tree-node.edited .name {
        font-style: italic;
    }
</style>
