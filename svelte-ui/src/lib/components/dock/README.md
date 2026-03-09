# Dock Components

## Purpose
Components and local types for the docked multi-panel workspace layout.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `DockContainer.svelte` | Top-level dock layout container. |
| `DockDragOverlay.svelte` | Drag feedback overlay. |
| `DockTabBar.svelte` | Tab strip for dock panels. |
| `DockZone.svelte` | Dock drop zone rendering. |
| `dockTypes.ts` | Local dock layout types. |
| `panelRegistry.ts` | Registry that maps dock panels to component implementations. |

## Problem
The UI needs a flexible workspace layout that can host multiple panels without hard-coding a single fixed arrangement.

## Constraints
- Dock state must remain compatible with persisted layout behavior.
- Panel registration must stay explicit.

## Decision
Keep dock-specific rendering, local types, and panel registry code together.

## Alternatives Rejected
- Hard-code every panel position in route markup: rejected because the app needs a configurable workspace.

## Invariants
- Panel identifiers stay aligned with the panel registry.
- Dock interactions remain separate from backend-owned business state.

## Revisit Triggers
- The app adopts a different docking system.
- Layout persistence rules become complex enough for a dedicated schema module.

## Dependencies
**Internal:** layout components, view models, persisted dock state.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: docking is currently an internal frontend layout concern.
- Revisit trigger: layout persistence or plugin panels expand materially.

## Usage Examples
```svelte
<DockContainer />
```

## API Consumer Contract
- Parent routes/layouts host the dock container.
- Panel registration and layout payloads must remain internally consistent.

## Structured Producer Contract
- Dock layout identifiers and persisted panel keys are a structured producer contract for saved workspace state.
- Adding or renaming panel keys requires a compatibility review for existing saved layouts.
