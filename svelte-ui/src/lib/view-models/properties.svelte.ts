/**
 * Properties View Model
 *
 * Holds a read-only view of the selected node's properties pushed from C#.
 * ALL data is owned by C# - this is just a presentation layer cache.
 *
 * IMPORTANT: Never mutate data directly. All changes go through IPC to C#.
 */

import { ipc } from '$lib/bridge/ipc';
import type { PropertyValue } from '$lib/bridge/types';

class PropertiesVM {
    properties = $state<PropertyValue[]>([]);
    nodePath = $state<string | null>(null);
    isLoading = $state(false);
    error = $state<string | null>(null);
    expandedPaths = $state<string[]>([]);
    editingPath = $state<string[] | null>(null);

    get editableCount(): number {
        return this.properties.filter((p) => p.editable).length;
    }

    get hasProperties(): boolean {
        return this.properties.length > 0;
    }

    setPropertyValue(path: string[], value: unknown): void {
        ipc.send({
            type: 'property',
            action: 'set',
            payload: { path, value },
        });
        this.editingPath = null;
    }

    resetProperty(path: string[]): void {
        ipc.send({
            type: 'property',
            action: 'reset',
            payload: { path },
        });
    }

    addProperty(parentPath: string[], type: string, name: string): void {
        ipc.send({
            type: 'property',
            action: 'add',
            payload: { parentPath, type, name },
        });
    }

    deleteProperty(path: string[]): void {
        ipc.send({
            type: 'property',
            action: 'delete',
            payload: { path },
        });
    }

    duplicateProperty(path: string[]): void {
        ipc.send({
            type: 'property',
            action: 'duplicate',
            payload: { path },
        });
    }

    togglePropertyExpand(pathKey: string): void {
        if (this.expandedPaths.includes(pathKey)) {
            this.expandedPaths = this.expandedPaths.filter((p) => p !== pathKey);
        } else {
            this.expandedPaths = [...this.expandedPaths, pathKey];
        }
    }

    isPropertyExpanded(pathKey: string): boolean {
        return this.expandedPaths.includes(pathKey);
    }

    startEditing(path: string[]): void {
        this.editingPath = path;
    }

    cancelEditing(): void {
        this.editingPath = null;
    }

    isEditing(path: string[]): boolean {
        return this.editingPath !== null && pathsEqual(this.editingPath, path);
    }

    clearError(): void {
        this.error = null;
    }

    getPropertyByPath(path: string[]): PropertyValue | undefined {
        return this.properties.find((p) => pathsEqual(p.path, path));
    }

    pathToKey(path: string[]): string {
        return path.join('.');
    }
}

export const properties = new PropertiesVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<{ path: string; properties: PropertyValue[] }>(
    'property',
    'update',
    (payload) => {
        properties.nodePath = payload.path;
        properties.properties = payload.properties;
        properties.isLoading = false;
        properties.error = null;
    }
);

ipc.onAction<{ path: string[]; value: unknown }>('property', 'changed', (payload) => {
    properties.properties = properties.properties.map((p) =>
        pathsEqual(p.path, payload.path) ? { ...p, value: payload.value } : p
    );
});

ipc.onAction<{ selectedId: string | null }>('selection', 'update', (payload) => {
    if (!payload.selectedId) {
        properties.properties = [];
        properties.nodePath = null;
        properties.expandedPaths = [];
        properties.editingPath = null;
        properties.isLoading = false;
    }
    // Note: Do NOT set isLoading=true here. Due to event ordering,
    // property:update (from SelectionChanged handler) arrives BEFORE
    // this selection:update (the handler response). Setting isLoading=true
    // would overwrite already-received property data.
});

ipc.onAction('asset', 'closed', () => {
    properties.properties = [];
    properties.nodePath = null;
    properties.expandedPaths = [];
    properties.editingPath = null;
    properties.error = null;
});

ipc.onAction<{ loading: boolean }>('property', 'loading', (payload) => {
    properties.isLoading = payload.loading;
});

ipc.onAction<{ message: string }>('property', 'error', (payload) => {
    properties.error = payload.message;
    properties.isLoading = false;
});

// =============================================================================
// Utilities
// =============================================================================

export function pathToKey(path: string[]): string {
    return path.join('.');
}

function pathsEqual(a: string[], b: string[]): boolean {
    if (a.length !== b.length) return false;
    return a.every((segment, i) => segment === b[i]);
}

export function filterByType(
    propList: PropertyValue[],
    type: string
): PropertyValue[] {
    return propList.filter((p) => p.type === type);
}
