/**
 * File Browser View Model
 *
 * Manages state for file browser dialogs (Open Project, Import PAK, etc.)
 * Uses Svelte 5 runes for reactive state management.
 */

import { ipc } from '$lib/bridge/ipc';

type FileBrowserMode = 'file' | 'directory';

interface FileBrowserConfig {
    title: string;
    mode: FileBrowserMode;
    filters: string[];
    initialPath?: string;
    basePath?: string; // Root path for relative display
    selectOnClick?: boolean; // If true, clicking a directory immediately selects it
    onSelect: (path: string) => void;
}

class FileBrowserVM {
    // Dialog visibility
    isOpen = $state(false);

    // Current configuration
    title = $state('Select File');
    mode = $state<FileBrowserMode>('file');
    filters = $state<string[]>([]);
    initialPath = $state<string>('');
    basePath = $state<string>('');
    selectOnClick = $state(false);

    // Cached projects directory path
    private _projectsDir: string | null = null;

    // Callback for selection
    private _onSelect: ((path: string) => void) | null = null;

    /**
     * Open the file browser dialog with the given configuration.
     */
    open(config: FileBrowserConfig) {
        this.title = config.title;
        this.mode = config.mode;
        this.filters = config.filters;
        this.initialPath = config.initialPath ?? '';
        this.basePath = config.basePath ?? '';
        this.selectOnClick = config.selectOnClick ?? false;
        this._onSelect = config.onSelect;
        this.isOpen = true;
    }

    /**
     * Fetch the projects directory path from the backend.
     */
    async getProjectsDir(): Promise<string> {
        if (this._projectsDir) {
            return this._projectsDir;
        }

        try {
            const result = await ipc.request<{ path: string }>({
                type: 'fs',
                action: 'getProjectsDir',
                payload: {},
            });
            this._projectsDir = result.path;
            return result.path;
        } catch (e) {
            console.error('[FileBrowser] Failed to get projects directory:', e);
            return '';
        }
    }

    /**
     * Close the file browser dialog.
     */
    close() {
        this.isOpen = false;
        this._onSelect = null;
    }

    /**
     * Handle file/folder selection.
     */
    handleSelect(path: string) {
        if (this._onSelect) {
            this._onSelect(path);
        }
        this.close();
    }

    /**
     * Handle dialog cancellation.
     */
    handleCancel() {
        this.close();
    }

    // Convenience methods for common dialogs

    /**
     * Open a dialog to select a project folder.
     * Starts in the projects directory and shows relative paths.
     */
    async openProjectDialog(onSelect: (path: string) => void) {
        const projectsDir = await this.getProjectsDir();
        this.open({
            title: 'Open Project',
            mode: 'directory',
            filters: [],
            initialPath: projectsDir,
            basePath: projectsDir,
            selectOnClick: true,
            onSelect,
        });
    }

    /**
     * Open a dialog to select a PAK file for import.
     */
    openImportPakDialog(onSelect: (path: string) => void) {
        this.open({
            title: 'Import PAK Archive',
            mode: 'file',
            filters: ['*.pak'],
            onSelect,
        });
    }

    /**
     * Open a dialog to select an asset file.
     */
    openAssetDialog(onSelect: (path: string) => void) {
        this.open({
            title: 'Open Asset',
            mode: 'file',
            filters: ['*.uasset', '*.umap', '*.pak'],
            onSelect,
        });
    }

    /**
     * Open a save dialog for exporting.
     */
    openExportDialog(onSelect: (path: string) => void, initialPath?: string) {
        const config: FileBrowserConfig = {
            title: 'Export as JSON',
            mode: 'file',
            filters: ['*.json'],
            onSelect,
        };

        if (initialPath !== undefined) {
            config.initialPath = initialPath;
        }

        this.open(config);
    }
}

export const fileBrowser = new FileBrowserVM();
