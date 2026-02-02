/**
 * UI Constants
 *
 * Centralized constants for the frontend. No magic numbers in component code.
 * All values that might need tuning or could be reused should be defined here.
 */

// =============================================================================
// Layout Constants
// =============================================================================

export const LAYOUT = {
    /** Minimum width for resizable panels */
    PANEL_MIN_WIDTH: 200,
    /** Maximum width for resizable panels */
    PANEL_MAX_WIDTH: 600,
    /** Default left panel width */
    PANEL_DEFAULT_WIDTH: 300,
    /** Splitter drag handle size */
    SPLITTER_SIZE: 4,
    /** Menu bar height */
    MENU_HEIGHT: 32,
    /** Status bar height */
    STATUS_HEIGHT: 24,
} as const;

// =============================================================================
// Tree Constants
// =============================================================================

export const TREE = {
    /** Indentation per depth level */
    INDENT_SIZE: 16,
    /** Row height for tree items */
    ROW_HEIGHT: 24,
    /** Maximum characters for value preview */
    VALUE_PREVIEW_MAX_CHARS: 50,
    /** Default expanded depth on load */
    DEFAULT_EXPAND_DEPTH: 2,
    /** Virtual scroll overscan (items above/below viewport) */
    OVERSCAN_COUNT: 10,
} as const;

// =============================================================================
// Property Grid Constants
// =============================================================================

export const PROPERTY_GRID = {
    /** Minimum width for property names column */
    NAME_COLUMN_MIN_WIDTH: 100,
    /** Default ratio for name:value columns */
    NAME_COLUMN_RATIO: 0.35,
    /** Maximum nested struct depth to show expanded */
    MAX_NESTED_DEPTH: 5,
} as const;

// =============================================================================
// Virtual List Constants
// =============================================================================

export const VIRTUAL_LIST = {
    /** Default row height if not specified */
    DEFAULT_ROW_HEIGHT: 24,
    /** Overscan count for smooth scrolling */
    OVERSCAN: 5,
    /** Throttle scroll events (ms) */
    SCROLL_THROTTLE: 16,
} as const;

// =============================================================================
// Animation Constants
// =============================================================================

export const ANIMATION = {
    /** Fast transitions (hover states) */
    FAST_MS: 100,
    /** Normal transitions (most UI changes) */
    NORMAL_MS: 200,
    /** Slow transitions (panel collapse) */
    SLOW_MS: 300,
} as const;

// =============================================================================
// Context Menu Constants
// =============================================================================

export const CONTEXT_MENU = {
    /** Minimum width */
    MIN_WIDTH: 160,
    /** Maximum width */
    MAX_WIDTH: 300,
    /** Distance from viewport edge */
    VIEWPORT_PADDING: 8,
} as const;

// =============================================================================
// IPC Constants
// =============================================================================

export const IPC = {
    /** Timeout for request/response correlation (ms) */
    REQUEST_TIMEOUT: 30000,
    /** Maximum retry attempts for failed messages */
    MAX_RETRIES: 3,
    /** Retry delay (ms) */
    RETRY_DELAY: 1000,
} as const;

// =============================================================================
// Color Mapping
// =============================================================================

import type { PropertyType, TreeNodeType } from '$lib/bridge/types';

/**
 * Maps property types to CSS custom property names for color coding.
 */
export const PROPERTY_TYPE_COLORS: Record<PropertyType, string> = {
    string: 'var(--color-string)',
    number: 'var(--color-number)',
    bool: 'var(--color-bool)',
    object: 'var(--color-object)',
    struct: 'var(--color-struct)',
    array: 'var(--color-array)',
    enum: 'var(--color-enum)',
    byte: 'var(--color-byte)',
    guid: 'var(--color-guid)',
    vector: 'var(--color-struct)',
    color: 'var(--color-struct)',
    map: 'var(--color-enum)',
    set: 'var(--color-array)',
    unknown: 'var(--color-unknown)',
};

/**
 * Maps tree node types to CSS custom property names for color coding.
 */
export const TREE_NODE_TYPE_COLORS: Record<TreeNodeType, string> = {
    export: 'var(--color-object)',
    property: 'var(--text-primary)',
    array: 'var(--color-array)',
    struct: 'var(--color-struct)',
    map: 'var(--color-enum)',
    import: 'var(--color-number)',
    name: 'var(--color-string)',
    header: 'var(--text-secondary)',
    unknown: 'var(--color-unknown)',
};

// =============================================================================
// Default Values
// =============================================================================

export const DEFAULTS = {
    /** Default asset file extension filter */
    ASSET_EXTENSIONS: ['.uasset', '.umap', '.pak'],
    /** Default mappings file extension */
    MAPPINGS_EXTENSION: '.usmap',
} as const;
