/**
 * Shared IPC Contracts - types.ts
 *
 * This file defines the immutable IPC contracts between the C# backend and Svelte frontend.
 * ALL agents MUST use these exact types. Breaking changes require coordination with ALL agents.
 *
 * @module bridge/types
 */

// =============================================================================
// IPC Message Types
// =============================================================================

/**
 * All valid IPC message categories.
 * Each category corresponds to a domain of functionality.
 */
export type MessageType =
    | 'asset'      // Asset file operations (open, save, close)
    | 'tree'       // Tree structure data (nodes, expansion)
    | 'property'   // Property values and editing
    | 'selection'  // Selection state changes
    | 'diff'       // Diff comparison results
    | 'viewport'   // 3D/2D viewport commands
    | 'scene'      // Scene/level viewer commands
    | 'agent'      // AI agent status and commands
    | 'dialog'     // Native file dialogs (open, save)
    | 'pak'        // PAK archive operations (import, list)
    | 'project'    // Project operations (open, list)
    | 'fs'         // Filesystem operations (list, navigate)
    | 'dependency'  // Asset dependency graph queries
    | 'error'      // Error responses
    | 'log'        // Application log messages
    | 'test';      // Testing/ping-pong messages

/**
 * Base IPC message structure for all communication between C# and Svelte.
 * All messages follow this format for consistent parsing and routing.
 */
export interface IpcMessage<T = unknown> {
    /** Message category for routing */
    type: MessageType;
    /** Specific action within the category */
    action: string;
    /** Action-specific data payload */
    payload: T;
    /** Optional correlation ID for request/response matching */
    id?: string;
    /** Optional timestamp for message ordering */
    timestamp?: number;
}

// =============================================================================
// Tree Types
// =============================================================================

/**
 * Node type identifiers for color coding in the tree view.
 * Maps to semantic colors defined in the design system.
 */
export type TreeNodeType =
    | 'export'     // Export nodes (purple - object)
    | 'property'   // Property nodes (varies by value type)
    | 'array'      // Array containers (red)
    | 'struct'     // Struct containers (cyan)
    | 'map'        // Map containers (orange)
    | 'import'     // Import references
    | 'name'       // Name map entries
    | 'header'     // Asset header info
    | 'folder'     // Filesystem folder
    | 'file'       // Filesystem file
    | 'unknown';   // Fallback type

/**
 * Represents a single node in the asset tree.
 * Used for both display and navigation.
 */
export interface TreeNode {
    /** Unique identifier for this node (path-based) */
    id: string;
    /** Display name shown in tree */
    name: string;
    /** Node type for color coding and icon selection */
    type: TreeNodeType;
    /** Whether this node can be expanded */
    hasChildren: boolean;
    /** Child nodes (populated on expand, undefined when collapsed) */
    children?: TreeNode[];
    /** Additional metadata for display */
    metadata?: TreeNodeMetadata;
}

/**
 * Optional metadata attached to tree nodes for enhanced display.
 */
export interface TreeNodeMetadata {
    /** Value preview for leaf nodes (e.g., "100", "true") */
    valuePreview?: string;
    /** Type name for display (e.g., "IntProperty", "StrProperty") */
    typeName?: string;
    /** Export class name if applicable */
    className?: string;
    /** Array index if this node is an array element */
    arrayIndex?: number;
    /** Whether this node represents a modified value (for diff highlighting) */
    isModified?: boolean;
}

// =============================================================================
// Property Types
// =============================================================================

/**
 * Property value types corresponding to UE property system.
 * Used for selecting appropriate editors in the UI.
 */
export type PropertyType =
    | 'string'     // StrProperty, TextProperty, NameProperty
    | 'number'     // IntProperty, FloatProperty, etc.
    | 'bool'       // BoolProperty
    | 'vector'     // Vector, Vector2D, Rotator, etc.
    | 'color'      // LinearColor, Color
    | 'enum'       // EnumProperty, ByteProperty (enum)
    | 'object'     // ObjectProperty, SoftObjectProperty
    | 'struct'     // StructProperty (nested)
    | 'array'      // ArrayProperty
    | 'map'        // MapProperty
    | 'set'        // SetProperty
    | 'byte'       // ByteProperty (raw)
    | 'guid'       // Guid values
    | 'unknown';   // Fallback for unsupported types

/**
 * Represents a property value that can be displayed and edited.
 */
