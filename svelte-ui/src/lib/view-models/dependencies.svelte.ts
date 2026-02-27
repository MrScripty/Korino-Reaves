/**
 * Dependencies View Model
 *
 * Holds the current dependency graph state pushed from C#.
 * ALL data is owned by C# — this is just a presentation layer cache.
 *
 * Supports lazy-loading tree expansion: when a node is expanded,
 * its dependencies are fetched via IPC and cached for display.
 *
 * IMPORTANT: Never mutate dependency data directly. All queries go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type {
    DependencyReference,
    DependencyStats,
    DependencyScanProgress,
} from '$lib/bridge/types';

class DependenciesVM {
    isScanning = $state(false);
    scanProgress = $state<DependencyScanProgress | null>(null);
    stats = $state<DependencyStats | null>(null);

    // Per-asset query results (root level)
    selectedPath = $state<string | null>(null);
    dependencies = $state<DependencyReference[]>([]);
    dependents = $state<DependencyReference[]>([]);

    // Tree expansion state
    /** Cached dependencies per asset path (for expanded tree nodes) */
    nodeChildren = $state<Map<string, DependencyReference[]>>(new Map());
    /** Paths currently being loaded */
    loadingNodes = $state<Set<string>>(new Set());
    /** Paths the user has expanded */
    expandedNodes = $state<Set<string>>(new Set());

    get hasGraph(): boolean {
        return this.stats?.exists === true;
    }

    startScan(): void {
        this.isScanning = true;
        this.scanProgress = null;
        ipc.send({
            type: 'dependency',
            action: 'scan',
            payload: {},
        });
    }

    cancelScan(): void {
        ipc.send({
            type: 'dependency',
            action: 'cancel',
            payload: {},
        });
    }

    requestStats(): void {
        ipc.send({
            type: 'dependency',
            action: 'getStats',
            payload: {},
        });
    }

    requestDependencies(path: string): void {
        this.selectedPath = path;
        this.dependencies = [];
        this.dependents = [];
        this.nodeChildren = new Map();
        this.loadingNodes = new Set();
        this.expandedNodes = new Set();
        ipc.send({
            type: 'dependency',
            action: 'getDependencies',
            payload: { path },
        });
        ipc.send({
            type: 'dependency',
            action: 'getDependents',
            payload: { path },
        });
    }

    /** Expand a tree node — fetch its dependencies if not cached */
    expandNode(path: string): void {
        this.expandedNodes = new Set([...this.expandedNodes, path]);

        if (!this.nodeChildren.has(path) && !this.loadingNodes.has(path)) {
            this.loadingNodes = new Set([...this.loadingNodes, path]);
            ipc.send({
                type: 'dependency',
                action: 'getDependencies',
                payload: { path },
            });
        }
    }

    collapseNode(path: string): void {
        const next = new Set(this.expandedNodes);
        next.delete(path);
        this.expandedNodes = next;
    }

    toggleNode(path: string): void {
        if (this.expandedNodes.has(path)) {
            this.collapseNode(path);
        } else {
            this.expandNode(path);
        }
    }

    isExpanded(path: string): boolean {
        return this.expandedNodes.has(path);
    }

    isLoading(path: string): boolean {
        return this.loadingNodes.has(path);
    }

    getChildren(path: string): DependencyReference[] {
        return this.nodeChildren.get(path) ?? [];
    }
}

export const deps = new DependenciesVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<DependencyScanProgress>('dependency', 'scanProgress', (payload) => {
    deps.scanProgress = payload;
});

ipc.onAction<{ assetCount: number; edgeCount: number; scannedAt: string }>(
    'dependency',
    'scanComplete',
    (payload) => {
        deps.isScanning = false;
        deps.scanProgress = null;
        deps.stats = {
            exists: true,
            assetCount: payload.assetCount,
            edgeCount: payload.edgeCount,
            scannedAt: payload.scannedAt,
        };
    }
);

ipc.onAction('dependency', 'scanCancelled', () => {
    deps.isScanning = false;
    deps.scanProgress = null;
});

ipc.onAction<{ error: string }>('dependency', 'scanError', () => {
    deps.isScanning = false;
    deps.scanProgress = null;
});

ipc.onAction<DependencyStats>('dependency', 'stats', (payload) => {
    deps.stats = payload;
});

ipc.onAction<{ assetPath: string; dependencies: DependencyReference[] }>(
    'dependency',
    'dependencies',
    (payload) => {
        // Root level dependencies
        if (payload.assetPath === deps.selectedPath) {
            deps.dependencies = payload.dependencies;
        }

        // Cache for tree expansion (always, so expanded nodes get their data)
        const next = new Map(deps.nodeChildren);
        next.set(payload.assetPath, payload.dependencies);
        deps.nodeChildren = next;

        const loading = new Set(deps.loadingNodes);
        loading.delete(payload.assetPath);
        deps.loadingNodes = loading;
    }
);

ipc.onAction<{ assetPath: string; dependents: DependencyReference[] }>(
    'dependency',
    'dependents',
    (payload) => {
        if (payload.assetPath === deps.selectedPath) {
            deps.dependents = payload.dependents;
        }
    }
);

ipc.onAction<{ assetPath: string; related: string[] }>(
    'dependency',
    'related',
    () => {
        // Related cluster not used in tree view
    }
);

// Request stats when a project opens (the mount-time $effect may have
// fired before any project was loaded, so we need to re-request here)
ipc.onAction('project', 'opened', () => {
    deps.requestStats();
});

// Clear state when project closes
ipc.onAction('project', 'closed', () => {
    deps.isScanning = false;
    deps.scanProgress = null;
    deps.stats = null;
    deps.selectedPath = null;
    deps.dependencies = [];
    deps.dependents = [];
    deps.nodeChildren = new Map();
    deps.loadingNodes = new Set();
    deps.expandedNodes = new Set();
});
