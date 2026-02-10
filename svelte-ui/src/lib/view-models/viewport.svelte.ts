/**
 * Viewport View Model
 *
 * Holds the current preview state pushed from C#.
 * ALL data is owned by C# — this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate preview data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { ViewportPreviewPayload, SceneInfo } from '$lib/bridge/types';

export type RenderMode = 'shaded' | 'shadeless' | 'wireframe';

class ViewportVM {
    previewData = $state<string | null>(null);
    mode = $state<'2d' | '3d' | 'none'>('none');
    contentType = $state<string | null>(null);
    isLoading = $state(false);
    assetName = $state<string | null>(null);

    textureInfo = $state<{ width: number; height: number; format: string } | null>(null);
    meshInfo = $state<{ vertexCount: number; triangleCount: number; lodCount: number } | null>(null);
    sceneInfo = $state<SceneInfo | null>(null);
    doubleSided = $state(true);
    renderMode = $state<RenderMode>('shaded');
    timeOfDay = $state(10.0);
    cameraYaw = $state(45);
    cameraPitch = $state(-30);

    get hasPreview(): boolean {
        return this.previewData !== null;
    }

    get is3D(): boolean {
        return this.mode === '3d';
    }

    get isScene(): boolean {
        return this.mode === 'scene';
    }

    get has3DControls(): boolean {
        return this.mode === '3d' || this.mode === 'scene';
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
        if (this.sceneInfo) {
            return `${this.sceneInfo.actorCount} actors — ${this.sceneInfo.levelName}`;
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

    panCamera(dx: number, dy: number): void {
        ipc.send({
            type: 'viewport',
            action: 'panCamera',
            payload: { dx, dy },
        });
    }

    resetCamera(): void {
        ipc.send({
            type: 'viewport',
            action: 'resetCamera',
            payload: {},
        });
    }

    setDoubleSided(enabled: boolean): void {
        this.doubleSided = enabled;
        ipc.send({
            type: 'viewport',
            action: 'setDoubleSided',
            payload: { enabled },
        });
    }

    setRenderMode(mode: RenderMode): void {
        this.renderMode = mode;
        ipc.send({
            type: 'viewport',
            action: 'setRenderMode',
            payload: { mode },
        });
    }

    setTimeOfDay(hours: number): void {
        this.timeOfDay = hours;
        ipc.send({
            type: 'viewport',
            action: 'setTimeOfDay',
            payload: { hours },
        });
    }

    setCameraView(yaw: number, pitch: number): void {
        ipc.send({
            type: 'viewport',
            action: 'setCameraView',
            payload: { yaw, pitch },
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
    viewport.sceneInfo = payload.sceneInfo ?? null;
    viewport.isLoading = false;
});

ipc.onAction<{ loading: boolean }>('viewport', 'loading', (payload) => {
    viewport.isLoading = payload.loading;
});

ipc.onAction<{ yaw: number; pitch: number }>('viewport', 'cameraState', (payload) => {
    viewport.cameraYaw = payload.yaw;
    viewport.cameraPitch = payload.pitch;
});

ipc.onAction('viewport', 'cleared', () => {
    viewport.previewData = null;
    viewport.mode = 'none';
    viewport.contentType = null;
    viewport.assetName = null;
    viewport.textureInfo = null;
    viewport.meshInfo = null;
    viewport.sceneInfo = null;
    viewport.isLoading = false;
    viewport.cameraYaw = 45;
    viewport.cameraPitch = -30;
});

// Clear viewport when project closes
ipc.onAction('project', 'closed', () => {
    viewport.previewData = null;
    viewport.mode = 'none';
    viewport.contentType = null;
    viewport.assetName = null;
    viewport.textureInfo = null;
    viewport.meshInfo = null;
    viewport.sceneInfo = null;
    viewport.isLoading = false;
    viewport.cameraYaw = 45;
    viewport.cameraPitch = -30;
});
