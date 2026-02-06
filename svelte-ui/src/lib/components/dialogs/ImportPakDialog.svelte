<!--
    Import PAK Dialog

    Modal dialog for entering a project name and selecting game version
    before importing a PAK file.
-->
<script lang="ts">
    import Modal from '$lib/components/common/Modal.svelte';
    import { pak } from '$lib/view-models/pak.svelte';
    import { project } from '$lib/view-models/project.svelte';
    import type { GameVersionEntry } from '$lib/bridge/types';

    let projectName = $state('');
    let inputRef = $state<HTMLInputElement | null>(null);
    let gameSearch = $state('');

    // Fetch game versions when dialog opens
    $effect(() => {
        if (pak.showDialog) {
            project.fetchGameVersions();
        }
    });

    // Focus input when dialog opens
    $effect(() => {
        if (pak.showDialog && inputRef) {
            inputRef.focus();
        }
    });

    // Filter and group game versions by search term
    let filteredVersions = $derived.by(() => {
        const search = gameSearch.toLowerCase().trim();
        if (!search) return project.gameVersions;
        return project.gameVersions.filter(
            (v) =>
                v.label.toLowerCase().includes(search) ||
                v.value.toLowerCase().includes(search) ||
                v.group.toLowerCase().includes(search),
        );
    });

    // Group filtered versions by UE version
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

    // Validate on input change (debounced)
    let validateTimeout: ReturnType<typeof setTimeout>;
    function handleInput() {
        clearTimeout(validateTimeout);
        // Local validation first
        if (!projectName) {
            pak.projectNameError = 'Project name is required';
            return;
        }
        const validPattern = /^[a-zA-Z0-9_-]+$/;
        if (!validPattern.test(projectName)) {
            pak.projectNameError = 'Only letters, numbers, underscores, and hyphens allowed';
            return;
        }
        pak.projectNameError = null;
        // Debounce backend validation
        validateTimeout = setTimeout(() => {
            pak.validateName(projectName);
        }, 300);
    }

    function selectGameVersion(value: string) {
        pak.selectedGameVersion = value;
    }

    function handleImport() {
        if (!projectName || pak.projectNameError) return;
        pak.startImport(projectName);
        projectName = '';
        gameSearch = '';
    }

    function handleClose() {
        pak.closeDialog();
        projectName = '';
        gameSearch = '';
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter' && projectName && !pak.projectNameError) {
            handleImport();
        }
    }

    // Get just the filename from the path
    const pakFileName = $derived(() => {
        if (!pak.pendingPakPath) return '';
        return pak.pendingPakPath.split('/').pop() || pak.pendingPakPath;
    });
</script>

<Modal
    title="Import PAK Archive"
    open={pak.showDialog}
    onClose={handleClose}
    width="560px"
