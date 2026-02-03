<!--
    DiffHighlight Component

    Displays a single diff change with appropriate color coding and value comparison.
-->
<script lang="ts">
    import type { DiffChangeType } from '$lib/bridge/types';
    import { getChangeColor, formatPath } from '$lib/view-models/diff.svelte';

    interface Props {
        /** Type of change */
        changeType: DiffChangeType;
        /** Path to the changed property */
        path: string[];
        /** Old value (undefined for added) */
        oldValue?: unknown;
        /** New value (undefined for removed) */
        newValue?: unknown;
        /** Whether to show compact version */
        compact?: boolean;
    }

    let { changeType, path, oldValue, newValue, compact = false }: Props = $props();

    // Get icon based on change type
    let icon = $derived(
        changeType === 'added' ? '+' :
        changeType === 'removed' ? '-' :
        changeType === 'modified' ? '~' :
        changeType === 'renamed' ? '>' :
        changeType === 'moved' ? '=>' : '?'
    );

    let color = $derived(getChangeColor(changeType));

    function formatValue(value: unknown): string {
        if (value === null) return 'null';
        if (value === undefined) return 'undefined';
        if (typeof value === 'string') {
            if (value.length > 40) return `"${value.slice(0, 40)}..."`;
            return `"${value}"`;
        }
        if (typeof value === 'boolean') return value ? 'true' : 'false';
        if (typeof value === 'number') return String(value);
        if (Array.isArray(value)) return `[${value.length} items]`;
        if (typeof value === 'object') {
            if ('x' in value && 'y' in value) {
                const v = value as { x: number; y: number; z?: number };
                return 'z' in v ? `(${v.x}, ${v.y}, ${v.z})` : `(${v.x}, ${v.y})`;
            }
            return '{...}';
        }
        return String(value);
    }
</script>

<div class="diff-highlight" class:compact style="--change-color: {color}">
    <span class="change-icon">{icon}</span>

    <span class="change-path" title={path.join(' / ')}>
        {formatPath(path)}
    </span>

    {#if !compact}
        <span class="change-values">
            {#if changeType === 'added'}
                <span class="new-value">{formatValue(newValue)}</span>
            {:else if changeType === 'removed'}
                <span class="old-value">{formatValue(oldValue)}</span>
            {:else if changeType === 'modified'}
                <span class="old-value">{formatValue(oldValue)}</span>
                <span class="arrow">&rarr;</span>
                <span class="new-value">{formatValue(newValue)}</span>
            {:else if changeType === 'renamed' || changeType === 'moved'}
                <span class="old-value">{formatValue(oldValue)}</span>
                <span class="arrow">&rarr;</span>
                <span class="new-value">{formatValue(newValue)}</span>
            {/if}
        </span>
    {/if}
</div>

<style>
    .diff-highlight {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--text-sm);
        line-height: 1.4;
    }

    .diff-highlight.compact {
        gap: var(--space-1);
        font-size: var(--text-xs);
    }

    .change-icon {
        width: 16px;
        height: 16px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--change-color);
        color: white;
        border-radius: 3px;
        font-family: var(--font-mono);
        font-size: var(--text-xs);
        font-weight: bold;
        flex-shrink: 0;
    }

    .change-path {
        flex: 1;
        color: var(--text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-family: var(--font-mono);
    }

    .change-values {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        font-family: var(--font-mono);
        font-size: var(--text-xs);
        color: var(--text-secondary);
        flex-shrink: 0;
        max-width: 50%;
    }

    .old-value {
        color: var(--diff-removed);
        text-decoration: line-through;
        opacity: 0.8;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .new-value {
        color: var(--diff-added);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .arrow {
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .compact .change-values {
        display: none;
    }
</style>
