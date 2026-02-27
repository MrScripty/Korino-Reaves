# Agent Scaffolding Remaining Plan

As of February 27, 2026, Phases 1 and 2 are complete. This document covers remaining execution work.

## Phase 3: Capability Plugins and Event Contract

### Objective

Expose new capabilities as explicit agent plugins and standardize event lifecycle for UI observability.

### Tasks

1. Add plugins in `godot/scripts/Agent/Plugins/`:
   - `ProjectPlugin`
   - `DependencyGraphPlugin`
   - `MetadataPlugin`
   - `GuiPlugin`
2. Register these plugins through `AgentManager` using `AgentCapabilityRegistry`.
3. Standardize agent events:
   - `agent:status`
   - `agent:step`
   - `agent:result`
   - `agent:error`
4. Update frontend bridge usage in `svelte-ui/src/lib/bridge/agent-api.ts` to subscribe by action.
5. Ensure responses carry correlation IDs for request/response tracking.

### Acceptance Criteria

- Agent can read project graph/metadata and issue selection operations through plugins only.
- UI receives consistent status/progress/result events per request ID.
- No direct capability calls from frontend; all through agent IPC.

## Phase 4: Guardrails, Policy, and Telemetry

### Objective

Constrain agent behavior and make operations auditable.

### Tasks

1. Add `AgentExecutionPolicy`:
   - read-only default
   - explicit enablement for side effects
   - max rows/depth/file traversal limits
2. Enforce policy checks inside capability-facing plugins.
3. Add structured telemetry tags:
   - capability
   - duration
   - requested vs bounded limits
   - result counts
   - cancellation/errors
4. Add cancellation propagation for long-running capability calls.
5. Add integration tests for complete path:
   - open project -> dependency scan -> metadata query -> GUI selection.

### Acceptance Criteria

- Side-effecting actions are blocked unless explicitly enabled.
- Large queries are bounded with deterministic behavior.
- Telemetry is sufficient for troubleshooting and audit.

## Phase 5: Rollout and Hardening

### Objective

Ship incrementally with low disruption to continued Korino development.

### Tasks

1. Add feature flag for agent runtime enablement.
2. Document operational prerequisites and failure modes.
3. Create smoke-test checklist for regressions in:
   - tree behavior
   - selection flow
   - dependency panel
   - property editing
4. Add follow-up backlog for:
   - richer metadata pagination APIs
   - plugin permission tiers
   - model/runtime health diagnostics.

### Acceptance Criteria

- Agent scaffolding can be toggled on/off safely.
- Regression checklist passes before enabling by default.
