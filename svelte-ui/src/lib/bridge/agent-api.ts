/**
 * Agent API - Frontend interface for AI agent operations.
 *
 * Provides typed functions for communicating with the C# AgentHandler
 * via IPC. All operations forward to the backend; no business logic here.
 *
 * @module bridge/agent-api
 */

import { ipc } from './ipc';
import type {
  AgentErrorMessage,
  AgentMessage,
  AgentResultMessage,
  AgentStatus,
  AgentStepMessage,
} from './types';

// =============================================================================
// Agent State (received from C# backend)
// =============================================================================

/** Current agent status, updated from C# push messages. */
export let agentStatus = $state<AgentStatus>('idle');

/** Human-readable status message from the agent. */
export let agentMessage = $state<string>('');

/** Progress percentage (0-100), if determinable. */
export let agentProgress = $state<number>(0);

/** Current action being performed by the agent. */
export let currentAction = $state<string | null>(null);

// Subscribe to status updates from C# backend
ipc.onAction<AgentMessage>('agent', 'status', (data) => {
  agentStatus = data.status;
  agentMessage = data.message;
  agentProgress = data.progress ?? 0;
  currentAction = data.currentAction ?? null;
});

// Subscribe to incremental steps for the in-flight operation
ipc.onAction<AgentStepMessage>('agent', 'step', (data) => {
  currentAction = data.step;
  agentMessage = data.message;
  if (typeof data.progress === 'number') {
    agentProgress = data.progress;
  }
});

// Subscribe to final result payloads
ipc.onAction<AgentResultMessage>('agent', 'result', (data) => {
  agentStatus = data.status;
  agentMessage = data.message;
  currentAction = null;
  if (data.status === 'complete' || data.status === 'idle') {
    agentProgress = 100;
  }
});

// Subscribe to error payloads
ipc.onAction<AgentErrorMessage>('agent', 'error', (data) => {
  agentStatus = 'error';
  agentMessage = data.message;
  currentAction = null;
});

// =============================================================================
// Agent Actions (forwarded to C# backend)
// =============================================================================

/**
 * Execute a natural language prompt using the AI agent.
 * The agent can call any registered plugin function to fulfill the request.
 */
export function executePrompt(prompt: string): void {
  ipc.send({
    type: 'agent',
    action: 'execute',
    payload: { prompt },
  });
}

/**
 * Start the mod porting workflow.
 * Compares game versions and applies non-conflicting mod changes.
 */
export function portMod(
  originalPath: string,
  updatedPath: string,
  modPath: string,
  outputPath: string
): void {
  ipc.send({
    type: 'agent',
    action: 'portMod',
    payload: { originalPath, updatedPath, modPath, outputPath },
  });
}

/**
 * Explore an asset using AI-driven analysis.
 * The agent opens the asset and answers the question.
 */
export function exploreAsset(assetPath: string, question: string): void {
  ipc.send({
    type: 'agent',
    action: 'explore',
    payload: { assetPath, question },
  });
}

/**
 * Cancel the current agent operation.
 */
export function cancelAgent(): void {
  ipc.send({
    type: 'agent',
    action: 'cancel',
    payload: {},
  });
}

/**
 * Request the current agent status from the backend.
 */
export function requestStatus(): void {
  ipc.send({
    type: 'agent',
    action: 'getStatus',
    payload: {},
  });
}