export interface PropertyValue {
    /** Path from root to this property (e.g., ["Export[0]", "Properties", "Health"]) */
    path: string[];
    /** Property type for editor selection */
    type: PropertyType;
    /** Current value (type depends on PropertyType) */
    value: unknown;
    /** Whether this property can be edited */
    editable: boolean;
    /** Display name (last segment of path by default) */
    displayName?: string;
    /** Type-specific metadata */
    metadata?: PropertyMetadata;
    /** Whether this property has been edited from its original value */
    isEdited?: boolean;
    /** Child properties for container types (struct, array, map) */
    children?: PropertyValue[];
}

/**
 * Type-specific metadata for properties.
 */
export interface PropertyMetadata {
    /** For enums: available enum values */
    enumValues?: string[];
    /** For numbers: minimum value */
    min?: number;
    /** For numbers: maximum value */
    max?: number;
    /** For objects: class restriction */
    objectClass?: string;
    /** For arrays: element type */
    elementType?: PropertyType;
    /** For structs: struct type name */
    structType?: string;
    /** Original UE property type name */
    ueTypeName?: string;
}

// =============================================================================
// Selection Types
// =============================================================================

/**
 * Current selection and expansion state.
 * C# owns this state; frontend reflects it.
 */
export interface SelectionState {
    /** Currently selected node ID, or null if nothing selected */
    selectedId: string | null;
    /** IDs of all expanded nodes in the tree */
    expandedIds: string[];
    /** Currently focused property path, if any */
    focusedPropertyPath?: string[];
}

// =============================================================================
// Diff Types
// =============================================================================

/**
 * Type of change detected between two versions.
 */
export type DiffChangeType =
    | 'added'      // New property/export in target
    | 'removed'    // Property/export deleted from base
    | 'modified'   // Value changed
    | 'renamed'    // Name/path changed (detected by similarity)
    | 'moved';     // Position changed (for arrays)

/**
 * Represents a single difference between two asset versions.
 */
export interface DiffChange {
    /** Path to the changed element */
    path: string[];
    /** Type of change */
    changeType: DiffChangeType;
    /** Value in base version (undefined for 'added') */
    oldValue?: unknown;
    /** Value in target version (undefined for 'removed') */
    newValue?: unknown;
    /** Confidence score for rename/move detection (0.0-1.0) */
    confidence?: number;
    /** For renames: the original path */
    originalPath?: string[];
}

/**
 * Summary statistics for a diff operation.
 */
export interface DiffSummary {
    /** Number of additions */
    added: number;
    /** Number of removals */
    removed: number;
    /** Number of modifications */
    modified: number;
    /** Number of unchanged elements */
    unchanged: number;
    /** Number of renames detected */
    renamed?: number;
}

/**
 * Complete result of comparing two assets.
 */
export interface DiffResult {
    /** Identifier for base (original) version */
    baseVersion: string;
    /** Identifier for target (new) version */
    targetVersion: string;
    /** List of all changes detected */
    changes: DiffChange[];
    /** Aggregated statistics */
    summary: DiffSummary;
}

/**
 * Three-way diff result for mod porting.
 * Compares: Original -> Updated (game changes) and Original -> Modded (mod changes)
 */
export interface ThreeWayDiffResult {
    /** Original game version identifier */
    originalVersion: string;
    /** Updated game version identifier */
    updatedVersion: string;
    /** Modded version identifier */
    moddedVersion: string;
    /** Changes the game made (original -> updated) */
    gameChanges: DiffChange[];
    /** Changes the mod made (original -> modded) */
    modChanges: DiffChange[];
    /** Conflicts where both game and mod changed the same thing */
    conflicts: DiffConflict[];
    /** Non-conflicting mod changes that can be auto-applied */
    safeToApply: DiffChange[];
}

/**
 * A conflict where both game update and mod changed the same property.
 */
export interface DiffConflict {
    /** Path to the conflicting element */
    path: string[];
    /** Original value before any changes */
    originalValue: unknown;
    /** Value in updated game version */
    gameValue: unknown;
    /** Value in modded version */
    modValue: unknown;
    /** Suggested resolution (if determinable) */
    suggestedResolution?: 'keep_game' | 'keep_mod' | 'merge' | 'manual';
}

// =============================================================================
// Agent Types
// =============================================================================

/**
 * Current status of an AI agent operation.
 */
export type AgentStatus =
    | 'idle'       // Agent ready, not processing
    | 'thinking'   // Agent analyzing/planning
    | 'executing'  // Agent performing actions
    | 'waiting'    // Agent waiting for user input
    | 'complete'   // Operation finished successfully
    | 'error';     // Operation failed

