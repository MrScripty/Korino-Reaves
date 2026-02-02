<!--
    BoolEditor Component

    Toggle switch for boolean property values.
-->
<script lang="ts">
    interface Props {
        value: boolean;
        onSubmit: (value: boolean) => void;
    }

    let { value, onSubmit }: Props = $props();

    function handleChange() {
        onSubmit(!value);
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            handleChange();
        }
    }
</script>

<button
    class="bool-editor"
    class:checked={value}
    role="switch"
    aria-checked={value}
    onclick={handleChange}
    onkeydown={handleKeyDown}
>
    <span class="toggle-track">
        <span class="toggle-thumb"></span>
    </span>
    <span class="toggle-label">{value ? 'true' : 'false'}</span>
</button>

<style>
    .bool-editor {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: 0;
        background: transparent;
        border: none;
        cursor: pointer;
        font: inherit;
        color: var(--color-bool);
    }

    .toggle-track {
        position: relative;
        width: 32px;
        height: 18px;
        background: var(--bg-tertiary);
        border: 1px solid var(--border);
        border-radius: 9px;
        transition: background-color var(--transition-fast);
    }

    .bool-editor.checked .toggle-track {
        background: var(--accent-success);
        border-color: var(--accent-success);
    }

    .toggle-thumb {
        position: absolute;
        top: 2px;
        left: 2px;
        width: 12px;
        height: 12px;
        background: var(--text-primary);
        border-radius: 50%;
        transition: transform var(--transition-fast);
    }

    .bool-editor.checked .toggle-thumb {
        transform: translateX(14px);
    }

    .toggle-label {
        font-family: var(--font-mono);
        font-size: var(--text-sm);
    }
</style>
