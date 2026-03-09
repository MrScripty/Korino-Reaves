# Agent Tests

## Purpose
Tests for the agent runtime shell and its orchestration boundaries.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `AgentExecutionPolicyTests.cs` | Agent execution policy and control-flow tests. |
| `AgentScaffoldingIntegrationTests.cs` | Higher-level runtime integration checks. |
| `Capabilities/` | Capability-specific tests. |

## Problem
Agent orchestration is easy to regress because it coordinates multiple services and execution policies.

## Constraints
- Tests should not require live model inference to validate structural behavior.

## Decision
Keep agent runtime tests separate from capability implementation tests.

## Alternatives Rejected
- Fold all agent tests into a single file: rejected because runtime policy and capability behavior change at different rates.

## Invariants
- Agent policy tests stay isolated from heavy external dependencies.
- Capability-focused cases live under `Capabilities/`.

## Revisit Triggers
- The agent runtime is split into multiple packages or projects.

## Dependencies
**Internal:** `godot/scripts/Agent`.
**External:** .NET test runner.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: current agent test organization is narrow in scope.
- Revisit trigger: the agent subsystem grows materially.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --filter Agent
```

## API Consumer Contract
- The .NET test runner is the intended consumer.
- Tests should remain deterministic and runnable without interactive UI state.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: these tests do not publish structured artifacts.
- Revisit trigger: snapshot fixtures or generated tool schemas are added.
