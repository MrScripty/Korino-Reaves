<!--
    PropertyRow Component

    Single property row with name and value editor.
    Uses appropriate editor based on property type.
    Shows reset button for properties with saved edits.
-->
<script lang="ts">
    import type { PropertyValue } from '$lib/bridge/types';
    import { PROPERTY_TYPE_COLORS } from '$lib/constants';
    import { properties } from '$lib/view-models/properties.svelte';
    import StringEditor from './editors/StringEditor.svelte';
    import NumberEditor from './editors/NumberEditor.svelte';
    import BoolEditor from './editors/BoolEditor.svelte';
    import EnumEditor from './editors/EnumEditor.svelte';
    import VectorEditor from './editors/VectorEditor.svelte';
    import ColorEditor from './editors/ColorEditor.svelte';

    interface Props {
        property: PropertyValue;
        depth?: number;
    }

    let { property, depth = 0 }: Props = $props();

    let isEditing = $derived(properties.isEditing(property.path));
    let isEdited = $derived(property.isEdited === true);
    let valueColor = $derived(
        PROPERTY_TYPE_COLORS[property.type] || 'var(--text-primary)'
    );
    let hasChildren = $derived(!!property.children?.length);
    let pathKey = $derived(properties.pathToKey(property.path));
    let isExpanded = $derived(properties.isPropertyExpanded(pathKey));

    // Display name is last segment of path
    let displayName = $derived(
        property.displayName || property.path[property.path.length - 1]
    );

    function handleStartEdit() {
        if (property.editable) {
            properties.startEditing(property.path);
        }
    }

    function handleValueChange(value: unknown) {
        properties.setPropertyValue(property.path, value);
    }

    function handleCancel() {
        properties.cancelEditing();
    }

    function handleReset() {
        properties.resetProperty(property.path);
    }
</script>

<div
    class="property-row"
    class:editable={property.editable}
    class:edited={isEdited}
    style="padding-left: {depth * 16 + 8}px"
