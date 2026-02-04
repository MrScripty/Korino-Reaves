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

class TreeVM {
    nodes = $state<TreeNode[]>([]);
    selection = $state<SelectionState>({
        selectedId: null,
        expandedIds: [],
    });
    isLoading = $state(false);
    filterQuery = $state('');

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
        ipc.send({
            type: 'tree',
            action: 'expandAll',
            payload: {},
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

    isExpanded(id: string): boolean {
        return this.selection.expandedIds.includes(id);
    }

    isSelected(id: string): boolean {
        return this.selection.selectedId === id;
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

ipc.onAction<TreeNode[]>('tree', 'update', (payload) => {
    tree.nodes = payload;
    tree.isLoading = false;
});

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
