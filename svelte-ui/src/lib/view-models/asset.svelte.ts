/**
 * Asset View Model
 *
 * Holds a read-only view of the current asset data pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { AssetInfo, OpenAssetRequest, IpcMessage } from '$lib/bridge/types';

// =============================================================================
// State (Received from C#)
// =============================================================================

/** Current loaded asset info, or null if no asset loaded */
export let assetInfo = $state<AssetInfo | null>(null);

/** Whether an asset operation is in progress */
export let isLoading = $state(false);

/** Error message from last operation, or null */
export let error = $state<string | null>(null);

/** Whether the current asset has unsaved changes */
export let isModified = $derived(assetInfo?.isModified ?? false);

/** Display name for the current asset */
export let displayName = $derived(assetInfo?.fileName ?? 'No asset loaded');

// =============================================================================
// IPC Listeners
// =============================================================================

// Subscribe to asset updates from C#
ipc.onAction<AssetInfo>('asset', 'loaded', (payload) => {
    assetInfo = payload;
    isLoading = false;
    error = null;
});

ipc.onAction('asset', 'closed', () => {
    assetInfo = null;
    isLoading = false;
    error = null;
});

ipc.onAction<{ isModified: boolean }>('asset', 'modified', (payload) => {
    if (assetInfo) {
        assetInfo = { ...assetInfo, isModified: payload.isModified };
    }
});

ipc.onAction('asset', 'saved', () => {
    if (assetInfo) {
        assetInfo = { ...assetInfo, isModified: false };
    }
    isLoading = false;
});

ipc.onAction<{ message: string }>('asset', 'error', (payload) => {
    error = payload.message;
    isLoading = false;
});

ipc.onAction<{ loading: boolean }>('asset', 'loading', (payload) => {
    isLoading = payload.loading;
});

// =============================================================================
// Actions (Forward to C#)
// =============================================================================

/**
 * Request to open an asset file.
 * Does NOT update local state - waits for C# to push the update.
 */
export function openAsset(request: OpenAssetRequest): void {
    isLoading = true;
    error = null;
    ipc.send({
        type: 'asset',
        action: 'open',
        payload: request,
    });
}

/**
 * Request to close the current asset.
 */
export function closeAsset(): void {
    ipc.send({
        type: 'asset',
        action: 'close',
        payload: {},
    });
}

/**
 * Request to save the current asset.
 */
export function saveAsset(): void {
    if (!assetInfo) return;
    isLoading = true;
    ipc.send({
        type: 'asset',
        action: 'save',
        payload: {},
    });
}

/**
 * Request to save the current asset to a new path.
 */
export function saveAssetAs(filePath: string): void {
    if (!assetInfo) return;
    isLoading = true;
    ipc.send({
        type: 'asset',
        action: 'saveAs',
        payload: { filePath },
    });
}

/**
 * Request to export asset as JSON.
 */
export function exportAsJson(filePath: string): void {
    if (!assetInfo) return;
    ipc.send({
        type: 'asset',
        action: 'exportJson',
        payload: { filePath },
    });
}

/**
 * Request to reload the current asset from disk.
 */
export function reloadAsset(): void {
    if (!assetInfo) return;
    isLoading = true;
    ipc.send({
        type: 'asset',
        action: 'reload',
        payload: {},
    });
}

// =============================================================================
// Utilities
// =============================================================================

/**
 * Check if an asset is currently loaded.
 */
export function hasAsset(): boolean {
    return assetInfo !== null;
}

/**
 * Clear any error state.
 */
export function clearError(): void {
    error = null;
}
