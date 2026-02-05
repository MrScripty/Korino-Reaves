/**
 * Project View Model
 *
 * Manages project state for extracted PAK contents.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { ProjectInfo } from '$lib/bridge/types';

class ProjectVM {
    /** Currently open project */
    currentProject = $state<ProjectInfo | null>(null);
    /** List of available projects */
    availableProjects = $state<ProjectInfo[]>([]);
    /** Loading state */
    isLoading = $state(false);
    /** Error message */
    error = $state<string | null>(null);

    get hasProject(): boolean {
        return this.currentProject !== null;
    }

    get projectName(): string {
        return this.currentProject?.name ?? 'No project';
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
});

ipc.onAction('project', 'closed', () => {
    project.currentProject = null;
    project.isLoading = false;
    project.error = null;
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