>
    <div class="dialog-content">
        <p class="pak-info">
            <span class="label">File:</span>
            <span class="value">{pakFileName()}</span>
        </p>

        <div class="form-group">
            <label for="project-name">Project Name</label>
            <input
                bind:this={inputRef}
                id="project-name"
                type="text"
                bind:value={projectName}
                oninput={handleInput}
                onkeydown={handleKeyDown}
                placeholder="MyProject"
                class:error={pak.projectNameError}
                autocomplete="off"
                spellcheck="false"
            />
            {#if pak.projectNameError}
                <p class="error-message">{pak.projectNameError}</p>
            {/if}
            <p class="help-text">
                Only letters, numbers, underscores, and hyphens allowed.
            </p>
        </div>

        <!-- Game Version Selector -->
        <div class="form-group">
            <label for="game-search">Game Version</label>
            <input
                id="game-search"
                type="text"
                bind:value={gameSearch}
                placeholder="Search games..."
                autocomplete="off"
                spellcheck="false"
            />

            <div class="game-list">
                <!-- Auto-detect option -->
                <button
                    class="game-option"
                    class:selected={pak.selectedGameVersion === 'AUTO'}
                    onclick={() => selectGameVersion('AUTO')}
                >
                    <span class="radio" class:checked={pak.selectedGameVersion === 'AUTO'}></span>
                    <span class="game-label">Auto-Detect</span>
                    <span class="game-hint">Detect UE version from file headers</span>
                </button>

                {#each [...groupedVersions] as [group, entries]}
                    <div class="game-group-header">{group}</div>
                    {#each entries as entry}
                        <button
                            class="game-option"
                            class:selected={pak.selectedGameVersion === entry.value}
                            onclick={() => selectGameVersion(entry.value)}
                        >
                            <span class="radio" class:checked={pak.selectedGameVersion === entry.value}></span>
                            <span class="game-label">{entry.label}</span>
                        </button>
                    {/each}
                {/each}

                {#if filteredVersions.length === 0 && gameSearch}
                    <div class="game-empty">No games match "{gameSearch}"</div>
                {/if}
            </div>
        </div>

        {#if projectName && !pak.projectNameError}
            <div class="preview">
                <span class="label">Output:</span>
                <code>./projects/{projectName}/UE_data/</code>
            </div>
        {/if}
    </div>

    {#snippet footer()}
        <button class="btn btn-secondary" onclick={handleClose}>
            Cancel
        </button>
        <button
            class="btn btn-primary"
            onclick={handleImport}
            disabled={!projectName || !!pak.projectNameError || pak.isValidating}
        >
            {pak.isValidating ? 'Checking...' : 'Import'}
        </button>
    {/snippet}
</Modal>

<style>
    .dialog-content {
        display: flex;
        flex-direction: column;
        gap: var(--space-4);
    }

    .pak-info {
        display: flex;
        gap: var(--space-2);
        color: var(--text-secondary);
        font-size: var(--text-sm);
        margin: 0;
    }

    .pak-info .label {
        color: var(--text-muted);
    }

    .pak-info .value {
        color: var(--text-primary);
        font-family: var(--font-mono);
        word-break: break-all;
    }

    .form-group {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
    }

    .form-group label {
        font-size: var(--text-sm);
        font-weight: 500;
        color: var(--text-primary);
    }

    .form-group input {
        padding: var(--space-2) var(--space-3);
        font-size: var(--text-base);
        background: var(--bg-primary);
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        color: var(--text-primary);
        outline: none;
        transition: border-color 150ms, box-shadow 150ms;
    }

    .form-group input:focus {
        border-color: var(--accent);
        box-shadow: 0 0 0 2px rgba(var(--accent-rgb), 0.2);
    }

    .form-group input.error {
        border-color: var(--error);
    }

    .error-message {
        margin: 0;
        font-size: var(--text-sm);
        color: var(--error);
    }

    .help-text {
        margin: 0;
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    /* Game version list */
    .game-list {
        max-height: 240px;
        overflow-y: auto;
        border: 1px solid var(--border);
        border-radius: var(--radius-md);
        background: var(--bg-primary);
    }

    .game-group-header {
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

    .game-option {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-1) var(--space-3);
        font-size: var(--text-sm);
        color: var(--text-primary);
        background: transparent;
        border: none;
        cursor: pointer;
        text-align: left;
    }

    .game-option:hover {
        background: var(--bg-hover);
    }

    .game-option.selected {
        background: rgba(var(--accent-rgb), 0.1);
    }

    .radio {
        flex-shrink: 0;
        width: 14px;
        height: 14px;
        border: 2px solid var(--border-strong, var(--border));
        border-radius: 50%;
        transition: border-color 150ms, background 150ms;
    }

    .radio.checked {
        border-color: var(--accent);
        background: var(--accent);
        box-shadow: inset 0 0 0 2px var(--bg-primary);
    }

    .game-label {
        flex: 1;
    }

    .game-hint {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    .game-empty {
        padding: var(--space-4);
        text-align: center;
        color: var(--text-muted);
        font-size: var(--text-sm);
    }

    .preview {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-3);
        background: var(--bg-primary);
        border-radius: var(--radius-md);
        font-size: var(--text-sm);
    }

    .preview .label {
        color: var(--text-muted);
    }

    .preview code {
        font-family: var(--font-mono);
        color: var(--accent);
    }

    .btn {
        padding: var(--space-2) var(--space-4);
        font-size: var(--text-sm);
        font-weight: 500;
        border-radius: var(--radius-md);
        cursor: pointer;
        transition: background 150ms, opacity 150ms;
    }

    .btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }

    .btn-secondary {
        background: var(--bg-hover);
        border: 1px solid var(--border);
        color: var(--text-primary);
    }

    .btn-secondary:hover:not(:disabled) {
        background: var(--bg-active);
    }

    .btn-primary {
        background: var(--accent);
        border: 1px solid transparent;
        color: white;
    }

    .btn-primary:hover:not(:disabled) {
        opacity: 0.9;
    }
</style>
