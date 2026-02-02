# Models

## Purpose

This directory contains the **immutable shared data contracts** that define the IPC communication protocol between the C# backend (Godot) and the Svelte frontend (CEF). These models mirror the TypeScript types in `svelte-ui/src/lib/bridge/types.ts`.

**CRITICAL**: These contracts are immutable once defined. All agents MUST use these exact types. Breaking changes require coordination with ALL agents.

## Contents

| File | Description |
| ---- | ----------- |
| `IpcMessage.cs` | Base IPC message structure and constants |
| `TreeNode.cs` | Asset tree node representation for UI |
| `PropertyValue.cs` | Property data for property grid display |
| `SelectionState.cs` | Current selection and tree expansion state |
| `DiffResult.cs` | Diff comparison results and conflicts |
| `AgentMessage.cs` | AI agent status and command structures |
| `ErrorResponse.cs` | Standardized error codes and responses |
| `AssetInfo.cs` | Loaded asset summary information |
| `ViewportState.cs` | 3D/2D viewport state |

## Design Decisions

- **Records over Classes**: All models use C# records for immutability and value equality
- **JsonPropertyName Attributes**: All properties have explicit JSON names to ensure consistent serialization with TypeScript
- **Static Constants Classes**: Type values (e.g., `MessageTypes`, `ErrorCodes`) are string constants in static classes rather than enums for JSON serialization compatibility
- **Nullable Reference Types**: Optional fields use nullable types to match TypeScript's optional properties
- **Mirror TypeScript Exactly**: Property names, types, and structure match `types.ts` 1:1

## Dependencies

### Internal

- None (this is a foundational module)

### External

| Package | Version | Purpose |
| ------- | ------- | ------- |
| System.Text.Json | Built-in | JSON serialization attributes |

## Usage Examples

### Creating an IPC Message

```csharp
using UAssetViewer.Models;

// Create a tree update message
var message = new IpcMessage(
    Type: MessageTypes.Tree,
    Action: "update",
    Payload: new TreeNode(
        Id: "export[0]",
        Name: "MyExport",
        Type: TreeNodeTypes.Export,
        HasChildren: true
    )
);

// Serialize to JSON
var json = JsonSerializer.Serialize(message);
```

### Handling Errors

```csharp
using UAssetViewer.Models;

var error = new ErrorResponse(
    Code: ErrorCodes.AssetNotLoaded,
    Message: "No asset is currently loaded",
    Details: new { requestedAction = "getProperties" }
);
```

### Working with Diff Results

```csharp
using UAssetViewer.Models;

var change = new DiffChange(
    Path: new[] { "Export[0]", "Properties", "Health" },
    ChangeType: DiffChangeTypes.Modified,
    OldValue: 100,
    NewValue: 150
);

var result = new DiffResult(
    BaseVersion: "v1.0",
    TargetVersion: "v1.1",
    Changes: new[] { change },
    Summary: new DiffSummary(Added: 0, Removed: 0, Modified: 1, Unchanged: 42)
);
```

## Agent Ownership

**Owner**: 00-shared-contracts

This directory is owned by the Shared Contracts agent and is **immutable** once the agent completes. Other agents depend on these types but cannot modify them.

## Related Documentation

- [types.ts](../../../svelte-ui/src/lib/bridge/types.ts) - TypeScript mirror of these contracts
- [ARCHITECTURE.md](../../../plans/ARCHITECTURE.md) - Overall project architecture
- [00-shared-contracts.md](../../../plans/00-shared-contracts.md) - Contract agent specification
