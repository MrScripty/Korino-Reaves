# Diff Tests

## Purpose
Tests for diff-engine behavior and conflict detection support.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DiffEngineTests.cs` | Core diff-engine behavior coverage. |

## Problem
Diff behavior is central to mod migration workflows and needs predictable conflict analysis.

## Constraints
- Tests must remain understandable without requiring full frontend flows.

## Decision
Keep diff tests isolated so comparison regressions are easy to identify.

## Alternatives Rejected
- Merge diff coverage into generic asset tests: rejected because diff semantics are a separate responsibility.

## Invariants
- Diff behavior remains testable independently of UI transport.
- Conflict semantics stay explicit in the suite.

## Revisit Triggers
- Additional diff subsystems require their own test partitions.

## Dependencies
**Internal:** `godot/scripts/Diff`.
**External:** .NET test runner.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a narrow verification surface.
- Revisit trigger: diff workflows gain incompatible execution modes.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --filter Diff
```

## API Consumer Contract
- The .NET test runner is the intended consumer.
- Tests should continue to validate diff semantics without UI dependencies.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: no structured artifacts are produced here.
- Revisit trigger: snapshot diff fixtures are introduced.
