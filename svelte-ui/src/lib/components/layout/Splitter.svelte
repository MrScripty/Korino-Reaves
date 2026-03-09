<!--
    Splitter Component

    Draggable divider for resizing adjacent panels.
    Supports both horizontal and vertical orientations.
-->
<script lang="ts">
    import { LAYOUT } from '$lib/constants';

    interface Props {
        /** Splitter orientation */
        orientation?: 'horizontal' | 'vertical';
        /** Minimum size for the element before the splitter */
        minBefore?: number;
        /** Maximum size for the element before the splitter */
        maxBefore?: number;
        /** Minimum size for the element after the splitter */
        minAfter?: number;
        /** Maximum size for the element after the splitter */
        maxAfter?: number;
        /** Current position (pixels from start) */
        position?: number;
        /** Callback when position changes */
        onPositionChange?: (position: number) => void;
    }

    let {
        orientation = 'horizontal',
        minBefore = LAYOUT.PANEL_MIN_WIDTH,
        maxBefore = LAYOUT.PANEL_MAX_WIDTH,
        position = $bindable(LAYOUT.PANEL_DEFAULT_WIDTH),
        onPositionChange,
    }: Props = $props();

    // Transient UI state - OK for Svelte to own
    let isDragging = $state(false);
    let startPosition = $state(0);
    let startMousePosition = $state(0);

    function handleMouseDown(event: MouseEvent) {
        event.preventDefault();
        isDragging = true;
        startPosition = position;
        startMousePosition =
            orientation === 'horizontal' ? event.clientX : event.clientY;

        window.addEventListener('mousemove', handleMouseMove);
        window.addEventListener('mouseup', handleMouseUp);
    }

    function handleMouseMove(event: MouseEvent) {
        if (!isDragging) return;

        const currentMousePosition =
            orientation === 'horizontal' ? event.clientX : event.clientY;
        const delta = currentMousePosition - startMousePosition;
        let newPosition = startPosition + delta;

        // Clamp to min/max
        newPosition = Math.max(minBefore, newPosition);
        newPosition = Math.min(maxBefore, newPosition);

        position = newPosition;
        onPositionChange?.(newPosition);
    }

    function handleMouseUp() {
        isDragging = false;
        window.removeEventListener('mousemove', handleMouseMove);
        window.removeEventListener('mouseup', handleMouseUp);
    }

    function handleKeyDown(event: KeyboardEvent) {
        const step = event.shiftKey ? 50 : 10;
        let newPosition = position;

        if (orientation === 'horizontal') {
            if (event.key === 'ArrowLeft') {
                newPosition = Math.max(minBefore, position - step);
            } else if (event.key === 'ArrowRight') {
                newPosition = Math.min(maxBefore, position + step);
            }
        } else {
            if (event.key === 'ArrowUp') {
                newPosition = Math.max(minBefore, position - step);
            } else if (event.key === 'ArrowDown') {
                newPosition = Math.min(maxBefore, position + step);
            }
        }

        if (newPosition !== position) {
            event.preventDefault();
            position = newPosition;
            onPositionChange?.(newPosition);
        }
    }
</script>

<div
    class="splitter"
    class:horizontal={orientation === 'horizontal'}
    class:vertical={orientation === 'vertical'}
    class:dragging={isDragging}
    role="separator"
    aria-orientation={orientation}
    aria-valuenow={position}
    aria-valuemin={minBefore}
    aria-valuemax={maxBefore}
    tabindex="0"
    onmousedown={handleMouseDown}
    onkeydown={handleKeyDown}
>
    <div class="splitter-handle"></div>
</div>

<style>
    .splitter {
        flex-shrink: 0;
        position: relative;
        background: transparent;
        transition: background-color var(--transition-fast);
    }

    .splitter.horizontal {
        width: var(--splitter-size);
        cursor: col-resize;
    }

    .splitter.vertical {
        height: var(--splitter-size);
        cursor: row-resize;
    }

    .splitter:hover,
    .splitter:focus-visible {
        background: var(--accent-primary);
        outline: none;
    }

    .splitter.dragging {
        background: var(--accent-primary);
    }

    .splitter-handle {
        position: absolute;
        background: var(--border);
        opacity: 0;
        transition: opacity var(--transition-fast);
    }

    .splitter:hover .splitter-handle,
    .splitter:focus-visible .splitter-handle,
    .splitter.dragging .splitter-handle {
        opacity: 1;
    }

    .splitter.horizontal .splitter-handle {
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        width: 2px;
        height: 32px;
        border-radius: 1px;
    }

    .splitter.vertical .splitter-handle {
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        width: 32px;
        height: 2px;
        border-radius: 1px;
    }
</style>
