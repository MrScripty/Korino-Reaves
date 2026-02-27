/**
 * Scene View Model
 *
 * Holds the current scene/level viewer state pushed from C#.
 * ALL data is owned by C# — this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate scene data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { SceneActor, SceneInfo, SubLevelSummary } from '$lib/bridge/types';

class SceneVM {
    isActive = $state(false);
    isLoading = $state(false);
    levelName = $state<string | null>(null);
    actors = $state<SceneActor[]>([]);
    selectedActorId = $state<string | null>(null);
    loadProgress = $state({ loaded: 0, total: 0 });
    filterQuery = $state('');

    // Multi-level state
    isMultiLevel = $state(false);
    subLevels = $state<SubLevelSummary[]>([]);
    collapsedLevels = $state<Set<string>>(new Set());

    get meshActors(): SceneActor[] {
        return this.actors.filter((a) => a.hasMesh);
    }

    get loadPercent(): number {
        if (this.loadProgress.total === 0) return 0;
        return Math.round((this.loadProgress.loaded / this.loadProgress.total) * 100);
    }

    get filteredActors(): SceneActor[] {
        if (!this.filterQuery) return this.actors;
        const q = this.filterQuery.toLowerCase();
        return this.actors.filter(
            (a) => a.name.toLowerCase().includes(q) || a.className.toLowerCase().includes(q)
        );
    }

    get actorsByLevel(): Map<string, SceneActor[]> {
        const map = new Map<string, SceneActor[]>();
        for (const actor of this.filteredActors) {
            const group = map.get(actor.levelName) ?? [];
            group.push(actor);
            map.set(actor.levelName, group);
        }
        return map;
    }

    toggleLevelCollapsed(levelName: string): void {
        const next = new Set(this.collapsedLevels);
        if (next.has(levelName)) next.delete(levelName);
        else next.add(levelName);
        this.collapsedLevels = next;
    }

    selectActor(id: string): void {
        ipc.send({
            type: 'scene',
            action: 'selectActor',
            payload: { actorId: id },
        });
    }

    focusActor(id: string): void {
        ipc.send({
            type: 'scene',
            action: 'focusActor',
            payload: { actorId: id },
        });
    }

    pickActor(normalizedX: number, normalizedY: number): void {
        ipc.send({
            type: 'scene',
            action: 'pickActor',
            payload: { normalizedX, normalizedY },
        });
    }

    deselectActor(): void {
        ipc.send({
            type: 'scene',
            action: 'deselectActor',
            payload: {},
        });
    }

    exitScene(): void {
        ipc.send({
            type: 'scene',
            action: 'exitScene',
            payload: {},
        });
    }
}

export const scene = new SceneVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<{ loading: boolean }>('scene', 'loading', (payload) => {
    scene.isLoading = payload.loading;
});

ipc.onAction<{
    levelName: string;
    actors: SceneActor[];
    totalCount: number;
    meshCount: number;
    isMultiLevel?: boolean;
    subLevels?: SubLevelSummary[];
}>('scene', 'actorList', (payload) => {
    scene.isActive = true;
    scene.levelName = payload.levelName;
    scene.actors = payload.actors;
    scene.isMultiLevel = payload.isMultiLevel ?? false;
    scene.subLevels = payload.subLevels ?? [];
});

ipc.onAction<{ loaded: number; total: number }>('scene', 'loadProgress', (payload) => {
    scene.loadProgress = payload;
});

ipc.onAction<SceneInfo>('scene', 'loaded', () => {
    scene.isLoading = false;
});

ipc.onAction<{ actorId: string }>('scene', 'actorSelected', (payload) => {
    scene.selectedActorId = payload.actorId;
});

ipc.onAction('scene', 'cleared', () => {
    scene.isActive = false;
    scene.isLoading = false;
    scene.levelName = null;
    scene.actors = [];
    scene.selectedActorId = null;
    scene.loadProgress = { loaded: 0, total: 0 };
    scene.filterQuery = '';
    scene.isMultiLevel = false;
    scene.subLevels = [];
    scene.collapsedLevels = new Set();
});

// Clear scene when project closes
ipc.onAction('project', 'closed', () => {
    scene.isActive = false;
    scene.isLoading = false;
    scene.levelName = null;
    scene.actors = [];
    scene.selectedActorId = null;
    scene.loadProgress = { loaded: 0, total: 0 };
    scene.filterQuery = '';
    scene.isMultiLevel = false;
    scene.subLevels = [];
    scene.collapsedLevels = new Set();
});
