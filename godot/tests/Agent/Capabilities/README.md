# Capability Tests

## Purpose
Focused tests for the agent capability boundary adapters and contracts.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DependencyGraphCapabilityTests.cs` | Dependency graph queries and bounds. |
| `GuiSelectionCapabilityTests.cs` | GUI selection adapter behavior. |
| `MetadataCapabilityTests.cs` | Metadata capability coverage. |
| `ProjectExplorerCapabilityTests.cs` | Project exploration capability coverage. |

## Problem
Capabilities are the stable tool surface for the agent and must remain bounded and predictable.

## Constraints
- Capability tests should validate behavior without needing live UI or model inference.

## Decision
Test capability adapters directly with focused cases that enforce their contract boundaries.

## Alternatives Rejected
- Cover capabilities only through higher-level agent tests: rejected because failures would be harder to localize.

## Invariants
- Capability methods remain bounded and deterministic under test.
- Tests stay aligned with the handwritten capability contracts.

## Revisit Triggers
- New capability families appear or shared capability fixtures become necessary.

## Dependencies
**Internal:** `godot/scripts/Agent/Capabilities`.
**External:** .NET test runner.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: capability tests are an implementation-level verification surface.
- Revisit trigger: capability versioning becomes explicit.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --filter Capabilities
```

## API Consumer Contract
- The test runner is the intended consumer.
- Tests should keep capability behavior reviewable and isolated.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: no persisted structured outputs are produced here.
- Revisit trigger: snapshot-based capability fixtures are added.
