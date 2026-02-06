<!--
    StatusBar Component

    Application status bar showing asset info, loading state, and messages.
-->
<script lang="ts">
    import { asset } from '$lib/view-models/asset.svelte';
    import { pak } from '$lib/view-models/pak.svelte';
    import { project } from '$lib/view-models/project.svelte';
    import { tree } from '$lib/view-models/tree.svelte';
    import GameVersionSelector from './GameVersionSelector.svelte';

    // Current status message (transient UI state)
    let statusMessage = $state<string | null>(null);

    // Derived status items
    let assetName = $derived(asset.assetInfo?.fileName ?? 'No asset');
    let engineVersion = $derived(asset.assetInfo?.engineVersion ?? '-');
    let exportCount = $derived(asset.assetInfo?.exportCount ?? 0);
    let importCount = $derived(asset.assetInfo?.importCount ?? 0);
    let isModified = $derived(asset.isModified);
</script>

<div class="status-bar">
    <!-- Left side: Asset info -->
    <div class="status-left">
        {#if pak.isExtracting}
            <div class="status-item extracting">
                <div class="loading-spinner small"></div>
                <span>Extracting: {pak.currentFile} / {pak.totalFiles}</span>
            </div>
        {:else if asset.isLoading || tree.isLoading}
            <div class="status-item loading">
                <div class="loading-spinner small"></div>
                <span>Loading...</span>
            </div>
        {:else if statusMessage}
            <div class="status-item message">
                {statusMessage}
            </div>
        {:else}
            <div class="status-item">
                <span class="status-label">Asset:</span>
                <span class="status-value" class:modified={isModified}>
                    {assetName}
                    {#if isModified}
                        <span class="modified-indicator">*</span>
                    {/if}
                </span>
            </div>
        {/if}
    </div>

    <!-- Right side: Statistics -->
    <div class="status-right">
        {#if project.hasProject}
            <div class="status-item version-item">
                <GameVersionSelector />
            </div>
        {/if}
        {#if asset.assetInfo}
            <div class="status-item">
                <span class="status-label">UE:</span>
                <span class="status-value">{engineVersion}</span>
            </div>
            <div class="status-item">
                <span class="status-label">Exports:</span>
                <span class="status-value">{exportCount}</span>
            </div>
            <div class="status-item">
                <span class="status-label">Imports:</span>
                <span class="status-value">{importCount}</span>
            </div>
        {/if}
    </div>
</div>

<style>
    .status-bar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        height: 100%;
        padding: 0 var(--space-3);
        font-size: var(--text-xs);
    }

    .status-left,
    .status-right {
        display: flex;
        align-items: center;
        gap: var(--space-1);
    }

    .status-item {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: 0 var(--space-2);
        border-right: 1px solid var(--border);
    }

    .status-left .status-item:last-child,
    .status-right .status-item:last-child {
        border-right: none;
    }

    .status-item.version-item {
        padding: 0;
    }

    .status-item.loading {
        color: var(--accent-info);
    }

    .status-item.extracting {
        color: var(--accent-info);
    }

    .status-item.message {
        color: var(--text-secondary);
    }

    .status-label {
        color: var(--text-muted);
    }

    .status-value {
        color: var(--text-secondary);
        font-family: var(--font-mono);
    }

    .status-value.modified {
        color: var(--accent-warning);
    }

    .modified-indicator {
        color: var(--accent-warning);
        font-weight: bold;
    }

    .loading-spinner.small {
        width: 12px;
        height: 12px;
        border-width: 1px;
    }
</style>
