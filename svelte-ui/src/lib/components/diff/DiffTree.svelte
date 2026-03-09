<!--
    DiffTree Component

    Tree view with diff markers showing which nodes have changes.
    Used in side-by-side diff view to show structure with change indicators.
-->
<script lang="ts">
    import type { TreeNode, DiffChange } from '$lib/bridge/types';
    import { TREE_NODE_TYPE_COLORS } from '$lib/constants';
    import { getChangeColor } from '$lib/view-models/diff.svelte';
    import Self from './DiffTree.svelte';

    interface Props {
        /** Tree nodes to display */
        nodes: TreeNode[];
        /** All changes from the diff */
        changes: DiffChange[];
        /** Currently selected node ID */
        selectedId?: string | null;
        /** Callback when node is selected */
        onSelect?: (nodeId: string) => void;
        /** Callback when node is expanded/collapsed */
        onToggle?: (nodeId: string) => void;
        /** Set of expanded node IDs */
        expandedIds?: Set<string>;
        /** Side of the diff ('base' or 'target') */
        side?: 'base' | 'target';
    }

    let {
        nodes,
        changes,
        selectedId = null,
        onSelect,
        onToggle,
        expandedIds = new Set(),
        side = 'base'
    }: Props = $props();

    // Build a map of path -> change for quick lookup
    let changesByPath = $derived.by(() => {
        const map = new Map<string, DiffChange>();
        for (const change of changes) {
            map.set(change.path.join('/'), change);
        }
        return map;
    });

    // Check if a node or any of its children have changes
    function hasChanges(nodeId: string): boolean {
        for (const path of changesByPath.keys()) {
            if (path.startsWith(nodeId) || nodeId.startsWith(path)) {
                return true;
            }
        }
        return false;
    }

    // Get the change type for a specific node
    function getChangeForNode(nodeId: string): DiffChange | undefined {
        return changesByPath.get(nodeId);
    }

    // Get marker color for a node
    function getMarkerColor(nodeId: string): string | null {
        const change = getChangeForNode(nodeId);
        if (change) {
            return getChangeColor(change.changeType);
        }
        // Check if any children have changes
        if (hasChanges(nodeId)) {
            return 'var(--text-muted)';
        }
        return null;
    }

    function handleNodeClick(nodeId: string, hasChildren: boolean) {
        onSelect?.(nodeId);
        if (hasChildren) {
            onToggle?.(nodeId);
        }
    }

    function handleNodeKeyDown(event: KeyboardEvent, nodeId: string, hasChildren: boolean) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            handleNodeClick(nodeId, hasChildren);
        }
    }
</script>

<div class="diff-tree">
    {#each nodes as node}
        {@const isExpanded = expandedIds.has(node.id)}
        {@const isSelected = selectedId === node.id}
        {@const change = getChangeForNode(node.id)}
        {@const markerColor = getMarkerColor(node.id)}
        {@const nodeColor = TREE_NODE_TYPE_COLORS[node.type] || 'var(--text-primary)'}

        <div
            class="tree-node"
            class:selected={isSelected}
            class:has-changes={change != null}
            class:expanded={isExpanded}
            role="treeitem"
            aria-selected={isSelected}
            tabindex={isSelected ? 0 : -1}
            onclick={() => handleNodeClick(node.id, node.hasChildren)}
            onkeydown={(event) => handleNodeKeyDown(event, node.id, node.hasChildren)}
        >
            <!-- Change marker -->
            {#if markerColor}
                <span class="change-marker" style="background: {markerColor}"></span>
            {/if}

            <!-- Icon (expand indicator for parents) -->
            <span class="icon" class:has-children={node.hasChildren} class:expanded={isExpanded} style="color: {nodeColor}">
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
                {:else}
                    <svg viewBox="0 0 16 16" fill="currentColor">
                        <circle cx="8" cy="8" r="2" />
                    </svg>
                {/if}
            </span>

            <!-- Name -->
            <span class="name" style="color: {nodeColor}">
                {node.name}
            </span>

            <!-- Change badge -->
            {#if change}
                <span
                    class="change-badge"
                    style="background: {getChangeColor(change.changeType)}"
                >
                    {change.changeType === 'added' ? '+' :
                     change.changeType === 'removed' ? '-' :
                     change.changeType === 'modified' ? '~' : '>'}
                </span>
            {/if}

            <!-- Value preview -->
            {#if node.metadata?.valuePreview}
                <span class="value-preview">
                    {node.metadata.valuePreview}
                </span>
            {/if}
        </div>

        <!-- Children (if expanded) -->
        {#if isExpanded && node.children}
            <div class="tree-children">
                <Self
                    nodes={node.children}
                    {changes}
                    {selectedId}
                    {expandedIds}
                    {side}
                    {...(onSelect ? { onSelect } : {})}
                    {...(onToggle ? { onToggle } : {})}
                />
            </div>
        {/if}
    {/each}
</div>

<style>
    .diff-tree {
        font-size: var(--text-sm);
    }

    .tree-node {
        display: flex;
        align-items: center;
        height: var(--tree-row-height, 24px);
        padding-right: var(--space-2);
        cursor: pointer;
        user-select: none;
        position: relative;
        transition: background-color var(--transition-fast);
    }

    .tree-node:hover {
        background: var(--bg-hover);
    }

    .tree-node.selected {
        background: var(--accent-primary);
    }

    .tree-node.selected .name,
    .tree-node.selected .value-preview {
        color: white !important;
    }

    .change-marker {
        position: absolute;
        left: 0;
        top: 0;
        bottom: 0;
        width: 3px;
    }

    .icon {
        width: 16px;
        height: 16px;
        margin-left: var(--space-1);
        margin-right: var(--space-1);
        flex-shrink: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        transition: opacity var(--transition-fast);
    }

    .icon.has-children {
        opacity: 0.4;
    }

    .icon.has-children.expanded {
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
    }

    .change-badge {
        width: 14px;
        height: 14px;
        display: flex;
        align-items: center;
        justify-content: center;
        border-radius: 2px;
        color: white;
        font-size: 10px;
        font-weight: bold;
        font-family: var(--font-mono);
        margin-left: var(--space-1);
        flex-shrink: 0;
    }

    .value-preview {
        color: var(--text-secondary);
        font-family: var(--font-mono);
        font-size: var(--text-xs);
        margin-left: var(--space-2);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        max-width: 100px;
    }

    .tree-children {
        padding-left: var(--tree-indent, 16px);
    }
</style>
