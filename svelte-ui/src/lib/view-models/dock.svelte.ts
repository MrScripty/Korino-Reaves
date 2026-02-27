/**
 * Dock Layout View Model
 *
 * Manages the zone-based dockable panel layout. This is purely frontend-owned
 * transient UI state — not backend-owned data. Persisted to localStorage.
 */

import type { DockLayoutConfig, PanelId, ZoneId, ZoneConfig } from '$lib/components/dock/dockTypes';
import { DEFAULT_LAYOUT, DROPPABLE_ZONES } from '$lib/components/dock/dockTypes';
import { PANEL_DEFINITIONS } from '$lib/components/dock/panelRegistry';
import { DOCK } from '$lib/constants';

class DockLayoutVM {
    // --- Persisted layout state ---
    zones = $state<Record<ZoneId, ZoneConfig>>(structuredClone(DEFAULT_LAYOUT.zones));
    sizes = $state({ ...DEFAULT_LAYOUT.sizes });
    collapsed = $state({ ...DEFAULT_LAYOUT.collapsed });
    hiddenPanels = $state<PanelId[]>([]);

    // --- Transient drag state (not persisted) ---
    dragPanel = $state<PanelId | null>(null);
    dragSourceZone = $state<ZoneId | null>(null);
    dragOverZone = $state<ZoneId | null>(null);
    dragPosition = $state<{ x: number; y: number }>({ x: 0, y: 0 });

    constructor() {
        this.load();
    }

    // --- Zone queries ---

    getZonePanels(zone: ZoneId): PanelId[] {
        return this.zones[zone].panels;
    }

    getActivePanel(zone: ZoneId): PanelId | null {
        return this.zones[zone].activePanel;
    }

    isZoneEmpty(zone: ZoneId): boolean {
        return this.zones[zone].panels.length === 0;
    }

    /** Returns which zone a panel is currently in, or null if hidden */
    findPanelZone(panelId: PanelId): ZoneId | null {
        for (const [zoneId, config] of Object.entries(this.zones)) {
            if (config.panels.includes(panelId)) return zoneId as ZoneId;
        }
        return null;
    }

    /** Whether a panel is visible (in a zone and not hidden) */
    isPanelVisible(panelId: PanelId): boolean {
        return !this.hiddenPanels.includes(panelId) && this.findPanelZone(panelId) !== null;
    }

    // --- Panel mutations ---

    /** Activate a tab within its zone */
    activatePanel(zone: ZoneId, panelId: PanelId): void {
        this.zones = {
            ...this.zones,
            [zone]: { ...this.zones[zone], activePanel: panelId },
        };
        this.save();
    }

    /** Move a panel from one zone to another */
    movePanel(panelId: PanelId, fromZone: ZoneId, toZone: ZoneId): void {
        if (panelId === 'viewport') return;
        if (toZone === 'center') return;
        if (fromZone === toZone) return;

        // Don't allow dragging the last panel out of a zone
        if (this.zones[fromZone].panels.length <= 1) return;

        // Remove from source
        const fromPanels = this.zones[fromZone].panels.filter(p => p !== panelId);
        const fromActive = this.zones[fromZone].activePanel === panelId
            ? (fromPanels[0] ?? null)
            : this.zones[fromZone].activePanel;

        // Insert into target
        const toPanels = [...this.zones[toZone].panels, panelId];

        this.zones = {
            ...this.zones,
            [fromZone]: { panels: fromPanels, activePanel: fromActive },
            [toZone]: { panels: toPanels, activePanel: panelId },
        };
        this.save();
    }

    /** Hide a panel (remove from all zones, add to hiddenPanels) */
    hidePanel(panelId: PanelId, force = false): void {
        if (panelId === 'viewport') return;

        const zone = this.findPanelZone(panelId);
        if (zone) {
            // Don't allow hiding the last panel unless forced (e.g. auto-hide)
            if (!force && this.zones[zone].panels.length <= 1) return;

            const panels = this.zones[zone].panels.filter(p => p !== panelId);
            const active = this.zones[zone].activePanel === panelId
                ? (panels[0] ?? null)
                : this.zones[zone].activePanel;
            this.zones = {
                ...this.zones,
                [zone]: { panels, activePanel: active },
            };
        }

        if (!this.hiddenPanels.includes(panelId)) {
            this.hiddenPanels = [...this.hiddenPanels, panelId];
        }
        this.save();
    }

