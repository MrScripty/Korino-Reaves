/**
 * PAK View Model
 *
 * Manages PAK import state and operations.
 * Communicates with C# backend for actual extraction work.
 */

import { ipc } from '$lib/bridge/ipc';
import type { FileExtractedPayload } from '$lib/bridge/types';

class PakVM {
    // Dialog state
    showDialog = $state(false);
    pendingPakPath = $state<string | null>(null);
    selectedGameVersion = $state('AUTO');

    // Import state
    isImporting = $state(false);
    progress = $state(0);
    progressMessage = $state('');
    currentFile = $state(0);
    totalFiles = $state(0);

    // Streaming file tracking
    extractedFiles = $state<string[]>([]);

    // Result state
    importedPath = $state<string | null>(null);
    error = $state<string | null>(null);

    // Validation state
    projectNameError = $state<string | null>(null);
    isValidating = $state(false);

    /**
     * Opens the import dialog for a PAK file.
     */
    openDialog(pakPath: string): void {
        this.pendingPakPath = pakPath;
        this.showDialog = true;
        this.error = null;
        this.projectNameError = null;
        this.extractedFiles = [];
        this.selectedGameVersion = 'AUTO';
    }

    /**
     * Closes the import dialog.
     */
    closeDialog(): void {
        this.showDialog = false;
        this.pendingPakPath = null;
        this.projectNameError = null;
    }

    /**
     * Validates a project name.
     */
    validateName(name: string): void {
        if (!name) {
            this.projectNameError = 'Project name is required';
            return;
        }

        const validPattern = /^[a-zA-Z0-9_-]+$/;
        if (!validPattern.test(name)) {
            this.projectNameError = 'Only letters, numbers, underscores, and hyphens allowed';
            return;
        }

        // Ask backend to check if directory exists
        this.isValidating = true;
        ipc.send({
            type: 'pak',
            action: 'validateName',
            payload: { name },
        });
    }

    /**
     * Starts import of the pending PAK file.
     */
    startImport(projectName: string): void {
        if (!this.pendingPakPath) {
            this.error = 'No PAK file selected';
            return;
        }

        this.isImporting = true;
        this.progress = 0;
        this.progressMessage = 'Starting import...';
        this.currentFile = 0;
        this.totalFiles = 0;
        this.extractedFiles = [];
        this.error = null;
        this.showDialog = false;

        ipc.send({
            type: 'pak',
            action: 'import',
            payload: {
                pakPath: this.pendingPakPath,
                projectName,
                gameVersion: this.selectedGameVersion === 'AUTO' ? null : this.selectedGameVersion,
            },
        });
    }

    /**
     * Cancels ongoing import.
     */
    cancelImport(): void {
        ipc.send({
            type: 'pak',
            action: 'cancel',
            payload: {},
        });
    }

    /**
     * Resets state after import completes or fails.
     */
    reset(): void {
        this.isImporting = false;
        this.progress = 0;
        this.progressMessage = '';
        this.currentFile = 0;
        this.totalFiles = 0;
        this.extractedFiles = [];
        this.pendingPakPath = null;
    }
}

export const pak = new PakVM();

// =============================================================================
// IPC Listeners
// =============================================================================

// Name validation result
ipc.onAction<{ name: string; isValid: boolean; error: string | null }>('pak', 'nameValidated', (payload) => {
    pak.isValidating = false;
    if (!payload.isValid) {
        pak.projectNameError = payload.error;
    } else {
        pak.projectNameError = null;
    }
});

// Import started acknowledgment
ipc.onAction<{ projectName: string }>('pak', 'importStarted', (payload) => {
    pak.progressMessage = `Importing to ${payload.projectName}...`;
});

// Progress updates
ipc.onAction<{ current: number; total: number; message: string; percent: number }>('pak', 'progress', (payload) => {
    pak.currentFile = payload.current;
    pak.totalFiles = payload.total;
    pak.progress = payload.percent;
    pak.progressMessage = payload.message;
});

// Streaming file extracted updates
ipc.onAction<FileExtractedPayload>('pak', 'fileExtracted', (payload) => {
    pak.extractedFiles = [...pak.extractedFiles, payload.filePath];
    pak.currentFile = payload.index;
    pak.totalFiles = payload.total;
});

// Import complete
ipc.onAction<{ outputPath: string; fileCount: number }>('pak', 'importComplete', (payload) => {
    pak.importedPath = payload.outputPath;
    pak.isImporting = false;
    pak.progress = 100;
    pak.progressMessage = `Imported ${payload.fileCount} files`;
    // Keep the success state for a moment, then reset
    setTimeout(() => pak.reset(), 2000);
});

// Import cancelled
ipc.onAction('pak', 'importCancelled', () => {
    pak.isImporting = false;
    pak.progressMessage = 'Import cancelled';
    setTimeout(() => pak.reset(), 1000);
});

// Import error
ipc.onAction<{ error: string }>('pak', 'importError', (payload) => {
    pak.error = payload.error;
    pak.isImporting = false;
    pak.progressMessage = 'Import failed';
});
