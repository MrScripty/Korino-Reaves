/**
 * Asset View Model
 *
 * Holds a read-only view of the current asset data pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { AssetInfo, OpenAssetRequest } from '$lib/bridge/types';

class AssetVM {
    assetInfo = $state<AssetInfo | null>(null);
    isLoading = $state(false);
    error = $state<string | null>(null);

    get isModified() { return this.assetInfo?.isModified ?? false; }
    get displayName() { return this.assetInfo?.fileName ?? 'No asset loaded'; }

    hasAsset(): boolean {
        return this.assetInfo !== null;
    }

    openAsset(request: OpenAssetRequest): void {
        this.isLoading = true;
        this.error = null;
        ipc.send({
            type: 'asset',
            action: 'open',
            payload: request,
        });
    }

    closeAsset(): void {
        ipc.send({
            type: 'asset',
            action: 'close',
            payload: {},
        });
    }

    saveAsset(): void {
        if (!this.assetInfo) return;
        this.isLoading = true;
        ipc.send({
            type: 'asset',
            action: 'save',
            payload: {},
        });
    }

    saveAssetAs(filePath: string): void {
        if (!this.assetInfo) return;
        this.isLoading = true;
        ipc.send({
            type: 'asset',
            action: 'saveAs',
            payload: { filePath },
        });
    }

    exportAsJson(filePath: string): void {
        if (!this.assetInfo) return;
        ipc.send({
            type: 'asset',
            action: 'exportJson',
            payload: { filePath },
        });
    }

    reloadAsset(): void {
        if (!this.assetInfo) return;
        this.isLoading = true;
        ipc.send({
            type: 'asset',
            action: 'reload',
            payload: {},
        });
    }

    clearError(): void {
        this.error = null;
    }
}

export const asset = new AssetVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<AssetInfo>('asset', 'opened', (payload) => {
    asset.assetInfo = payload;
    asset.isLoading = false;
    asset.error = null;
});

ipc.onAction('asset', 'closed', () => {
    asset.assetInfo = null;
    asset.isLoading = false;
    asset.error = null;
});

ipc.onAction<{ isModified: boolean }>('asset', 'modified', (payload) => {
    if (asset.assetInfo) {
        asset.assetInfo = { ...asset.assetInfo, isModified: payload.isModified };
    }
});

ipc.onAction('asset', 'saved', () => {
    if (asset.assetInfo) {
        asset.assetInfo = { ...asset.assetInfo, isModified: false };
    }
    asset.isLoading = false;
});

ipc.onAction<{ message: string }>('asset', 'error', (payload) => {
    asset.error = payload.message;
    asset.isLoading = false;
});

ipc.onAction<{ loading: boolean }>('asset', 'loading', (payload) => {
    asset.isLoading = payload.loading;
});
