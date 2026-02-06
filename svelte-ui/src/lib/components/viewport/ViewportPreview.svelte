<!--
    Viewport Preview

    Displays asset previews (textures and mesh renders) received from the C# backend.
    Provides mouse-based camera controls for 3D mesh previews.
-->
<script lang="ts">
    import { viewport } from '$lib/view-models/viewport.svelte';

    let isDragging = $state(false);
    let lastX = 0;
    let lastY = 0;

    function handleMouseDown(e: MouseEvent) {
        if (!viewport.is3D) return;
        if (e.button !== 0) return; // Left button only
        isDragging = true;
        lastX = e.clientX;
        lastY = e.clientY;
    }

    function handleMouseMove(e: MouseEvent) {
        if (!isDragging) return;
        const dx = e.clientX - lastX;
        const dy = e.clientY - lastY;
        lastX = e.clientX;
        lastY = e.clientY;
        viewport.orbitCamera(dx, dy);
    }

    function handleMouseUp() {
        isDragging = false;
    }

    function handleWheel(e: WheelEvent) {
        if (!viewport.is3D) return;
        e.preventDefault();
        const delta = e.deltaY > 0 ? -1 : 1;
        viewport.zoomCamera(delta);
    }

    function handleDblClick() {
        if (!viewport.is3D) return;
        viewport.resetCamera();
    }
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
            src={viewport.previewData}
            alt={viewport.assetName ?? 'Asset preview'}
            class="preview-image"
            class:mode-2d={!viewport.is3D}
            class:mode-3d={viewport.is3D}
            draggable="false"
            onmousedown={handleMouseDown}
            onwheel={handleWheel}
            ondblclick={handleDblClick}
        />
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
