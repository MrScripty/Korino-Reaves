# Scene Components

## Purpose
Components for scene/outliner-specific views.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `SceneOutliner.svelte` | Scene hierarchy/outliner UI. |

## Problem
Rendered scenes need a dedicated hierarchy view that is distinct from raw asset trees.

## Constraints
- Scene data is backend-owned.
- The component must coexist with viewport-focused workflows.

## Decision
Keep scene-specific rendering in its own directory even while the subsystem is small.

## Alternatives Rejected
- Reuse the raw asset tree UI unchanged: rejected because scene semantics are different.

## Invariants
- Scene hierarchy rendering remains separate from asset-tree rendering.
- The component consumes backend-driven scene state.

## Revisit Triggers
- Additional scene panels or editors are added.

## Dependencies
**Internal:** scene view model and layout/dock components.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a focused UI boundary.
- Revisit trigger: scene tooling expands materially.

## Usage Examples
```svelte
<SceneOutliner />
```

## API Consumer Contract
- Parent layouts host the outliner alongside scene/viewport state.
- Backend payload shape remains authoritative for scene hierarchy entries.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the component does not publish persisted structured artifacts.
- Revisit trigger: saved scene filters or outliner configs are added.
