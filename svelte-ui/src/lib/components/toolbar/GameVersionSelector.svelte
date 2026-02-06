<!--
    Game Version Selector

    Compact dropdown in the status bar for viewing and changing the
    game version (EGame) for the current project. Opens upward since
    the status bar is at the bottom of the viewport.
-->
<script lang="ts">
    import { project } from '$lib/view-models/project.svelte';
    import type { GameVersionEntry } from '$lib/bridge/types';

    let isOpen = $state(false);
    let searchQuery = $state('');
    let dropdownRef = $state<HTMLDivElement | null>(null);
    let searchRef = $state<HTMLInputElement | null>(null);

    // Close dropdown on outside click
    function handleWindowClick(e: MouseEvent) {
        if (dropdownRef && !dropdownRef.contains(e.target as Node)) {
            close();
        }
    }

    function toggle() {
        if (isOpen) {
            close();
        } else {
            open();
        }
    }

    function open() {
        isOpen = true;
        searchQuery = '';
        // Focus search input after render
        requestAnimationFrame(() => searchRef?.focus());
    }

    function close() {
        isOpen = false;
        searchQuery = '';
    }

    function selectVersion(value: string) {
        project.setGameVersion(value);
        close();
    }

    // Filter versions by search
    let filteredVersions = $derived.by(() => {
        const q = searchQuery.toLowerCase().trim();
        if (!q) return project.gameVersions;
        return project.gameVersions.filter(
            (v) =>
                v.label.toLowerCase().includes(q) ||
                v.value.toLowerCase().includes(q) ||
                v.group.toLowerCase().includes(q),
        );
    });

    // Group filtered versions
    let groupedVersions = $derived.by(() => {
        const groups = new Map<string, GameVersionEntry[]>();
        for (const entry of filteredVersions) {
            const existing = groups.get(entry.group);
            if (existing) {
                existing.push(entry);
            } else {
                groups.set(entry.group, [entry]);
            }
        }
        return groups;
    });

    let currentSelected = $derived(
        project.gameVersionState?.isAutoDetect ? 'AUTO' : (project.gameVersionState?.selected ?? 'AUTO'),
    );
</script>

<svelte:window onclick={handleWindowClick} />

<div class="version-selector" bind:this={dropdownRef}>
    <button class="version-button" onclick={toggle} title="Game version">
        <span class="version-label">{project.currentVersionLabel || 'Auto'}</span>
        <span class="caret">{isOpen ? '\u25BC' : '\u25B2'}</span>
    </button>

    {#if isOpen}
        <div class="dropdown">
            <input
                bind:this={searchRef}
                type="text"
                class="search-input"
                bind:value={searchQuery}
                placeholder="Search games..."
                autocomplete="off"
                spellcheck="false"
                onkeydown={(e) => e.key === 'Escape' && close()}
            />

            <div class="option-list">
                <!-- Auto-detect option -->
                <button
                    class="option"
                    class:selected={currentSelected === 'AUTO'}
                    onclick={() => selectVersion('AUTO')}
                >
                    Auto-Detect
                </button>

                {#each [...groupedVersions] as [group, entries]}
                    <div class="group-header">{group}</div>
                    {#each entries as entry}
                        <button
                            class="option"
                            class:selected={currentSelected === entry.value}
                            onclick={() => selectVersion(entry.value)}
                        >
                            {entry.label}
                        </button>
                    {/each}
                {/each}

                {#if filteredVersions.length === 0 && searchQuery}
                    <div class="empty">No matches</div>
                {/if}
            </div>
        </div>
    {/if}
</div>

<style>
    .version-selector {
        position: relative;
    }

    .version-button {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: 0 var(--space-2);
        height: 100%;
        font-size: var(--text-xs);
        color: var(--text-secondary);
        background: transparent;
        border: none;
        cursor: pointer;
        white-space: nowrap;
    }

    .version-button:hover {
        color: var(--text-primary);
        background: var(--bg-hover);
    }

    .caret {
        font-size: 8px;
        opacity: 0.6;
    }

    .dropdown {
        position: absolute;
        bottom: 100%;
        left: 0;
        width: 320px;
        max-height: 400px;
        margin-bottom: 4px;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        box-shadow: 0 -4px 16px rgba(0, 0, 0, 0.3);
        display: flex;
        flex-direction: column;
        z-index: 1000;
    }

    .search-input {
        padding: var(--space-2) var(--space-3);
        font-size: var(--text-sm);
        background: var(--bg-primary);
        border: none;
        border-bottom: 1px solid var(--border);
        border-radius: var(--radius-md) var(--radius-md) 0 0;
        color: var(--text-primary);
        outline: none;
    }

    .search-input::placeholder {
        color: var(--text-muted);
    }

    .option-list {
        flex: 1;
        overflow-y: auto;
        max-height: 340px;
    }

    .group-header {
        position: sticky;
        top: 0;
        padding: var(--space-1) var(--space-3);
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--text-muted);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
        text-transform: uppercase;
        letter-spacing: 0.05em;
    }

    .option {
        display: block;
        width: 100%;
        padding: var(--space-1) var(--space-3);
        font-size: var(--text-sm);
        color: var(--text-primary);
        background: transparent;
        border: none;
        cursor: pointer;
        text-align: left;
    }

    .option:hover {
        background: var(--bg-hover);
    }

    .option.selected {
        background: rgba(var(--accent-rgb), 0.15);
        color: var(--accent);
    }

    .empty {
        padding: var(--space-4);
        text-align: center;
        color: var(--text-muted);
        font-size: var(--text-sm);
    }
</style>
