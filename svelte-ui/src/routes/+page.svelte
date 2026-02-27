<!--
    Main Application Page

    Integrates all components into the main asset viewer layout.
    Panel arrangement is managed by the dock system (DockContainer).
-->
<script lang="ts">
    import AppShell from '$lib/components/layout/AppShell.svelte';
    import MenuBar from '$lib/components/toolbar/MenuBar.svelte';
    import StatusBar from '$lib/components/toolbar/StatusBar.svelte';
    import DockContainer from '$lib/components/dock/DockContainer.svelte';
    import ImportPakDialog from '$lib/components/dialogs/ImportPakDialog.svelte';
    import FileBrowser from '$lib/components/dialogs/FileBrowser.svelte';
    import { fileBrowser } from '$lib/view-models/fileBrowser.svelte';
    // Import view models to ensure module-level side effects run
    import '$lib/view-models/dock.svelte';
    import '$lib/view-models/datatable.svelte';
</script>

<AppShell>
    {#snippet menuBar()}
        <MenuBar />
    {/snippet}

    <DockContainer />

    {#snippet statusBar()}
        <StatusBar />
    {/snippet}
</AppShell>

<!-- Global dialogs -->
<ImportPakDialog />
<FileBrowser
    open={fileBrowser.isOpen}
    title={fileBrowser.title}
    mode={fileBrowser.mode}
    filters={fileBrowser.filters}
    initialPath={fileBrowser.initialPath}
    basePath={fileBrowser.basePath}
    selectOnClick={fileBrowser.selectOnClick}
    onSelect={(path) => fileBrowser.handleSelect(path)}
    onCancel={() => fileBrowser.handleCancel()}
/>
