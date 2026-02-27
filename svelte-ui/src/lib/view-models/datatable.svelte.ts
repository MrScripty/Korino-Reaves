/**
 * Data Table View Model
 *
 * Holds per-asset DB table data pushed from C#.
 * ALL data is owned by C# -- this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate table data directly. All queries go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { AssetTablesPayload } from '$lib/bridge/types';

/** Identifiers for each sub-tab in the data table panel */
export type DataTableTab =
    | 'assetInfo'
    | 'imports'
    | 'exports'
    | 'properties'
    | 'customVersions'
    | 'edges'
    | 'exportDependencies'
    | 'gatherableText'
    | 'searchableNames'
    | 'worldTileInfo';

class DataTableVM {
    /** The asset path for which we currently have data */
    selectedPath = $state<string | null>(null);
    /** Full table data for the selected asset */
    data = $state<AssetTablesPayload | null>(null);
    /** Whether a request is in flight */
    isLoading = $state(false);
    /** Currently active sub-tab */
    activeTab = $state<DataTableTab>('assetInfo');

    /** Sub-tabs that have data (non-empty arrays or non-null info) */
    get availableTabs(): { id: DataTableTab; label: string; count: number }[] {
        if (!this.data) return [];
        const tabs: { id: DataTableTab; label: string; count: number }[] = [];
        tabs.push({ id: 'assetInfo', label: 'Asset Info', count: 1 });
        if (this.data.imports.length > 0)
            tabs.push({ id: 'imports', label: 'Imports', count: this.data.imports.length });
        if (this.data.exports.length > 0)
            tabs.push({ id: 'exports', label: 'Exports', count: this.data.exports.length });
        if (this.data.properties.length > 0)
            tabs.push({ id: 'properties', label: 'Properties', count: this.data.properties.length });
        if (this.data.customVersions.length > 0)
            tabs.push({ id: 'customVersions', label: 'Custom Versions', count: this.data.customVersions.length });
        if (this.data.edges.length > 0)
            tabs.push({ id: 'edges', label: 'Edges', count: this.data.edges.length });
        if (this.data.exportDependencies.length > 0)
            tabs.push({ id: 'exportDependencies', label: 'Export Deps', count: this.data.exportDependencies.length });
        if (this.data.gatherableText.length > 0)
            tabs.push({ id: 'gatherableText', label: 'Text', count: this.data.gatherableText.length });
        if (this.data.searchableNames.length > 0)
            tabs.push({ id: 'searchableNames', label: 'Names', count: this.data.searchableNames.length });
        if (this.data.worldTileInfo.length > 0)
            tabs.push({ id: 'worldTileInfo', label: 'World Tiles', count: this.data.worldTileInfo.length });
        return tabs;
    }

    requestTables(path: string): void {
        this.selectedPath = path;
        this.isLoading = true;
        ipc.send({
            type: 'dependency',
            action: 'getAssetTables',
            payload: { path },
        });
    }

    clear(): void {
        this.selectedPath = null;
        this.data = null;
        this.isLoading = false;
        this.activeTab = 'assetInfo';
    }
}

export const dataTable = new DataTableVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<AssetTablesPayload>('dependency', 'assetTables', (payload) => {
    if (payload.assetPath === dataTable.selectedPath) {
        dataTable.data = payload;
        dataTable.isLoading = false;
        // Auto-select first available tab if current tab has no data
        const available = dataTable.availableTabs;
        if (available.length > 0 && !available.find((t) => t.id === dataTable.activeTab)) {
            dataTable.activeTab = available[0].id;
        }
    }
});

// Clear when project closes
ipc.onAction('project', 'closed', () => {
    dataTable.clear();
});