    /** Show a hidden panel (add to specified zone) */
    showPanel(panelId: PanelId, targetZone: ZoneId = 'left'): void {
        if (targetZone === 'center') targetZone = 'left';

        // Remove from hidden list
        this.hiddenPanels = this.hiddenPanels.filter(p => p !== panelId);

        // Don't add if already in a zone
        if (this.findPanelZone(panelId)) return;

        const panels = [...this.zones[targetZone].panels, panelId];
        this.zones = {
            ...this.zones,
            [targetZone]: { panels, activePanel: panelId },
        };
        this.save();
    }

    /** Toggle a panel's visibility (for View > Panels menu) */
    togglePanel(panelId: PanelId): void {
        if (this.isPanelVisible(panelId)) {
            this.hidePanel(panelId);
        } else {
            // Find the default zone for this panel
            const defaultZone = this.getDefaultZone(panelId);
            this.showPanel(panelId, defaultZone);
        }
    }

    /** Get the default zone for a panel (from DEFAULT_LAYOUT) */
    private getDefaultZone(panelId: PanelId): ZoneId {
        for (const [zoneId, config] of Object.entries(DEFAULT_LAYOUT.zones)) {
            if (config.panels.includes(panelId)) return zoneId as ZoneId;
        }
        return 'left';
    }

    // --- Zone size mutations ---

    setLeftWidth(width: number): void {
        this.sizes = { ...this.sizes, leftWidth: width };
        this.scheduleSave();
    }

    setRightWidth(width: number): void {
        this.sizes = { ...this.sizes, rightWidth: width };
        this.scheduleSave();
    }

    setBottomHeight(height: number): void {
        this.sizes = { ...this.sizes, bottomHeight: height };
        this.scheduleSave();
    }

    toggleBottomCollapsed(): void {
        this.collapsed = { ...this.collapsed, bottom: !this.collapsed.bottom };
        this.save();
    }

    // --- Drag operations ---

    startDrag(panelId: PanelId, zone: ZoneId, x: number, y: number): void {
        this.dragPanel = panelId;
        this.dragSourceZone = zone;
        this.dragPosition = { x, y };
    }

    updateDrag(x: number, y: number): void {
        this.dragPosition = { x, y };
    }

    setDragOverZone(zone: ZoneId | null): void {
        // Only allow dropping on droppable zones
        if (zone !== null && !DROPPABLE_ZONES.includes(zone)) return;
        this.dragOverZone = zone;
    }

    completeDrag(): void {
        if (this.dragPanel && this.dragSourceZone && this.dragOverZone) {
            this.movePanel(this.dragPanel, this.dragSourceZone, this.dragOverZone);
        }
        this.cancelDrag();
    }

    cancelDrag(): void {
        this.dragPanel = null;
        this.dragSourceZone = null;
        this.dragOverZone = null;
    }

    get isDragging(): boolean {
        return this.dragPanel !== null;
    }

    // --- Reset ---

    resetLayout(): void {
        const defaults = structuredClone(DEFAULT_LAYOUT);
        this.zones = defaults.zones;
        this.sizes = defaults.sizes;
        this.collapsed = defaults.collapsed;
        this.hiddenPanels = defaults.hiddenPanels;
        this.save();
    }

    // --- Persistence ---

    private saveTimer: ReturnType<typeof setTimeout> | null = null;

    /** Debounced save for continuous operations like splitter drag */
    private scheduleSave(): void {
        if (this.saveTimer) clearTimeout(this.saveTimer);
        this.saveTimer = setTimeout(() => this.save(), DOCK.SAVE_DEBOUNCE);
    }

    private save(): void {
        if (typeof window === 'undefined') return;
        const config: DockLayoutConfig = {
            version: 1,
            zones: $state.snapshot(this.zones),
            sizes: { ...this.sizes },
            collapsed: { ...this.collapsed },
            hiddenPanels: [...this.hiddenPanels],
        };
        try {
            localStorage.setItem(DOCK.STORAGE_KEY, JSON.stringify(config));
        } catch (e) {
            console.warn('[Dock] Failed to save layout:', e);
        }
    }

    private load(): void {
        if (typeof window === 'undefined') return;
        try {
            const raw = localStorage.getItem(DOCK.STORAGE_KEY);
            if (!raw) return;

            const config = JSON.parse(raw) as DockLayoutConfig;
            if (config.version !== 1) return;

            // Validate center zone is intact
            if (!config.zones.center?.panels?.includes('viewport')) return;

            this.zones = config.zones;
            this.sizes = config.sizes;
            this.collapsed = config.collapsed;
            this.hiddenPanels = config.hiddenPanels ?? [];
        } catch (e) {
            console.warn('[Dock] Failed to load layout, using defaults:', e);
        }
    }
}

export const dock = new DockLayoutVM();
