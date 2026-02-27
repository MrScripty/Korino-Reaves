# Agent Scaffolding Status

Date: February 27, 2026

## Completed So Far

### Planning

- Created scaffolding architecture plan:
  - `plans/07-agent-scaffolding.md`

### Phase 1 Implementation (Complete)

- Added capability contracts for:
  - project exploration
  - dependency graph traversal/search
  - metadata retrieval
  - GUI selection control
- Added shared capability DTO models (transport-agnostic).
- Added adapters that wrap existing systems:
  - `FileTreeBuilder`
  - `DependencyDatabase`
  - `ProjectHandler`
  - `SelectionHandler`
  - `IpcDispatcher` selection updates
- Added capability registry:
  - `godot/scripts/Agent/AgentCapabilityRegistry.cs`
- Extended `SelectionHandler` with public `SelectNode(string? id)` API to support capability-based selection mutation.
- Added capability README for discoverability and maintenance.

### Phase 2 Implementation (Complete)

- Added runtime bootstrap and context:
  - `godot/scripts/Agent/AgentRuntimeBootstrap.cs`
  - `godot/scripts/Agent/AgentRuntimeContext.cs`
- Added safe fallback model library when pumas-core is unavailable:
  - `godot/scripts/Agent/NoOpModelLibrary.cs`
- Integrated runtime wiring in `MainController.SetupDispatcher()`:
  - composes agent runtime context
  - registers `AgentHandler` in dispatcher
  - logs initialization state and capability availability
- Added cleanup disposal for agent runtime context in `MainController.Cleanup()`.

### Phase 3 Implementation (Complete)

- Added capability-backed Semantic Kernel plugins:
  - `ProjectPlugin.cs`
  - `DependencyGraphPlugin.cs`
  - `MetadataPlugin.cs`
  - `GuiPlugin.cs`
- Updated `AgentManager` plugin registration to include capability-backed plugins via `AgentCapabilityRegistry`.
- Standardized agent event lifecycle in `AgentHandler`:
  - `agent:status`
  - `agent:step`
  - `agent:result`
  - `agent:error`
- Added event emitter wiring from `AgentRuntimeBootstrap` to `AgentHandler` via dispatcher send callback.
- Updated frontend bridge listener wiring in `agent-api.ts` to subscribe with `ipc.onAction(...)` for status, step, result, and error actions.
- Extended TypeScript bridge contracts with:
  - `AgentStepMessage`
  - `AgentResultMessage`
  - `AgentErrorMessage`

### Tests Added

- Added unit tests for:
  - project explorer capability
  - dependency graph capability
  - metadata capability
  - GUI selection capability
- Updated test project includes to compile new capability sources.

### Validation Run

- `dotnet build godot/UAssetViewer.csproj` succeeded.
- `dotnet test godot/tests/UAssetViewer.Tests.csproj` blocked in sandbox due NuGet network access restrictions.

## Not Yet Completed

### Phase 4

- Execution policy enforcement (read-only default, side-effect gates).
- Telemetry instrumentation and cancellation propagation across capability calls.

### Phase 5

- Feature-flagged rollout, hardening checklist, and operational docs.

## References

- Remaining execution plan:
  - `plans/08-agent-scaffolding-remaining.md`
