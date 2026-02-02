<!--
    SplitPane Component

    A container with two resizable panes separated by a splitter.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';
    import Splitter from './Splitter.svelte';
    import { LAYOUT } from '$lib/constants';

    interface Props {
        /** Split direction */
        direction?: 'horizontal' | 'vertical';
        /** Initial size of the first pane (pixels) */
        initialSize?: number;
        /** Minimum size of the first pane */
        minSize?: number;
        /** Maximum size of the first pane */
        maxSize?: number;
        /** First pane content */
        first: Snippet;
        /** Second pane content */
        second: Snippet;
    }

    let {
        direction = 'horizontal',
        initialSize = LAYOUT.PANEL_DEFAULT_WIDTH,
        minSize = LAYOUT.PANEL_MIN_WIDTH,
        maxSize = LAYOUT.PANEL_MAX_WIDTH,
        first,
        second,
    }: Props = $props();

    // Transient UI state - OK for Svelte to own
    let size = $state(initialSize);
</script>

<div class="split-pane" class:horizontal={direction === 'horizontal'} class:vertical={direction === 'vertical'}>
    <div
        class="pane first-pane"
        style={direction === 'horizontal'
            ? `width: ${size}px`
            : `height: ${size}px`}
    >
        {@render first()}
    </div>

    <Splitter
        orientation={direction}
        minBefore={minSize}
        maxBefore={maxSize}
        bind:position={size}
    />

    <div class="pane second-pane">
        {@render second()}
    </div>
</div>

<style>
    .split-pane {
        display: flex;
        width: 100%;
        height: 100%;
        overflow: hidden;
    }

    .split-pane.horizontal {
        flex-direction: row;
    }

    .split-pane.vertical {
        flex-direction: column;
    }

    .pane {
        overflow: hidden;
    }

    .first-pane {
        flex-shrink: 0;
    }

    .second-pane {
        flex: 1;
        min-width: 0;
        min-height: 0;
    }
</style>
