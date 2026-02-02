/**
 * Tree View Model
 *
 * Holds a read-only view of the asset tree structure pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { TreeNode, SelectionState } from '$lib/bridge/types';

// =============================================================================
// State (Received from C#)
// =============================================================================

/** Root nodes of the tree */
export let nodes = $state<TreeNode[]>([]);

/** Current selection and expansion state */
export let selection = $state<SelectionState>({
    selectedId: null,
    expandedIds: [],
});

/** Whether the tree is loading */
export let isLoading = $state(false);

/** Search/filter query (transient UI state - OK for Svelte to own) */
export let filterQuery = $state('');

// =============================================================================
// Derived State
// =============================================================================

/** Currently selected node, if any */
export let selectedNode = $derived.by(() => {
    if (!selection.selectedId) return null;
    return findNodeById(nodes, selection.selectedId);
});

/** Number of visible nodes (for virtual list) */
export let visibleNodeCount = $derived.by(() => {
    return countVisibleNodes(nodes, selection.expandedIds);
});

/** Whether any nodes are expanded */
export let hasExpanded = $derived(selection.expandedIds.length > 0);

// =============================================================================
// IPC Listeners
// =============================================================================

// Full tree update
ipc.onAction<TreeNode[]>('tree', 'update', (payload) => {
    nodes = payload;
    isLoading = false;
});

// Incremental children load
ipc.onAction<{ parentId: string; children: TreeNode[] }>(
    'tree',
    'children',
    (payload) => {
        nodes = updateNodeChildren(nodes, payload.parentId, payload.children);
    }
);

// Selection state update
ipc.onAction<SelectionState>('selection', 'update', (payload) => {
    selection = payload;
});

// Clear tree on asset close
ipc.onAction('asset', 'closed', () => {
    nodes = [];
    selection = { selectedId: null, expandedIds: [] };
    filterQuery = '';
});

// Loading state
ipc.onAction<{ loading: boolean }>('tree', 'loading', (payload) => {
    isLoading = payload.loading;
});

// =============================================================================
// Actions (Forward to C#)
// =============================================================================

/**
 * Request to select a node.
 * Does NOT update local state - waits for C# to push the update.
 */
export function selectNode(id: string): void {
    ipc.send({
        type: 'selection',
        action: 'select',
        payload: { id },
    });
}

/**
 * Request to toggle a node's expanded state.
 */
export function toggleExpand(id: string): void {
    ipc.send({
        type: 'tree',
        action: 'toggle',
        payload: { id },
    });
}

/**
 * Request to expand a node (and load children if needed).
 */
export function expandNode(id: string): void {
    ipc.send({
        type: 'tree',
        action: 'expand',
        payload: { id },
    });
}

/**
 * Request to collapse a node.
 */
export function collapseNode(id: string): void {
    ipc.send({
        type: 'tree',
        action: 'collapse',
        payload: { id },
    });
}

/**
 * Request to expand all nodes.
 */
export function expandAll(): void {
    ipc.send({
        type: 'tree',
        action: 'expandAll',
        payload: {},
    });
}

/**
 * Request to collapse all nodes.
 */
export function collapseAll(): void {
    ipc.send({
        type: 'tree',
        action: 'collapseAll',
        payload: {},
    });
}

/**
 * Request to search/filter the tree.
 * Note: filterQuery is transient UI state, but actual filtering is done by C#.
 */
export function setFilter(query: string): void {
    filterQuery = query;
    ipc.send({
        type: 'tree',
        action: 'filter',
        payload: { query },
    });
}

/**
 * Clear the filter and show all nodes.
 */
export function clearFilter(): void {
    filterQuery = '';
    ipc.send({
        type: 'tree',
        action: 'filter',
        payload: { query: '' },
    });
}

// =============================================================================
// Utilities (Pure functions, no state mutation)
// =============================================================================

/**
 * Check if a node is expanded.
 */
export function isExpanded(id: string): boolean {
    return selection.expandedIds.includes(id);
}

/**
 * Check if a node is selected.
 */
export function isSelected(id: string): boolean {
    return selection.selectedId === id;
}

/**
 * Find a node by ID in the tree (recursive).
 */
function findNodeById(nodeList: TreeNode[], id: string): TreeNode | null {
    for (const node of nodeList) {
        if (node.id === id) return node;
        if (node.children) {
            const found = findNodeById(node.children, id);
            if (found) return found;
        }
    }
    return null;
}

/**
 * Count visible nodes (for virtual list sizing).
 */
function countVisibleNodes(
    nodeList: TreeNode[],
    expandedIds: string[]
): number {
    let count = 0;
    for (const node of nodeList) {
        count++;
        if (expandedIds.includes(node.id) && node.children) {
            count += countVisibleNodes(node.children, expandedIds);
        }
    }
    return count;
}

/**
 * Update children of a specific node (immutable update).
 */
function updateNodeChildren(
    nodeList: TreeNode[],
    parentId: string,
    children: TreeNode[]
): TreeNode[] {
    return nodeList.map((node) => {
        if (node.id === parentId) {
            return { ...node, children };
        }
        if (node.children) {
            return {
                ...node,
                children: updateNodeChildren(node.children, parentId, children),
            };
        }
        return node;
    });
}

/**
 * Flatten the tree for virtual list rendering.
 */
export function flattenTree(
    nodeList: TreeNode[],
    expandedIds: string[],
    depth: number = 0
): Array<{ node: TreeNode; depth: number }> {
    const result: Array<{ node: TreeNode; depth: number }> = [];

    for (const node of nodeList) {
        result.push({ node, depth });

        if (expandedIds.includes(node.id) && node.children) {
            result.push(...flattenTree(node.children, expandedIds, depth + 1));
        }
    }

    return result;
}
