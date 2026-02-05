/**
 * IPC Bridge
 *
 * Handles all communication between the Svelte frontend and C# backend.
 * Uses console.log interception for JS→C# and window.__UASSET_RECV__ for C#→JS.
 *
 * IMPORTANT: This is a pure communication layer. No business logic here.
 */

import {
    type IpcMessage,
    type MessageType,
    IPC_PREFIX,
    IPC_RECEIVER,
} from './types';
import { IPC } from '$lib/constants';

// =============================================================================
// Types
// =============================================================================

/** Handler function signature for IPC messages */
export type MessageHandler<T = unknown> = (payload: T) => void;

/** Unsubscribe function returned when registering a handler */
export type Unsubscribe = () => void;

/** Pending request for request/response correlation */
interface PendingRequest {
    resolve: (payload: unknown) => void;
    reject: (error: Error) => void;
    timeout: ReturnType<typeof setTimeout>;
}

// =============================================================================
// IPC Bridge Implementation
// =============================================================================

class IpcBridge {
    /** Handlers registered for each message type */
    private handlers = new Map<MessageType, Set<MessageHandler>>();

    /** Action-specific handlers (type:action as key) */
    private actionHandlers = new Map<string, Set<MessageHandler>>();

    /** Pending requests awaiting responses */
    private pendingRequests = new Map<string, PendingRequest>();

    /** Counter for generating unique request IDs */
    private requestIdCounter = 0;

    /** Whether mock mode is enabled (for development without backend) */
    private mockMode = false;

    /** Mock handlers for development */
    private mockHandlers = new Map<string, (msg: IpcMessage) => unknown>();

    /** Whether to log all IPC messages */
    private debugMode = false;

    constructor() {
        // Expose receiver function for C# to call (only in browser)
        if (typeof window !== 'undefined') {
            this.setupReceiver();
        }
    }

    /**
     * Set up the global receiver function that C# calls to push data.
     */
    private setupReceiver(): void {
        const self = this;

        // Define the receiver on window
        (window as Record<string, unknown>)[IPC_RECEIVER] = function (
            json: string
        ): void {
            self.receive(json);
        };
    }

    /**
     * Send a message to the C# backend.
     *
     * @param message The message to send
     */
    send(message: Omit<IpcMessage, 'timestamp'>): void {
        // Skip sending during SSR
        if (typeof window === 'undefined') {
            return;
        }

        const fullMessage: IpcMessage = {
            ...message,
            timestamp: Date.now(),
        };

        if (this.debugMode) {
            console.debug('[IPC →]', fullMessage);
        }

        if (this.mockMode) {
            this.handleMockMessage(fullMessage);
            return;
        }

        // Send via console.log with IPC prefix
        // C# intercepts this via DisplayHandler.OnConsoleMessage
        console.log(IPC_PREFIX + JSON.stringify(fullMessage));
    }

    /**
     * Send a request and wait for a response with correlation.
     *
     * @param message The message to send (id will be added)
     * @returns Promise that resolves with the response payload
     */
    request<T = unknown>(
        message: Omit<IpcMessage, 'id' | 'timestamp'>
    ): Promise<T> {
        return new Promise((resolve, reject) => {
            const id = `req_${++this.requestIdCounter}_${Date.now()}`;

            // Set up timeout
            const timeout = setTimeout(() => {
                this.pendingRequests.delete(id);
                reject(
                    new Error(`IPC request timeout: ${message.type}:${message.action}`)
                );
            }, IPC.REQUEST_TIMEOUT);

            // Store pending request
            this.pendingRequests.set(id, {
                resolve: resolve as (payload: unknown) => void,
                reject,
                timeout,
            });

            // Send with ID
            this.send({ ...message, id });
        });
    }

    /**
     * Receive a message from the C# backend.
     * Called by C# via window.__UASSET_RECV__.
     *
     * @param json JSON string of the message
     */
    receive(json: string): void {
        let message: IpcMessage;

        try {
            message = JSON.parse(json) as IpcMessage;
        } catch (e) {
            console.error('[IPC] Failed to parse message:', json, e);
            return;
        }

        if (this.debugMode) {
            console.debug('[IPC ←]', message);
        }

        // Check if this is a response to a pending request
        if (message.id && this.pendingRequests.has(message.id)) {
            const pending = this.pendingRequests.get(message.id)!;
            this.pendingRequests.delete(message.id);
            clearTimeout(pending.timeout);

            if (message.type === 'error') {
                pending.reject(new Error(JSON.stringify(message.payload)));
            } else {
                pending.resolve(message.payload);
            }
            return;
        }

        // Dispatch to type handlers
        const typeHandlers = this.handlers.get(message.type);
        if (typeHandlers) {
            for (const handler of typeHandlers) {
                try {
                    handler(message.payload);
                } catch (e) {
                    console.error(
                        `[IPC] Handler error for ${message.type}:`,
                        e
                    );
                }
            }
        }

        // Dispatch to action-specific handlers
        const actionKey = `${message.type}:${message.action}`;
        const actionHandlersSet = this.actionHandlers.get(actionKey);
        if (actionHandlersSet) {
            for (const handler of actionHandlersSet) {
                try {
                    handler(message.payload);
                } catch (e) {
                    console.error(`[IPC] Handler error for ${actionKey}:`, e);
                }
            }
        }
    }

