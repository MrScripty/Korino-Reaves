<!--
    DiffView Component

    Side-by-side comparison view for asset diffs.
    Shows base and target versions with highlighted changes.
-->
<script lang="ts">
    import type { DiffChange, DiffChangeType } from '$lib/bridge/types';
    import * as diff from '$lib/view-models/diff.svelte';
    import DiffTree from './DiffTree.svelte';
    import DiffHighlight from './DiffHighlight.svelte';
    import ConflictPanel from './ConflictPanel.svelte';

    // Filter toggles
    let filterButtons: { type: DiffChangeType; label: string; icon: string }[] = [
        { type: 'added', label: 'Added', icon: '+' },
        { type: 'removed', label: 'Removed', icon: '-' },
        { type: 'modified', label: 'Modified', icon: '~' },
        { type: 'renamed', label: 'Renamed', icon: '>' },
    ];

    function handleFilterToggle(type: string) {
        diff.toggleChangeTypeFilter(type);
    }

    function handleChangeClick(change: DiffChange) {
        diff.selectChange(change);
        diff.navigateToChange(change);
    }

    function handleClearDiff() {
        diff.clearDiff();
    }

    function handleApplySafe() {
        diff.applySafeChanges();
    }
</script>

<div class="diff-view">
    <!-- Header -->
    <div class="diff-header">
        <div class="diff-title">
            {#if diff.isThreeWayDiff}
                <span class="mode-badge three-way">Three-Way Diff</span>
                <span class="version-info">Mod Porting Mode</span>
            {:else if diff.hasDiff}
                <span class="mode-badge two-way">Two-Way Diff</span>
            {:else}
                <span class="placeholder">No diff loaded</span>
            {/if}
        </div>

        <div class="diff-actions">
            {#if diff.hasDiff}
                <!-- Filter buttons -->
                <div class="filter-group">
                    {#each filterButtons as btn}
                        <button
                            class="filter-btn"
                            class:active={diff.changeTypeFilter.has(btn.type)}
                            style="--btn-color: var(--diff-{btn.type === 'renamed' ? 'moved' : btn.type})"
                            onclick={() => handleFilterToggle(btn.type)}
                            title="Toggle {btn.label}"
                        >
                            <span class="filter-icon">{btn.icon}</span>
                            <span class="filter-label">{btn.label}</span>
                        </button>
                    {/each}
                </div>

                {#if diff.isThreeWayDiff && diff.safeChanges.length > 0}
                    <button class="action-btn apply-btn" onclick={handleApplySafe}>
                        Apply Safe ({diff.safeChanges.length})
                    </button>
                {/if}

                <button class="action-btn clear-btn" onclick={handleClearDiff}>
                    Clear
                </button>
            {/if}
        </div>
    </div>

    {#if diff.isLoading}
        <div class="loading-overlay">
            <div class="spinner"></div>
            <span>Computing diff...</span>
        </div>
    {:else if diff.error}
        <div class="error-panel">
            <span class="error-icon">!</span>
            <span class="error-message">{diff.error}</span>
            <button class="error-dismiss" onclick={() => diff.clearError()}>Dismiss</button>
        </div>
    {:else if diff.hasDiff}
        <div class="diff-content">
            <!-- Summary stats -->
            {#if diff.summary}
                <div class="diff-summary">
                    {#if diff.isThreeWayDiff}
                        <div class="summary-section">
                            <span class="summary-label">Game Changes:</span>
                            <span class="stat added">+{diff.summary.game?.added ?? 0}</span>
                            <span class="stat removed">-{diff.summary.game?.removed ?? 0}</span>
                            <span class="stat modified">~{diff.summary.game?.modified ?? 0}</span>
                        </div>
                        <div class="summary-section">
                            <span class="summary-label">Mod Changes:</span>
                            <span class="stat added">+{diff.summary.mod?.added ?? 0}</span>
                            <span class="stat removed">-{diff.summary.mod?.removed ?? 0}</span>
                            <span class="stat modified">~{diff.summary.mod?.modified ?? 0}</span>
                        </div>
                        <div class="summary-section">
                            <span class="summary-label">Conflicts:</span>
                            <span class="stat conflict">{diff.summary.conflicts ?? 0}</span>
                            <span class="summary-label">Safe:</span>
                            <span class="stat safe">{diff.summary.safeToApply ?? 0}</span>
                        </div>
                    {:else}
                        <span class="stat added">+{diff.summary.added ?? 0} added</span>
                        <span class="stat removed">-{diff.summary.removed ?? 0} removed</span>
                        <span class="stat modified">~{diff.summary.modified ?? 0} modified</span>
                        {#if diff.summary.renamed}
                            <span class="stat renamed">{diff.summary.renamed} renamed</span>
                        {/if}
                    {/if}
                </div>
            {/if}

            <!-- Conflicts panel (for three-way) -->
            {#if diff.isThreeWayDiff && diff.conflicts.length > 0}
                <ConflictPanel
                    conflicts={diff.conflicts}
                    showOnlyConflicts={diff.showOnlyConflicts}
                    onToggleFilter={(v) => diff.setShowOnlyConflicts(v)}
                />
            {/if}

            <!-- Change list -->
            <div class="changes-list">
                {#each diff.filteredChanges as change (diff.formatPath(change.path))}
                    <div
                        class="change-item"
                        class:selected={diff.selectedChange === change}
                        onclick={() => handleChangeClick(change)}
                        role="button"
                        tabindex="0"
                        onkeydown={(e) => e.key === 'Enter' && handleChangeClick(change)}
                    >
                        <DiffHighlight
                            changeType={change.changeType}
                            path={change.path}
                            oldValue={change.oldValue}
                            newValue={change.newValue}
                        />
                    </div>
                {/each}

                {#if diff.filteredChanges.length === 0}
                    <div class="no-changes">
                        No changes match current filters
                    </div>
                {/if}
            </div>
        </div>
    {:else}
        <div class="empty-state">
            <div class="empty-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2" />
                    <rect x="9" y="3" width="6" height="4" rx="1" />
                    <path d="M9 12h6M9 16h6" />
                </svg>
            </div>
            <p class="empty-text">Load two assets to compare</p>
            <p class="empty-hint">Use File &gt; Compare Assets or drag & drop</p>
        </div>
    {/if}
</div>

<style>
    .diff-view {
        display: flex;
        flex-direction: column;
        height: 100%;
        background: var(--bg-secondary);
    }

    .diff-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border);
        background: var(--bg-tertiary);
    }

    .diff-title {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .mode-badge {
        padding: var(--space-1) var(--space-2);
        border-radius: 4px;
        font-size: var(--text-xs);
        font-weight: 500;
    }

    .mode-badge.two-way {
        background: var(--accent-info);
        color: white;
    }

    .mode-badge.three-way {
        background: var(--diff-conflict);
        color: white;
    }

    .version-info {
        font-size: var(--text-sm);
        color: var(--text-secondary);
    }

    .placeholder {
        color: var(--text-muted);
        font-size: var(--text-sm);
    }

    .diff-actions {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .filter-group {
        display: flex;
        gap: 2px;
        background: var(--bg-primary);
        padding: 2px;
        border-radius: 4px;
    }

    .filter-btn {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-1) var(--space-2);
        background: transparent;
        border: none;
        border-radius: 3px;
        color: var(--text-muted);
        font-size: var(--text-xs);
        cursor: pointer;
        transition: all var(--transition-fast);
    }

    .filter-btn:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .filter-btn.active {
        background: var(--btn-color);
        color: white;
    }

    .filter-icon {
        font-family: var(--font-mono);
        font-weight: bold;
    }

    .action-btn {
        padding: var(--space-1) var(--space-2);
        border: none;
        border-radius: 4px;
        font-size: var(--text-xs);
        cursor: pointer;
        transition: opacity var(--transition-fast);
    }

    .action-btn:hover {
        opacity: 0.9;
    }

    .apply-btn {
        background: var(--accent-success);
        color: white;
    }

    .clear-btn {
        background: var(--bg-hover);
        color: var(--text-secondary);
    }

    .loading-overlay {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-3);
        flex: 1;
        color: var(--text-secondary);
    }

    .spinner {
        width: 32px;
        height: 32px;
        border: 3px solid var(--border);
        border-top-color: var(--accent-primary);
        border-radius: 50%;
        animation: spin 1s linear infinite;
    }

    @keyframes spin {
        to { transform: rotate(360deg); }
    }

    .error-panel {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: var(--accent-error);
        color: white;
    }

    .error-icon {
        width: 20px;
        height: 20px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(255, 255, 255, 0.2);
        border-radius: 50%;
        font-weight: bold;
    }

    .error-message {
        flex: 1;
        font-size: var(--text-sm);
    }

    .error-dismiss {
        padding: var(--space-1) var(--space-2);
        background: rgba(255, 255, 255, 0.2);
        border: none;
        border-radius: 3px;
        color: white;
        cursor: pointer;
    }

    .diff-content {
        flex: 1;
        display: flex;
        flex-direction: column;
        overflow: hidden;
    }

    .diff-summary {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        background: var(--bg-primary);
        border-bottom: 1px solid var(--border);
        font-size: var(--text-xs);
    }

    .summary-section {
        display: flex;
        align-items: center;
        gap: var(--space-1);
    }

    .summary-label {
        color: var(--text-muted);
    }

    .stat {
        padding: 2px 6px;
        border-radius: 3px;
        font-family: var(--font-mono);
    }

    .stat.added { background: var(--diff-added-bg); color: var(--diff-added); }
    .stat.removed { background: var(--diff-removed-bg); color: var(--diff-removed); }
    .stat.modified { background: var(--diff-modified-bg); color: var(--diff-modified); }
    .stat.renamed { background: var(--diff-moved-bg); color: var(--diff-moved); }
    .stat.conflict { background: rgba(198, 120, 221, 0.2); color: var(--diff-conflict); }
    .stat.safe { background: var(--diff-added-bg); color: var(--diff-added); }

    .changes-list {
        flex: 1;
        overflow-y: auto;
        padding: var(--space-2);
    }

    .change-item {
        padding: var(--space-1) var(--space-2);
        border-radius: 4px;
        cursor: pointer;
        transition: background var(--transition-fast);
    }

    .change-item:hover {
        background: var(--bg-hover);
    }

    .change-item.selected {
        background: var(--accent-primary);
    }

    .change-item.selected :global(.change-path),
    .change-item.selected :global(.change-values) {
        color: white;
    }

    .no-changes {
        text-align: center;
        padding: var(--space-4);
        color: var(--text-muted);
        font-size: var(--text-sm);
    }

    .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
        flex: 1;
        padding: var(--space-4);
    }

    .empty-icon {
        width: 64px;
        height: 64px;
        color: var(--text-muted);
        opacity: 0.5;
    }

    .empty-icon svg {
        width: 100%;
        height: 100%;
    }

    .empty-text {
        color: var(--text-secondary);
        font-size: var(--text-base);
        margin: 0;
    }

    .empty-hint {
        color: var(--text-muted);
        font-size: var(--text-sm);
        margin: 0;
    }
</style>
