<!--
    PropertyGrid Component

    Grid display of properties for the selected node.
    Receives data from properties view model and forwards edits to C#.
-->
<script lang="ts">
    import PropertyRow from './PropertyRow.svelte';
    import ScrollContainer from '$lib/components/common/ScrollContainer.svelte';
    import { properties } from '$lib/view-models/properties.svelte';
    import type { PropertyValue } from '$lib/bridge/types';

    interface Props {
        /** Optional CSS class */
        class?: string;
    }

    let { class: className = '' }: Props = $props();

    function flattenProperties(
        props: PropertyValue[],
        expandedPaths: string[],
        depth = 0
    ): Array<{ property: PropertyValue; depth: number }> {
        const result: Array<{ property: PropertyValue; depth: number }> = [];
        for (const prop of props) {
            const key = prop.path.join('.');
            result.push({ property: prop, depth });
            if (prop.children?.length && expandedPaths.includes(key)) {
                result.push(...flattenProperties(prop.children, expandedPaths, depth + 1));
            }
        }
        return result;
    }

    let flatList = $derived(
        flattenProperties(properties.properties, properties.expandedPaths)
    );
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
        <ScrollContainer>
            <div class="properties-list">
                {#each flatList as { property, depth } (properties.pathToKey(property.path))}
                    <PropertyRow {property} {depth} />
                {/each}
            </div>
        </ScrollContainer>
    {/if}
</div>

<style>
    .property-grid {
        height: 100%;
        display: flex;
        flex-direction: column;
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
