# Agent Plugins

## Purpose
Semantic Kernel plugin adapters that expose project capabilities to the local agent runtime.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `AssetPlugin.cs` | Asset-level load/export helpers. |
| `DependencyGraphPlugin.cs` | Dependency traversal and search helpers. |
| `DiffPlugin.cs` | Diff and conflict-analysis helpers. |
| `EditPlugin.cs` | Property read/write operations. |
| `GuiPlugin.cs` | GUI selection/expansion hooks. |
| `MetadataPlugin.cs` | Metadata summary helpers. |
| `ModelPlugin.cs` | Local model-library management hooks. |
| `NavigationPlugin.cs` | Project navigation helpers. |
| `ProjectPlugin.cs` | Project-scoped filesystem and selection helpers. |

## Problem
The agent needs a stable, bounded tool surface instead of direct access to internal services and handlers.

## Constraints
- Plugin methods are invoked by an LLM orchestration layer.
- Calls must stay scoped and auditable.

## Decision
Expose focused plugin classes that wrap existing capabilities and runtime services rather than letting the agent call arbitrary internals.

## Alternatives Rejected
- Direct handler access from the agent: rejected because it would couple prompt execution to transport details.

## Invariants
- Plugin methods remain bounded and side-effect aware.
- Plugins delegate to typed capabilities or existing runtime services.

## Revisit Triggers
- The agent framework changes away from Semantic Kernel plugins.
- Tool-call auditability requirements become stricter.

## Dependencies
**Internal:** `godot/scripts/Agent/Capabilities`, `godot/scripts/Bridge`, `godot/scripts/Data`.
**External:** Semantic Kernel.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: plugin boundaries are currently documented here instead of ADRs.
- Revisit trigger: multiple competing plugin boundary designs appear.

## Usage Examples
```csharp
kernel.Plugins.AddFromObject(new AssetPlugin(...));
```

## API Consumer Contract
- The agent runtime is the intended consumer.
- Plugin methods should accept validated, bounded inputs and return serializable outputs.
- Exceptions should surface as agent-visible failures rather than corrupting app state.

## Structured Producer Contract
- Plugin return payloads must remain serializable through the agent runtime.
- Any persisted tool schema or prompt contract should be added here when introduced.
