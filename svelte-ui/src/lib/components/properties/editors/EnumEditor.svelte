<!--
    EnumEditor Component

    Dropdown selector for enum property values.
-->
<script lang="ts">
    interface Props {
        value: string;
        options: string[];
        onSubmit: (value: string) => void;
    }

    let { value, options, onSubmit }: Props = $props();

    function handleChange(event: Event) {
        const target = event.target as HTMLSelectElement;
        onSubmit(target.value);
    }
</script>

<select class="enum-editor" {value} onchange={handleChange}>
    {#each options as option}
        <option {value}={option}>{option}</option>
    {/each}
</select>

<style>
    .enum-editor {
        width: 100%;
        padding: var(--space-1);
        background: var(--bg-tertiary);
        border: 1px solid var(--accent-primary);
        border-radius: var(--radius-sm);
        font: inherit;
        color: var(--color-enum);
        cursor: pointer;
    }

    .enum-editor:focus {
        outline: none;
    }

    .enum-editor option {
        background: var(--bg-secondary);
        color: var(--text-primary);
    }
</style>
