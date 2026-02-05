/**
 * Dialog Listeners
 *
 * Handles responses from native file dialogs shown by the C# backend.
 * Routes file selections to appropriate asset operations.
 */

import { ipc } from './ipc';
import { asset } from '$lib/view-models/asset.svelte';
import { pak } from '$lib/view-models/pak.svelte';

// Handle file selected from open dialog
ipc.onAction<{ filePath: string; dialogAction: string }>('dialog', 'fileSelected', (payload) => {
    switch (payload.dialogAction) {
        case 'open':
            // Check if it's a PAK file
            if (payload.filePath.toLowerCase().endsWith('.pak')) {
                pak.openDialog(payload.filePath);
            } else {
                asset.openAsset({ filePath: payload.filePath });
            }
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
