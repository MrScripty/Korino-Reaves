<!--
    PropertyRow Component

    Single property row with name and value editor.
    Uses appropriate editor based on property type.
-->
<script lang="ts">
    import type { PropertyValue } from '$lib/bridge/types';
    import { PROPERTY_TYPE_COLORS } from '$lib/constants';
    import * as properties from '$lib/view-models/properties.svelte';
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
    let valueColor = $derived(
        PROPERTY_TYPE_COLORS[property.type] || 'var(--text-primary)'
    );

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
</script>

<div
    class="property-row"
    class:editable={property.editable}
    style="padding-left: {depth * 16 + 8}px"
>
    <div class="property-name" title={property.path.join(' / ')}>
        {displayName}
    </div>

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
                    min={property.metadata?.min}
                    max={property.metadata?.max}
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
        {/if}
    </div>
</div>

<script context="module" lang="ts">
    function formatValue(value: unknown): string {
        if (value === null) return 'null';
        if (value === undefined) return 'undefined';
        if (typeof value === 'boolean') return value ? 'true' : 'false';
        if (typeof value === 'number') return value.toString();
        if (typeof value === 'string') return value.length > 50 ? value.slice(0, 50) + '...' : value;
        if (Array.isArray(value)) return `[${value.length} items]`;
        if (typeof value === 'object') {
            // Handle vector-like objects
            if ('x' in value && 'y' in value) {
                const v = value as { x: number; y: number; z?: number; w?: number };
                if ('w' in v) return `(${v.x}, ${v.y}, ${v.z}, ${v.w})`;
                if ('z' in v) return `(${v.x}, ${v.y}, ${v.z})`;
                return `(${v.x}, ${v.y})`;
            }
            // Handle color-like objects
            if ('r' in value && 'g' in value && 'b' in value) {
                const c = value as { r: number; g: number; b: number; a?: number };
                if ('a' in c) return `rgba(${c.r}, ${c.g}, ${c.b}, ${c.a})`;
                return `rgb(${c.r}, ${c.g}, ${c.b})`;
            }
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
        font-size: var(--text-sm);
        color: var(--text-secondary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .property-value {
        font-family: var(--font-mono);
        font-size: var(--text-sm);
        overflow: hidden;
    }

    .value-display {
        display: block;
        width: 100%;
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
</style>
