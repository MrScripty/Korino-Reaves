<!--
    DockZone Component

    Renders a single dock zone: a tab bar at the top and the active
    panel's component content below. Panel components are rendered via
    an {#if} chain (matching established codebase patterns).
-->
<script lang="ts">
    import type { ZoneId } from './dockTypes';
    import DockTabBar from './DockTabBar.svelte';
    import { dock } from '$lib/view-models/dock.svelte';

    // Panel components
    import AssetTree from '$lib/components/tree/AssetTree.svelte';
    import TreeToolbar from '$lib/components/tree/TreeToolbar.svelte';
    import PropertyGrid from '$lib/components/properties/PropertyGrid.svelte';
    import DataTablePanel from '$lib/components/datatable/DataTablePanel.svelte';
    import LogPanel from '$lib/components/log/LogPanel.svelte';
    import DependencyPanel from '$lib/components/dependencies/DependencyPanel.svelte';
    import SceneOutliner from '$lib/components/scene/SceneOutliner.svelte';

    interface Props {
        zoneId: ZoneId;
        class?: string;
    }

    let { zoneId, class: className = '' }: Props = $props();

    const activePanel = $derived(dock.getActivePanel(zoneId));
</script>

<div class="dock-zone {className}">
    <DockTabBar {zoneId} />
    <div class="dock-zone-content">
        {#if activePanel === 'assetTree'}
            <div class="tree-container">
                <TreeToolbar />
                <AssetTree />
            </div>
        {:else if activePanel === 'properties'}
            <PropertyGrid />
        {:else if activePanel === 'hexView'}
            <div class="hex-placeholder">
                <span class="text-muted text-sm">
                    Hex view will appear here
                </span>
            </div>
        {:else if activePanel === 'dataTable'}
            <DataTablePanel />
        {:else if activePanel === 'log'}
            <LogPanel />
        {:else if activePanel === 'dependencies'}
            <DependencyPanel />
        {:else if activePanel === 'sceneOutliner'}
            <SceneOutliner />
        {/if}
    </div>
</div>

<style>
    .dock-zone {
        display: flex;
        flex-direction: column;
        height: 100%;
        overflow: hidden;
        background: var(--panel-bg);
        backdrop-filter: blur(var(--panel-blur));
        -webkit-backdrop-filter: blur(var(--panel-blur));
        border: 1px solid var(--panel-border);
        border-radius: var(--radius-lg);
    }

    .dock-zone-content {
        flex: 1;
        overflow: auto;
        min-height: 0;
    }

    .tree-container {
        display: flex;
        flex-direction: column;
        height: 100%;
        min-height: 0;
    }

    .hex-placeholder {
        flex: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-4);
    }
</style>
