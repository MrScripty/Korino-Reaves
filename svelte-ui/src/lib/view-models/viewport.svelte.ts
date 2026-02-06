/**
 * Viewport View Model
 *
 * Holds the current preview state pushed from C#.
 * ALL data is owned by C# — this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate preview data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { ViewportPreviewPayload } from '$lib/bridge/types';

class ViewportVM {
    previewData = $state<string | null>(null);
    mode = $state<'2d' | '3d' | 'none'>('none');
    contentType = $state<string | null>(null);
    isLoading = $state(false);
    assetName = $state<string | null>(null);

    textureInfo = $state<{ width: number; height: number; format: string } | null>(null);
    meshInfo = $state<{ vertexCount: number; triangleCount: number; lodCount: number } | null>(null);

    get hasPreview(): boolean {
        return this.previewData !== null;
    }

    get is3D(): boolean {
        return this.mode === '3d';
    }

    get infoText(): string {
        if (this.textureInfo) {
            return `${this.textureInfo.width} × ${this.textureInfo.height} — ${this.textureInfo.format}`;
        }
        if (this.meshInfo) {
            const parts = [
                `${this.meshInfo.vertexCount.toLocaleString()} verts`,
                `${this.meshInfo.triangleCount.toLocaleString()} tris`,
            ];
            if (this.meshInfo.lodCount > 1) {
                parts.push(`${this.meshInfo.lodCount} LODs`);
            }
            return parts.join(' — ');
        }
        return '';
    }

    orbitCamera(dx: number, dy: number): void {
        ipc.send({
            type: 'viewport',
            action: 'orbitCamera',
            payload: { dx, dy },
        });
    }

    zoomCamera(delta: number): void {
        ipc.send({
            type: 'viewport',
            action: 'zoomCamera',
            payload: { delta },
        });
    }

    resetCamera(): void {
        ipc.send({
            type: 'viewport',
            action: 'resetCamera',
            payload: {},
        });
    }
}

export const viewport = new ViewportVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<ViewportPreviewPayload>('viewport', 'preview', (payload) => {
    viewport.previewData = payload.imageData;
    viewport.mode = payload.mode;
    viewport.contentType = payload.contentType;
    viewport.assetName = payload.assetName;
    viewport.textureInfo = payload.textureInfo ?? null;
    viewport.meshInfo = payload.meshInfo ?? null;
    viewport.isLoading = false;
});

ipc.onAction<{ loading: boolean }>('viewport', 'loading', (payload) => {
    viewport.isLoading = payload.loading;
});

ipc.onAction('viewport', 'cleared', () => {
    viewport.previewData = null;
    viewport.mode = 'none';
    viewport.contentType = null;
    viewport.assetName = null;
    viewport.textureInfo = null;
    viewport.meshInfo = null;
    viewport.isLoading = false;
});

// Clear viewport when project closes
ipc.onAction('project', 'closed', () => {
    viewport.previewData = null;
    viewport.mode = 'none';
    viewport.contentType = null;
    viewport.assetName = null;
    viewport.textureInfo = null;
    viewport.meshInfo = null;
    viewport.isLoading = false;
});
