# Property Editors

## Purpose
Inline editors for typed property values in the property grid.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `BoolEditor.svelte` | Boolean editor. |
| `ColorEditor.svelte` | Color editor. |
| `EnumEditor.svelte` | Enum editor. |
| `NumberEditor.svelte` | Numeric editor. |
| `StringEditor.svelte` | String editor. |
| `VectorEditor.svelte` | Vector/compound numeric editor. |

## Problem
Different property types need different editing affordances while still routing all changes through the backend-owned property model.

## Constraints
- Editors must stay accessible.
- Backend validation remains authoritative.

## Decision
Keep typed editors in their own directory under the property-grid subsystem.

## Alternatives Rejected
- Use a single generic editor for all property types: rejected because type-specific semantics and validation differ too much.

## Invariants
- Editors submit changes through the property update flow rather than mutating backend-owned state locally.
- Typed editors stay focused on one value family each.

## Revisit Triggers
- More complex property types require nested editors or schemas.

## Dependencies
**Internal:** property components and property view models.
**External:** Svelte.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: editor composition is currently local to the property subsystem.
- Revisit trigger: editor generation becomes schema-driven.

## Usage Examples
```svelte
<StringEditor />
```

## API Consumer Contract
- `PropertyRow.svelte` is the primary consumer.
- Editors expect typed property metadata and backend-facing submit/cancel behavior.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: editors consume property contracts but do not publish persisted schemas.
- Revisit trigger: reusable editor schemas or generated editor configs are added.
