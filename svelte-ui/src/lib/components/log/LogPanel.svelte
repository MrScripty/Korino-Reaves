<!--
    Log Panel Component

    Displays application log entries with level-based color coding.
    Uses VirtualList for performance with many log entries.
-->
<script lang="ts">
    import VirtualList from '$lib/components/common/VirtualList.svelte';
    import { log } from '$lib/view-models/log.svelte';
    import type { LogEntry } from '$lib/bridge/types';

    const levelColors: Record<string, string> = {
        verbose: 'var(--text-muted)',
        debug: 'var(--text-secondary)',
        information: 'var(--text-primary)',
        warning: 'var(--color-warning, #e5c07b)',
        error: 'var(--color-error, #e06c75)',
        fatal: 'var(--color-error, #e06c75)',
    };

    const levelLabels: Record<string, string> = {
        verbose: 'VRB',
        debug: 'DBG',
        information: 'INF',
        warning: 'WRN',
        error: 'ERR',
        fatal: 'FTL',
    };

    function formatTime(timestamp: number): string {
        const d = new Date(timestamp);
        return d.toLocaleTimeString('en-US', { hour12: false });
    }

    function getKey(_entry: LogEntry, index: number): number {
        return index;
    }
</script>

<div class="log-panel">
    <div class="log-toolbar">
        <span class="log-count">{log.entryCount} entries</span>
        <button class="log-clear" onclick={() => log.clear()}>Clear</button>
    </div>
    <div class="log-entries">
        {#if log.entries.length === 0}
            <div class="log-empty">
                <span class="text-muted text-sm">No log entries yet</span>
            </div>
        {:else}
            <VirtualList items={log.entries} rowHeight={20} {getKey}>
                {#snippet children(entry: LogEntry, _index: number)}
                    <div
                        class="log-entry"
                        style="color: {levelColors[entry.level] ?? 'var(--text-primary)'}"
                    >
                        <span class="log-time">{formatTime(entry.timestamp)}</span>
                        <span class="log-level">[{levelLabels[entry.level] ?? entry.level}]</span>
                        <span class="log-message">{entry.message}</span>
                        {#if entry.exception}
                            <span class="log-exception"> — {entry.exception}</span>
                        {/if}
                    </div>
                {/snippet}
            </VirtualList>
        {/if}
    </div>
</div>

<style>
    .log-panel {
        flex: 1;
        min-height: 0;
        display: flex;
        flex-direction: column;
    }

    .log-toolbar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 2px var(--space-2);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
    }

    .log-count {
        font-size: 11px;
        color: var(--text-muted);
    }

    .log-clear {
        font-size: 11px;
        padding: 1px 6px;
        background: transparent;
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        color: var(--text-secondary);
        cursor: pointer;
    }

    .log-clear:hover {
        background: var(--bg-hover);
    }

    .log-entries {
        flex: 1;
        min-height: 0;
    }

    .log-empty {
        padding: var(--space-4);
        display: flex;
        align-items: center;
        justify-content: center;
        height: 100%;
    }

    .log-entry {
        display: flex;
        gap: 6px;
        padding: 0 var(--space-2);
        font-family: var(--font-mono);
        font-size: 11px;
        line-height: 20px;
        white-space: nowrap;
    }

    .log-time {
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .log-level {
        flex-shrink: 0;
        font-weight: 600;
    }

    .log-message {
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .log-exception {
        color: var(--color-error, #e06c75);
    }
</style>
