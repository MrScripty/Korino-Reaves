/**
 * Panel Registry
 *
 * Maps panel IDs to display metadata. Component rendering is handled
 * via {#if} chain in DockZone.svelte (matching existing codebase patterns).
 */

import type { PanelId } from './dockTypes';

export interface PanelDefinition {
    id: PanelId;
    title: string;
    /** Whether this panel cannot be moved by the user */
    locked?: boolean;
    /** Whether this panel only appears when a condition is met */
    conditional?: boolean;
}

export const PANEL_DEFINITIONS: Record<PanelId, PanelDefinition> = {
    assetTree: { id: 'assetTree', title: 'Asset Tree' },
    properties: { id: 'properties', title: 'Properties' },
    viewport: { id: 'viewport', title: 'Viewport', locked: true },
    hexView: { id: 'hexView', title: 'Hex View' },
    dataTable: { id: 'dataTable', title: 'Data Table' },
    log: { id: 'log', title: 'Log' },
    dependencies: { id: 'dependencies', title: 'Dependencies' },
    sceneOutliner: { id: 'sceneOutliner', title: 'Scene Outliner', conditional: true },
};
