<!--
    Viewport Preview

    Displays asset previews (textures and mesh renders) received from the C# backend.
    Provides mouse-based camera controls for 3D mesh previews.
-->
<script lang="ts">
    import { viewport, type RenderMode } from '$lib/view-models/viewport.svelte';
    import { scene } from '$lib/view-models/scene.svelte';
    import OrientationGizmo from './OrientationGizmo.svelte';

    let dragMode = $state<'none' | 'orbit' | 'pan'>('none');
    let lastX = 0;
    let lastY = 0;

    // Click-vs-drag detection
    let mouseDownX = 0;
    let mouseDownY = 0;
    let didDrag = false;
    const CLICK_THRESHOLD = 3;

    let imgElement: HTMLImageElement | undefined = $state();

    function handleMouseDown(e: MouseEvent) {
        if (!viewport.has3DControls) return;

        mouseDownX = e.clientX;
        mouseDownY = e.clientY;
        didDrag = false;

        if (e.button === 0) {
            dragMode = 'orbit';
        } else if (e.button === 2) {
            dragMode = 'pan';
        } else {
            return;
        }
        lastX = e.clientX;
        lastY = e.clientY;
    }

    function handleMouseMove(e: MouseEvent) {
        if (dragMode === 'none') return;
        const dx = e.clientX - lastX;
        const dy = e.clientY - lastY;

        if (!didDrag) {
            const totalDx = e.clientX - mouseDownX;
            const totalDy = e.clientY - mouseDownY;
            if (Math.abs(totalDx) > CLICK_THRESHOLD || Math.abs(totalDy) > CLICK_THRESHOLD) {
                didDrag = true;
            } else {
                return;
            }
        }

        lastX = e.clientX;
        lastY = e.clientY;
        if (dragMode === 'orbit') {
            viewport.orbitCamera(dx, dy);
        } else {
            viewport.panCamera(dx, dy);
        }
    }

    function handleMouseUp(e: MouseEvent) {
        const wasDragMode = dragMode;
        dragMode = 'none';

        if (wasDragMode === 'orbit' && !didDrag && viewport.isScene && imgElement) {
            handleViewportClick(e);
        }
    }

    function handleViewportClick(e: MouseEvent) {
        if (!imgElement) return;

        const rect = imgElement.getBoundingClientRect();
        const imgNaturalWidth = imgElement.naturalWidth;
        const imgNaturalHeight = imgElement.naturalHeight;
        if (imgNaturalWidth === 0 || imgNaturalHeight === 0) return;

        // Account for object-fit: contain letterboxing/pillarboxing
        const elemAspect = rect.width / rect.height;
        const imgAspect = imgNaturalWidth / imgNaturalHeight;

        let renderedWidth: number, renderedHeight: number;
        let offsetX: number, offsetY: number;

        if (elemAspect > imgAspect) {
            renderedHeight = rect.height;
            renderedWidth = rect.height * imgAspect;
            offsetX = (rect.width - renderedWidth) / 2;
            offsetY = 0;
        } else {
            renderedWidth = rect.width;
            renderedHeight = rect.width / imgAspect;
            offsetX = 0;
            offsetY = (rect.height - renderedHeight) / 2;
        }

        const relX = e.clientX - rect.left - offsetX;
        const relY = e.clientY - rect.top - offsetY;
        const normalizedX = relX / renderedWidth;
        const normalizedY = relY / renderedHeight;

        if (normalizedX < 0 || normalizedX > 1 || normalizedY < 0 || normalizedY > 1) {
            scene.deselectActor();
            return;
        }

        scene.pickActor(normalizedX, normalizedY);
    }

    function handleContextMenu(e: MouseEvent) {
        if (viewport.has3DControls) e.preventDefault();
    }

    function handleWheel(e: WheelEvent) {
        if (!viewport.has3DControls) return;
        e.preventDefault();
        const delta = e.deltaY > 0 ? -1 : 1;
        viewport.zoomCamera(delta);
    }

    function handleDblClick() {
        if (!viewport.has3DControls) return;
        if (viewport.isScene && scene.selectedActorId) {
            scene.focusActor(scene.selectedActorId);
        } else {
            viewport.resetCamera();
        }
    }

    function handleTimeOfDay(e: Event) {
        const target = e.target as HTMLInputElement;
        viewport.setTimeOfDay(parseFloat(target.value));
    }

    function formatTime(hours: number): string {
        const h = Math.floor(hours);
        const m = Math.floor((hours - h) * 60);
        return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
    }

    const renderModes: { mode: RenderMode; title: string }[] = [
        { mode: 'shadeless', title: 'Shadeless' },
        { mode: 'shaded', title: 'Shaded' },
        { mode: 'wireframe', title: 'Wireframe' },
    ];
</script>

<svelte:window
    onmousemove={handleMouseMove}
    onmouseup={handleMouseUp}
/>

<div
    class="viewport-preview"
    role="img"
    aria-label={viewport.assetName ?? 'Viewport preview'}
