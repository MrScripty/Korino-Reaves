# Dependency Components

## Purpose
Components for visualizing dependency graphs and scan progress in the UI.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DependencyPanel.svelte` | Dependency graph panel and scan controls. |

## Problem
Users need a focused way to inspect what assets depend on each other and to trigger scans.

## Constraints
- Dependency data can be large.
- Scan state is backend-owned and asynchronous.

## Decision
Keep dependency visualization in a dedicated component directory tied to the dependency view model.

## Alternatives Rejected
- Mix dependency UI into generic tree components: rejected because scan state and graph semantics are distinct.

## Invariants
- Scan progress and graph data come from backend-driven view models.
- Component semantics stay accessible for keyboard and status use.

## Revisit Triggers
- Multiple dependency-specific components are added.

## Dependencies
**Internal:** `svelte-ui/src/lib/view-models`.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a focused UI boundary.
- Revisit trigger: dependency visualization adopts a different interaction model.

## Usage Examples
```svelte
<DependencyPanel />
```

## API Consumer Contract
- Parent layouts consume it through dependency view-model state.
- Backend events remain the source of truth for scan progress and graph contents.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the component consumes graph data but does not publish persisted schemas.
- Revisit trigger: saved graph views or exported graph files are introduced.
