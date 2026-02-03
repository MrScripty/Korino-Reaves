# Assets

## Purpose

This directory contains the asset loading, parsing, and manipulation layer.
It wraps UAssetAPI and CUE4Parse to provide a clean interface for the rest of the application.

## Contents

- `AssetManager.cs` - Main API facade coordinating all asset operations
- `AssetLoader.cs` - UAssetAPI wrapper for loading and saving .uasset files
- `TreeBuilder.cs` - Builds tree structure from UAsset exports and properties
- `PropertyService.cs` - Property read/write operations with path-based access
- `PakManager.cs` - PAK file browsing and extraction
- `MappingsManager.cs` - .usmap mappings file support

## Design Decisions

- **Facade Pattern**: AssetManager provides a single entry point hiding internal complexity
- **Godot-Agnostic**: All classes use plain C# types to enable unit testing without Godot runtime
- **Lazy Tree Loading**: Tree nodes load children on-demand to handle large assets efficiently
- **Path-Based Properties**: Properties are accessed via string paths (e.g., `["export-0", "Health"]`)
- **UAssetAPI for Editing**: Used for property manipulation and serialization
- **CUE4Parse for Extraction**: Used for texture/mesh extraction (read-only)

## Dependencies

### Internal
- `Models/` - TreeNode, PropertyValue, AssetInfo data structures
- `Infrastructure/` - IAppLogger for logging
- `Services/` - IAssetService, ITreeService interfaces

### External (NuGet)
- UAssetAPI - Asset parsing and editing
- CUE4Parse - Texture/mesh extraction
- CUE4Parse-Conversion - Format conversion utilities

## Usage Examples

```csharp
// Load an asset
var manager = new AssetManager(logger);
var info = await manager.LoadAsync("/path/to/asset.uasset");

// Get tree structure
var rootNodes = manager.GetRootNodes();
var children = manager.GetChildren("export-0");

// Read/write properties
var value = manager.GetPropertyValue(["export-0", "Health"]);
manager.SetPropertyValue(["export-0", "Health"], 150);

// Save changes
await manager.SaveAsync();
```

## Reference Implementation

Tree building patterns derived from UAssetGUI's TableHandler.cs approach:
- Lazy child loading with dummy nodes
- Property type-based child detection
- Export/Import/NameMap structure organization
