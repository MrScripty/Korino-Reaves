# Diff Components

## Purpose
Components for rendering diff trees, highlights, conflicts, and comparison views.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `ConflictPanel.svelte` | Conflict summary and review UI. |
| `DiffHighlight.svelte` | Inline diff/highlight rendering. |
| `DiffTree.svelte` | Navigable diff tree view. |
| `DiffView.svelte` | Comparison view container. |

## Problem
Diff and mod-porting workflows need specialized UI that is more expressive than generic tree or property rendering.

## Constraints
- Diff payloads come from backend-owned contracts.
- Interaction must stay understandable across large comparisons.

## Decision
Keep diff-specific rendering in its own component directory.

## Alternatives Rejected
- Fold diff UI into generic tree/property components: rejected because conflict semantics and comparison rendering are distinct.

## Invariants
- Diff rendering consumes typed diff payloads.
- Conflict semantics stay visually and structurally distinct from ordinary tree views.

## Revisit Triggers
- A second diff presentation mode or editor workflow appears.

## Dependencies
**Internal:** `svelte-ui/src/lib/view-models/diff.svelte.ts`.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a subsystem-specific presentation boundary.
- Revisit trigger: diff workflows expand beyond the current UI model.

## Usage Examples
```svelte
<DiffView />
```

## API Consumer Contract
- Parent layouts consume these components through diff view-model state.
- Backend payload shape remains the source of truth for conflicts and highlights.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the components render structured diff data but do not publish persisted schemas.
- Revisit trigger: saved diff sessions or export formats are introduced.
