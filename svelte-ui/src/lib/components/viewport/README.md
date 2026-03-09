# Viewport Components

## Purpose
Components for preview rendering controls and viewport overlays.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `OrientationGizmo.svelte` | Orientation widget for preview navigation. |
| `ViewportPreview.svelte` | Main preview/viewport surface. |

## Problem
Asset and scene previews need interactive viewport controls that differ from ordinary panel content.

## Constraints
- Rendering data is backend-owned.
- Pointer and keyboard interactions must remain accessible and predictable.

## Decision
Keep viewport-specific overlays and preview rendering in a dedicated directory.

## Alternatives Rejected
- Mix viewport controls into generic layout components: rejected because rendering interactions are specialized.

## Invariants
- Viewport components consume backend-driven preview state.
- Overlay controls remain coordinated with the preview interaction model.

## Revisit Triggers
- Additional viewport overlays, tools, or camera modes are introduced.

## Dependencies
**Internal:** viewport view model and scene/preview state.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: current viewport behavior is local to the frontend subsystem.
- Revisit trigger: camera/tool architecture changes materially.

## Usage Examples
```svelte
<ViewportPreview />
```

## API Consumer Contract
- Parent layouts supply preview state through the viewport view model.
- Input events should route through the established preview interaction flow.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: viewport components render structured state but do not publish persisted schemas.
- Revisit trigger: saved viewport presets or tool manifests are added.
