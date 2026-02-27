# Capabilities

## Purpose

Stable capability boundary for AI agent tooling. Capabilities wrap existing
project, dependency, metadata, and selection systems without exposing IPC
transport types directly.

## Contents

- `IProjectExplorerCapability.cs` - Project tree exploration contract
- `ProjectExplorerCapability.cs` - File tree adapter-backed implementation
- `IDependencyGraphCapability.cs` - Dependency traversal/search contract
- `DependencyGraphCapability.cs` - Bounded dependency query implementation
- `IMetadataCapability.cs` - Asset metadata snapshot contract
- `MetadataCapability.cs` - Bounded metadata query implementation
- `IGuiSelectionCapability.cs` - GUI selection/expansion contract
- `GuiSelectionCapability.cs` - Selection + broadcast implementation
- `IDependencyDataAccess.cs` - Dependency/metadata data access abstraction
- `DependencyDatabaseDataAccess.cs` - SQLite adapter using `DependencyDatabase`
- `IProjectPathProvider.cs` - Current project path abstraction
- `ProjectHandlerPathProvider.cs` - `ProjectHandler` path adapter
- `ISelectionStateController.cs` - Selection state abstraction
- `SelectionHandlerController.cs` - `SelectionHandler` adapter
- `ISelectionBroadcaster.cs` - Selection broadcast abstraction
- `IpcSelectionBroadcaster.cs` - IPC-backed broadcaster
- `CapabilityModels.cs` - Shared capability DTOs

## Design Decisions

- Capabilities are domain-facing, not IPC-facing.
- All list/search methods are bounded for predictable runtime cost.
- Adapters reuse existing systems (`FileTreeBuilder`, `DependencyDatabase`,
  `SelectionHandler`) to avoid parallel implementations.
