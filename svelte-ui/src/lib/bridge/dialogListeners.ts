/**
 * Dialog Listeners
 *
 * Handles responses from native file dialogs shown by the C# backend.
 * Routes file selections to appropriate operations.
 */

import { ipc } from './ipc';
import { asset } from '$lib/view-models/asset.svelte';
import { pak } from '$lib/view-models/pak.svelte';
import { project } from '$lib/view-models/project.svelte';

// Handle file selected from dialogs
ipc.onAction<{ filePath: string; dialogAction: string }>('dialog', 'fileSelected', (payload) => {
    switch (payload.dialogAction) {
        case 'importPak':
            // Open import dialog for PAK file
            pak.openDialog(payload.filePath);
            break;
        case 'openProject':
            // Open the selected project directory
            project.openProject(payload.filePath);
            break;
        case 'open':
            // Open individual asset file
            asset.openAsset({ filePath: payload.filePath });
            break;
        case 'save':
            asset.saveAssetAs(payload.filePath);
            break;
        case 'export':
            asset.exportAsJson(payload.filePath);
            break;
    }
});

// Handle dialog canceled - nothing to do, just log for debugging
ipc.onAction<{ dialogAction: string }>('dialog', 'canceled', (payload) => {
    console.debug(`Dialog canceled: ${payload.dialogAction}`);
});
