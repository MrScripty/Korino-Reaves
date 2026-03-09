# Datatable Components

## Purpose
Components for tabular inspection of backend-provided asset or metadata rows.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DataTablePanel.svelte` | Panel wrapper that renders the tabular view. |

## Problem
Some asset and metadata views are easier to inspect in rows and columns than in trees or property grids.

## Constraints
- Table data is backend-owned.
- The component must remain usable inside docked layouts.

## Decision
Keep datatable rendering in a dedicated component directory rather than mixing it into generic layout components.

## Alternatives Rejected
- Render tables through ad hoc route markup: rejected because the panel is reusable UI.

## Invariants
- The component consumes typed view-model data.
- Table interactions do not mutate backend-owned data locally.

## Revisit Triggers
- Additional datatable-specific components or editors are added.

## Dependencies
**Internal:** `svelte-ui/src/lib/view-models`.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a small presentation boundary.
- Revisit trigger: table behavior expands materially.

## Usage Examples
```svelte
<DataTablePanel />
```

## API Consumer Contract
- Parent layouts supply backend-driven data through the relevant view model.
- Empty/loading/error states should remain explicit in the component.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the component renders structured data but does not publish a persisted schema.
- Revisit trigger: saved table layouts or exports become a checked-in contract.