/**
 * Message from an AI agent about its current state.
 */
export interface AgentMessage {
    /** Unique identifier for this agent instance */
    agentId: string;
    /** Current operation status */
    status: AgentStatus;
    /** Human-readable status message */
    message: string;
    /** Progress percentage (0-100), if determinable */
    progress?: number;
    /** Current action being performed */
    currentAction?: string;
    /** Pending actions in queue */
    pendingActions?: string[];
}

/**
 * Incremental step event emitted during an agent operation.
 */
export interface AgentStepMessage {
    /** Logical step name (e.g. execute, explore, portMod) */
    step: string;
    /** Human-readable description for the current step */
    message: string;
    /** Current overall status */
    status?: AgentStatus;
    /** Optional progress percentage */
    progress?: number;
}

/**
 * Final result payload for an agent operation.
 */
export interface AgentResultMessage {
    /** Final status */
    status: AgentStatus;
    /** Result summary text */
    message: string;
    /** Optional structured result */
    data?: unknown;
}

/**
 * Error payload for an agent operation.
 */
export interface AgentErrorMessage {
    /** Always 'error' */
    status: 'error';
    /** Human-readable error text */
    message: string;
    /** Optional diagnostic payload */
    details?: unknown;
}

/**
 * Command to send to an AI agent.
 */
export interface AgentCommand {
    /** Target agent ID */
    agentId: string;
    /** Command type */
    command: 'start' | 'stop' | 'pause' | 'resume' | 'query';
    /** Command parameters */
    params?: Record<string, unknown>;
}

// =============================================================================
// Error Types
// =============================================================================

/**
 * Standardized error codes for IPC responses.
 */
export type ErrorCode =
    | 'INVALID_MESSAGE'      // Message format error
    | 'UNKNOWN_ACTION'       // Unrecognized action
    | 'ASSET_NOT_LOADED'     // No asset currently loaded
    | 'ASSET_LOAD_FAILED'    // Failed to open asset file
    | 'ASSET_SAVE_FAILED'    // Failed to save asset file
    | 'PROPERTY_NOT_FOUND'   // Property path doesn't exist
    | 'PROPERTY_READ_ONLY'   // Attempted to edit read-only property
    | 'INVALID_VALUE'        // Value doesn't match property type
    | 'DIFF_FAILED'          // Diff computation failed
    | 'AGENT_ERROR'          // AI agent operation failed
    | 'INTERNAL_ERROR';      // Unexpected internal error

/**
 * Standardized error response structure.
 */
export interface ErrorResponse {
    /** Error code for programmatic handling */
    code: ErrorCode;
    /** Human-readable error message */
    message: string;
    /** Additional error context */
    details?: unknown;
    /** Stack trace (debug builds only) */
    stackTrace?: string;
}

// =============================================================================
// Asset Types
// =============================================================================

/**
 * Summary information about a loaded asset.
 */
export interface AssetInfo {
    /** File path of the loaded asset */
    filePath: string;
    /** Asset file name */
    fileName: string;
    /** Detected Unreal Engine version */
    engineVersion: string;
    /** Number of exports in the asset */
    exportCount: number;
    /** Number of imports in the asset */
    importCount: number;
    /** Number of names in the name map */
    nameCount: number;
    /** Whether the asset has been modified */
    isModified: boolean;
    /** Asset class name (if determinable) */
    assetClass?: string;
}

/**
 * Request to open an asset file.
 */
export interface OpenAssetRequest {
    /** Path to the asset file */
    filePath: string;
    /** Optional mappings file path (.usmap) */
    mappingsPath?: string;
}

// =============================================================================
// Project Types
// =============================================================================

/**
 * Information about a project (extracted PAK contents).
 */
export interface ProjectInfo {
    /** Project name (directory name) */
    name: string;
    /** Full path to the project directory */
    path: string;
    /** Number of files in the project */
    fileCount: number;
    /** Last modification timestamp */
    lastModified?: string;
}

/**
 * Payload for streaming file extraction updates.
 */
export interface FileExtractedPayload {
    /** Relative path of the extracted file within the project */
    filePath: string;
    /** Current file index (1-based) */
    index: number;
    /** Total number of files to extract */
    total: number;
}

/**
 * Payload for incremental tree updates during import.
 */
export interface IncrementalTreeUpdate {
    /** New nodes to add */
    nodes: TreeNode[];
    /** Parent node ID (undefined = root level) */
    parentId?: string;
}