>
    {#if viewport.isLoading}
        <div class="viewport-loading">
            <div class="spinner"></div>
            <span class="text-sm text-muted">Loading preview...</span>
        </div>
    {:else if viewport.hasPreview}
        <img
            bind:this={imgElement}
            src={viewport.previewData}
            alt={viewport.assetName ?? 'Asset preview'}
            class="preview-image"
            class:mode-2d={!viewport.has3DControls}
            class:mode-3d={viewport.has3DControls}
            draggable="false"
            onmousedown={handleMouseDown}
            onwheel={handleWheel}
            ondblclick={handleDblClick}
            oncontextmenu={handleContextMenu}
        />
        <!-- 3D/Scene viewport toolbar -->
        {#if viewport.has3DControls}
            <div class="viewport-toolbar">
                <!-- Render mode toggle group -->
                <div class="toolbar-group">
                    {#each renderModes as { mode, title }}
                        <button
                            class="toolbar-icon-btn"
                            class:active={viewport.renderMode === mode}
                            {title}
                            onclick={() => viewport.setRenderMode(mode)}
                        >
                            {#if mode === 'shadeless'}
                                <svg viewBox="0 0 16 16" fill="currentColor">
                                    <circle cx="8" cy="8" r="6" />
                                </svg>
                            {:else if mode === 'shaded'}
                                <svg viewBox="0 0 16 16">
                                    <defs>
                                        <radialGradient id="shade-grad" cx="0.35" cy="0.35" r="0.65">
                                            <stop offset="0%" stop-color="currentColor" stop-opacity="1" />
                                            <stop offset="100%" stop-color="currentColor" stop-opacity="0.2" />
                                        </radialGradient>
                                    </defs>
                                    <circle cx="8" cy="8" r="6" fill="url(#shade-grad)" />
                                </svg>
                            {:else}
                                <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1">
                                    <rect x="3" y="3" width="10" height="10" />
                                    <line x1="3" y1="8" x2="13" y2="8" />
                                    <line x1="8" y1="3" x2="8" y2="13" />
                                </svg>
                            {/if}
                        </button>
                    {/each}
                </div>

                <div class="toolbar-separator"></div>

                <!-- Time of day -->
                <div class="toolbar-group time-group">
                    <svg class="time-icon" viewBox="0 0 16 16" fill="currentColor">
                        <circle cx="8" cy="8" r="3" />
                        <line x1="8" y1="1" x2="8" y2="3" stroke="currentColor" stroke-width="1.5" />
                        <line x1="8" y1="13" x2="8" y2="15" stroke="currentColor" stroke-width="1.5" />
                        <line x1="1" y1="8" x2="3" y2="8" stroke="currentColor" stroke-width="1.5" />
                        <line x1="13" y1="8" x2="15" y2="8" stroke="currentColor" stroke-width="1.5" />
                        <line x1="3.05" y1="3.05" x2="4.46" y2="4.46" stroke="currentColor" stroke-width="1.5" />
                        <line x1="11.54" y1="11.54" x2="12.95" y2="12.95" stroke="currentColor" stroke-width="1.5" />
                        <line x1="3.05" y1="12.95" x2="4.46" y2="11.54" stroke="currentColor" stroke-width="1.5" />
                        <line x1="11.54" y1="4.46" x2="12.95" y2="3.05" stroke="currentColor" stroke-width="1.5" />
                    </svg>
                    <input
                        type="range"
                        class="time-slider"
                        min="0"
                        max="24"
                        step="0.25"
                        value={viewport.timeOfDay}
                        oninput={handleTimeOfDay}
                        title="Time of day: {formatTime(viewport.timeOfDay)}"
                    />
                    <span class="time-label">{formatTime(viewport.timeOfDay)}</span>
                </div>

                <div class="toolbar-separator"></div>

                <!-- Double-sided toggle -->
                <button
                    class="toolbar-btn"
                    class:active={viewport.doubleSided}
                    title={viewport.doubleSided ? 'Double-Sided (click for Single-Sided)' : 'Single-Sided (click for Double-Sided)'}
                    onclick={() => viewport.setDoubleSided(!viewport.doubleSided)}
                >
                    {viewport.doubleSided ? 'Double-Sided' : 'Single-Sided'}
                </button>

                <!-- Exit Scene button (scene mode only) -->
                {#if viewport.isScene}
                    <div class="toolbar-separator"></div>
                    <button
                        class="toolbar-btn exit-scene-btn"
                        title="Exit scene view"
                        onclick={() => scene.exitScene()}
                    >
                        Exit Scene
                    </button>
                {/if}
            </div>
            <div class="gizmo-overlay">
                <OrientationGizmo
                    yaw={viewport.cameraYaw}
                    pitch={viewport.cameraPitch}
                    onSnapView={(yaw, pitch) => viewport.setCameraView(yaw, pitch)}
                />
            </div>
        {/if}
        <!-- Asset info overlay -->
        <div class="info-overlay">
            {#if viewport.assetName}
                <span class="info-name">{viewport.assetName}</span>
            {/if}
            {#if viewport.infoText}
                <span class="info-detail">{viewport.infoText}</span>
            {/if}
        </div>
    {:else}
        <div class="viewport-empty">
            <span class="text-muted">3D/2D Viewport</span>
            <span class="text-xs text-muted">
                Select a mesh or texture to preview
            </span>
        </div>
    {/if}
</div>

<style>
    .viewport-preview {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        overflow: hidden;
        user-select: none;
    }

    .viewport-loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-3);
    }

    .spinner {
        width: 24px;
        height: 24px;
        border: 2px solid var(--border);
        border-top-color: var(--text-secondary);
        border-radius: 50%;
        animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
        to { transform: rotate(360deg); }
    }

    .preview-image {
        max-width: 100%;
        max-height: 100%;
        object-fit: contain;
        image-rendering: auto;
    }

    .preview-image.mode-3d {
        cursor: grab;
        width: 100%;
        height: 100%;
        object-fit: contain;
    }

    .preview-image.mode-3d:active {
        cursor: grabbing;
    }

    .preview-image.mode-2d {
        /* Checkerboard background for textures with alpha */
        background-image:
            linear-gradient(45deg, #1a1a1a 25%, transparent 25%),
            linear-gradient(-45deg, #1a1a1a 25%, transparent 25%),
            linear-gradient(45deg, transparent 75%, #1a1a1a 75%),
            linear-gradient(-45deg, transparent 75%, #1a1a1a 75%);
        background-size: 16px 16px;
        background-position: 0 0, 0 8px, 8px -8px, -8px 0;
        background-color: #222;
    }

    .viewport-toolbar {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-2);
        background: rgba(0, 0, 0, 0.7);
        border-bottom: 1px solid var(--border);
        z-index: 1;
    }

    .toolbar-group {
        display: flex;
        align-items: center;
        gap: 2px;
    }

    .toolbar-icon-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        padding: 0;
        background: transparent;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        color: var(--text-muted);
        cursor: pointer;
        transition: background 0.15s, color 0.15s, border-color 0.15s;
    }

    .toolbar-icon-btn:hover {
        background: rgba(255, 255, 255, 0.08);
        color: var(--text-primary);
    }

    .toolbar-icon-btn.active {
        color: var(--text-secondary);
        border-color: var(--text-secondary);
        background: rgba(255, 255, 255, 0.05);
    }

    .toolbar-icon-btn svg {
        width: 14px;
        height: 14px;
    }

    .toolbar-separator {
        width: 1px;
        height: 16px;
        background: var(--border);
        flex-shrink: 0;
    }

    .time-group {
        gap: var(--space-1);
    }

    .time-icon {
        width: 14px;
        height: 14px;
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .time-slider {
        width: 80px;
        height: 4px;
        -webkit-appearance: none;
        appearance: none;
        background: var(--border);
        border: none;
        border-radius: 2px;
        outline: none;
        cursor: pointer;
        padding: 0;
    }

    .time-slider::-webkit-slider-thumb {
        -webkit-appearance: none;
        appearance: none;
        width: 10px;
        height: 10px;
        border-radius: 50%;
        background: var(--text-secondary);
        cursor: pointer;
    }

    .time-slider::-moz-range-thumb {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        background: var(--text-secondary);
        border: none;
        cursor: pointer;
    }

    .time-label {
        font-size: var(--text-xs);
        color: var(--text-muted);
        font-family: var(--font-mono);
        min-width: 34px;
    }

    .toolbar-btn {
        padding: var(--space-1) var(--space-2);
        background: transparent;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        color: var(--text-muted);
        font-size: var(--text-xs);
        cursor: pointer;
        transition: background 0.15s, color 0.15s;
        margin-left: auto;
    }

    .toolbar-btn:hover {
        background: rgba(255, 255, 255, 0.08);
        color: var(--text-primary);
    }

    .toolbar-btn.active {
        color: var(--text-secondary);
        border-color: var(--text-secondary);
    }

    .exit-scene-btn {
        color: var(--color-array);
        border-color: var(--color-array);
        margin-left: 0;
    }

    .exit-scene-btn:hover {
        color: var(--text-primary);
        background: rgba(255, 80, 80, 0.15);
    }

    .gizmo-overlay {
        position: absolute;
        top: calc(var(--space-1) * 2 + 24px + 1px + var(--space-2));
        right: var(--space-2);
        pointer-events: none;
        z-index: 1;
    }

    .info-overlay {
        position: absolute;
        bottom: var(--space-2);
        left: var(--space-2);
        display: flex;
        flex-direction: column;
        gap: 2px;
        padding: var(--space-1) var(--space-2);
        background: rgba(0, 0, 0, 0.65);
        border-radius: var(--radius-sm);
        pointer-events: none;
    }

    .info-name {
        font-size: var(--text-sm);
        color: var(--text-primary);
        font-weight: 500;
    }

    .info-detail {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    .info-overlay:empty {
        display: none;
    }

    .viewport-empty {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
    }
</style>
