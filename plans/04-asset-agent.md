# Asset Agent

**Phase**: 2 - Features
**Depends on**: Backend Agent (01), Shared Contracts (00)

## Scope

UAssetAPI/CUE4Parse integration, asset loading, tree building, property read/write.

## Reference Materials

- **UAssetGUI** (`/media/jeremy/OrangeCream/Linux Software/UAssetGUI/`):
  - `TableHandler.cs` - Property serialization patterns
  - `FileContainerForm.cs` - PAK browser implementation
  - `Form1.cs` - Asset loading orchestration

- **CUE4Parse** (`/media/jeremy/OrangeCream/Linux Software/CUE4Parse/`):
  - Texture decoding pipelines
  - Mesh/skeletal mesh extraction
  - PAK file reading

## Files to Create

```
godot/scripts/
├── Assets/
│   ├── AssetManager.cs        # Main API facade
│   ├── AssetLoader.cs         # UAssetAPI wrapper
│   ├── PakManager.cs          # PAK file handling
│   ├── MappingsManager.cs     # .usmap support
│   ├── TreeBuilder.cs         # Build tree from asset
│   └── README.md
├── Rendering/
│   ├── TextureExtractor.cs    # CUE4Parse → Godot Image
│   ├── MeshExtractor.cs       # CUE4Parse → Godot ArrayMesh
│   └── README.md
├── Bridge/handlers/
│   ├── AssetHandler.cs        # Implement from stub
│   ├── TreeHandler.cs         # Implement from stub
│   └── PropertyHandler.cs     # Implement from stub
└── Models/
    ├── AssetInfo.cs           # Asset metadata
    └── PropertyData.cs        # Property serialization
```

## Tasks

### 1. NuGet Dependencies
- [ ] Add UAssetAPI package
- [ ] Add CUE4Parse packages
- [ ] Verify compatibility with .NET 8.0

### 2. Asset Manager Facade

```csharp
public interface IAssetManager
{
    Task<AssetInfo> LoadAsset(string path);
    Task<TreeNode[]> GetTree(string? parentPath = null);
    Task<PropertyValue[]> GetProperties(int exportIndex);
    Task<object?> GetPropertyValue(string[] path);
    Task SetPropertyValue(string[] path, object value);
    Task Save();
    Task SaveAs(string path);
    Task<DiffResult> ComputeDiff(string pathA, string pathB);
}

public class AssetManager : IAssetManager
{
    private readonly IAssetLoader _loader;
    private readonly ITreeBuilder _treeBuilder;
    private readonly IAppLogger _logger;

    // Current asset state
    private UAsset? _currentAsset;
    private string? _currentPath;
}
```

- [ ] Create interface (Godot-agnostic)
- [ ] Implement AssetManager class
- [ ] Add activity tracing for operations
- [ ] Handle errors with Result<T>

### 3. Asset Loader

```csharp
public interface IAssetLoader
{
    Task<UAsset> Load(string path);
    Task<UAsset> LoadFromPak(string pakPath, string assetPath);
    Task Save(UAsset asset, string path);
}
```

- [ ] Wrap UAssetAPI loading
- [ ] Support multiple UE versions (4.0 - 5.7)
- [ ] Handle .uasset + .uexp pairs
- [ ] Support .usmap mappings

### 4. Tree Builder

```csharp
public interface ITreeBuilder
{
    TreeNode[] BuildTree(UAsset asset);
    TreeNode[] GetChildren(UAsset asset, string parentId);
}
```

Study UAssetGUI's `TableHandler.cs` for patterns:
- [ ] Build tree from exports
- [ ] Handle properties recursively
- [ ] Handle arrays and structs
- [ ] Support lazy loading of children
- [ ] Assign unique IDs to each node

### 5. Property Read/Write

```csharp
public interface IPropertyService
{
    PropertyValue[] GetProperties(UAsset asset, int exportIndex);
    object? GetValue(UAsset asset, string[] path);
    void SetValue(UAsset asset, string[] path, object value);
}
```

- [ ] Read property values by path
- [ ] Write property values
- [ ] Validate value types
- [ ] Track changes for save

### 6. PAK Manager

```csharp
public interface IPakManager
{
    Task<string[]> ListFiles(string pakPath);
    Task<byte[]> ExtractFile(string pakPath, string filePath);
    Task<UAsset> LoadAsset(string pakPath, string assetPath);
}
```

- [ ] Open PAK files
- [ ] List contents
- [ ] Extract files
- [ ] Load assets directly from PAK

### 7. Texture Extractor

```csharp
public interface ITextureExtractor
{
    Image ExtractTexture(UAsset asset, int exportIndex);
    Image ExtractTextureFromCUE4(UTexture2D texture);
}
```

- [ ] Decode texture formats (DXT, BC7, etc.)
- [ ] Convert to Godot Image
- [ ] Handle mip levels

### 8. Mesh Extractor

```csharp
public interface IMeshExtractor
{
    ArrayMesh ExtractStaticMesh(UAsset asset, int exportIndex);
    ArrayMesh ExtractSkeletalMesh(UAsset asset, int exportIndex);
}
```

- [ ] Extract vertex data
- [ ] Extract indices
- [ ] Handle UVs and normals
- [ ] Convert to Godot ArrayMesh

### 9. IPC Handlers

Implement the handler stubs from Backend Agent:

**AssetHandler.cs**:
```csharp
public class AssetHandler : IMessageHandler
{
    private readonly IAssetManager _assetManager;

    public async Task<object> Handle(IpcMessage message)
    {
        return message.Action switch
        {
            "open" => await _assetManager.LoadAsset(message.Payload.Path),
            "save" => await _assetManager.Save(),
            "saveAs" => await _assetManager.SaveAs(message.Payload.Path),
            _ => throw new NotSupportedException()
        };
    }
}
```

- [ ] Implement AssetHandler
- [ ] Implement TreeHandler
- [ ] Implement PropertyHandler
- [ ] Register handlers with IpcDispatcher

### 10. Testing

- [ ] Unit tests for TreeBuilder
- [ ] Unit tests for PropertyService
- [ ] Integration test: Load asset → Get tree → Get properties
- [ ] Test with various UE versions

## Data Models

```csharp
public record AssetInfo(
    string Path,
    string Name,
    string EngineVersion,
    int ExportCount,
    int ImportCount
);

public record PropertyData(
    string Name,
    string Type,
    object Value,
    bool IsArray,
    int? ArrayIndex
);
```

## Error Handling

```csharp
public class AssetLoadException : Exception { }
public class PropertyNotFoundException : Exception { }
public class InvalidPropertyValueException : Exception { }
```

- Return Result<T> for expected failures
- Throw exceptions for unexpected errors
- Log all operations with correlation IDs

## Outputs for Other Agents

1. **AssetManager API** - Diff Agent wraps this
2. **Tree data structure** - Frontend displays this
3. **Property read/write** - AI Agent uses this
4. **Texture/Mesh extraction** - Viewport Controller uses this

## Acceptance Criteria

- [ ] Can load .uasset files
- [ ] Tree displays exports and properties
- [ ] Properties can be read and written
- [ ] PAK files can be browsed
- [ ] Textures extract correctly
- [ ] Meshes extract correctly
- [ ] Multiple UE versions supported
- [ ] All operations traced with OpenTelemetry
- [ ] Unit tests pass

## Sync Point

**Sync 3: Asset Foundation** - Must complete before Diff Agent and AI Agent can begin their work.