// =============================================================================
// Game Version Types
// =============================================================================

/**
 * A single EGame enum entry for display in game version selectors.
 */
export interface GameVersionEntry {
    /** EGame enum name (e.g., "GAME_HogwartsLegacy") */
    value: string;
    /** Human-readable label (e.g., "Hogwarts Legacy") */
    label: string;
    /** UE version group (e.g., "UE4.27") */
    group: string;
}

/**
 * Current game version selection state for a project.
 */
export interface GameVersionState {
    /** Selected version enum name, or "AUTO" for auto-detect */
    selected: string;
    /** Auto-detected version enum name */
    autoDetected: string;
    /** Whether auto-detect mode is active */
    isAutoDetect: boolean;
}

// =============================================================================
// Viewport Types
// =============================================================================

/**
 * Commands for controlling the 3D/2D viewport.
 */
export type ViewportCommand =
    | 'reset_camera'     // Reset camera to default position
    | 'focus_selection'  // Focus camera on selected item
    | 'toggle_grid'      // Toggle grid visibility
    | 'toggle_wireframe' // Toggle wireframe rendering
    | 'set_background';  // Set background color

/**
 * Viewport state information.
 */
export interface ViewportState {
    /** Current preview mode */
    mode: '2d' | '3d' | 'none';
    /** Type of content being previewed */
    contentType?: 'texture' | 'mesh' | 'skeleton' | 'animation';
    /** Whether grid is visible */
    gridVisible: boolean;
    /** Whether wireframe mode is active */
    wireframe: boolean;
}

/**
 * Payload sent from C# when an asset preview is ready.
 */
export interface ViewportPreviewPayload {
    /** Base64 PNG data URL (data:image/png;base64,...) */
    imageData: string;
    /** Preview mode */
    mode: '2d' | '3d' | 'scene';
    /** Type of content being previewed */
    contentType: 'texture' | 'mesh' | 'level';
    /** Display name of the asset */
    assetName: string;
    /** Texture-specific metadata */
    textureInfo?: { width: number; height: number; format: string };
    /** Mesh-specific metadata */
    meshInfo?: { vertexCount: number; triangleCount: number; lodCount: number };
    /** Scene-specific metadata (when mode is 'scene') */
    sceneInfo?: SceneInfo;
}

// =============================================================================
// Scene Types
// =============================================================================

/**
 * Represents a single actor extracted from a UE level.
 * Used in the scene outliner.
 */
export interface SceneActor {
    /** Unique identifier for this actor */
    id: string;
    /** Actor name from the level */
    name: string;
    /** UE class name (e.g., "StaticMeshActor") */
    className: string;
    /** Game path to the mesh asset, if any */
    meshPath: string | null;
    /** Position in Godot coordinates [x, y, z] */
    position: [number, number, number] | null;
    /** Whether this actor has a renderable mesh */
    hasMesh: boolean;
    /** Whether the mesh has been loaded into the viewport */
    isLoaded: boolean;
    /** Which sub-level this actor belongs to */
    levelName: string;
}

/**
 * Summary of a discovered sub-level in a multi-level scene.
 */
export interface SubLevelSummary {
    name: string;
    actorCount: number;
    meshCount: number;
    source: string;
}

/**
 * Summary info about the loaded scene.
 */
export interface SceneInfo {
    /** Number of actors rendered in the viewport */
    actorCount: number;
    /** Name of the loaded level */
    levelName: string;
    /** Number of sub-levels loaded (1 for single-level) */
    subLevelCount?: number;
}

// =============================================================================
// Log Types
// =============================================================================

/** A single log entry forwarded from the C# backend. */
export interface LogEntry {
    level: 'verbose' | 'debug' | 'information' | 'warning' | 'error' | 'fatal';
    message: string;
    timestamp: number;
    exception?: string;
}

// =============================================================================
// Dependency Graph Types
// =============================================================================

/**
 * A single directed reference from one asset to another.
 */
export interface DependencyReference {
    /** Relative path of the referenced asset */
    path: string;
    /** Import class name (e.g., "StaticMesh", "Material", "LevelStreamingKismet") */
    refType: string;
}

/**
 * Dependency graph statistics.
 */
export interface DependencyStats {
    /** Whether a dependency database exists */
    exists: boolean;
    /** Number of asset nodes in the graph */
    assetCount?: number;
    /** Number of directed edges */
    edgeCount?: number;
    /** Engine version used during scan */
    engineVersion?: string;
    /** ISO timestamp of when the scan completed */
    scannedAt?: string;
}

