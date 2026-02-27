# Agent Rollout Hardening Guide

Date: February 27, 2026

## Purpose

Define operational prerequisites, common failure modes, and a smoke-test checklist
for safe rollout of agent scaffolding in Korino.

## Runtime Prerequisites

1. Agent runtime flag:
   - `KORINO_AGENT_ENABLED=true` (default if unset).
2. Core app/runtime:
   - `godot/UAssetViewer.csproj` builds successfully.
   - IPC bridge initializes and `MainController` registers handlers.
3. Model runtime:
   - Local Ollama service available for live model execution.
   - If unavailable, agent must degrade safely (error response, no crash).
4. Dependency/metadata path:
   - Project can be opened.
   - Dependency scan database exists (or queries fail with clear message).
5. Policy safety:
   - Read-only defaults remain active unless explicitly overridden by env vars.

## Failure Modes

### Agent Disabled by Flag

- Trigger: `KORINO_AGENT_ENABLED=false`.
- Expected behavior:
  - `agent` handler still registered.
  - `execute`/workflow calls return deterministic unavailable message.
- Recovery:
  - Set `KORINO_AGENT_ENABLED=true` and restart app.

### Model Library Unavailable

- Trigger: pumas/ollama init failure.
- Expected behavior:
  - Runtime uses fallback model library where applicable.
  - Agent returns explicit initialization error instead of crashing.
- Recovery:
  - Verify local model runtime and restart app.

### Dependency Database Missing

- Trigger: metadata/dependency query before scan.
- Expected behavior:
  - Capability/plugin returns bounded empty result or clear error path.
  - No UI hang or process crash.
- Recovery:
  - Run project dependency scan, then retry query.

### Policy Violation on Side Effects

- Trigger: write/edit/download tool call in read-only mode.
- Expected behavior:
  - Policy violation exception path is returned to caller.
  - No unintended state mutation.
- Recovery:
  - Enable specific policy env var only when needed.

## Smoke-Test Checklist

Run this checklist before enabling agent runtime by default:

Automated subset:

- `./scripts/smoke-agent-rollout.sh`
- Optional full test attempt: `./scripts/smoke-agent-rollout.sh --with-main-tests`

1. Runtime toggle:
   - Start app with `KORINO_AGENT_ENABLED=false`; verify agent calls return disabled reason.
   - Start app with `KORINO_AGENT_ENABLED=true`; verify agent status lifecycle events emit.
2. Tree behavior:
   - Open project; verify tree renders and expand/collapse works.
   - Agent-driven select/expand updates UI selection state correctly.
3. Selection flow:
   - Manual selection still auto-loads assets and property panel updates.
   - Agent selection does not break manual selection updates.
4. Dependency panel:
   - Run dependency scan.
   - Query dependency graph stats/edges through agent capability path.
5. Property editing safety:
   - With read-only defaults, verify edit/write tools are blocked.
   - With explicit write flags, verify edit path works and remains auditable.
6. Metadata query bounds:
   - Request very large row limit; verify bounded response and telemetry reflects clamping.
7. Error resilience:
   - Stop model runtime; verify clear agent error response and app remains responsive.

## Follow-Up Backlog

1. Metadata pagination:
   - Add cursor/page-based metadata APIs for large assets.
2. Permission tiers:
   - Introduce per-plugin or per-tool permission profiles beyond binary enablement.
3. Runtime diagnostics:
   - Add model/runtime health endpoint and UI status surface.
4. Test infrastructure:
   - Repair main `godot/tests` compile drift so integration coverage runs in primary test suite.

## Execution Record

As of February 27, 2026:

- `./scripts/smoke-agent-rollout.sh --with-main-tests` passed.
- `dotnet test godot/tests/UAssetViewer.Tests.csproj` passed (`75/75`).
- `dotnet test /tmp/korino-agent-capability-tests/AgentCapabilityTests.csproj` passed (`1/1`).

Manual interactive UI checks from the checklist are still required before
default-on release.