    /**
     * Register a handler for a message type.
     *
     * @param type Message type to handle
     * @param handler Function to call when message is received
     * @returns Unsubscribe function
     */
    on<T = unknown>(type: MessageType, handler: MessageHandler<T>): Unsubscribe {
        if (!this.handlers.has(type)) {
            this.handlers.set(type, new Set());
        }
        this.handlers.get(type)!.add(handler as MessageHandler);

        return () => {
            this.handlers.get(type)?.delete(handler as MessageHandler);
        };
    }

    /**
     * Register a handler for a specific type:action combination.
     *
     * @param type Message type
     * @param action Action name
     * @param handler Function to call when message is received
     * @returns Unsubscribe function
     */
    onAction<T = unknown>(
        type: MessageType,
        action: string,
        handler: MessageHandler<T>
    ): Unsubscribe {
        const key = `${type}:${action}`;
        if (!this.actionHandlers.has(key)) {
            this.actionHandlers.set(key, new Set());
        }
        this.actionHandlers.get(key)!.add(handler as MessageHandler);

        return () => {
            this.actionHandlers.get(key)?.delete(handler as MessageHandler);
        };
    }

    /**
     * Remove all handlers (useful for cleanup).
     */
    removeAllHandlers(): void {
        this.handlers.clear();
        this.actionHandlers.clear();
    }

    // =========================================================================
    // Development / Mock Mode
    // =========================================================================

    /**
     * Enable mock mode for development without C# backend.
     */
    enableMockMode(): void {
        this.mockMode = true;
        console.info('[IPC] Mock mode enabled');
    }

    /**
     * Disable mock mode.
     */
    disableMockMode(): void {
        this.mockMode = false;
        console.info('[IPC] Mock mode disabled');
    }

    /**
     * Check if mock mode is enabled.
     */
    isMockMode(): boolean {
        return this.mockMode;
    }

    /**
     * Register a mock handler for development.
     *
     * @param type Message type
     * @param action Action name
     * @param handler Function that returns the mock response payload
     */
    registerMockHandler(
        type: MessageType,
        action: string,
        handler: (msg: IpcMessage) => unknown
    ): void {
        this.mockHandlers.set(`${type}:${action}`, handler);
    }

    /**
     * Handle a message in mock mode.
     */
    private handleMockMessage(message: IpcMessage): void {
        const key = `${message.type}:${message.action}`;
        const mockHandler = this.mockHandlers.get(key);

        if (mockHandler) {
            // Simulate async response
            setTimeout(() => {
                try {
                    const response = mockHandler(message);
                    this.receive(
                        JSON.stringify({
                            type: message.type,
                            action: message.action,
                            payload: response,
                            id: message.id,
                            timestamp: Date.now(),
                        })
                    );
                } catch (e) {
                    this.receive(
                        JSON.stringify({
                            type: 'error',
                            action: 'mock_error',
                            payload: {
                                code: 'MOCK_ERROR',
                                message: String(e),
                            },
                            id: message.id,
                            timestamp: Date.now(),
                        })
                    );
                }
            }, 50);
        } else {
            console.warn(`[IPC Mock] No handler for ${key}`);
        }
    }

    // =========================================================================
    // Debug Mode
    // =========================================================================

    /**
     * Enable debug logging of all IPC messages.
     */
    enableDebugMode(): void {
        this.debugMode = true;
        console.info('[IPC] Debug mode enabled');
    }

    /**
     * Disable debug logging.
     */
    disableDebugMode(): void {
        this.debugMode = false;
    }
}

// =============================================================================
// Singleton Export
// =============================================================================

/**
 * Global IPC bridge instance.
 * Import this to send/receive messages with the C# backend.
 *
 * @example
 * ```typescript
 * import { ipc } from '$lib/bridge/ipc';
 *
 * // Send a message
 * ipc.send({ type: 'tree', action: 'select', payload: { id: 'node1' } });
 *
 * // Register a handler
 * const unsub = ipc.on('tree', (payload) => {
 *     console.log('Tree update:', payload);
 * });
 *
 * // Clean up
 * unsub();
 * ```
 */
export const ipc = new IpcBridge();

// Enable debug mode in development
if (typeof window !== 'undefined' && window.location.hostname === 'localhost') {
    ipc.enableDebugMode();
}
