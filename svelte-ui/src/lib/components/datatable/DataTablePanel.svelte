<!--
    Data Table Panel

    Displays SQLite database tables for the currently selected file.
    Shows sub-tabs for each table that has data (imports, exports,
    properties, custom versions, edges, etc.).
-->
<script lang="ts">
    import { tree } from '$lib/view-models/tree.svelte';
    import { dataTable, type DataTableTab } from '$lib/view-models/datatable.svelte';
    import { deps } from '$lib/view-models/dependencies.svelte';
    import ScrollContainer from '$lib/components/common/ScrollContainer.svelte';
    import type {
        DbAssetInfo,
        DbFlatPropertyEntry,
    } from '$lib/bridge/types';

    const selectedFilePath = $derived.by(() => {
        const sel = tree.selection.selectedId;
        if (sel && sel.startsWith('file:')) {
            return sel.slice(5);
        }
        return null;
    });

    // Auto-request when file selection changes
    $effect(() => {
        const path = selectedFilePath;
        if (path && deps.hasGraph) {
            dataTable.requestTables(path);
        } else {
            dataTable.clear();
        }
    });

    function selectTab(tab: DataTableTab) {
        dataTable.activeTab = tab;
    }

    function basename(path: string): string {
        const parts = path.split('/');
        return parts[parts.length - 1] ?? path;
    }

    function formatFlags(flags: number): string {
        return `0x${flags.toString(16).toUpperCase()}`;
    }

    function formatSize(bytes: number): string {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    }

    function propertyValue(p: DbFlatPropertyEntry): string {
        if (p.valueText != null) return p.valueText;
        if (p.valueInt != null) return String(p.valueInt);
        if (p.valueFloat != null) return String(p.valueFloat);
        if (p.valueRef != null) return p.valueRef;
        return '';
    }

    /** Build a map of property id → nesting depth from parentId chains */
    function computePropertyDepths(props: DbFlatPropertyEntry[]): Map<number, number> {
        const depthMap = new Map<number, number>();
        // First pass: index by id for O(1) parent lookup
        const byId = new Map<number, DbFlatPropertyEntry>();
        for (const p of props) {
            byId.set(p.id, p);
        }
        // Compute depth by walking up the parent chain
        function getDepth(p: DbFlatPropertyEntry): number {
            const cached = depthMap.get(p.id);
            if (cached !== undefined) return cached;
            if (p.parentId == null) {
                depthMap.set(p.id, 0);
                return 0;
            }
            const parent = byId.get(p.parentId);
            const depth = parent ? getDepth(parent) + 1 : 0;
            depthMap.set(p.id, depth);
            return depth;
        }
        for (const p of props) {
            getDepth(p);
        }
        return depthMap;
    }

    const propertyDepths = $derived.by(() => {
        if (!dataTable.data?.properties) return new Map<number, number>();
        return computePropertyDepths(dataTable.data.properties);
    });

    /** Set of property IDs that have children (precomputed for performance) */
    const propertyParentIds = $derived.by(() => {
        if (!dataTable.data?.properties) return new Set<number>();
        const parentIds = new Set<number>();
        for (const p of dataTable.data.properties) {
            if (p.parentId != null) parentIds.add(p.parentId);
        }
        return parentIds;
    });

    function assetInfoRows(info: DbAssetInfo): { key: string; value: string }[] {
        const rows = [
            { key: 'Path', value: info.path },
            { key: 'Type', value: info.assetType },
            { key: 'Imports', value: String(info.importCount) },
            { key: 'Exports', value: String(info.exportCount) },
            { key: 'Package Flags', value: formatFlags(info.packageFlags) },
            { key: 'Unversioned', value: info.isUnversioned ? 'Yes' : 'No' },
        ];
        if (info.objectVersion)
            rows.push({ key: 'Object Version', value: info.objectVersion });
        if (info.objectVersionUE5)
            rows.push({ key: 'Object Version UE5', value: info.objectVersionUE5 });
        if (info.engineMajor != null) {
            const ver = `${info.engineMajor}.${info.engineMinor ?? 0}.${info.enginePatch ?? 0}`;
            rows.push({ key: 'Engine Version', value: ver });
        }
        if (info.engineChangelist != null)
            rows.push({ key: 'Engine Changelist', value: String(info.engineChangelist) });
        if (info.engineBranch)
            rows.push({ key: 'Engine Branch', value: info.engineBranch });
        return rows;
    }
</script>

