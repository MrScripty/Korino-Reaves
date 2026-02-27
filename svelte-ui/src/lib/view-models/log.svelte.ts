/**
 * Log View Model
 *
 * Holds application log entries pushed from C#.
 * ALL data is owned by C# — this is just a presentation layer cache.
 */

import { ipc } from '$lib/bridge/ipc';
import type { LogEntry } from '$lib/bridge/types';

const MAX_LOG_ENTRIES = 1000;

class LogVM {
    entries = $state<LogEntry[]>([]);

    get entryCount(): number {
        return this.entries.length;
    }

    clear(): void {
        this.entries = [];
    }
}

export const log = new LogVM();

// =============================================================================
// IPC Listeners
// =============================================================================

ipc.onAction<LogEntry>('log', 'entry', (payload) => {
    if (log.entries.length >= MAX_LOG_ENTRIES) {
        log.entries = [...log.entries.slice(-MAX_LOG_ENTRIES + 1), payload];
    } else {
        log.entries = [...log.entries, payload];
    }
});

ipc.onAction('project', 'closed', () => {
    log.entries = [];
});
