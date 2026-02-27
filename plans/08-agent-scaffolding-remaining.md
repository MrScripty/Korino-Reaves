# Agent Scaffolding Remaining Plan

As of February 27, 2026, Phases 1-3 are complete. This document covers remaining execution work.

## Phase 4: Guardrails, Policy, and Telemetry (In Progress)

### Objective

Constrain agent behavior and make operations auditable.

### Tasks

1. Completed:
   - Added `AgentExecutionPolicy` with read-only defaults.
   - Added explicit side-effect enablement hooks for future write rollout.
   - Added policy-driven max rows/depth/search bounds.
2. Completed:
   - Enforced policy checks in side-effecting plugins (`Asset`, `Edit`, `Model`).
   - Added policy gating for GUI mutation controls (`Gui`), enabled by default.
3. Completed:
   - Added capability telemetry logging:
   - capability
   - duration
   - requested vs bounded limits
   - result counts
   - cancellation/errors
4. Completed:
   - Added cancellation token propagation across capability and dependency data-access calls.
5. Remaining:
   - Add integration tests for complete path:
   - open project -> dependency scan -> metadata query -> GUI selection.

### Acceptance Criteria

- Side-effecting actions are blocked unless explicitly enabled.
- Large queries are bounded with deterministic behavior.
- Telemetry is sufficient for troubleshooting and audit.
- End-to-end integration path test still pending.

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
