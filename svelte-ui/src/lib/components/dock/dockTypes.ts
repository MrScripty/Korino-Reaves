/**
 * Dock Layout Type Definitions
 *
 * Data model for the zone-based dockable panel system.
 * All types are serializable to JSON for localStorage persistence.
 */

/** Unique identifier for each dockable panel */
export type PanelId =
    | 'assetTree'
    | 'properties'
    | 'viewport'
    | 'hexView'
    | 'dataTable'
    | 'log'
    | 'dependencies'
    | 'sceneOutliner';

/** The four fixed dock zones */
export type ZoneId = 'left' | 'center' | 'right' | 'bottom';

/** Configuration for a single zone */
export interface ZoneConfig {
    /** Ordered list of panel IDs in this zone (displayed as tabs) */
    panels: PanelId[];
    /** ID of the currently active (visible) tab, or null if zone is empty */
    activePanel: PanelId | null;
}

/** The complete serializable layout state */
export interface DockLayoutConfig {
    /** Version number for migration if the schema changes */
    version: number;
    /** Panel arrangement per zone */
    zones: Record<ZoneId, ZoneConfig>;
    /** Zone sizes in pixels (persisted splitter positions) */
    sizes: {
        leftWidth: number;
        rightWidth: number;
        bottomHeight: number;
    };
    /** Which zones are collapsed */
    collapsed: {
        bottom: boolean;
    };
    /** Panels that are hidden (removed from all zones) */
    hiddenPanels: PanelId[];
}

/** Default layout configuration */
export const DEFAULT_LAYOUT: DockLayoutConfig = {
    version: 1,
    zones: {
        left: {
            panels: ['assetTree', 'properties'],
            activePanel: 'assetTree',
        },
        center: {
            panels: ['viewport'],
            activePanel: 'viewport',
        },
        right: {
            panels: ['dependencies'],
            activePanel: 'dependencies',
        },
        bottom: {
            panels: ['hexView', 'dataTable', 'log'],
            activePanel: 'hexView',
        },
    },
    sizes: {
        leftWidth: 300,
        rightWidth: 260,
        bottomHeight: 200,
    },
    collapsed: {
        bottom: false,
    },
    hiddenPanels: [],
};

/** All panel IDs (for validation) */
export const ALL_PANEL_IDS: PanelId[] = [
    'assetTree',
    'properties',
    'viewport',
    'hexView',
    'dataTable',
    'log',
    'dependencies',
    'sceneOutliner',
];

/** Zones that accept user-dragged panels */
export const DROPPABLE_ZONES: ZoneId[] = ['left', 'right', 'bottom'];
