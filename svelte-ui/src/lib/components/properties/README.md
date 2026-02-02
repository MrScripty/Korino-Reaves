# Property Components

## Purpose

Components for displaying and editing property values for the selected asset node.

## Contents

| Component | Description |
|-----------|-------------|
| `PropertyGrid.svelte` | Container listing all properties for selected node |
| `PropertyRow.svelte` | Single property with name and value/editor |
| `editors/` | Type-specific inline editors |

### Editors

| Editor | Property Types |
|--------|---------------|
| `StringEditor.svelte` | String, Text, Name properties |
| `NumberEditor.svelte` | Int, Float, Double properties |
| `BoolEditor.svelte` | Boolean properties |
| `EnumEditor.svelte` | Enum, ByteEnum properties |
| `VectorEditor.svelte` | Vector, Vector2D, Rotator |
| `ColorEditor.svelte` | LinearColor, Color |

## Design Decisions

- **Inline Editing**: Click value to edit, Enter to submit, Escape to cancel
- **Type Color Coding**: Values colored by property type
- **Submit to C#**: All edits forwarded via IPC, no local updates

## Data Flow

```
C# Backend
    │
    ├─ Pushes properties for selected node
    ▼
properties.svelte.ts (view model)
    │
    ├─ properties: PropertyValue[]
    ├─ editingPath: string[] | null
    ▼
PropertyGrid.svelte
    │
    ├─ Maps properties to rows
    ▼
PropertyRow.svelte
    │
    ├─ Shows display value or editor
    ├─ On edit: properties.setPropertyValue()
    └─ (C# receives IPC, validates, pushes update)
```

## Usage Examples

### Basic Usage

```svelte
<Panel title="Properties">
    <PropertyGrid />
</Panel>
```

## Agent Ownership

**Owner**: 02-frontend-agent
