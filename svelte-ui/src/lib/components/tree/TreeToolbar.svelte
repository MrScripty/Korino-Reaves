<!--
    TreeToolbar Component

    Toolbar for the asset tree with search and expand/collapse controls.
-->
<script lang="ts">
    import { tree } from '$lib/view-models/tree.svelte';

    // Transient UI state - OK for Svelte to own
    let searchInput = $state('');
    let searchTimeout: ReturnType<typeof setTimeout> | null = null;

    // Clean up debounce timer on component destroy
    $effect(() => {
        return () => {
            if (searchTimeout) clearTimeout(searchTimeout);
        };
    });

    function handleSearchInput(event: Event) {
        const target = event.target as HTMLInputElement;
        searchInput = target.value;

        // Debounce search
        if (searchTimeout) {
            clearTimeout(searchTimeout);
        }
        searchTimeout = setTimeout(() => {
            tree.setFilter(searchInput);
        }, 300);
    }

    function handleSearchKeyDown(event: KeyboardEvent) {
        // Stop propagation so parent tree/CEF don't intercept keys (especially Backspace)
        event.stopPropagation();
    }

    function handleClearSearch() {
        searchInput = '';
        tree.clearFilter();
    }

    function handleExpandAll() {
        tree.expandAll();
    }

    function handleCollapseAll() {
        tree.collapseAll();
    }
</script>

<div class="tree-toolbar">
    <div class="search-box">
        <svg class="search-icon" viewBox="0 0 16 16" fill="currentColor">
            <path
                d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"
            />
        </svg>
        <input
            type="text"
            placeholder="Search..."
            value={searchInput}
            oninput={handleSearchInput}
            onkeydown={handleSearchKeyDown}
            class="search-input"
        />
        {#if searchInput}
            <button
                class="clear-button"
                onclick={handleClearSearch}
                aria-label="Clear search"
            >
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <path
                        d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"
                    />
                </svg>
            </button>
        {/if}
    </div>

    <div class="toolbar-actions">
        <button
            class="toolbar-button"
            onclick={handleExpandAll}
            title="Expand all"
            aria-label="Expand all nodes"
        >
            <svg viewBox="0 0 16 16" fill="currentColor">
                <path d="M8 4l4 4H4l4-4zm0 8l-4-4h8l-4 4z" />
            </svg>
        </button>
        <button
            class="toolbar-button"
            onclick={handleCollapseAll}
            title="Collapse all"
            aria-label="Collapse all nodes"
            disabled={!tree.hasExpanded}
        >
            <svg viewBox="0 0 16 16" fill="currentColor">
                <path d="M4 7h8v2H4V7z" />
            </svg>
        </button>
    </div>
</div>

<style>
    .tree-toolbar {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2);
        border-bottom: 1px solid var(--border);
    }

    .search-box {
        flex: 1;
        display: flex;
        align-items: center;
        gap: var(--space-1);
        background: var(--bg-tertiary);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        padding: 0 var(--space-2);
    }

    .search-box:focus-within {
        border-color: var(--accent-primary);
    }

    .search-icon {
        width: 14px;
        height: 14px;
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .search-input {
        flex: 1;
        background: transparent;
        border: none;
        padding: var(--space-1) 0;
        font-size: var(--text-sm);
        color: var(--text-primary);
        outline: none;
    }

    .search-input::placeholder {
        color: var(--text-muted);
    }

    .clear-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 16px;
        height: 16px;
        padding: 0;
        background: transparent;
        border: none;
        cursor: pointer;
        color: var(--text-muted);
        border-radius: var(--radius-sm);
    }

    .clear-button:hover {
        color: var(--text-primary);
        background: var(--bg-hover);
    }

    .clear-button svg {
        width: 12px;
        height: 12px;
    }

    .toolbar-actions {
        display: flex;
        gap: var(--space-1);
    }

    .toolbar-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        padding: 0;
        background: transparent;
        border: none;
        cursor: pointer;
        color: var(--text-secondary);
        border-radius: var(--radius-md);
    }

    .toolbar-button:hover:not(:disabled) {
        color: var(--text-primary);
        background: var(--bg-hover);
    }

    .toolbar-button:disabled {
        color: var(--text-disabled);
        cursor: not-allowed;
    }

    .toolbar-button svg {
        width: 14px;
        height: 14px;
    }
</style>
