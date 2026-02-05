/**
 * PAK View Model
 *
 * Manages PAK extraction state and operations.
 * Communicates with C# backend for actual extraction work.
 */

import { ipc } from '$lib/bridge/ipc';

class PakVM {
    // Dialog state
    showDialog = $state(false);
    pendingPakPath = $state<string | null>(null);

    // Extraction state
    isExtracting = $state(false);
    progress = $state(0);
    progressMessage = $state('');
    currentFile = $state(0);
    totalFiles = $state(0);

    // Result state
    extractedPath = $state<string | null>(null);
    error = $state<string | null>(null);

    // Validation state
    projectNameError = $state<string | null>(null);
    isValidating = $state(false);

    /**
     * Opens the extraction dialog for a PAK file.
     */
    openDialog(pakPath: string): void {
        this.pendingPakPath = pakPath;
        this.showDialog = true;
        this.error = null;
        this.projectNameError = null;
    }

    /**
     * Closes the extraction dialog.
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
     * Starts extraction of the pending PAK file.
     */
    startExtraction(projectName: string): void {
        if (!this.pendingPakPath) {
            this.error = 'No PAK file selected';
            return;
        }

        this.isExtracting = true;
        this.progress = 0;
        this.progressMessage = 'Starting extraction...';
        this.currentFile = 0;
        this.totalFiles = 0;
        this.error = null;
        this.showDialog = false;

        ipc.send({
            type: 'pak',
            action: 'extract',
            payload: {
                pakPath: this.pendingPakPath,
                projectName,
            },
        });
    }

    /**
     * Cancels ongoing extraction.
     */
    cancelExtraction(): void {
        ipc.send({
            type: 'pak',
            action: 'cancel',
            payload: {},
        });
    }

    /**
     * Resets state after extraction completes or fails.
     */
    reset(): void {
        this.isExtracting = false;
        this.progress = 0;
        this.progressMessage = '';
        this.currentFile = 0;
        this.totalFiles = 0;
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

// Extraction started acknowledgment
ipc.onAction<{ projectName: string }>('pak', 'extractionStarted', (payload) => {
    pak.progressMessage = `Extracting to ${payload.projectName}...`;
});

// Progress updates
ipc.onAction<{ current: number; total: number; message: string; percent: number }>('pak', 'progress', (payload) => {
    pak.currentFile = payload.current;
    pak.totalFiles = payload.total;
    pak.progress = payload.percent;
    pak.progressMessage = payload.message;
});

// Extraction complete
ipc.onAction<{ outputPath: string; fileCount: number }>('pak', 'extractionComplete', (payload) => {
    pak.extractedPath = payload.outputPath;
    pak.isExtracting = false;
    pak.progress = 100;
    pak.progressMessage = `Extracted ${payload.fileCount} files`;
    // Keep the success state for a moment, then reset
    setTimeout(() => pak.reset(), 2000);
});

// Extraction cancelled
ipc.onAction('pak', 'extractionCancelled', () => {
    pak.isExtracting = false;
    pak.progressMessage = 'Extraction cancelled';
    setTimeout(() => pak.reset(), 1000);
});

// Extraction error
ipc.onAction<{ error: string }>('pak', 'extractionError', (payload) => {
    pak.error = payload.error;
    pak.isExtracting = false;
    pak.progressMessage = 'Extraction failed';
});
