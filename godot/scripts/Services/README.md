# Services

## Purpose

Business logic services that are Godot-agnostic. These can be unit tested without
a Godot runtime and contain the core application logic.

## Contents

- `IAssetService.cs` - Asset loading and manipulation interface (to be implemented)
- `ITreeService.cs` - Tree building interface (to be implemented)

## Design Decisions

- **Godot-Agnostic**: Services MUST NOT depend on Godot types. This enables:
  - Unit testing without Godot runtime
  - Potential reuse in other contexts
  - Clear separation between game engine and business logic

- **Interface-First**: Define interfaces before implementations. This allows:
  - Mock implementations for testing
  - Parallel development (stub while building real implementation)
  - Dependency injection

- **Controllers Translate**: Controllers (Godot nodes) translate between Godot types
  and service types. Services work with plain C# objects.

## Dependencies

- Internal: `UAssetViewer.Models` (data types)
- External: None (services should be pure C#)

## Planned Interfaces

```csharp
// IAssetService - will be implemented by Asset Agent
public interface IAssetService
{
    Task<AssetInfo> LoadAsync(string path);
    Task SaveAsync(string path);
    void Close();
}

// ITreeService - will be implemented by Asset Agent
public interface ITreeService
{
    TreeNode[] GetRootNodes();
    TreeNode[] GetChildren(string nodeId);
    PropertyValue[] GetProperties(string nodeId);
}
```

## Usage Pattern

```csharp
// In a controller/handler
public class AssetHandler : IMessageHandler
{
    private readonly IAssetService _assetService;

    public AssetHandler(IAssetService assetService)
    {
        _assetService = assetService;
    }

    public async Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        if (message.Action == "open")
        {
            var asset = await _assetService.LoadAsync(path);
            return new IpcMessage("asset", "opened", asset);
        }
        // ...
    }
}
```
