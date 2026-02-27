<!--
    Scene Outliner

    Lists all actors from the loaded level scene.
    When multiple sub-levels are loaded, actors are grouped by level with collapsible headers.
    Click to select an actor, double-click to focus the camera on it.
-->
<script lang="ts">
    import { scene } from '$lib/view-models/scene.svelte';
    import { tick } from 'svelte';
    import ScrollContainer from '$lib/components/common/ScrollContainer.svelte';
    import type { SceneActor } from '$lib/bridge/types';

    let showMeshOnly = $state(false);
    let actorListEl = $state<HTMLDivElement | null>(null);

    function getActorIcon(actor: SceneActor): string {
        if (actor.className.includes('StaticMesh')) return 'mesh';
        if (actor.className.includes('Skeletal')) return 'skeletal';
        return 'other';
    }

    function handleClick(actor: SceneActor) {
        scene.selectActor(actor.id);
    }

    function handleDblClick(actor: SceneActor) {
        scene.focusActor(actor.id);
    }

    const displayedActors = $derived.by(() => {
        const filtered = scene.filteredActors;
        if (showMeshOnly) return filtered.filter((a) => a.hasMesh);
        return filtered;
    });

    const displayedActorsByLevel = $derived.by(() => {
        const map = new Map<string, SceneActor[]>();
        for (const actor of displayedActors) {
            const group = map.get(actor.levelName) ?? [];
            group.push(actor);
            map.set(actor.levelName, group);
        }
        return map;
    });

    // Auto-scroll to selected actor when selection changes (e.g. from viewport pick)
    $effect(() => {
        const id = scene.selectedActorId;
        if (!id || !actorListEl) return;
        tick().then(() => {
            const row = actorListEl?.querySelector(`[data-actor-id="${CSS.escape(id)}"]`);
            row?.scrollIntoView({ block: 'nearest' });
        });
    });
</script>

