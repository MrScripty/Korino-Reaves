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

// =============================================================================
// State (Received from C#)
// =============================================================================

/** Properties of the currently selected node */
export let properties = $state<PropertyValue[]>([]);

/** Path of the node whose properties are displayed */
export let nodePath = $state<string | null>(null);

/** Whether properties are loading */
export let isLoading = $state(false);

/** Error message from last property operation */
export let error = $state<string | null>(null);

// =============================================================================
// Transient UI State (Svelte can own this)
// =============================================================================

/** Property paths that are expanded (for nested structs/arrays) */
export let expandedPaths = $state<string[]>([]);

/** Currently editing property path, if any */
export let editingPath = $state<string[] | null>(null);

// =============================================================================
// Derived State
// =============================================================================

/** Number of editable properties */
export let editableCount = $derived(
    properties.filter((p) => p.editable).length
);

/** Whether there are any properties */
export let hasProperties = $derived(properties.length > 0);

// =============================================================================
// IPC Listeners
// =============================================================================

// Full properties update
ipc.onAction<{ path: string; properties: PropertyValue[] }>(
    'property',
    'update',
    (payload) => {
        nodePath = payload.path;
        properties = payload.properties;
        isLoading = false;
        error = null;
    }
);

// Property value changed
ipc.onAction<{ path: string[]; value: unknown }>('property', 'changed', (payload) => {
    properties = properties.map((p) =>
        pathsEqual(p.path, payload.path) ? { ...p, value: payload.value } : p
    );
});

// Clear properties on deselection
ipc.onAction<{ selectedId: string | null }>('selection', 'update', (payload) => {
    if (!payload.selectedId) {
        properties = [];
        nodePath = null;
        expandedPaths = [];
        editingPath = null;
    }
});

// Clear on asset close
ipc.onAction('asset', 'closed', () => {
    properties = [];
    nodePath = null;
    expandedPaths = [];
    editingPath = null;
    error = null;
});

// Loading state
ipc.onAction<{ loading: boolean }>('property', 'loading', (payload) => {
    isLoading = payload.loading;
});

// Error state
ipc.onAction<{ message: string }>('property', 'error', (payload) => {
    error = payload.message;
    isLoading = false;
});

// =============================================================================
// Actions (Forward to C#)
// =============================================================================

/**
 * Request to set a property value.
 * Does NOT update local state - waits for C# to push the update.
 */
export function setPropertyValue(path: string[], value: unknown): void {
    ipc.send({
        type: 'property',
        action: 'set',
        payload: { path, value },
    });
    // Clear editing state after submit
    editingPath = null;
}

/**
 * Request to add a new property.
 */
export function addProperty(
    parentPath: string[],
    type: string,
    name: string
): void {
    ipc.send({
        type: 'property',
        action: 'add',
        payload: { parentPath, type, name },
    });
}

/**
 * Request to delete a property.
 */
export function deleteProperty(path: string[]): void {
    ipc.send({
        type: 'property',
        action: 'delete',
        payload: { path },
    });
}

/**
 * Request to duplicate a property.
 */
export function duplicateProperty(path: string[]): void {
    ipc.send({
        type: 'property',
        action: 'duplicate',
        payload: { path },
    });
}

// =============================================================================
// Transient UI Actions (Svelte can manage these)
// =============================================================================

/**
 * Toggle expanded state of a nested property (transient UI state).
 */
export function togglePropertyExpand(pathKey: string): void {
    if (expandedPaths.includes(pathKey)) {
        expandedPaths = expandedPaths.filter((p) => p !== pathKey);
    } else {
        expandedPaths = [...expandedPaths, pathKey];
    }
}

/**
 * Check if a property path is expanded.
 */
export function isPropertyExpanded(pathKey: string): boolean {
    return expandedPaths.includes(pathKey);
}

/**
 * Start editing a property (transient UI state).
 */
export function startEditing(path: string[]): void {
    editingPath = path;
}

/**
 * Cancel editing (transient UI state).
 */
export function cancelEditing(): void {
    editingPath = null;
}

/**
 * Check if a property is being edited.
 */
export function isEditing(path: string[]): boolean {
    return editingPath !== null && pathsEqual(editingPath, path);
}

/**
 * Clear error state.
 */
export function clearError(): void {
    error = null;
}

// =============================================================================
// Utilities
// =============================================================================

/**
 * Convert a property path to a string key.
 */
export function pathToKey(path: string[]): string {
    return path.join('.');
}

/**
 * Check if two paths are equal.
 */
function pathsEqual(a: string[], b: string[]): boolean {
    if (a.length !== b.length) return false;
    return a.every((segment, i) => segment === b[i]);
}

/**
 * Get property by path.
 */
export function getPropertyByPath(path: string[]): PropertyValue | undefined {
    return properties.find((p) => pathsEqual(p.path, path));
}

/**
 * Filter properties by type.
 */
export function filterByType(
    propList: PropertyValue[],
    type: string
): PropertyValue[] {
    return propList.filter((p) => p.type === type);
}
