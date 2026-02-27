# Agent Scaffolding Remaining Plan

As of February 27, 2026, Phases 1-3 are complete. This document covers remaining execution work.

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
