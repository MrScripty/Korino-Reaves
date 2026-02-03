# Rendering

## Purpose

This directory contains texture and mesh extraction utilities.
It uses CUE4Parse to decode Unreal Engine textures and meshes,
then converts them to Godot-compatible formats.

## Contents

- `TextureExtractor.cs` - Extracts textures from UAsset/CUE4Parse and converts to Godot Image
- `MeshExtractor.cs` - Extracts static/skeletal meshes and converts to Godot ArrayMesh

## Design Decisions

- **CUE4Parse for Extraction**: CUE4Parse handles texture decoding (DXT, BC7, etc.)
- **Godot Types for Output**: Outputs Godot Image and ArrayMesh for direct rendering
- **Async Operations**: Heavy extraction operations run on background threads
- **Format Mapping**: Maps UE texture formats to equivalent Godot Image.Format values

## Dependencies

### Internal
- `Assets/` - Uses PakManager for PAK-based asset access
- `Infrastructure/` - IAppLogger for logging

### External (NuGet)
- CUE4Parse - Texture/mesh extraction
- CUE4Parse-Conversion - Format conversion utilities

## Supported Formats

### Textures
- DXT1/DXT5 (BC1/BC3)
- BC4/BC5/BC6H/BC7
- RGBA8/BGRA8
- Various compressed formats

### Meshes
- StaticMesh (LOD 0)
- SkeletalMesh (LOD 0, basic)
- Vertex positions, normals, UVs
- Triangle indices

## Usage Examples

```csharp
// Extract texture
var extractor = new TextureExtractor(logger);
var image = await extractor.ExtractAsync(pakManager, "Textures/T_Character_D.uasset");

// Extract mesh
var meshExtractor = new MeshExtractor(logger);
var arrayMesh = await meshExtractor.ExtractStaticMeshAsync(pakManager, "Meshes/SM_Prop.uasset");
```

## Limitations

- Only LOD 0 is extracted for meshes
- Skeletal mesh animation data is not extracted
- Some exotic texture formats may not be supported
- Material data is not extracted (just raw geometry/textures)
