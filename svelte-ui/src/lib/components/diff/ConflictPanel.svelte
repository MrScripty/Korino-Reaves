<!--
    ConflictPanel Component

    Panel for displaying and resolving conflicts in three-way diffs.
    Shows game vs mod changes and allows user to choose resolution.
-->
<script lang="ts">
    import type { DiffConflict } from '$lib/bridge/types';
    import { resolveConflict, formatPath } from '$lib/view-models/diff.svelte';

    interface Props {
        /** List of conflicts to display */
        conflicts: DiffConflict[];
        /** Whether to only show conflicts in the main list */
        showOnlyConflicts: boolean;
        /** Callback to toggle conflict filter */
        onToggleFilter: (value: boolean) => void;
    }

    let { conflicts, showOnlyConflicts, onToggleFilter }: Props = $props();

    // Track which conflict is expanded
    let expandedConflict = $state<string | null>(null);

    // Track custom value input
    let customValue = $state<string>('');

    function toggleExpand(path: string[]) {
        const key = path.join('/');
        expandedConflict = expandedConflict === key ? null : key;
        customValue = '';
    }

    function handleResolve(conflict: DiffConflict, resolution: 'keep_game' | 'keep_mod' | 'custom') {
        if (resolution === 'custom') {
            // Parse custom value
            let parsed: unknown;
            try {
                parsed = JSON.parse(customValue);
            } catch {
                parsed = customValue; // Use as string if not valid JSON
            }
            resolveConflict(conflict.path, resolution, parsed);
        } else {
            resolveConflict(conflict.path, resolution);
        }
        expandedConflict = null;
    }

    function formatValue(value: unknown): string {
        if (value === null) return 'null';
        if (value === undefined) return 'undefined';
        if (typeof value === 'string') return `"${value}"`;
        if (typeof value === 'object') {
            try {
                return JSON.stringify(value);
            } catch {
                return '{...}';
            }
        }
        return String(value);
    }
</script>

