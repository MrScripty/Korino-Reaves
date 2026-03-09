<!--
    FileBrowser Component

    A Svelte 5 file browser dialog for selecting files and directories.
    Communicates with C# backend via IPC for filesystem operations.
-->
<script lang="ts">
    import Modal from '$lib/components/common/Modal.svelte';
    import { ipc } from '$lib/bridge/ipc';

    interface FileEntry {
        name: string;
        path: string;
        isDirectory: boolean;
        size?: number;
        modified?: string;
    }

    interface Props {
        open: boolean;
        title?: string;
        mode: 'file' | 'directory';
        filters?: string[]; // e.g., ['*.pak', '*.uasset']
        initialPath?: string;
        basePath?: string; // Root path for relative display (hides prefix)
        selectOnClick?: boolean; // If true, clicking a directory immediately selects it
        onSelect: (path: string) => void;
        onCancel: () => void;
    }

    let {
        open,
        title = 'Select File',
        mode = 'file',
        filters = [],
        initialPath = '',
        basePath = '',
        selectOnClick = false,
        onSelect,
        onCancel,
    }: Props = $props();

    let currentPath = $state('');
    let entries = $state<FileEntry[]>([]);
    let selectedEntry = $state<FileEntry | null>(null);
    let isLoading = $state(false);
    let error = $state<string | null>(null);
    let pathInput = $state('');

    // Convert absolute path to display path (relative if basePath is set)
    function toDisplayPath(absolutePath: string): string {
        if (!basePath || !absolutePath.startsWith(basePath)) {
            return absolutePath;
        }
        const relative = absolutePath.slice(basePath.length);
        // Ensure it starts with / for consistency, or show "." for root
        if (!relative || relative === '/') return '.';
        return relative.startsWith('/') ? '.' + relative : './' + relative;
    }

    // Convert display/input path to absolute path
    function toAbsolutePath(inputPath: string): string {
        if (!inputPath) return basePath || '/';
        // If already absolute, use as-is
        if (inputPath.startsWith('/')) return inputPath;
        // Handle relative paths (starting with . or just a name)
        if (basePath) {
            if (inputPath === '.' || inputPath === './') return basePath;
            if (inputPath.startsWith('./')) {
                return basePath + inputPath.slice(1);
            }
            if (inputPath.startsWith('../')) {
                // Handle parent directory navigation
                const baseParent = basePath.split('/').slice(0, -1).join('/') || '/';
                return baseParent + inputPath.slice(2);
            }
            // Assume relative to basePath
            return basePath + '/' + inputPath;
        }
        return '/' + inputPath;
    }

    // Breadcrumb parts for navigation (uses display paths)
    const pathParts = $derived.by(() => {
        if (!currentPath) return [];

        // If basePath is set, show paths relative to it
        if (basePath && currentPath.startsWith(basePath)) {
            const relativePath = currentPath.slice(basePath.length);
            if (!relativePath || relativePath === '/') {
                return []; // At root of basePath, no breadcrumbs needed
            }
            const parts = relativePath.split('/').filter(Boolean);
            const result: { name: string; path: string; displayPath: string }[] = [];
            let accumulated = basePath;
            for (const part of parts) {
                accumulated += '/' + part;
                result.push({
                    name: part,
                    path: accumulated,
                    displayPath: toDisplayPath(accumulated),
                });
            }
            return result;
        }

        // No basePath - show full path
        const parts = currentPath.split('/').filter(Boolean);
        const result: { name: string; path: string; displayPath: string }[] = [];
        let accumulated = '';
        for (const part of parts) {
            accumulated += '/' + part;
            result.push({ name: part, path: accumulated, displayPath: accumulated });
        }
        return result;
    });

    // Filter entries based on mode and filters
    const filteredEntries = $derived.by(() => {
        return entries.filter(entry => {
            // Always show directories
            if (entry.isDirectory) return true;
            // In directory mode, only show directories
            if (mode === 'directory') return false;
            // Apply file filters
            if (filters.length === 0) return true;
            const name = entry.name.toLowerCase();
            return filters.some(filter => {
                const pattern = filter.replace('*.', '.').toLowerCase();
                return name.endsWith(pattern);
            });
        });
    });

    // Load directory when path changes or dialog opens
    $effect(() => {
        if (open) {
            if (initialPath && !currentPath) {
                loadDirectory(initialPath);
            } else if (!currentPath) {
                // Get home directory
                getHomeDirectory();
            }
        }
    });

    // Reset state when dialog closes
    $effect(() => {
        if (!open) {
            selectedEntry = null;
            error = null;
        }
    });

    async function getHomeDirectory() {
        try {
            const result = await ipc.request<{ path: string }>({
                type: 'fs',
                action: 'getHome',
                payload: {},
            });
            loadDirectory(result.path);
        } catch (e) {
            // Fallback to root
            loadDirectory('/');
        }
    }

    async function loadDirectory(path: string) {
        isLoading = true;
        error = null;
        selectedEntry = null;

        try {
            const result = await ipc.request<{ entries: FileEntry[]; path: string }>({
                type: 'fs',
                action: 'list',
                payload: { path },
            });
            entries = result.entries;
            currentPath = result.path;
            // Show relative path in input if basePath is set
            pathInput = toDisplayPath(result.path);
        } catch (e) {
            error = e instanceof Error ? e.message : 'Failed to load directory';
            entries = [];
        } finally {
            isLoading = false;
        }
    }

    function handleEntryClick(entry: FileEntry) {
        if (entry.isDirectory) {
            if (selectOnClick && mode === 'directory') {
                // Immediately select the directory (e.g., project picker)
                onSelect(entry.path);
                return;
            }
            if (mode === 'directory') {
                // In directory mode, single-click selects a folder
                selectedEntry = selectedEntry?.path === entry.path ? null : entry;
            } else {
                // In file mode, single-click navigates into folder
                loadDirectory(entry.path);
            }
        } else {
            // Single-click toggles file selection
            selectedEntry = selectedEntry?.path === entry.path ? null : entry;
        }
    }

    function handleEntryDoubleClick(entry: FileEntry) {
        if (entry.isDirectory) {
            if (mode === 'directory') {
                // In directory mode, double-click navigates into folder
                loadDirectory(entry.path);
            } else {
                // In file mode, double-click also navigates
                loadDirectory(entry.path);
            }
        } else {
            // Double-click file selects it
            onSelect(entry.path);
        }
    }

    function handleSelect() {
        if (mode === 'directory') {
            // Select the highlighted folder, or current directory if none selected
            if (selectedEntry && selectedEntry.isDirectory) {
                onSelect(selectedEntry.path);
            } else {
                onSelect(currentPath);
            }
        } else if (selectedEntry && !selectedEntry.isDirectory) {
            onSelect(selectedEntry.path);
        }
    }

    function handlePathSubmit() {
        if (!pathInput) return;
        const absolutePath = toAbsolutePath(pathInput);
        if (absolutePath !== currentPath) {
            loadDirectory(absolutePath);
        }
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter') {
            if (mode === 'directory' || selectedEntry) {
                handleSelect();
            }
        } else if (event.key === 'Escape') {
            onCancel();
        }
    }

    function navigateUp() {
        const parent = currentPath.split('/').slice(0, -1).join('/') || '/';
        // Don't navigate above basePath if it's set
        if (basePath && !parent.startsWith(basePath) && parent !== basePath) {
            loadDirectory(basePath);
        } else {
            loadDirectory(parent);
        }
    }

    function navigateTo(path: string) {
        loadDirectory(path);
    }

    function formatSize(bytes?: number): string {
        if (bytes === undefined) return '';
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
        return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
    }

    const canSelect = $derived.by(() => {
        if (mode === 'directory') return true;
        return selectedEntry !== null && !selectedEntry.isDirectory;
    });
