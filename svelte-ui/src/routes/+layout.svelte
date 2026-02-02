<!--
    Root Layout

    Imports global styles and provides the layout structure.
-->
<script lang="ts">
    import '../app.css';
    import type { Snippet } from 'svelte';

    interface Props {
        children: Snippet;
    }

    let { children }: Props = $props();
</script>

{@render children()}
