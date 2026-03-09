# Godot Tests

## Purpose
Automated tests for the Godot/C# runtime, IPC boundaries, persistence helpers, and agent capability layer.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `Assets/` | Asset and mappings tests. |
| `Diff/` | Diff engine tests. |
| `Infrastructure/` | Boundary validation and infrastructure tests. |
| `Agent/` | Agent runtime and capability tests. |
| `Stubs/` | Test doubles for Godot and dispatcher behavior. |
| `IpcDispatcherTests.cs` | Dispatcher-level coverage for routing/validation behavior. |

## Problem
The runtime crosses UI, filesystem, parsing, and persistence boundaries. These tests keep those seams verifiable without requiring full manual Godot sessions.

## Constraints
- Tests run under `dotnet test`.
- Many production types interact with Godot APIs and need stubs or focused coverage.

## Decision
Keep unit and focused integration tests close to the C# runtime with dedicated subdirectories for major subsystems.

## Alternatives Rejected
- Rely on manual Godot smoke testing only: rejected because boundary regressions are too easy to miss.

## Invariants
- Tests remain runnable through the canonical .NET test command.
- Test doubles stay isolated under `Stubs/`.

## Revisit Triggers
- Godot integration requires a separate dedicated integration test harness.
- Test count or runtime growth forces further suite partitioning.

## Dependencies
**Internal:** `godot/scripts/`.
**External:** .NET test SDK and assertion libraries transitively referenced by the test project.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: the current test layout is straightforward and local to the runtime.
- Revisit trigger: a second test harness or runner is introduced.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --no-restore
```

## API Consumer Contract
- CI, hooks, and local operators consume this suite through `dotnet test` or `./launcher.sh --test`.
- Tests should remain deterministic and non-interactive.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: this directory consumes runtime contracts but does not publish persisted machine artifacts.
- Revisit trigger: checked-in test fixtures or generated snapshots are added here.
