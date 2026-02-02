<!--
    VectorEditor Component

    Multi-field editor for vector property values (2D, 3D, 4D).
-->
<script lang="ts">
    interface VectorValue {
        x: number;
        y: number;
        z?: number;
        w?: number;
    }

    interface Props {
        value: VectorValue;
        onSubmit: (value: VectorValue) => void;
        onCancel: () => void;
    }

    let { value, onSubmit, onCancel }: Props = $props();

    // Determine dimension
    let dimension = $derived(
        'w' in value && value.w !== undefined ? 4 :
        'z' in value && value.z !== undefined ? 3 : 2
    );

    // Transient UI state - editing values before submit
    let x = $state(String(value.x));
    let y = $state(String(value.y));
    let z = $state(String(value.z ?? 0));
    let w = $state(String(value.w ?? 0));

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter') {
            event.preventDefault();
            submit();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            onCancel();
        }
    }

    function submit() {
        const result: VectorValue = {
            x: parseFloat(x) || 0,
            y: parseFloat(y) || 0,
        };
        if (dimension >= 3) result.z = parseFloat(z) || 0;
        if (dimension >= 4) result.w = parseFloat(w) || 0;
        onSubmit(result);
    }
</script>

<div class="vector-editor">
    <label class="vector-field">
        <span class="field-label">X</span>
        <input
            type="text"
            inputmode="decimal"
            class="field-input"
            bind:value={x}
            onkeydown={handleKeyDown}
        />
    </label>
    <label class="vector-field">
        <span class="field-label">Y</span>
        <input
            type="text"
            inputmode="decimal"
            class="field-input"
            bind:value={y}
            onkeydown={handleKeyDown}
        />
    </label>
    {#if dimension >= 3}
        <label class="vector-field">
            <span class="field-label">Z</span>
            <input
                type="text"
                inputmode="decimal"
                class="field-input"
                bind:value={z}
                onkeydown={handleKeyDown}
            />
        </label>
    {/if}
    {#if dimension >= 4}
        <label class="vector-field">
            <span class="field-label">W</span>
            <input
                type="text"
                inputmode="decimal"
                class="field-input"
                bind:value={w}
                onkeydown={handleKeyDown}
            />
        </label>
    {/if}
    <button class="submit-button" onclick={submit}>
        <svg viewBox="0 0 16 16" fill="currentColor">
            <path d="M13.854 3.646a.5.5 0 0 1 0 .708l-7 7a.5.5 0 0 1-.708 0l-3.5-3.5a.5.5 0 1 1 .708-.708L6.5 10.293l6.646-6.647a.5.5 0 0 1 .708 0z"/>
        </svg>
    </button>
</div>

<style>
    .vector-editor {
        display: flex;
        gap: var(--space-1);
        align-items: center;
    }

    .vector-field {
        display: flex;
        align-items: center;
        gap: 2px;
    }

    .field-label {
        font-size: var(--text-xs);
        color: var(--text-muted);
        width: 12px;
    }

    .field-input {
        width: 50px;
        padding: 2px 4px;
        background: var(--bg-tertiary);
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        font: inherit;
        font-size: var(--text-xs);
        color: var(--color-number);
    }

    .field-input:focus {
        border-color: var(--accent-primary);
        outline: none;
    }

    .submit-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 20px;
        height: 20px;
        padding: 0;
        background: var(--accent-primary);
        border: none;
        border-radius: var(--radius-sm);
        color: white;
        cursor: pointer;
    }

    .submit-button:hover {
        background: var(--accent-primary-hover);
    }

    .submit-button svg {
        width: 12px;
        height: 12px;
    }
</style>
