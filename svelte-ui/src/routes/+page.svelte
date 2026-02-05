<!--
    Main Application Page

    Integrates all components into the main asset viewer layout.
-->
<script lang="ts">
    import AppShell from '$lib/components/layout/AppShell.svelte';
    import SplitPane from '$lib/components/layout/SplitPane.svelte';
    import Panel from '$lib/components/layout/Panel.svelte';
    import MenuBar from '$lib/components/toolbar/MenuBar.svelte';
    import StatusBar from '$lib/components/toolbar/StatusBar.svelte';
    import AssetTree from '$lib/components/tree/AssetTree.svelte';
    import TreeToolbar from '$lib/components/tree/TreeToolbar.svelte';
    import PropertyGrid from '$lib/components/properties/PropertyGrid.svelte';
    import Tabs from '$lib/components/common/Tabs.svelte';
    import ImportPakDialog from '$lib/components/dialogs/ImportPakDialog.svelte';
    import FileBrowser from '$lib/components/dialogs/FileBrowser.svelte';
    import { fileBrowser } from '$lib/view-models/fileBrowser.svelte';
    import { LAYOUT } from '$lib/constants';

    // Bottom panel tabs
    const bottomTabs = [
        { id: 'hex', label: 'Hex View' },
        { id: 'data', label: 'Data Table' },
        { id: 'log', label: 'Log' },
    ];

    let activeBottomTab = $state('hex');
    let bottomPanelCollapsed = $state(false);
</script>

<AppShell>
    {#snippet menuBar()}
        <MenuBar />
    {/snippet}

    <!-- Main content area -->
    <div class="main-content">
        <!-- Left panel: Tree + Properties -->
        <SplitPane
            direction="horizontal"
            initialSize={LAYOUT.PANEL_DEFAULT_WIDTH}
            minSize={LAYOUT.PANEL_MIN_WIDTH}
            maxSize={LAYOUT.PANEL_MAX_WIDTH}
        >
            {#snippet first()}
                <div class="left-panel">
                    <!-- Tree and Properties in vertical split -->
                    <SplitPane direction="vertical" initialSize={300} minSize={150} maxSize={500}>
                        {#snippet first()}
                            <Panel title="Asset Tree" class="tree-panel">
                                {#snippet headerActions()}
                                    <!-- Tree actions handled by TreeToolbar -->
                                {/snippet}
                                <div class="tree-container">
                                    <TreeToolbar />
                                    <AssetTree />
                                </div>
                            </Panel>
                        {/snippet}

                        {#snippet second()}
                            <Panel title="Properties" class="properties-panel">
                                <PropertyGrid />
                            </Panel>
                        {/snippet}
                    </SplitPane>
                </div>
            {/snippet}

            {#snippet second()}
                <!-- Main viewport area with bottom panel -->
                <div class="viewport-area">
                    <!-- 3D/2D Viewport -->
                    <div class="viewport">
                        <div class="viewport-placeholder">
                            <div class="viewport-text">
                                <span class="text-muted">3D/2D Viewport</span>
                                <span class="text-xs text-muted">
                                    Select a mesh or texture to preview
                                </span>
                            </div>
                        </div>
                    </div>

                    <!-- Bottom panel: Hex/Data/Log -->
                    {#if !bottomPanelCollapsed}
                        <div class="bottom-panel">
                            <Panel solid>
                                <Tabs
                                    tabs={bottomTabs}
                                    bind:activeTab={activeBottomTab}
                                >
                                    {#snippet children(tabId)}
                                        <div class="tab-content">
                                            {#if tabId === 'hex'}
                                                <div class="hex-placeholder">
                                                    <span class="text-muted text-sm">
                                                        Hex view will appear here
                                                    </span>
                                                </div>
                                            {:else if tabId === 'data'}
                                                <div class="data-placeholder">
                                                    <span class="text-muted text-sm">
                                                        Data table will appear here
                                                    </span>
                                                </div>
                                            {:else if tabId === 'log'}
                                                <div class="log-placeholder">
                                                    <span class="text-muted text-sm">
                                                        Log messages will appear here
                                                    </span>
                                                </div>
                                            {/if}
                                        </div>
                                    {/snippet}
                                </Tabs>
                            </Panel>
                        </div>
                    {/if}

                    <!-- Bottom panel toggle -->
                    <button
                        class="bottom-panel-toggle"
                        onclick={() => { bottomPanelCollapsed = !bottomPanelCollapsed; }}
                        aria-label={bottomPanelCollapsed ? 'Show bottom panel' : 'Hide bottom panel'}
                    >
                        <svg
                            viewBox="0 0 16 16"
                            fill="currentColor"
                            class:rotated={bottomPanelCollapsed}
                        >
                            <path d="M4 8l4 4 4-4H4z" />
                        </svg>
                    </button>
                </div>
            {/snippet}
        </SplitPane>
    </div>

    {#snippet statusBar()}
        <StatusBar />
    {/snippet}
</AppShell>

<!-- Global dialogs -->
<ImportPakDialog />
<FileBrowser
    open={fileBrowser.isOpen}
    title={fileBrowser.title}
    mode={fileBrowser.mode}
    filters={fileBrowser.filters}
    initialPath={fileBrowser.initialPath}
    basePath={fileBrowser.basePath}
    onSelect={(path) => fileBrowser.handleSelect(path)}
    onCancel={() => fileBrowser.handleCancel()}
/>

<style>
    .main-content {
        height: 100%;
        display: flex;
        flex-direction: column;
    }

    .left-panel {
        height: 100%;
        display: flex;
        flex-direction: column;
    }

    .tree-container {
        display: flex;
        flex-direction: column;
        height: 100%;
    }

    :global(.tree-panel),
    :global(.properties-panel) {
        height: 100%;
    }

    :global(.tree-panel .panel-content),
    :global(.properties-panel .panel-content) {
        display: flex;
        flex-direction: column;
    }

    .viewport-area {
        height: 100%;
        display: flex;
        flex-direction: column;
        position: relative;
    }

    .viewport {
        flex: 1;
        background: var(--bg-primary);
        position: relative;
        overflow: hidden;
    }

    .viewport-placeholder {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .viewport-text {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
    }

    .bottom-panel {
        height: 200px;
        flex-shrink: 0;
        border-top: 1px solid var(--border);
    }

    .tab-content {
        height: 100%;
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .hex-placeholder,
    .data-placeholder,
    .log-placeholder {
        padding: var(--space-4);
    }

    .bottom-panel-toggle {
        position: absolute;
        bottom: 200px;
        right: var(--space-2);
        width: 24px;
        height: 16px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-bottom: none;
        border-radius: var(--radius-md) var(--radius-md) 0 0;
        cursor: pointer;
        z-index: var(--z-panel);
        padding: 0;
    }

    .bottom-panel-toggle:hover {
        background: var(--bg-hover);
    }

    .bottom-panel-toggle svg {
        width: 12px;
        height: 12px;
        color: var(--text-secondary);
        transition: transform var(--transition-fast);
    }

    .bottom-panel-toggle svg.rotated {
        transform: rotate(180deg);
    }

    /* Adjust toggle position when panel is collapsed */
    :global(.viewport-area:has(.bottom-panel)) .bottom-panel-toggle {
        bottom: 200px;
    }

    :global(.viewport-area:not(:has(.bottom-panel))) .bottom-panel-toggle {
        bottom: 0;
    }
</style>