>
    <button
        type="button"
        class="property-name"
        class:has-children={hasChildren}
        class:expanded={isExpanded}
        title={property.path.join(' / ')}
        onclick={() => hasChildren && properties.togglePropertyExpand(pathKey)}
        disabled={!hasChildren}
    >
        {#if hasChildren}
            <span class="expand-chevron" class:expanded={isExpanded}>
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <path d="M6 4l4 4-4 4z" />
                </svg>
            </span>
        {:else}
            <span class="expand-spacer"></span>
        {/if}
        <span class="icon" style="color: {valueColor}">
            {#if property.type === 'struct' || property.type === 'object'}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <path d="M3 3h10v2H3V3zm0 4h10v2H3V7zm0 4h10v2H3v-2z" />
                </svg>
            {:else if property.type === 'array' || property.type === 'set'}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <path d="M4 3h2v10H4V3zm6 0h2v10h-2V3z" />
                </svg>
            {:else if property.type === 'map'}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <rect x="2" y="2" width="12" height="12" rx="2" />
                </svg>
            {:else}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <circle cx="8" cy="8" r="3" />
                </svg>
            {/if}
        </span>
        {displayName}
    </button>

    <div class="property-value" style="color: {valueColor}">
        {#if isEditing}
            <!-- Show appropriate editor based on type -->
            {#if property.type === 'string'}
                <StringEditor
                    value={property.value as string}
                    onSubmit={handleValueChange}
                    onCancel={handleCancel}
                />
            {:else if property.type === 'number'}
                <NumberEditor
                    value={property.value as number}
                    {...(property.metadata?.min !== undefined ? { min: property.metadata.min } : {})}
                    {...(property.metadata?.max !== undefined ? { max: property.metadata.max } : {})}
                    onSubmit={handleValueChange}
                    onCancel={handleCancel}
                />
            {:else if property.type === 'bool'}
                <BoolEditor
                    value={property.value as boolean}
                    onSubmit={handleValueChange}
                />
            {:else if property.type === 'enum'}
                <EnumEditor
                    value={property.value as string}
                    options={property.metadata?.enumValues || []}
                    onSubmit={handleValueChange}
                />
            {:else if property.type === 'vector'}
                <VectorEditor
                    value={property.value as { x: number; y: number; z?: number; w?: number }}
                    onSubmit={handleValueChange}
                    onCancel={handleCancel}
                />
            {:else if property.type === 'color'}
                <ColorEditor
                    value={property.value as { r: number; g: number; b: number; a?: number }}
                    onSubmit={handleValueChange}
                />
            {:else}
                <!-- Fallback to string display -->
                <span class="readonly-value">{formatValue(property.value)}</span>
            {/if}
        {:else}
            <!-- Display mode -->
            <button
                class="value-display"
                class:readonly={!property.editable}
                onclick={handleStartEdit}
                disabled={!property.editable}
            >
                {formatValue(property.value)}
            </button>

            {#if isEdited}
                <button
                    class="reset-button"
                    onclick={handleReset}
                    title="Reset to original value"
                >
                    <svg viewBox="0 0 16 16" fill="currentColor" width="12" height="12">
                        <path d="M2 8a6 6 0 1 1 1.76 4.24l1.42-1.42A4 4 0 1 0 4 8h2L3 11 0 8h2z" />
                    </svg>
                </button>
            {/if}
        {/if}
    </div>
</div>

<script module lang="ts">
    function formatValue(value: unknown): string {
        if (value === null) return 'null';
        if (value === undefined) return 'undefined';
        if (typeof value === 'boolean') return value ? 'true' : 'false';
        if (typeof value === 'number') return value.toString();
        if (typeof value === 'string') return value.length > 50 ? value.slice(0, 50) + '...' : value;
        if (Array.isArray(value)) return `[${value.length} items]`;
        if (typeof value === 'object') {
            const obj = value as Record<string, unknown>;
            const keys = Object.keys(obj);
            // Handle vector-like objects
            if ('x' in obj && 'y' in obj) {
                const v = obj as { x: number; y: number; z?: number; w?: number };
                if ('w' in v) return `(${v.x}, ${v.y}, ${v.z}, ${v.w})`;
                if ('z' in v) return `(${v.x}, ${v.y}, ${v.z})`;
                return `(${v.x}, ${v.y})`;
            }
            // Handle color-like objects
            if ('r' in obj && 'g' in obj && 'b' in obj) {
                const c = obj as { r: number; g: number; b: number; a?: number };
                if ('a' in c) return `rgba(${c.r}, ${c.g}, ${c.b}, ${c.a})`;
                return `rgb(${c.r}, ${c.g}, ${c.b})`;
            }
            // Handle resolved object references (camelCase from C# serialization)
            if ('name' in obj && 'refType' in obj) {
                const ref = obj as { name: string; class?: string; refType: string };
                if (ref.class) return `${ref.name} (${ref.class})`;
                return ref.name;
            }
            // Handle soft object references (camelCase from C# serialization)
            if ('assetPath' in obj) {
                const soft = obj as { assetPath: string; subPath?: string };
                if (soft.subPath) return `${soft.assetPath}:${soft.subPath}`;
                return soft.assetPath || 'None';
            }
            // Handle struct summary {type, propertyCount} (camelCase from C# serialization)
            if ('type' in obj && 'propertyCount' in obj) {
                const count = obj['propertyCount'] as number;
                return `type: ${obj['type']}, propertyCount: ${count}`;
            }
            // Generic small object: show key-value pairs
            if (keys.length > 0 && keys.length <= 4) {
                return keys.map((k) => `${k}: ${obj[k]}`).join(', ');
            }
            if (keys.length > 4) return `{${keys.length} fields}`;
            return '{...}';
        }
        return String(value);
    }
</script>

<style>
    .property-row {
        display: grid;
        grid-template-columns: 1fr 2fr;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-2);
        border-bottom: 1px solid var(--border);
        align-items: center;
        min-height: var(--tree-row-height);
    }

    .property-row:hover {
        background: var(--bg-hover);
    }

    .property-name {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--text-sm);
        color: var(--text-secondary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .property-name.has-children {
        cursor: pointer;
    }

    .expand-chevron {
        width: 14px;
        height: 14px;
        flex-shrink: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        color: var(--text-muted);
        transition: transform var(--transition-fast);
    }

    .expand-chevron.expanded {
        transform: rotate(90deg);
    }

    .expand-chevron svg {
        width: 10px;
        height: 10px;
    }

    .expand-spacer {
        width: 14px;
        flex-shrink: 0;
    }

    .icon {
        width: 16px;
        height: 16px;
        flex-shrink: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        transition: opacity var(--transition-fast);
    }

    .icon svg {
        width: 12px;
        height: 12px;
    }

    .property-name.has-children .icon {
        opacity: 0.4;
    }

    .property-name.has-children.expanded .icon {
        opacity: 1;
    }

    .property-row.edited {
        background: color-mix(in srgb, var(--color-warning, #f59e0b) 12%, transparent);
    }

    .property-row.edited:hover {
        background: color-mix(in srgb, var(--color-warning, #f59e0b) 20%, transparent);
    }

    .property-row.edited .property-name {
        color: var(--color-warning, #f59e0b);
    }

    .property-value {
        display: flex;
        align-items: center;
        gap: 4px;
        font-family: var(--font-mono);
        font-size: var(--text-sm);
        overflow: hidden;
    }

    .value-display {
        flex: 1;
        min-width: 0;
        text-align: left;
        background: transparent;
        border: none;
        padding: 0;
        font: inherit;
        color: inherit;
        cursor: pointer;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .value-display:hover:not(.readonly) {
        text-decoration: underline;
    }

    .value-display.readonly {
        cursor: default;
        opacity: 0.8;
    }

    .readonly-value {
        opacity: 0.8;
    }

    .reset-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 18px;
        height: 18px;
        padding: 0;
        border: none;
        border-radius: 3px;
        background: transparent;
        color: var(--text-muted);
        cursor: pointer;
        flex-shrink: 0;
        opacity: 0;
        transition: opacity var(--transition-fast);
    }

    .property-row:hover .reset-button {
        opacity: 1;
    }

    .reset-button:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }
</style>
