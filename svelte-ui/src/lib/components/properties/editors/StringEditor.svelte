<!--
    StringEditor Component

    Inline editor for string property values.
-->
<script lang="ts">
    interface Props {
        value: string;
        onSubmit: (value: string) => void;
        onCancel: () => void;
    }

    let { value, onSubmit, onCancel }: Props = $props();

    // Transient UI state - editing value before submit
    let editValue = $state('');
    let inputRef = $state<HTMLInputElement | null>(null);

    $effect(() => {
        editValue = value;
    });

    // Auto-focus on mount
    $effect(() => {
        inputRef?.focus();
        inputRef?.select();
    });

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter') {
            event.preventDefault();
            onSubmit(editValue);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            onCancel();
        }
    }

    function handleBlur() {
        // Submit on blur if value changed
        if (editValue !== value) {
            onSubmit(editValue);
        } else {
            onCancel();
        }
    }
</script>

<input
    bind:this={inputRef}
    type="text"
    class="string-editor"
    bind:value={editValue}
    onkeydown={handleKeyDown}
    onblur={handleBlur}
/>

<style>
    .string-editor {
        width: 100%;
        padding: var(--space-1);
        background: var(--bg-tertiary);
        border: 1px solid var(--accent-primary);
        border-radius: var(--radius-sm);
        font: inherit;
        color: var(--color-string);
    }

    .string-editor:focus {
        outline: none;
    }
</style>
