# Test Stubs

## Purpose
Reusable test doubles for Godot-dependent or dispatcher-dependent code paths.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DispatcherHandlerStubs.cs` | Stub message handlers for dispatcher tests. |
| `GodotStubs.cs` | Minimal Godot-facing stand-ins for unit tests. |

## Problem
Many runtime units depend on Godot or IPC infrastructure that is awkward to construct directly in tests.

## Constraints
- Stubs should stay lightweight and test-oriented.
- They must not become alternate production implementations.

## Decision
Keep shared test doubles in one directory reused across the C# test suite.

## Alternatives Rejected
- Inline duplicate stubs in every test file: rejected because drift would be likely.

## Invariants
- Stub behavior stays narrow and explicit.
- Production code does not take a dependency on this directory.

## Revisit Triggers
- Shared test factories or builders replace the current stubs.

## Dependencies
**Internal:** `godot/tests`.
**External:** None beyond the test project.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: these are test-only helpers.
- Revisit trigger: test infrastructure becomes large enough to justify ADR coverage.

## Usage Examples
```csharp
var handler = new StubMessageHandler(...);
```

## API Consumer Contract
- Only tests should consume these helpers.
- Behavior should stay intentionally limited to the scenarios under test.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the directory does not publish structured artifacts.
- Revisit trigger: shared generated fixtures or schemas are added.
