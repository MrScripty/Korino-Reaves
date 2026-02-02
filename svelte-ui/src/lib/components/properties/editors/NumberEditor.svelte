<!--
    NumberEditor Component

    Inline editor for numeric property values.
-->
<script lang="ts">
    interface Props {
        value: number;
        min?: number;
        max?: number;
        step?: number;
        onSubmit: (value: number) => void;
        onCancel: () => void;
    }

    let { value, min, max, step = 1, onSubmit, onCancel }: Props = $props();

    // Transient UI state - editing value before submit
    let editValue = $state(String(value));
    let inputRef = $state<HTMLInputElement | null>(null);
    let isValid = $derived(!isNaN(parseFloat(editValue)));

    // Auto-focus on mount
    $effect(() => {
        inputRef?.focus();
        inputRef?.select();
    });

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter') {
            event.preventDefault();
            submit();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            onCancel();
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            adjustValue(step);
        } else if (event.key === 'ArrowDown') {
            event.preventDefault();
            adjustValue(-step);
        }
    }

    function adjustValue(delta: number) {
        const current = parseFloat(editValue) || 0;
        let newValue = current + delta;

        if (min !== undefined) newValue = Math.max(min, newValue);
        if (max !== undefined) newValue = Math.min(max, newValue);

        editValue = String(newValue);
    }

    function submit() {
        if (!isValid) {
            onCancel();
            return;
        }

        let numValue = parseFloat(editValue);
        if (min !== undefined) numValue = Math.max(min, numValue);
        if (max !== undefined) numValue = Math.min(max, numValue);

        onSubmit(numValue);
    }

    function handleBlur() {
        if (isValid && parseFloat(editValue) !== value) {
            submit();
        } else {
            onCancel();
        }
    }
</script>

<input
    bind:this={inputRef}
    type="text"
    inputmode="decimal"
    class="number-editor"
    class:invalid={!isValid}
    bind:value={editValue}
    onkeydown={handleKeyDown}
    onblur={handleBlur}
/>

<style>
    .number-editor {
        width: 100%;
        padding: var(--space-1);
        background: var(--bg-tertiary);
        border: 1px solid var(--accent-primary);
        border-radius: var(--radius-sm);
        font: inherit;
        color: var(--color-number);
    }

    .number-editor:focus {
        outline: none;
    }

    .number-editor.invalid {
        border-color: var(--accent-error);
        color: var(--accent-error);
    }
</style>
