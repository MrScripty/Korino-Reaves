<!--
    Tabs Component

    Tab navigation with panels.
-->
<script lang="ts">
    import type { Snippet } from 'svelte';

    interface Tab {
        id: string;
        label: string;
        disabled?: boolean;
    }

    interface Props {
        /** Available tabs */
        tabs: Tab[];
        /** Currently active tab ID */
        activeTab?: string;
        /** Callback when tab changes */
        onTabChange?: (tabId: string) => void;
        /** Tab panel content (receives active tab ID) */
        children: Snippet<[string]>;
    }

    let {
        tabs,
        activeTab = $bindable(tabs[0]?.id ?? ''),
        onTabChange,
        children,
    }: Props = $props();

    function selectTab(tabId: string) {
        const tab = tabs.find((t) => t.id === tabId);
        if (tab && !tab.disabled) {
            activeTab = tabId;
            onTabChange?.(tabId);
        }
    }

    function handleKeyDown(event: KeyboardEvent, currentIndex: number) {
        let newIndex = currentIndex;

        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            newIndex = currentIndex > 0 ? currentIndex - 1 : tabs.length - 1;
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            newIndex = currentIndex < tabs.length - 1 ? currentIndex + 1 : 0;
        } else if (event.key === 'Home') {
            event.preventDefault();
            newIndex = 0;
        } else if (event.key === 'End') {
            event.preventDefault();
            newIndex = tabs.length - 1;
        }

        if (newIndex !== currentIndex) {
            // Skip disabled tabs
            while (tabs[newIndex]?.disabled) {
                newIndex = event.key === 'ArrowLeft' || event.key === 'Home'
                    ? (newIndex > 0 ? newIndex - 1 : tabs.length - 1)
                    : (newIndex < tabs.length - 1 ? newIndex + 1 : 0);
                if (newIndex === currentIndex) break; // All tabs disabled
            }
            const nextTab = tabs[newIndex];
            if (nextTab) {
                selectTab(nextTab.id);
            }
        }
    }
</script>

<div class="tabs-container">
    <div class="tabs-list" role="tablist">
        {#each tabs as tab, index}
            <button
                class="tab"
                class:active={activeTab === tab.id}
                class:disabled={tab.disabled}
                role="tab"
                id="tab-{tab.id}"
                aria-selected={activeTab === tab.id}
                aria-controls="panel-{tab.id}"
                aria-disabled={tab.disabled}
                tabindex={activeTab === tab.id ? 0 : -1}
                onclick={() => selectTab(tab.id)}
                onkeydown={(e) => handleKeyDown(e, index)}
            >
                {tab.label}
            </button>
        {/each}
    </div>

    <div
        class="tab-panel"
        role="tabpanel"
        id="panel-{activeTab}"
        aria-labelledby="tab-{activeTab}"
    >
        {@render children(activeTab)}
    </div>
</div>

<style>
    .tabs-container {
        display: flex;
        flex-direction: column;
        height: 100%;
    }

    .tabs-list {
        display: flex;
        gap: var(--space-1);
        padding: var(--space-1);
        background: var(--bg-secondary);
        border-bottom: 1px solid var(--border);
    }

    .tab {
        padding: var(--space-1) var(--space-3);
        font-size: var(--text-sm);
        border-radius: var(--radius-md) var(--radius-md) 0 0;
        border: 1px solid transparent;
        border-bottom: none;
        background: transparent;
        color: var(--text-secondary);
        cursor: pointer;
        transition: all var(--transition-fast);
    }

    .tab:hover:not(.disabled) {
        background: var(--bg-hover);
        color: var(--text-primary);
    }

    .tab.active {
        background: var(--bg-tertiary);
        border-color: var(--border);
        color: var(--text-primary);
    }

    .tab.disabled {
        color: var(--text-disabled);
        cursor: not-allowed;
    }

    .tab-panel {
        flex: 1;
        overflow: auto;
    }
</style>
