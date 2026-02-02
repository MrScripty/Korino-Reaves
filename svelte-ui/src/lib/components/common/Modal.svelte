<!--
    Modal Component

    Accessible modal dialog with backdrop, header, content, and footer.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Props {
        /** Modal title */
        title: string;
        /** Whether the modal is open */
        open: boolean;
        /** Callback when modal is closed */
        onClose: () => void;
        /** Modal content */
        children: Snippet;
        /** Optional footer content (usually buttons) */
        footer?: Snippet;
        /** Modal width (default: 480px) */
        width?: string;
    }

    let {
        title,
        open = $bindable(false),
        onClose,
        children,
        footer,
        width = '480px',
    }: Props = $props();

    let dialogRef = $state<HTMLDivElement | null>(null);

    // Focus trap
    $effect(() => {
        if (open && dialogRef) {
            // Focus the dialog when opened
            dialogRef.focus();
            // Prevent body scroll
            document.body.style.overflow = 'hidden';
        } else {
            document.body.style.overflow = '';
        }

        return () => {
            document.body.style.overflow = '';
        };
    });

    function handleBackdropClick(event: MouseEvent) {
        if (event.target === event.currentTarget) {
            onClose();
        }
    }

    function handleKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            onClose();
        }
    }
</script>

{#if open}
    <div
        class="modal-backdrop"
        role="presentation"
        onclick={handleBackdropClick}
        onkeydown={handleKeyDown}
    >
        <div
            bind:this={dialogRef}
            class="modal"
            style="max-width: {width}"
            role="dialog"
            aria-modal="true"
            aria-labelledby="modal-title"
            tabindex="-1"
        >
            <header class="modal-header">
                <h2 id="modal-title" class="modal-title">{title}</h2>
                <button
                    class="close-button"
                    onclick={onClose}
                    aria-label="Close modal"
                >
                    <svg viewBox="0 0 16 16" fill="currentColor">
                        <path d="M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z"/>
                    </svg>
                </button>
            </header>

            <div class="modal-body">
                {@render children()}
            </div>

            {#if footer}
                <footer class="modal-footer">
                    {@render footer()}
                </footer>
            {/if}
        </div>
    </div>
{/if}

<style>
    .modal-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.6);
        display: flex;
        align-items: center;
        justify-content: center;
        z-index: var(--z-modal);
        animation: fadeIn 150ms ease-out;
    }

    @keyframes fadeIn {
        from { opacity: 0; }
        to { opacity: 1; }
    }

    .modal {
        width: 100%;
        background: var(--bg-secondary);
        border: 1px solid var(--border);
        border-radius: var(--radius-xl);
        box-shadow: var(--shadow-lg);
        max-height: 90vh;
        display: flex;
        flex-direction: column;
        outline: none;
        animation: slideIn 150ms ease-out;
    }

    @keyframes slideIn {
        from {
            opacity: 0;
            transform: translateY(-20px);
        }
        to {
            opacity: 1;
            transform: translateY(0);
        }
    }

    .modal-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-4);
        border-bottom: 1px solid var(--border);
    }

    .modal-title {
        margin: 0;
        font-size: var(--text-lg);
        font-weight: 600;
        color: var(--text-primary);
    }

    .close-button {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        padding: 0;
        background: transparent;
        border: none;
        border-radius: var(--radius-md);
        color: var(--text-secondary);
        cursor: pointer;
    }

    .close-button:hover {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .close-button svg {
        width: 16px;
        height: 16px;
    }

    .modal-body {
        flex: 1;
        padding: var(--space-4);
        overflow: auto;
    }

    .modal-footer {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-2);
        padding: var(--space-4);
        border-top: 1px solid var(--border);
    }
</style>
