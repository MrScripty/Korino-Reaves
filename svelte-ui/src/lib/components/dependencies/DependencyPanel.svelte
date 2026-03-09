<!--
    Dependency Panel

    Shows dependencies and dependents for the currently selected file
    as a recursive tree grouped by reference type. Each node can be
    expanded to show its own transitive dependencies (lazy-loaded).
-->
<script lang="ts">
    import { deps } from '$lib/view-models/dependencies.svelte';
    import { tree } from '$lib/view-models/tree.svelte';
    import ScrollContainer from '$lib/components/common/ScrollContainer.svelte';
    import type { DependencyReference } from '$lib/bridge/types';

    // Track the selected file path extracted from tree selection
    const selectedFilePath = $derived.by(() => {
        const node = tree.selectedNode;
        if (node && node.id.startsWith('file:')) {
            return node.id.slice(5);
        }
        return null;
    });

    // Request dependency data when the selected file changes
    $effect(() => {
        const path = selectedFilePath;
        if (path && deps.hasGraph) {
            deps.requestDependencies(path);
        }
    });

    function basename(path: string): string {
        const parts = path.split('/');
        return parts[parts.length - 1] ?? path;
    }

    function handleScan() {
        deps.startScan();
    }

    function handleCancelScan() {
        deps.cancelScan();
    }

    // Request stats on mount if we don't have them
    $effect(() => {
        if (deps.stats === null) {
            deps.requestStats();
        }
    });

    /** Group an array of DependencyReferences by refType */
    function groupByType(refs: DependencyReference[]): Map<string, DependencyReference[]> {
        const groups = new Map<string, DependencyReference[]>();
        for (const ref of refs) {
            const type = ref.refType || 'Unknown';
            const group = groups.get(type);
            if (group) {
                group.push(ref);
            } else {
                groups.set(type, [ref]);
            }
        }
        return groups;
    }

    function navigateTo(path: string) {
        tree.selectNode('file:' + path);
    }
</script>

