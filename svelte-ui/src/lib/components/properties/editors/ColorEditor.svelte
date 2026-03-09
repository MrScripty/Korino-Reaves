<!--
    ColorEditor Component

    Color picker for color property values.
-->
<script lang="ts">
    interface ColorValue {
        r: number;
        g: number;
        b: number;
        a?: number;
    }

    interface Props {
        value: ColorValue;
        onSubmit: (value: ColorValue) => void;
    }

    let { value, onSubmit }: Props = $props();

    // Convert 0-255 to hex
    function toHex(color: ColorValue): string {
        const r = Math.round(color.r).toString(16).padStart(2, '0');
        const g = Math.round(color.g).toString(16).padStart(2, '0');
        const b = Math.round(color.b).toString(16).padStart(2, '0');
        return `#${r}${g}${b}`;
    }

    // Convert hex to 0-255
    function fromHex(hex: string): ColorValue {
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return value.a === undefined
            ? { r, g, b }
            : { r, g, b, a: value.a };
    }

    let hexValue = $state('');
    let hasAlpha = $derived('a' in value && value.a !== undefined);
    let alpha = $state('');

    $effect(() => {
        hexValue = toHex(value);
        alpha = String(value.a ?? 1);
    });

    function handleColorChange(event: Event) {
        const target = event.target as HTMLInputElement;
        hexValue = target.value;
        submitColor();
    }

    function handleAlphaChange(event: Event) {
        const target = event.target as HTMLInputElement;
        alpha = target.value;
        submitColor();
    }

    function submitColor() {
        const color = fromHex(hexValue);
        if (hasAlpha) {
            color.a = parseFloat(alpha) || 1;
        }
        onSubmit(color);
    }
</script>

<div class="color-editor">
    <input
        type="color"
        class="color-picker"
        value={hexValue}
        oninput={handleColorChange}
    />
    <span class="color-value">{hexValue}</span>
    {#if hasAlpha}
        <label class="alpha-field">
            <span class="alpha-label">A</span>
            <input
                type="text"
                inputmode="decimal"
                class="alpha-input"
                value={alpha}
                oninput={handleAlphaChange}
            />
        </label>
    {/if}
</div>

<style>
    .color-editor {
        display: flex;
        align-items: center;
        gap: var(--space-2);
    }

    .color-picker {
        width: 24px;
        height: 24px;
        padding: 0;
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        cursor: pointer;
    }

    .color-picker::-webkit-color-swatch-wrapper {
        padding: 2px;
    }

    .color-picker::-webkit-color-swatch {
        border: none;
        border-radius: 2px;
    }

    .color-value {
        font-family: var(--font-mono);
        font-size: var(--text-sm);
        color: var(--text-secondary);
    }

    .alpha-field {
        display: flex;
        align-items: center;
        gap: 2px;
    }

    .alpha-label {
        font-size: var(--text-xs);
        color: var(--text-muted);
    }

    .alpha-input {
        width: 40px;
        padding: 2px 4px;
        background: var(--bg-tertiary);
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        font: inherit;
        font-size: var(--text-xs);
        color: var(--text-primary);
    }

    .alpha-input:focus {
        border-color: var(--accent-primary);
        outline: none;
    }
</style>
