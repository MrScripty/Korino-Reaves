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

class DiffVM {
    diffResult = $state<DiffResult | null>(null);
    threeWayResult = $state<ThreeWayDiffResult | null>(null);
    isLoading = $state(false);
    error = $state<string | null>(null);
    selectedChange = $state<DiffChange | null>(null);
    changeTypeFilter = $state<Set<string>>(new Set(['added', 'removed', 'modified', 'renamed']));
    showOnlyConflicts = $state(false);

    get hasDiff(): boolean {
        return this.diffResult !== null || this.threeWayResult !== null;
    }

    get isThreeWayDiff(): boolean {
        return this.threeWayResult !== null;
    }

    get allChanges(): DiffChange[] {
        if (this.threeWayResult) {
            return [...this.threeWayResult.gameChanges, ...this.threeWayResult.modChanges];
        }
        return this.diffResult?.changes ?? [];
    }

    get filteredChanges(): DiffChange[] {
        return this.allChanges.filter((c) => this.changeTypeFilter.has(c.changeType));
    }

    get conflicts(): DiffConflict[] {
        return this.threeWayResult?.conflicts ?? [];
    }

    get safeChanges(): DiffChange[] {
        return this.threeWayResult?.safeToApply ?? [];
    }

    get summary() {
        if (this.threeWayResult) {
            const gameSum = summarizeChanges(this.threeWayResult.gameChanges);
            const modSum = summarizeChanges(this.threeWayResult.modChanges);
            return {
                game: gameSum,
                mod: modSum,
                conflicts: this.threeWayResult.conflicts.length,
                safeToApply: this.threeWayResult.safeToApply.length,
            };
        }
        if (this.diffResult) {
            return {
                ...this.diffResult.summary,
                total: this.diffResult.changes.length,
            };
        }
        return null;
    }

    compareAssets(basePath: string, targetPath: string): void {
        this.isLoading = true;
        this.error = null;
        ipc.send({
            type: 'diff',
            action: 'compare',
            payload: { basePath, targetPath },
        });
    }

    compareForModPort(
        originalPath: string,
        updatedPath: string,
        moddedPath: string
    ): void {
        this.isLoading = true;
        this.error = null;
        ipc.send({
            type: 'diff',
            action: 'threeWayCompare',
            payload: { originalPath, updatedPath, moddedPath },
        });
    }

    applySafeChanges(): void {
        if (!this.threeWayResult) return;
        ipc.send({
            type: 'diff',
            action: 'applySafe',
            payload: {},
        });
    }

    resolveConflict(
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

    clearDiff(): void {
        ipc.send({
            type: 'diff',
            action: 'clear',
            payload: {},
        });
    }

    navigateToChange(change: DiffChange): void {
        ipc.send({
            type: 'diff',
            action: 'navigateTo',
            payload: { path: change.path },
        });
    }

    selectChange(change: DiffChange | null): void {
        this.selectedChange = change;
    }

    toggleChangeTypeFilter(type: string): void {
        if (this.changeTypeFilter.has(type)) {
            this.changeTypeFilter.delete(type);
            this.changeTypeFilter = new Set(this.changeTypeFilter);
        } else {
            this.changeTypeFilter.add(type);
            this.changeTypeFilter = new Set(this.changeTypeFilter);
        }
    }

    setShowOnlyConflicts(value: boolean): void {
        this.showOnlyConflicts = value;
    }

    clearError(): void {
        this.error = null;
    }

    formatPath(path: string[]): string {
        return path.join(' / ');
    }
}

export const diff = new DiffVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<DiffResult>('diff', 'result', (payload) => {
    diff.diffResult = payload;
    diff.threeWayResult = null;
    diff.isLoading = false;
    diff.error = null;
    diff.selectedChange = null;
});

ipc.onAction<ThreeWayDiffResult>('diff', 'threeWayResult', (payload) => {
    diff.threeWayResult = payload;
    diff.diffResult = null;
    diff.isLoading = false;
    diff.error = null;
    diff.selectedChange = null;
});

ipc.onAction('diff', 'clear', () => {
    diff.diffResult = null;
    diff.threeWayResult = null;
    diff.selectedChange = null;
    diff.error = null;
});

ipc.onAction<{ loading: boolean }>('diff', 'loading', (payload) => {
    diff.isLoading = payload.loading;
});

ipc.onAction<{ message: string }>('diff', 'error', (payload) => {
    diff.error = payload.message;
    diff.isLoading = false;
});

// =============================================================================
// Utilities (standalone exports for backward compat)
// =============================================================================

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

export function formatPath(path: string[]): string {
    return path.join(' / ');
}

export function resolveConflict(
    path: string[],
    resolution: 'keep_game' | 'keep_mod' | 'custom',
    customValue?: unknown
): void {
    diff.resolveConflict(path, resolution, customValue);
}
