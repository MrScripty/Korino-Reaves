# Infrastructure Tests

## Purpose
Tests for infrastructure helpers and boundary validation behavior.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `BoundaryValidationTests.cs` | Input/path validation coverage for trust boundaries. |

## Problem
Infrastructure helpers guard the runtime boundary and need explicit regression coverage.

## Constraints
- These tests should stay small and deterministic.

## Decision
Keep infrastructure boundary tests isolated from higher-level subsystem tests.

## Alternatives Rejected
- Verify boundary logic only through end-to-end flows: rejected because failures would be harder to localize.

## Invariants
- Validation behavior remains explicitly tested.
- Tests stay free of unnecessary Godot runtime coupling.

## Revisit Triggers
- More infrastructure helpers need dedicated test partitions.

## Dependencies
**Internal:** `godot/scripts/Infrastructure`.
**External:** .NET test runner.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a focused verification boundary.
- Revisit trigger: infrastructure policy becomes broad enough for ADR coverage.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --filter Infrastructure
```

## API Consumer Contract
- The test runner is the intended consumer.
- Tests should remain deterministic and boundary-focused.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: this directory does not publish structured outputs.
- Revisit trigger: generated infrastructure fixtures are introduced.
