<!--
    AppShell Component

    Main application layout with menu bar, content area, and status bar.
    The viewport fills the background, with semi-transparent panels floating on top.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Props {
        /** Menu bar content */
        menuBar?: Snippet;
        /** Main content area */
        children: Snippet;
        /** Status bar content */
        statusBar?: Snippet;
    }

    let { menuBar, children, statusBar }: Props = $props();
</script>

<div class="app-shell">
    <!-- Menu Bar (solid, always visible) -->
    {#if menuBar}
        <header class="app-menu-bar">
            {@render menuBar()}
        </header>
    {/if}

    <!-- Main Content Area -->
    <main class="app-content">
        {@render children()}
    </main>

    <!-- Status Bar (solid, always visible) -->
    {#if statusBar}
        <footer class="app-status-bar">
            {@render statusBar()}
        </footer>
    {/if}
</div>

<style>
    .app-shell {
        display: flex;
        flex-direction: column;
        width: 100vw;
        height: 100vh;
        overflow: hidden;
        background: var(--bg-primary);
    }

    .app-menu-bar {
        flex-shrink: 0;
        height: var(--menu-height);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
        z-index: var(--z-panel);
    }

    .app-content {
        flex: 1;
        position: relative;
        overflow: hidden;
    }

    .app-status-bar {
        flex-shrink: 0;
        height: var(--status-height);
        background: var(--bg-secondary);
        border-top: 1px solid var(--border);
        z-index: var(--z-panel);
    }
</style>
