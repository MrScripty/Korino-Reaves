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

    expandedSet = $derived(new Set(this.selection.expandedIds));

    get selectedNode(): TreeNode | null {
        if (!this.selection.selectedId) return null;
        return findNodeById(this.nodes, this.selection.selectedId);
    }

    /** Returns nodes filtered by filterQuery, preserving ancestor structure. */
    get filteredNodes(): TreeNode[] {
        if (!this.filterQuery) return this.nodes;
        return filterTree(this.nodes, this.filterQuery.toLowerCase());
    }

    get visibleNodeCount(): number {
        return countVisibleNodes(this.filteredNodes, this.expandedSet);
    }

    get hasExpanded(): boolean {
        return this.expandedSet.size > 0;
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
            type: 'selection',
            action: 'toggle',
            payload: { id },
        });
    }

    expandNode(id: string): void {
        ipc.send({
            type: 'selection',
            action: 'expand',
            payload: { id },
        });
    }

    collapseNode(id: string): void {
        ipc.send({
            type: 'selection',
            action: 'collapse',
            payload: { id },
        });
    }

    expandAll(): void {
        const ids = collectExpandableIds(this.nodes);
        ipc.send({
            type: 'selection',
            action: 'expandAll',
            payload: { ids },
        });
    }

    collapseAll(): void {
        ipc.send({
            type: 'selection',
            action: 'collapseAll',
            payload: {},
        });
    }

    expandBranch(id: string): void {
        // Collect IDs from already-loaded children (works for file tree).
        // Backend will also recursively load lazy children (asset tree).
        const ids = collectSubtreeExpandableIds(this.nodes, id);
        ipc.send({
            type: 'tree',
            action: 'expandBranch',
            payload: { id, ids },
        });
    }

    collapseBranch(id: string): void {
        const ids = collectAllSubtreeIds(this.nodes, id);
        if (ids.length === 0) return;
        ipc.send({
            type: 'selection',
            action: 'collapseBranch',
            payload: { ids },
        });
    }

    setFilter(query: string): void {
        this.filterQuery = query;
    }

    clearFilter(): void {
        this.filterQuery = '';
    }

    openInFileBrowser(id: string): void {
        ipc.send({
            type: 'tree',
            action: 'openInFileBrowser',
            payload: { id },
        });
    }

    isExpanded(id: string): boolean {
        return this.expandedSet.has(id);
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
        expandedIds: Set<string>,
        depth: number = 0
    ): Array<{ node: TreeNode; depth: number }> {
        return flattenTree(nodeList, expandedIds, depth);
    }
}

export const tree = new TreeVM();

// =============================================================================
// IPC Listeners
// =============================================================================

// Note: tree:update handler is defined below

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
    tree.nodes = payload.nodes ?? [];
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
    expandedIds: Set<string>
): number {
    let count = 0;
    for (const node of nodeList) {
        count++;
        if (expandedIds.has(node.id) && node.children) {
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
 * Collect all expandable IDs from a subtree rooted at rootId.
 */
function collectSubtreeExpandableIds(nodeList: TreeNode[], rootId: string): string[] {
    const node = findNodeById(nodeList, rootId);
    if (!node || !node.hasChildren) return [];

    const ids: string[] = [node.id];
    if (node.children) {
        ids.push(...collectExpandableIds(node.children));
    }
    return ids;
}

/**
 * Collect ALL descendant IDs from a subtree rooted at rootId.
 */
function collectAllSubtreeIds(nodeList: TreeNode[], rootId: string): string[] {
    const node = findNodeById(nodeList, rootId);
    if (!node) return [];

    const ids: string[] = [node.id];
    collectDescendantIds(node, ids);
    return ids;
}

function collectDescendantIds(node: TreeNode, ids: string[]): void {
    if (!node.children) return;
    for (const child of node.children) {
        ids.push(child.id);
        collectDescendantIds(child, ids);
    }
}

/**
 * Filter tree nodes by a search query, preserving ancestor structure.
 * A node is included if its name matches or any descendant matches.
 */
function filterTree(nodeList: TreeNode[], query: string): TreeNode[] {
    const result: TreeNode[] = [];

    for (const node of nodeList) {
        const nameMatches = node.name.toLowerCase().includes(query);
        const filteredChildren = node.children
            ? filterTree(node.children, query)
            : [];

        if (nameMatches || filteredChildren.length > 0) {
            if (filteredChildren.length > 0) {
                result.push({
                    ...node,
                    children: filteredChildren,
                });
            } else if (node.children) {
                result.push({
                    ...node,
                    children: [],
                });
            } else {
                result.push(node);
            }
        }
    }

    return result;
}

/**
 * Flatten the tree for virtual list rendering.
 */
export function flattenTree(
    nodeList: TreeNode[],
    expandedIds: Set<string>,
    depth: number = 0
): Array<{ node: TreeNode; depth: number }> {
    const result: Array<{ node: TreeNode; depth: number }> = [];

    for (const node of nodeList) {
        result.push({ node, depth });

        if (expandedIds.has(node.id) && node.children) {
            result.push(...flattenTree(node.children, expandedIds, depth + 1));
        }
    }

    return result;
}
