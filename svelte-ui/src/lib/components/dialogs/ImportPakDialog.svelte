<!--
    Import PAK Dialog

    Modal dialog for entering a project name before importing a PAK file.
    Validates the name and shows preview of output path.
-->
<script lang="ts">
    import Modal from '$lib/components/common/Modal.svelte';
    import { pak } from '$lib/view-models/pak.svelte';

    let projectName = $state('');
    let inputRef = $state<HTMLInputElement | null>(null);

    // Focus input when dialog opens
    $effect(() => {
        if (pak.showDialog && inputRef) {
            inputRef.focus();
        }
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

    function handleImport() {
        if (!projectName || pak.projectNameError) return;
        pak.startImport(projectName);
        projectName = '';
    }

    function handleClose() {
        pak.closeDialog();
        projectName = '';
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
    width="500px"
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