</script>

<Modal
    {title}
    {open}
    onClose={onCancel}
    width="700px"
>
    <div class="file-browser" onkeydown={handleKeyDown}>
        <!-- Path bar -->
        <div class="path-bar">
            <button
                class="nav-btn"
                onclick={navigateUp}
                disabled={currentPath === '/' || Boolean(basePath && currentPath === basePath)}
            >
                <svg viewBox="0 0 16 16" fill="currentColor">
                    <path d="M8 2l-6 6h4v6h4V8h4L8 2z"/>
                </svg>
            </button>
            <input
                type="text"
                class="path-input"
                bind:value={pathInput}
                onkeydown={(e) => e.key === 'Enter' && handlePathSubmit()}
                onblur={handlePathSubmit}
            />
        </div>

        <!-- Breadcrumbs -->
        <div class="breadcrumbs">
            <button class="breadcrumb" onclick={() => navigateTo(basePath || '/')}>
                {basePath ? '.' : '/'}
            </button>
            {#each pathParts as part}
                <span class="breadcrumb-separator">/</span>
                <button class="breadcrumb" onclick={() => navigateTo(part.path)}>
                    {part.name}
                </button>
            {/each}
        </div>

        <!-- File list -->
        <div class="file-list">
            {#if isLoading}
                <div class="state-container state-loading">Loading...</div>
            {:else if error}
                <div class="state-container state-error">{error}</div>
            {:else if filteredEntries.length === 0}
                <div class="state-container state-empty">No files found</div>
            {:else}
                {#each filteredEntries as entry}
                    <button
                        class="file-entry"
                        class:selected={selectedEntry?.path === entry.path}
                        class:directory={entry.isDirectory}
                        onclick={() => handleEntryClick(entry)}
                        ondblclick={() => handleEntryDoubleClick(entry)}
                    >
                        <span class="icon">
                            {#if entry.isDirectory}
                                <svg viewBox="0 0 16 16" fill="currentColor">
                                    <path d="M1 3.5A1.5 1.5 0 012.5 2h3.379a1.5 1.5 0 011.06.44l.94.94a.5.5 0 00.354.12H13.5A1.5 1.5 0 0115 5v7.5a1.5 1.5 0 01-1.5 1.5h-11A1.5 1.5 0 011 12.5v-9z"/>
                                </svg>
                            {:else}
                                <svg viewBox="0 0 16 16" fill="currentColor">
                                    <path d="M3 1a1 1 0 00-1 1v12a1 1 0 001 1h10a1 1 0 001-1V5.414a1 1 0 00-.293-.707L10.293 1.293A1 1 0 009.586 1H3zm6 2.5V5h1.5L9 3.5zM4 7h8v1H4V7zm0 2h8v1H4V9zm0 2h5v1H4v-1z"/>
                                </svg>
                            {/if}
                        </span>
                        <span class="name">{entry.name}</span>
                        <span class="size">{formatSize(entry.size)}</span>
                    </button>
                {/each}
            {/if}
        </div>

        <!-- Filter info -->
        {#if filters.length > 0 && mode === 'file'}
            <div class="filter-info">
                Showing: {filters.join(', ')}
            </div>
        {/if}
    </div>

    {#snippet footer()}
        <button class="btn btn-secondary" onclick={onCancel}>
            Cancel
        </button>
        <button
            class="btn btn-primary"
            onclick={handleSelect}
            disabled={!canSelect}
        >
            {mode === 'directory' ? 'Select Folder' : 'Select'}
        </button>
    {/snippet}
</Modal>

<style>
    .file-browser {
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        min-height: 400px;
    }

    .path-bar {
        display: flex;
        gap: var(--space-2);
        align-items: center;
    }

    .nav-btn {
        padding: var(--space-2);
        background: var(--bg-hover);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .nav-btn:hover:not(:disabled) {
        background: var(--bg-active);
    }

    .nav-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }

    .nav-btn svg {
        width: 16px;
        height: 16px;
        color: var(--text-secondary);
    }

    .path-input {
        flex: 1;
        padding: var(--space-2) var(--space-3);
        background: var(--bg-primary);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        color: var(--text-primary);
        font-family: var(--font-mono);
        font-size: var(--text-sm);
    }

    .path-input:focus {
        outline: none;
        border-color: var(--accent);
    }

    .file-list {
        flex: 1;
        overflow-y: auto;
        background: var(--bg-primary);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        min-height: 300px;
    }

    .file-entry {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        background: transparent;
        border: none;
        border-bottom: 1px solid var(--border-subtle);
        color: var(--text-primary);
        cursor: pointer;
        text-align: left;
    }

    .file-entry:last-child {
        border-bottom: none;
    }

    .file-entry:hover {
        background: var(--bg-hover);
    }

    .file-entry.selected {
        background: var(--accent-muted);
    }

    .file-entry .icon {
        flex-shrink: 0;
        width: 20px;
        height: 20px;
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .file-entry .icon svg {
        width: 16px;
        height: 16px;
        color: var(--text-muted);
    }

    .file-entry.directory .icon svg {
        color: var(--accent);
    }

    .file-entry .name {
        flex: 1;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: var(--text-sm);
    }

    .file-entry .size {
        flex-shrink: 0;
        font-size: var(--text-xs);
        color: var(--text-muted);
        min-width: 60px;
        text-align: right;
    }

    .filter-info {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }
</style>
