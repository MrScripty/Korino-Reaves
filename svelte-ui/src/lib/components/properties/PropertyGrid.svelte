<!--
    PropertyGrid Component

    Grid display of properties for the selected node.
    Receives data from properties view model and forwards edits to C#.
-->
<script lang="ts">
    import PropertyRow from './PropertyRow.svelte';
    import * as properties from '$lib/view-models/properties.svelte';

    interface Props {
        /** Optional CSS class */
        class?: string;
    }

    let { class: className = '' }: Props = $props();
</script>

<div class="property-grid {className}">
    {#if properties.isLoading}
        <div class="loading">
            <div class="loading-spinner"></div>
            <span>Loading properties...</span>
        </div>
    {:else if properties.error}
        <div class="error">
            <span class="color-error">{properties.error}</span>
            <button onclick={properties.clearError}>Dismiss</button>
        </div>
    {:else if !properties.hasProperties}
        <div class="empty">
            <span class="text-muted">Select a node to view properties</span>
        </div>
    {:else}
        <div class="properties-list">
            {#each properties.properties as property (properties.pathToKey(property.path))}
                <PropertyRow {property} />
            {/each}
        </div>
    {/if}
</div>

<style>
    .property-grid {
        height: 100%;
        overflow: auto;
    }

    .loading,
    .empty,
    .error {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
        height: 100%;
        min-height: 100px;
        padding: var(--space-4);
        text-align: center;
        font-size: var(--text-sm);
    }

    .properties-list {
        padding: var(--space-1) 0;
    }
</style>