<div class="dependency-panel">
    <!-- Header -->
    <div class="panel-header">
        <span class="panel-title">Dependencies</span>
        {#if deps.hasGraph && selectedFilePath}
            <span class="panel-count">
                {deps.dependencies.length + deps.dependents.length}
            </span>
        {/if}
    </div>

    <!-- Scanning progress -->
    {#if deps.isScanning}
        <div class="scan-section">
            {#if deps.scanProgress}
                <div class="progress-bar">
                    <div
                        class="progress-fill"
                        style="width: {Math.round((deps.scanProgress.current / Math.max(deps.scanProgress.total, 1)) * 100)}%"
                    ></div>
                </div>
                <div class="progress-text">
                    {deps.scanProgress.phase === 'enumerating' ? 'Enumerating files...' : ''}
                    {deps.scanProgress.phase === 'scanning' ? `Scanning ${deps.scanProgress.current}/${deps.scanProgress.total}` : ''}
                    {deps.scanProgress.phase === 'writing' ? 'Writing database...' : ''}
                </div>
                {#if deps.scanProgress.currentFile}
                    <div class="progress-file">{basename(deps.scanProgress.currentFile)}</div>
                {/if}
            {:else}
                <div class="progress-text">Starting scan...</div>
            {/if}
            <button class="cancel-btn" onclick={handleCancelScan}>Cancel</button>
        </div>

    <!-- No graph available -->
    {:else if !deps.hasGraph}
        <div class="empty-state">
            <span class="text-muted text-sm">No dependency data</span>
            <button class="scan-btn" onclick={handleScan}>Scan Project</button>
            {#if deps.stats && !deps.stats.exists}
                <span class="text-muted text-xs">
                    Scans all assets to build the dependency graph
                </span>
            {/if}
        </div>

    <!-- No file selected -->
    {:else if !selectedFilePath}
        <div class="empty-state">
            <span class="text-muted text-sm">Select a file to view dependencies</span>
            {#if deps.stats}
                <span class="stats-text">
                    {deps.stats.assetCount?.toLocaleString()} assets, {deps.stats.edgeCount?.toLocaleString()} edges
                </span>
            {/if}
        </div>

    <!-- Dependency tree -->
    {:else}
        <div class="selected-file" title={selectedFilePath}>
            {basename(selectedFilePath)}
        </div>

        <ScrollContainer>
            <!-- Dependencies section -->
            {#if deps.dependencies.length > 0}
                <div class="section-label">Dependencies</div>
                {@const groups = groupByType(deps.dependencies)}
                {#each [...groups.entries()] as [typeName, refs] (typeName)}
                    <div class="type-group">
                        <div class="type-header">{typeName}</div>
                        {#each refs as ref (ref.path)}
                            {@const expanded = deps.isExpanded(ref.path)}
                            {@const loading = deps.isLoading(ref.path)}
                            {@const children = deps.getChildren(ref.path)}
                            {@const childGroups = groupByType(children)}
                            <div class="tree-node">
                                <div class="node-row">
                                    <button
                                        class="expand-btn"
                                        onclick={() => deps.toggleNode(ref.path)}
                                        class:loading
                                    >
                                        {#if loading}
                                            <svg class="spinner" viewBox="0 0 16 16"><circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.5" stroke-dasharray="20 12" /></svg>
                                        {:else if expanded && children.length > 0}
                                            <svg viewBox="0 0 16 16" fill="currentColor"><path d="M4 6l4 4 4-4z" /></svg>
                                        {:else}
                                            <svg viewBox="0 0 16 16" fill="currentColor"><path d="M6 4l4 4-4 4z" /></svg>
                                        {/if}
                                    </button>
                                    <button
                                        class="node-label"
                                        title={ref.path}
                                        onclick={() => navigateTo(ref.path)}
                                    >
                                        {basename(ref.path)}
                                    </button>
                                </div>
                                {#if expanded}
                                    <div class="node-children">
                                        {#if children.length === 0 && !loading}
                                            <div class="leaf-hint">No dependencies</div>
                                        {:else}
                                            {#each [...childGroups.entries()] as [childType, childRefs] (childType)}
                                                <div class="type-group nested">
                                                    <div class="type-header">{childType}</div>
                                                    {#each childRefs as childRef (childRef.path)}
                                                        {@const cExpanded = deps.isExpanded(childRef.path)}
                                                        {@const cLoading = deps.isLoading(childRef.path)}
                                                        {@const cChildren = deps.getChildren(childRef.path)}
                                                        {@const cChildGroups = groupByType(cChildren)}
                                                        <div class="tree-node">
                                                            <div class="node-row">
                                                                <button
                                                                    class="expand-btn"
                                                                    onclick={() => deps.toggleNode(childRef.path)}
                                                                    class:loading={cLoading}
                                                                >
                                                                    {#if cLoading}
                                                                        <svg class="spinner" viewBox="0 0 16 16"><circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.5" stroke-dasharray="20 12" /></svg>
                                                                    {:else if cExpanded && cChildren.length > 0}
                                                                        <svg viewBox="0 0 16 16" fill="currentColor"><path d="M4 6l4 4 4-4z" /></svg>
                                                                    {:else}
                                                                        <svg viewBox="0 0 16 16" fill="currentColor"><path d="M6 4l4 4-4 4z" /></svg>
                                                                    {/if}
                                                                </button>
                                                                <button
                                                                    class="node-label"
                                                                    title={childRef.path}
                                                                    onclick={() => navigateTo(childRef.path)}
                                                                >
                                                                    {basename(childRef.path)}
                                                                </button>
                                                            </div>
                                                            {#if cExpanded}
                                                                <div class="node-children">
                                                                    {#if cChildren.length === 0 && !cLoading}
                                                                        <div class="leaf-hint">No dependencies</div>
                                                                    {:else}
                                                                        {#each [...cChildGroups.entries()] as [gcType, gcRefs] (gcType)}
                                                                            <div class="type-group nested">
                                                                                <div class="type-header">{gcType}</div>
                                                                                {#each gcRefs as gcRef (gcRef.path)}
                                                                                    <div class="tree-node">
                                                                                        <div class="node-row">
                                                                                            <button
                                                                                                class="expand-btn"
                                                                                                onclick={() => deps.toggleNode(gcRef.path)}
                                                                                            >
                                                                                                <svg viewBox="0 0 16 16" fill="currentColor"><path d="M6 4l4 4-4 4z" /></svg>
                                                                                            </button>
                                                                                            <button
                                                                                                class="node-label"
                                                                                                title={gcRef.path}
                                                                                                onclick={() => navigateTo(gcRef.path)}
                                                                                            >
                                                                                                {basename(gcRef.path)}
                                                                                            </button>
                                                                                        </div>
                                                                                    </div>
                                                                                {/each}
                                                                            </div>
                                                                        {/each}
                                                                    {/if}
                                                                </div>
                                                            {/if}
                                                        </div>
                                                    {/each}
                                                </div>
                                            {/each}
                                        {/if}
                                    </div>
                                {/if}
                            </div>
                        {/each}
                    </div>
                {/each}
            {:else}
                <div class="empty-list">
                    <span class="text-muted text-xs">No dependencies</span>
                </div>
            {/if}

            <!-- Dependents section -->
            {#if deps.dependents.length > 0}
                <div class="section-label">Dependents</div>
                {@const deptGroups = groupByType(deps.dependents)}
                {#each [...deptGroups.entries()] as [typeName, refs] (typeName)}
                    <div class="type-group">
                        <div class="type-header">{typeName}</div>
                        {#each refs as ref (ref.path)}
                            <div class="tree-node">
                                <div class="node-row">
                                    <button
                                        class="node-label leaf"
                                        title={ref.path}
                                        onclick={() => navigateTo(ref.path)}
                                    >
                                        {basename(ref.path)}
                                    </button>
                                </div>
                            </div>
                        {/each}
                    </div>
                {/each}
            {:else if deps.dependencies.length > 0}
                <div class="section-label">Dependents</div>
                <div class="empty-list">
                    <span class="text-muted text-xs">No dependents</span>
                </div>
            {/if}
        </ScrollContainer>
    {/if}
</div>

<style>
    .dependency-panel {
        display: flex;
        flex-direction: column;
        height: 100%;
        background: var(--bg-primary);
    }

    .panel-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
    }

    .panel-title {
        font-size: var(--text-sm);
        font-weight: 600;
        color: var(--text-primary);
    }

    .panel-count {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    /* Scan progress */
    .scan-section {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-4) var(--space-3);
    }

    .progress-bar {
        width: 100%;
        height: 2px;
        background: var(--border);
        border-radius: 1px;
    }

    .progress-fill {
        height: 100%;
        background: var(--text-secondary);
        transition: width 0.2s ease;
        border-radius: 1px;
    }

    .progress-text {
        font-size: var(--text-xs);
        color: var(--text-muted);
        text-align: center;
    }

    .progress-file {
        font-size: 10px;
        color: var(--text-muted);
        text-align: center;
        max-width: 100%;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .cancel-btn {
        padding: 2px var(--space-3);
        background: transparent;
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        color: var(--text-secondary);
        font-size: var(--text-xs);
        cursor: pointer;
    }

    .cancel-btn:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    /* Empty states */
    .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-3);
        padding: var(--space-6) var(--space-3);
        flex: 1;
    }

    .scan-btn {
        padding: var(--space-1) var(--space-4);
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        color: var(--text-primary);
        font-size: var(--text-sm);
        cursor: pointer;
    }

    .scan-btn:hover {
        background: var(--bg-hover);
    }

    .stats-text {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    /* Selected file */
    .selected-file {
        padding: var(--space-1) var(--space-3);
        font-size: var(--text-xs);
        color: var(--text-secondary);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        flex-shrink: 0;
    }

    /* Tree (handled by ScrollContainer) */

    .section-label {
        padding: 4px var(--space-3);
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--text-secondary);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
        border-top: 1px solid var(--border);
    }

    .type-group {
        border-bottom: 1px solid var(--border);
    }

    .type-group.nested {
        border-bottom: none;
    }

    .type-header {
        padding: 2px var(--space-3) 2px var(--space-4);
        font-size: 10px;
        font-weight: 600;
        color: var(--text-muted);
        text-transform: uppercase;
        letter-spacing: 0.05em;
    }

    .tree-node {
        /* container for a node row + its children */
    }

    .node-row {
        display: flex;
        align-items: center;
        height: 24px;
    }

    .expand-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 20px;
        height: 24px;
        padding: 0;
        background: transparent;
        border: none;
        color: var(--text-muted);
        cursor: pointer;
        flex-shrink: 0;
        margin-left: var(--space-2);
    }

    .expand-btn:hover {
        color: var(--text-primary);
    }

    .expand-btn svg {
        width: 12px;
        height: 12px;
    }

    .expand-btn .spinner {
        animation: spin 1s linear infinite;
    }

    @keyframes spin {
        to { transform: rotate(360deg); }
    }

    .node-label {
        flex: 1;
        min-width: 0;
        padding: 0 var(--space-2);
        background: transparent;
        border: none;
        cursor: pointer;
        text-align: left;
        font-size: var(--text-xs);
        color: var(--text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        height: 24px;
        display: flex;
        align-items: center;
    }

    .node-label:hover {
        background: var(--bg-hover);
    }

    .node-label.leaf {
        padding-left: calc(var(--space-2) + 20px);
    }

    .node-children {
        padding-left: 12px;
        border-left: 1px solid var(--border);
        margin-left: calc(var(--space-2) + 9px);
    }

    .leaf-hint {
        padding: 2px var(--space-3);
        font-size: 10px;
        color: var(--text-muted);
        font-style: italic;
    }

    .empty-list {
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-2) var(--space-3);
    }
</style>