<div class="datatable-panel">
    {#if !deps.hasGraph}
        <div class="empty-state">
            <span class="text-muted text-sm">No asset database. Run a dependency scan first.</span>
        </div>
    {:else if !selectedFilePath}
        <div class="empty-state">
            <span class="text-muted text-sm">Select a file to view data tables</span>
        </div>
    {:else if dataTable.isLoading}
        <div class="empty-state">
            <span class="text-muted text-sm">Loading...</span>
        </div>
    {:else if dataTable.data}
        <!-- Sub-tab bar -->
        <div class="sub-tab-bar">
            {#each dataTable.availableTabs as tab (tab.id)}
                <button
                    class="sub-tab"
                    class:active={dataTable.activeTab === tab.id}
                    onclick={() => selectTab(tab.id)}
                >
                    {tab.label}
                    <span class="tab-count">{tab.count}</span>
                </button>
            {/each}
        </div>

        <!-- Table content -->
        <ScrollContainer direction="both">
            {#if dataTable.activeTab === 'assetInfo'}
                <table class="data-table kv-table">
                    <tbody>
                        {#each assetInfoRows(dataTable.data.assetInfo) as row (row.key)}
                            <tr>
                                <td class="kv-key">{row.key}</td>
                                <td class="kv-value">{row.value}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'imports'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Index</th>
                            <th>Object Name</th>
                            <th>Class</th>
                            <th>Package</th>
                            <th>Outer</th>
                            <th>Optional</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.imports as imp (imp.id)}
                            <tr>
                                <td class="num">{imp.importIndex}</td>
                                <td>{imp.objectName}</td>
                                <td class="type">{imp.className}</td>
                                <td class="path" title={imp.packageName ?? ''}>{imp.packageName ?? ''}</td>
                                <td class="num">{imp.outerIndex ?? ''}</td>
                                <td class="bool">{imp.isOptional ? 'Y' : ''}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'exports'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Index</th>
                            <th>Object Name</th>
                            <th>Class</th>
                            <th>Super</th>
                            <th>Size</th>
                            <th>Flags</th>
                            <th>Asset</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.exports as exp (exp.id)}
                            <tr>
                                <td class="num">{exp.exportIndex}</td>
                                <td>{exp.objectName}</td>
                                <td class="type">{exp.className ?? ''}</td>
                                <td class="type">{exp.superName ?? ''}</td>
                                <td class="num">{formatSize(exp.serialSize)}</td>
                                <td class="num">{formatFlags(exp.objectFlags)}</td>
                                <td class="bool">{exp.isAsset ? 'Y' : ''}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'properties'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Export</th>
                            <th>Name</th>
                            <th>Type</th>
                            <th>Struct</th>
                            <th>Value</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.properties as prop (prop.id)}
                            {@const depth = propertyDepths.get(prop.id) ?? 0}
                            {@const isContainer = propertyParentIds.has(prop.id)}
                            <tr class:prop-container={isContainer}>
                                <td class="type" title={`Export ${prop.exportIndex}`}>
                                    {#if depth === 0}{prop.exportName}{/if}
                                </td>
                                <td class="prop-name" style="padding-left: {8 + depth * 16}px">
                                    {#if isContainer}<span class="prop-expand-icon">&#9662;</span>{/if}
                                    {prop.name}
                                    {#if prop.arrayIndex > 0}<span class="prop-array-index">[{prop.arrayIndex}]</span>{/if}
                                </td>
                                <td class="type">{prop.propertyType}</td>
                                <td class="type">{prop.structType ?? ''}</td>
                                <td class="value" title={propertyValue(prop)}>{propertyValue(prop)}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'customVersions'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Friendly Name</th>
                            <th>GUID</th>
                            <th>Version</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.customVersions as cv (cv.id)}
                            <tr>
                                <td>{cv.friendlyName ?? ''}</td>
                                <td class="guid">{cv.guid}</td>
                                <td class="num">{cv.version}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'edges'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Target Path</th>
                            <th>Ref Type</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.edges as edge (edge.id)}
                            <tr>
                                <td class="path" title={edge.targetPath}>{basename(edge.targetPath)}</td>
                                <td class="type">{edge.refType}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'exportDependencies'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Export</th>
                            <th>Dep Type</th>
                            <th>Target Index</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.exportDependencies as ed (ed.id)}
                            <tr>
                                <td class="type" title={`Export ${ed.exportIndex}`}>{ed.exportName ?? String(ed.exportIndex)}</td>
                                <td class="type">{ed.depType}</td>
                                <td class="num">{ed.targetIndex}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'gatherableText'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Namespace</th>
                            <th>Key</th>
                            <th>Source String</th>
                            <th>Editor Only</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.gatherableText as gt (gt.id)}
                            <tr>
                                <td>{gt.namespace ?? ''}</td>
                                <td>{gt.keyName ?? ''}</td>
                                <td class="value" title={gt.sourceString ?? ''}>{gt.sourceString ?? ''}</td>
                                <td class="bool">{gt.isEditorOnly ? 'Y' : ''}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'searchableNames'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Export Index</th>
                            <th>Name</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.searchableNames as sn (sn.id)}
                            <tr>
                                <td class="num">{sn.exportIndex}</td>
                                <td>{sn.name}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>

            {:else if dataTable.activeTab === 'worldTileInfo'}
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Position</th>
                            <th>Abs Position</th>
                            <th>Layer</th>
                            <th>Streaming Dist</th>
                            <th>Parent Tile</th>
                            <th>Z-Order</th>
                        </tr>
                    </thead>
                    <tbody>
                        {#each dataTable.data.worldTileInfo as wti (wti.id)}
                            <tr>
                                <td class="num">{wti.positionX ?? '?'}, {wti.positionY ?? '?'}, {wti.positionZ ?? '?'}</td>
                                <td class="num">{wti.absPositionX ?? '?'}, {wti.absPositionY ?? '?'}, {wti.absPositionZ ?? '?'}</td>
                                <td>{wti.layerName ?? ''}</td>
                                <td class="num">{wti.streamingDistance ?? ''}</td>
                                <td class="path" title={wti.parentTilePackage ?? ''}>{wti.parentTilePackage ? basename(wti.parentTilePackage) : ''}</td>
                                <td class="num">{wti.zOrder ?? ''}</td>
                            </tr>
                        {/each}
                    </tbody>
                </table>
            {/if}
        </ScrollContainer>
    {:else}
        <div class="empty-state">
            <span class="text-muted text-sm">Asset not found in database</span>
        </div>
    {/if}
</div>

<style>
    .datatable-panel {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
        background: var(--bg-primary);
    }

    .empty-state {
        flex: 1;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-4);
    }

    /* Sub-tab bar */
    .sub-tab-bar {
        display: flex;
        gap: 1px;
        padding: 2px var(--space-2);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
        overflow-x: auto;
        scrollbar-width: none;
    }

    .sub-tab-bar::-webkit-scrollbar {
        display: none;
    }

    .sub-tab {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 2px 8px;
        background: transparent;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        color: var(--text-secondary);
        font-size: 11px;
        cursor: pointer;
        white-space: nowrap;
        flex-shrink: 0;
    }

    .sub-tab:hover {
        background: var(--bg-hover);
    }

    .sub-tab.active {
        background: var(--bg-secondary);
        border-color: var(--border);
        color: var(--text-primary);
    }

    .tab-count {
        font-size: 10px;
        color: var(--text-muted);
    }

    /* Table scroll area handled by ScrollContainer */

    /* Data table */
    .data-table {
        width: 100%;
        border-collapse: collapse;
        font-family: var(--font-mono);
        font-size: 11px;
    }

    .data-table th {
        position: sticky;
        top: 0;
        background: var(--bg-secondary);
        padding: 2px 8px;
        text-align: left;
        font-weight: 600;
        font-size: 10px;
        color: var(--text-secondary);
        border-bottom: 1px solid var(--border);
        white-space: nowrap;
        z-index: 1;
    }

    .data-table td {
        padding: 1px 8px;
        line-height: 20px;
        color: var(--text-primary);
        border-bottom: 1px solid var(--border-subtle, var(--border));
        max-width: 300px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .data-table tbody tr:hover {
        background: var(--bg-hover);
    }

    /* Cell types */
    .num {
        text-align: right;
        color: var(--text-secondary);
        font-variant-numeric: tabular-nums;
    }

    .type {
        color: var(--color-type, #61afef);
    }

    .bool {
        text-align: center;
        color: var(--text-muted);
    }

    .path {
        color: var(--text-secondary);
    }

    .guid {
        font-size: 10px;
        color: var(--text-muted);
    }

    .value {
        max-width: 200px;
    }

    /* Key-value table (for Asset Info) */
    .kv-table {
        max-width: 500px;
    }

    .kv-key {
        font-weight: 600;
        color: var(--text-secondary);
        white-space: nowrap;
        width: 1%;
    }

    .kv-value {
        color: var(--text-primary);
        word-break: break-all;
        white-space: normal;
    }

    /* Property hierarchy */
    .prop-name {
        display: flex;
        align-items: center;
        gap: 4px;
    }

    .prop-expand-icon {
        font-size: 8px;
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .prop-array-index {
        color: var(--text-muted);
        font-size: 10px;
    }

    :global(.prop-container) td {
        font-weight: 600;
    }

    :global(.prop-container) .prop-name {
        color: var(--text-primary);
    }
</style>