<div class="conflict-panel">
    <div class="panel-header">
        <div class="header-title">
            <span class="conflict-icon">!</span>
            <span class="title-text">{conflicts.length} Conflict{conflicts.length !== 1 ? 's' : ''}</span>
        </div>

        <label class="filter-toggle">
            <input
                type="checkbox"
                checked={showOnlyConflicts}
                onchange={() => onToggleFilter(!showOnlyConflicts)}
            />
            <span>Show only conflicts</span>
        </label>
    </div>

    <div class="conflicts-list">
        {#each conflicts as conflict (conflict.path.join('/'))}
            {@const pathKey = conflict.path.join('/')}
            {@const isExpanded = expandedConflict === pathKey}

            <div class="conflict-item" class:expanded={isExpanded}>
                <button
                    class="conflict-header"
                    onclick={() => toggleExpand(conflict.path)}
                >
                    <span class="expand-icon">{isExpanded ? '▼' : '▶'}</span>
                    <span class="conflict-path">{formatPath(conflict.path)}</span>
                    {#if conflict.suggestedResolution}
                        <span class="suggestion-badge">
                            Suggested: {conflict.suggestedResolution}
                        </span>
                    {/if}
                </button>

                {#if isExpanded}
                    <div class="conflict-details">
                        <div class="value-comparison">
                            <div class="value-row original">
                                <span class="value-label">Original:</span>
                                <code class="value-code">{formatValue(conflict.originalValue)}</code>
                            </div>
                            <div class="value-row game">
                                <span class="value-label">Game (v1.1):</span>
                                <code class="value-code">{formatValue(conflict.gameValue)}</code>
                            </div>
                            <div class="value-row mod">
                                <span class="value-label">Mod:</span>
                                <code class="value-code">{formatValue(conflict.modValue)}</code>
                            </div>
                        </div>

                        <div class="resolution-options">
                            <span class="options-label">Resolution:</span>
                            <div class="options-buttons">
                                <button
                                    class="resolution-btn game"
                                    onclick={() => handleResolve(conflict, 'keep_game')}
                                >
                                    Keep Game
                                </button>
                                <button
                                    class="resolution-btn mod"
                                    onclick={() => handleResolve(conflict, 'keep_mod')}
                                >
                                    Keep Mod
                                </button>
                            </div>

                            <div class="custom-resolution">
                                <input
                                    type="text"
                                    class="custom-input"
                                    bind:value={customValue}
                                    placeholder="Custom value..."
                                />
                                <button
                                    class="resolution-btn custom"
                                    onclick={() => handleResolve(conflict, 'custom')}
                                    disabled={!customValue.trim()}
                                >
                                    Use Custom
                                </button>
                            </div>
                        </div>
                    </div>
                {/if}
            </div>
        {/each}
    </div>
</div>

<style>
    .conflict-panel {
        background: rgba(198, 120, 221, 0.1);
        border: 1px solid var(--diff-conflict);
        border-radius: 4px;
        margin: var(--space-2);
        overflow: hidden;
    }

    .panel-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        background: rgba(198, 120, 221, 0.15);
        border-bottom: 1px solid var(--diff-conflict);
    }

    .header-title {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .conflict-icon {
        width: 20px;
        height: 20px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--diff-conflict);
        color: white;
        border-radius: 50%;
        font-weight: bold;
        font-size: var(--text-xs);
    }

    .title-text {
        font-weight: 500;
        color: var(--diff-conflict);
    }

    .filter-toggle {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--text-xs);
        color: var(--text-secondary);
        cursor: pointer;
    }

    .filter-toggle input {
        cursor: pointer;
    }

    .conflicts-list {
        max-height: 300px;
        overflow-y: auto;
    }

    .conflict-item {
        border-bottom: 1px solid rgba(198, 120, 221, 0.2);
    }

    .conflict-item:last-child {
        border-bottom: none;
    }

    .conflict-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        background: transparent;
        border: none;
        color: var(--text-primary);
        text-align: left;
        cursor: pointer;
        transition: background var(--transition-fast);
    }

    .conflict-header:hover {
        background: rgba(198, 120, 221, 0.1);
    }

    .expand-icon {
        color: var(--text-muted);
        font-size: var(--text-xs);
        width: 12px;
    }

    .conflict-path {
        flex: 1;
        font-family: var(--font-mono);
        font-size: var(--text-sm);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .suggestion-badge {
        padding: 2px 6px;
        background: var(--bg-tertiary);
        border-radius: 3px;
        font-size: var(--text-xs);
        color: var(--text-secondary);
    }

    .conflict-details {
        padding: var(--space-2) var(--space-3);
        background: var(--bg-primary);
    }

    .value-comparison {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        margin-bottom: var(--space-3);
    }

    .value-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
    }

    .value-label {
        width: 80px;
        flex-shrink: 0;
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    .value-code {
        flex: 1;
        font-family: var(--font-mono);
        font-size: var(--text-xs);
        padding: 2px 4px;
        border-radius: 2px;
        background: var(--bg-tertiary);
        overflow-x: auto;
        white-space: nowrap;
    }

    .value-row.original .value-code {
        color: var(--text-secondary);
    }

    .value-row.game .value-code {
        color: var(--accent-info);
    }

    .value-row.mod .value-code {
        color: var(--diff-conflict);
    }

    .resolution-options {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
    }

    .options-label {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    .options-buttons {
        display: flex;
        gap: var(--space-2);
    }

    .resolution-btn {
        padding: var(--space-1) var(--space-2);
        border: none;
        border-radius: 3px;
        font-size: var(--text-xs);
        cursor: pointer;
        transition: opacity var(--transition-fast);
    }

    .resolution-btn:hover:not(:disabled) {
        opacity: 0.9;
    }

    .resolution-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }

    .resolution-btn.game {
        background: var(--accent-info);
        color: white;
    }

    .resolution-btn.mod {
        background: var(--diff-conflict);
        color: white;
    }

    .resolution-btn.custom {
        background: var(--accent-primary);
        color: white;
    }

    .custom-resolution {
        display: flex;
        gap: var(--space-2);
    }

    .custom-input {
        flex: 1;
        padding: var(--space-1) var(--space-2);
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: 3px;
        color: var(--text-primary);
        font-family: var(--font-mono);
        font-size: var(--text-xs);
    }

    .custom-input::placeholder {
        color: var(--text-muted);
    }

    .custom-input:focus {
        outline: none;
        border-color: var(--accent-primary);
    }
</style>
