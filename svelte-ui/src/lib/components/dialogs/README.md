# Dialog Components

## Purpose
Components for modal file-selection and import flows.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `FileBrowser.svelte` | Modal file browser and selection flow. |
| `ImportPakDialog.svelte` | PAK import dialog and validation flow. |

## Problem
The UI needs focused modal workflows for file selection and import operations without scattering dialog logic across unrelated panels.

## Constraints
- Dialog actions cross the IPC boundary.
- Accessibility and keyboard handling matter for modal interactions.

## Decision
Keep modal workflows in a dedicated dialogs directory.

## Alternatives Rejected
- Inline modal flows in toolbar or panel components: rejected because state and accessibility logic would be harder to manage.

## Invariants
- Dialog submission routes through IPC-backed actions.
- Focus and dismissal behavior remain explicit and accessible.

## Revisit Triggers
- More dialog families appear or a shared dialog state layer is added.

## Dependencies
**Internal:** `svelte-ui/src/lib/bridge`, `svelte-ui/src/lib/view-models`, common components.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: current dialog flows are implementation-local.
- Revisit trigger: dialog orchestration becomes a broader framework concern.

## Usage Examples
```svelte
<ImportPakDialog />
```

## API Consumer Contract
- Parent components open these dialogs and consume their emitted user actions.
- Backend validation remains authoritative for filesystem and import operations.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: dialogs do not publish persisted structured artifacts.
- Revisit trigger: saved dialog presets or import templates are added.
