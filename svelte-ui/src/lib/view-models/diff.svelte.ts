/**
 * Diff View Model
 *
 * Holds a read-only view of diff comparison results pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type {
    DiffResult,
    ThreeWayDiffResult,
    DiffChange,
    DiffConflict,
} from '$lib/bridge/types';

// =============================================================================
// State (Received from C#)
// =============================================================================

/** Two-way diff result (comparing two assets) */
export let diffResult = $state<DiffResult | null>(null);

/** Three-way diff result (for mod porting) */
export let threeWayResult = $state<ThreeWayDiffResult | null>(null);

/** Whether a diff operation is in progress */
export let isLoading = $state(false);

/** Error message from last diff operation */
export let error = $state<string | null>(null);

// =============================================================================
// Transient UI State (Svelte can own this)
// =============================================================================

/** Currently selected change for detail view */
export let selectedChange = $state<DiffChange | null>(null);

/** Filter for change types to show */
export let changeTypeFilter = $state<Set<string>>(new Set(['added', 'removed', 'modified', 'renamed']));

/** Whether to show only conflicts (for three-way diff) */
export let showOnlyConflicts = $state(false);

// =============================================================================
// Derived State
// =============================================================================

/** Whether a diff is loaded */
export let hasDiff = $derived(diffResult !== null || threeWayResult !== null);

/** Whether this is a three-way diff */
export let isThreeWayDiff = $derived(threeWayResult !== null);

/** All changes from the current diff */
export let allChanges = $derived.by(() => {
    if (threeWayResult) {
        return [...threeWayResult.gameChanges, ...threeWayResult.modChanges];
    }
    return diffResult?.changes ?? [];
});

/** Filtered changes based on current filter settings */
export let filteredChanges = $derived.by(() => {
    return allChanges.filter((c) => changeTypeFilter.has(c.changeType));
});

/** Conflicts from three-way diff */
export let conflicts = $derived(threeWayResult?.conflicts ?? []);

/** Safe to apply changes from three-way diff */
export let safeChanges = $derived(threeWayResult?.safeToApply ?? []);

/** Summary statistics */
export let summary = $derived.by(() => {
    if (threeWayResult) {
        const gameSum = summarizeChanges(threeWayResult.gameChanges);
        const modSum = summarizeChanges(threeWayResult.modChanges);
        return {
            game: gameSum,
            mod: modSum,
            conflicts: threeWayResult.conflicts.length,
            safeToApply: threeWayResult.safeToApply.length,
        };
    }
    if (diffResult) {
        return {
            ...diffResult.summary,
            total: diffResult.changes.length,
        };
    }
    return null;
});

// =============================================================================
// IPC Listeners
// =============================================================================

// Two-way diff result
ipc.onAction<DiffResult>('diff', 'result', (payload) => {
    diffResult = payload;
    threeWayResult = null;
    isLoading = false;
    error = null;
    selectedChange = null;
});

// Three-way diff result
ipc.onAction<ThreeWayDiffResult>('diff', 'threeWayResult', (payload) => {
    threeWayResult = payload;
    diffResult = null;
    isLoading = false;
    error = null;
    selectedChange = null;
});

// Clear diff
ipc.onAction('diff', 'clear', () => {
    diffResult = null;
    threeWayResult = null;
    selectedChange = null;
    error = null;
});

// Loading state
ipc.onAction<{ loading: boolean }>('diff', 'loading', (payload) => {
    isLoading = payload.loading;
});

// Error state
ipc.onAction<{ message: string }>('diff', 'error', (payload) => {
    error = payload.message;
    isLoading = false;
});

// =============================================================================
// Actions (Forward to C#)
// =============================================================================

/**
 * Request to compare two assets.
 */
export function compareAssets(basePath: string, targetPath: string): void {
    isLoading = true;
    error = null;
    ipc.send({
        type: 'diff',
        action: 'compare',
        payload: { basePath, targetPath },
    });
}

/**
 * Request to perform a three-way diff for mod porting.
 */
export function compareForModPort(
    originalPath: string,
    updatedPath: string,
    moddedPath: string
): void {
    isLoading = true;
    error = null;
    ipc.send({
        type: 'diff',
        action: 'threeWayCompare',
        payload: { originalPath, updatedPath, moddedPath },
    });
}

/**
 * Request to apply safe changes (for mod porting).
 */
export function applySafeChanges(): void {
    if (!threeWayResult) return;
    ipc.send({
        type: 'diff',
        action: 'applySafe',
        payload: {},
    });
}

/**
 * Request to resolve a conflict.
 */
export function resolveConflict(
    path: string[],
    resolution: 'keep_game' | 'keep_mod' | 'custom',
    customValue?: unknown
): void {
    ipc.send({
        type: 'diff',
        action: 'resolveConflict',
        payload: { path, resolution, customValue },
    });
}

/**
 * Request to clear the current diff.
 */
export function clearDiff(): void {
    ipc.send({
        type: 'diff',
        action: 'clear',
        payload: {},
    });
}

/**
 * Request to navigate to a change in the tree view.
 */
export function navigateToChange(change: DiffChange): void {
    ipc.send({
        type: 'diff',
        action: 'navigateTo',
        payload: { path: change.path },
    });
}

// =============================================================================
// Transient UI Actions (Svelte can manage these)
// =============================================================================

/**
 * Select a change for detail view (transient UI state).
 */
export function selectChange(change: DiffChange | null): void {
    selectedChange = change;
}

/**
 * Toggle a change type filter (transient UI state).
 */
export function toggleChangeTypeFilter(type: string): void {
    if (changeTypeFilter.has(type)) {
        changeTypeFilter.delete(type);
        changeTypeFilter = new Set(changeTypeFilter);
    } else {
        changeTypeFilter.add(type);
        changeTypeFilter = new Set(changeTypeFilter);
    }
}

/**
 * Set show only conflicts filter (transient UI state).
 */
export function setShowOnlyConflicts(value: boolean): void {
    showOnlyConflicts = value;
}

/**
 * Clear error state.
 */
export function clearError(): void {
    error = null;
}

// =============================================================================
// Utilities
// =============================================================================

/**
 * Summarize changes by type.
 */
function summarizeChanges(changes: DiffChange[]): {
    added: number;
    removed: number;
    modified: number;
    renamed: number;
} {
    return {
        added: changes.filter((c) => c.changeType === 'added').length,
        removed: changes.filter((c) => c.changeType === 'removed').length,
        modified: changes.filter((c) => c.changeType === 'modified').length,
        renamed: changes.filter((c) => c.changeType === 'renamed').length,
    };
}

/**
 * Get color CSS variable for a change type.
 */
export function getChangeColor(type: string): string {
    switch (type) {
        case 'added':
            return 'var(--diff-added)';
        case 'removed':
            return 'var(--diff-removed)';
        case 'modified':
            return 'var(--diff-modified)';
        case 'renamed':
        case 'moved':
            return 'var(--diff-moved)';
        default:
            return 'var(--text-secondary)';
    }
}

/**
 * Get background color CSS variable for a change type.
 */
export function getChangeBgColor(type: string): string {
    switch (type) {
        case 'added':
            return 'var(--diff-added-bg)';
        case 'removed':
            return 'var(--diff-removed-bg)';
        case 'modified':
            return 'var(--diff-modified-bg)';
        case 'renamed':
        case 'moved':
            return 'var(--diff-moved-bg)';
        default:
            return 'transparent';
    }
}

/**
 * Format a path for display.
 */
export function formatPath(path: string[]): string {
    return path.join(' / ');
}