{#snippet actorRow(actor: SceneActor)}
    <button
        class="actor-row"
        class:selected={scene.selectedActorId === actor.id}
        class:has-mesh={actor.hasMesh}
        class:loaded={actor.isLoaded}
        data-actor-id={actor.id}
        onclick={() => handleClick(actor)}
        ondblclick={() => handleDblClick(actor)}
    >
        <span class="actor-icon" class:mesh={getActorIcon(actor) === 'mesh'} class:skeletal={getActorIcon(actor) === 'skeletal'}>
            {#if getActorIcon(actor) === 'mesh'}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <rect x="3" y="3" width="10" height="10" rx="1" />
                </svg>
            {:else if getActorIcon(actor) === 'skeletal'}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <circle cx="8" cy="4" r="2" />
                    <line x1="8" y1="6" x2="8" y2="11" stroke="currentColor" stroke-width="1.5" />
                    <line x1="5" y1="8" x2="11" y2="8" stroke="currentColor" stroke-width="1.5" />
                    <line x1="8" y1="11" x2="5" y2="14" stroke="currentColor" stroke-width="1.5" />
                    <line x1="8" y1="11" x2="11" y2="14" stroke="currentColor" stroke-width="1.5" />
                </svg>
            {:else}
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <circle cx="8" cy="8" r="4" />
                </svg>
            {/if}
        </span>
        <span class="actor-name">{actor.name}</span>
        <span class="actor-class">{actor.className}</span>
    </button>
{/snippet}

<div class="scene-outliner">
    <!-- Header -->
    <div class="outliner-header">
        <span class="outliner-title">
            {scene.levelName ?? 'Scene'}
            {#if scene.isMultiLevel}
                <span class="multi-level-badge">
                    {scene.subLevels.length} levels
                </span>
            {/if}
        </span>
        <span class="outliner-count">
            {scene.actors.length} actors
        </span>
    </div>

    <!-- Loading progress -->
    {#if scene.isLoading}
        <div class="loading-bar">
            <div class="loading-fill" style="width: {scene.loadPercent}%"></div>
        </div>
        <div class="loading-text">
            {#if scene.isMultiLevel}
                Loading meshes across {scene.subLevels.length} levels...
            {:else}
                Loading meshes...
            {/if}
            {scene.loadProgress.loaded}/{scene.loadProgress.total}
        </div>
    {/if}

    <!-- Filter + controls -->
    <div class="outliner-controls">
        <input
            type="text"
            class="filter-input"
            placeholder="Filter actors..."
            value={scene.filterQuery}
            oninput={(e) => { scene.filterQuery = (e.target as HTMLInputElement).value; }}
        />
        <button
            class="filter-toggle"
            class:active={showMeshOnly}
            title={showMeshOnly ? 'Showing mesh actors only' : 'Showing all actors'}
            onclick={() => { showMeshOnly = !showMeshOnly; }}
        >
            <svg viewBox="0 0 16 16" fill="currentColor">
                <path d="M3 2l5 5-5 5V2z M8 2l5 5-5 5V2z" />
            </svg>
        </button>
    </div>

    <!-- Actor list -->
    <ScrollContainer bind:viewport={actorListEl}>
        {#if scene.isMultiLevel}
            <!-- Grouped by sub-level -->
            {#each Array.from(displayedActorsByLevel.entries()) as [levelName, levelActors] (levelName)}
                <button
                    class="level-group-header"
                    onclick={() => scene.toggleLevelCollapsed(levelName)}
                >
                    <span class="collapse-icon">
                        {#if scene.collapsedLevels.has(levelName)}
                            <svg viewBox="0 0 16 16" fill="currentColor"><path d="M6 4l4 4-4 4z" /></svg>
                        {:else}
                            <svg viewBox="0 0 16 16" fill="currentColor"><path d="M4 6l4 4 4-4z" /></svg>
                        {/if}
                    </span>
                    <span class="level-name">{levelName}</span>
                    <span class="level-count">{levelActors.length}</span>
                </button>
                {#if !scene.collapsedLevels.has(levelName)}
                    {#each levelActors as actor (actor.id)}
                        {@render actorRow(actor)}
                    {/each}
                {/if}
            {/each}
        {:else}
            <!-- Flat list (single level) -->
            {#each displayedActors as actor (actor.id)}
                {@render actorRow(actor)}
            {/each}
        {/if}

        {#if displayedActors.length === 0}
            <div class="empty-state">
                {#if scene.filterQuery || showMeshOnly}
                    <span class="text-muted text-xs">No matching actors</span>
                {:else}
                    <span class="text-muted text-xs">No actors in scene</span>
                {/if}
            </div>
        {/if}
    </ScrollContainer>
</div>

<style>
    .scene-outliner {
        display: flex;
        flex-direction: column;
        height: 100%;
        background: var(--bg-primary);
    }

    .outliner-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
    }

    .outliner-title {
        font-size: var(--text-sm);
        font-weight: 600;
        color: var(--text-primary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .multi-level-badge {
        font-size: 10px;
        font-weight: 400;
        color: var(--text-muted);
        background: var(--bg-secondary);
        padding: 1px 6px;
        border-radius: var(--radius-sm);
        border: 1px solid var(--border);
        flex-shrink: 0;
    }

    .outliner-count {
        font-size: var(--text-xs);
        color: var(--text-muted);
        flex-shrink: 0;
        margin-left: var(--space-2);
    }

    .loading-bar {
        height: 2px;
        background: var(--border);
        flex-shrink: 0;
    }

    .loading-fill {
        height: 100%;
        background: var(--text-secondary);
        transition: width 0.2s ease;
    }

    .loading-text {
        font-size: var(--text-xs);
        color: var(--text-muted);
        text-align: center;
        padding: var(--space-1) var(--space-2);
        flex-shrink: 0;
    }

    .outliner-controls {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-1) var(--space-2);
        border-bottom: 1px solid var(--border);
        flex-shrink: 0;
    }

    .filter-input {
        flex: 1;
        min-width: 0;
        height: 24px;
        padding: 0 var(--space-2);
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        color: var(--text-primary);
        font-size: var(--text-xs);
        outline: none;
    }

    .filter-input:focus {
        border-color: var(--text-secondary);
    }

    .filter-input::placeholder {
        color: var(--text-muted);
    }

    .filter-toggle {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        padding: 0;
        background: transparent;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        color: var(--text-muted);
        cursor: pointer;
        flex-shrink: 0;
    }

    .filter-toggle:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .filter-toggle.active {
        color: var(--text-secondary);
        border-color: var(--text-secondary);
    }

    .filter-toggle svg {
        width: 12px;
        height: 12px;
    }

    /* Actor list scrolling handled by ScrollContainer */

    /* Level group headers for multi-level scenes */
    .level-group-header {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        width: 100%;
        padding: 4px var(--space-3);
        background: var(--bg-secondary);
        border: none;
        border-bottom: 1px solid var(--border);
        cursor: pointer;
        font-size: var(--text-xs);
        font-weight: 600;
        color: var(--text-secondary);
        text-align: left;
    }

    .level-group-header:hover {
        background: var(--bg-hover);
    }

    .collapse-icon {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 12px;
        height: 12px;
        flex-shrink: 0;
    }

    .collapse-icon svg {
        width: 10px;
        height: 10px;
    }

    .level-name {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .level-count {
        font-weight: 400;
        color: var(--text-muted);
        flex-shrink: 0;
    }

    .actor-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: 2px var(--space-3);
        background: transparent;
        border: none;
        cursor: pointer;
        text-align: left;
        font-size: var(--text-xs);
        color: var(--text-primary);
        height: 24px;
    }

    .actor-row:hover {
        background: var(--bg-hover);
    }

    .actor-row.selected {
        background: var(--bg-selected);
    }

    .actor-row:not(.has-mesh) {
        opacity: 0.5;
    }

    .actor-icon {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 14px;
        height: 14px;
        flex-shrink: 0;
        color: var(--text-muted);
    }

    .actor-icon.mesh {
        color: var(--color-struct);
    }

    .actor-icon.skeletal {
        color: var(--color-object);
    }

    .actor-icon svg {
        width: 12px;
        height: 12px;
    }

    .actor-name {
        flex: 1;
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .actor-class {
        flex-shrink: 0;
        color: var(--text-muted);
        font-size: 10px;
        max-width: 100px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .empty-state {
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-4);
    }
</style>
