/**
 * Project View Model
 *
 * Manages project state for extracted PAK contents.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { GameVersionEntry, GameVersionState, ProjectInfo } from '$lib/bridge/types';

class ProjectVM {
    /** Currently open project */
    currentProject = $state<ProjectInfo | null>(null);
    /** List of available projects */
    availableProjects = $state<ProjectInfo[]>([]);
    /** Loading state */
    isLoading = $state(false);
    /** Error message */
    error = $state<string | null>(null);
    /** All available game versions (fetched once, cached) */
    gameVersions = $state<GameVersionEntry[]>([]);
    /** Current project's game version state */
    gameVersionState = $state<GameVersionState | null>(null);

    get hasProject(): boolean {
        return this.currentProject !== null;
    }

    get projectName(): string {
        return this.currentProject?.name ?? 'No project';
    }

    /** Display label for the current game version */
    get currentVersionLabel(): string {
        if (!this.gameVersionState) return '';
        if (this.gameVersionState.isAutoDetect) {
            const autoLabel = this.findVersionLabel(this.gameVersionState.autoDetected);
            return `Auto (${autoLabel})`;
        }
        return this.findVersionLabel(this.gameVersionState.selected);
    }

    private findVersionLabel(value: string): string {
        const entry = this.gameVersions.find((v) => v.value === value);
        return entry?.label ?? value;
    }

    /**
     * Opens an existing project directory.
     */
    openProject(projectPath: string): void {
        this.isLoading = true;
        this.error = null;
        ipc.send({
            type: 'project',
            action: 'open',
            payload: { projectPath },
        });
    }

    /**
     * Lists all available projects.
     */
    listProjects(): void {
        ipc.send({
            type: 'project',
            action: 'list',
            payload: {},
        });
    }

    /**
     * Fetches all available game versions from the backend.
     */
    fetchGameVersions(): void {
        if (this.gameVersions.length > 0) return; // Already cached
        ipc.send({
            type: 'project',
            action: 'getGameVersions',
            payload: {},
        });
    }

    /**
     * Sets the game version for the current project.
     */
    setGameVersion(version: string): void {
        ipc.send({
            type: 'project',
            action: 'setGameVersion',
            payload: { version },
        });
    }

    /**
     * Closes the current project.
     */
    closeProject(): void {
        ipc.send({
            type: 'project',
            action: 'close',
            payload: {},
        });
    }

    clearError(): void {
        this.error = null;
    }
}

export const project = new ProjectVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<ProjectInfo>('project', 'opened', (payload) => {
    project.currentProject = payload;
    project.isLoading = false;
    project.error = null;
    // Fetch game versions list if not cached
    project.fetchGameVersions();
});

ipc.onAction('project', 'closed', () => {
    project.currentProject = null;
    project.isLoading = false;
    project.error = null;
    project.gameVersionState = null;
});

ipc.onAction<{ versions: GameVersionEntry[] }>('project', 'gameVersions', (payload) => {
    project.gameVersions = payload.versions;
});

ipc.onAction<GameVersionState>('project', 'gameVersion', (payload) => {
    project.gameVersionState = payload;
});

ipc.onAction<{ projects: ProjectInfo[] }>('project', 'list', (payload) => {
    project.availableProjects = payload.projects;
});

ipc.onAction<{ message: string }>('project', 'error', (payload) => {
    project.error = payload.message;
    project.isLoading = false;
});

ipc.onAction<{ loading: boolean }>('project', 'loading', (payload) => {
    project.isLoading = payload.loading;
});
