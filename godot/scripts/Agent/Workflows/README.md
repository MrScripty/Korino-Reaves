# Agent Workflows

## Purpose
Predefined multi-step workflows that coordinate agent plugins for higher-level tasks.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `AssetExplorerWorkflow.cs` | Guided asset-inspection workflow. |
| `ModPortingWorkflow.cs` | Workflow for version-to-version mod migration support. |

## Problem
Some agent tasks need ordered multi-step orchestration rather than isolated tool calls.

## Constraints
- Workflows must stay explainable and bounded.
- They operate on top of the plugin/capability surface, not hidden state.

## Decision
Keep reusable workflow orchestration in dedicated classes separate from plugin definitions.

## Alternatives Rejected
- Embed workflow logic in prompts only: rejected because repeatable orchestration would be harder to test and review.

## Invariants
- Workflows use existing plugins/capabilities as their execution surface.
- Ordered multi-step behavior remains explicit in code.

## Revisit Triggers
- Workflow definitions become data-driven instead of code-driven.
- Additional workflows make a shared planning abstraction necessary.

## Dependencies
**Internal:** `godot/scripts/Agent/Plugins`, `godot/scripts/Agent/Capabilities`.
**External:** Semantic Kernel.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: current workflow choices are limited and local to the agent subsystem.
- Revisit trigger: workflow orchestration expands materially.

## Usage Examples
```csharp
var workflow = new ModPortingWorkflow(...);
```

## API Consumer Contract
- The agent manager is the primary consumer.
- Callers should treat workflows as ordered operations that may span multiple plugin calls.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: workflows are code-defined orchestration, not checked-in machine schemas.
- Revisit trigger: workflow definitions move into persisted templates or manifests.
