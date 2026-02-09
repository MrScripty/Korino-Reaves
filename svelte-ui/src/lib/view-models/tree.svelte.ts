/**
 * Tree View Model
 *
 * Holds a read-only view of the asset tree structure pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { TreeNode, SelectionState, IncrementalTreeUpdate } from '$lib/bridge/types';

class TreeVM {
    nodes = $state<TreeNode[]>([]);
    selection = $state<SelectionState>({
        selectedId: null,
        expandedIds: [],
    });
    isLoading = $state(false);
    filterQuery = $state('');
    editedFilePaths = $state<Set<string>>(new Set());

    get selectedNode(): TreeNode | null {
        if (!this.selection.selectedId) return null;
        return findNodeById(this.nodes, this.selection.selectedId);
    }

    get visibleNodeCount(): number {
        return countVisibleNodes(this.nodes, this.selection.expandedIds);
    }

    get hasExpanded(): boolean {
        return this.selection.expandedIds.length > 0;
    }

    selectNode(id: string): void {
        ipc.send({
            type: 'selection',
            action: 'select',
            payload: { id },
        });
    }

    toggleExpand(id: string): void {
        ipc.send({
            type: 'tree',
            action: 'toggle',
            payload: { id },
        });
    }

    expandNode(id: string): void {
        ipc.send({
            type: 'tree',
            action: 'expand',
            payload: { id },
        });
    }

    collapseNode(id: string): void {
        ipc.send({
            type: 'tree',
            action: 'collapse',
            payload: { id },
        });
    }

    expandAll(): void {
        const ids = collectExpandableIds(this.nodes);
        ipc.send({
            type: 'tree',
            action: 'expandAll',
            payload: { ids },
        });
    }

    collapseAll(): void {
        ipc.send({
            type: 'tree',
            action: 'collapseAll',
            payload: {},
        });
    }

    setFilter(query: string): void {
        this.filterQuery = query;
        ipc.send({
            type: 'tree',
            action: 'filter',
            payload: { query },
        });
    }

    clearFilter(): void {
        this.filterQuery = '';
        ipc.send({
            type: 'tree',
            action: 'filter',
            payload: { query: '' },
        });
    }

    openInFileBrowser(id: string): void {
        ipc.send({
            type: 'tree',
            action: 'openInFileBrowser',
            payload: { id },
        });
    }

    isExpanded(id: string): boolean {
        return this.selection.expandedIds.includes(id);
    }

    isSelected(id: string): boolean {
        return this.selection.selectedId === id;
    }

    isFileEdited(fileId: string): boolean {
        const relativePath = fileId.startsWith('file:') ? fileId.substring(5) : fileId;
        return this.editedFilePaths.has(relativePath);
    }

    flattenTree(
        nodeList: TreeNode[],
        expandedIds: string[],
        depth: number = 0
    ): Array<{ node: TreeNode; depth: number }> {
        return flattenTree(nodeList, expandedIds, depth);
    }
}

export const tree = new TreeVM();

// =============================================================================
// IPC Listeners
// =============================================================================

// Note: tree:update handler is defined below with enhanced support for both
// array and object payloads (for backwards compatibility)

ipc.onAction<{ parentId: string; children: TreeNode[] }>(
    'tree',
    'children',
    (payload) => {
        tree.nodes = updateNodeChildren(tree.nodes, payload.parentId, payload.children);
    }
);

ipc.onAction<SelectionState>('selection', 'update', (payload) => {
    tree.selection = payload;
});

ipc.onAction('asset', 'closed', () => {
    tree.nodes = [];
    tree.selection = { selectedId: null, expandedIds: [] };
    tree.filterQuery = '';
});

ipc.onAction<{ loading: boolean }>('tree', 'loading', (payload) => {
    tree.isLoading = payload.loading;
});

// Incremental tree update for streaming (during import)
ipc.onAction<IncrementalTreeUpdate>('tree', 'incrementalUpdate', (payload) => {
    if (payload.parentId) {
        tree.nodes = mergeNodesIntoTree(tree.nodes, payload.parentId, payload.nodes);
    } else {
        // Add to root level
        tree.nodes = [...tree.nodes, ...payload.nodes];
    }
});

// Clear tree when project is closed
ipc.onAction('project', 'closed', () => {
    tree.nodes = [];
    tree.selection = { selectedId: null, expandedIds: [] };
    tree.filterQuery = '';
    tree.editedFilePaths = new Set();
});

// Track which files have property edits
ipc.onAction<{ files: string[] }>('property', 'editedFiles', (payload) => {
    tree.editedFilePaths = new Set(payload.files);
});

// Handle full tree update from project open (with nodes wrapper)
ipc.onAction<{ nodes: TreeNode[] }>('tree', 'update', (payload) => {
    if (Array.isArray(payload)) {
        tree.nodes = payload;
    } else if (payload.nodes) {
        tree.nodes = payload.nodes;
    }
    tree.isLoading = false;
});

// =============================================================================
// Utilities (Pure functions)
// =============================================================================

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
 * Merge new nodes into an existing tree structure at a given parent.
 * Used for streaming/incremental tree updates during import.
 */
function mergeNodesIntoTree(
    existing: TreeNode[],
    parentId: string,
    newNodes: TreeNode[]
): TreeNode[] {
    return existing.map((node) => {
        if (node.id === parentId) {
            return {
                ...node,
                children: [...(node.children || []), ...newNodes],
                hasChildren: true,
            };
        }
        if (node.children) {
            return {
                ...node,
                children: mergeNodesIntoTree(node.children, parentId, newNodes),
            };
        }
        return node;
    });
}

/**
 * Collect IDs of all nodes that have children (for expandAll).
 */
function collectExpandableIds(nodeList: TreeNode[]): string[] {
    const ids: string[] = [];
    for (const node of nodeList) {
        if (node.hasChildren) {
            ids.push(node.id);
            if (node.children) {
                ids.push(...collectExpandableIds(node.children));
            }
        }
    }
    return ids;
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