/**
 * Progress update during dependency scanning.
 */
export interface DependencyScanProgress {
    current: number;
    total: number;
    currentFile: string;
    phase: 'enumerating' | 'scanning' | 'writing' | 'complete';
}

// =============================================================================
// Data Table Types (per-asset DB metadata)
// =============================================================================

/** Asset info row from the assets table */
export interface DbAssetInfo {
    id: number;
    path: string;
    assetType: string;
    objectVersion: string | null;
    objectVersionUE5: string | null;
    isUnversioned: boolean;
    packageFlags: number;
    engineMajor: number | null;
    engineMinor: number | null;
    enginePatch: number | null;
    engineChangelist: number | null;
    engineBranch: string | null;
    importCount: number;
    exportCount: number;
}

/** Import table entry */
export interface DbImportEntry {
    id: number;
    importIndex: number;
    objectName: string;
    className: string;
    classPackage: string | null;
    outerIndex: number | null;
    packageName: string | null;
    isOptional: boolean;
}

/** Export table entry */
export interface DbExportEntry {
    id: number;
    exportIndex: number;
    objectName: string;
    className: string | null;
    superName: string | null;
    outerIndex: number | null;
    objectFlags: number;
    serialSize: number;
    isAsset: boolean;
    notForClient: boolean;
    notForServer: boolean;
    isForcedExport: boolean;
    extrasSize: number;
}

/** Flat property entry with export context */
export interface DbFlatPropertyEntry {
    id: number;
    exportId: number;
    exportIndex: number;
    exportName: string;
    parentId: number | null;
    sortOrder: number;
    name: string;
    propertyType: string;
    structType: string | null;
    arrayIndex: number;
    valueInt: number | null;
    valueFloat: number | null;
    valueText: string | null;
    valueRef: string | null;
}

/** Custom version entry */
export interface DbCustomVersionEntry {
    id: number;
    assetId: number;
    guid: string;
    friendlyName: string | null;
    version: number;
}

/** Edge (dependency) entry */
export interface DbEdgeEntry {
    id: number;
    targetPath: string;
    refType: string;
}

/** Export dependency entry */
export interface DbExportDependencyEntry {
    id: number;
    exportId: number;
    exportIndex: number;
    exportName: string | null;
    depType: string;
    targetIndex: number;
}

/** Gatherable text entry */
export interface DbGatherableTextEntry {
    id: number;
    namespace: string | null;
    sourceString: string | null;
    keyName: string | null;
    siteDescription: string | null;
    isEditorOnly: boolean;
}

/** Searchable name entry */
export interface DbSearchableNameEntry {
    id: number;
    exportIndex: number;
    name: string;
}

/** World tile info entry */
export interface DbWorldTileInfoEntry {
    id: number;
    positionX: number | null;
    positionY: number | null;
    positionZ: number | null;
    absPositionX: number | null;
    absPositionY: number | null;
    absPositionZ: number | null;
    layerName: string | null;
    streamingDistance: number | null;
    distanceStreamingEnabled: boolean;
    parentTilePackage: string | null;
    zOrder: number | null;
    hideInTileView: boolean;
    lodListJson: string | null;
}

/** Combined response from getAssetTables */
export interface AssetTablesPayload {
    assetPath: string;
    assetInfo: DbAssetInfo;
    imports: DbImportEntry[];
    exports: DbExportEntry[];
    properties: DbFlatPropertyEntry[];
    customVersions: DbCustomVersionEntry[];
    edges: DbEdgeEntry[];
    gatherableText: DbGatherableTextEntry[];
    searchableNames: DbSearchableNameEntry[];
    worldTileInfo: DbWorldTileInfoEntry[];
    exportDependencies: DbExportDependencyEntry[];
}

// =============================================================================
// Utility Types
// =============================================================================

/**
 * Generic paginated response for large data sets.
 */
export interface PaginatedResponse<T> {
    /** Items in this page */
    items: T[];
    /** Total number of items */
    totalCount: number;
    /** Current page index (0-based) */
    pageIndex: number;
    /** Items per page */
    pageSize: number;
    /** Whether more pages exist */
    hasMore: boolean;
}

/**
 * IPC message prefix for console.log interception.
 */
export const IPC_PREFIX = '__UASSET_IPC__:' as const;

/**
 * Receiver function name called by C# to push data to Svelte.
 */
export const IPC_RECEIVER = '__UASSET_RECV__' as const;
