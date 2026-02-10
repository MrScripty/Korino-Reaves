// Level Extractor - Parse .umap Files
//
// Extracts actor data (transforms, mesh references) from UE level files
// using UAssetAPI for structure parsing. Mesh paths are resolved for
// subsequent loading via CUE4Parse.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Data extracted from a single actor in a level.
/// </summary>
public sealed record ActorData(
    string Id,
    string Name,
    string ClassName,
    string? MeshPath,
    Transform3D Transform
);

/// <summary>
/// Result of extracting actor data from a level.
/// </summary>
public sealed record LevelExtractionResult(
    string LevelName,
    ActorData[] Actors
);

/// <summary>
/// Extracts actor and mesh data from UE level (.umap) files using UAssetAPI.
/// </summary>
public sealed class LevelExtractor
{
    private readonly IAppLogger _logger;

    public LevelExtractor(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads a .umap file and extracts all actors with their transforms and mesh references.
    /// </summary>
    public async Task<LevelExtractionResult?> ExtractLevelAsync(
        string filePath,
        EngineVersion? version,
        IProgress<(int loaded, int total)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() => ExtractLevel(filePath, version, progress, ct), ct);
    }

    private LevelExtractionResult? ExtractLevel(
        string filePath,
        EngineVersion? version,
        IProgress<(int loaded, int total)>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            _logger.Warning("Level file not found: {Path}", filePath);
            return null;
        }

        UAsset asset;
        try
        {
            asset = new UAsset(filePath, version ?? EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load level asset: {Path}", filePath);
            return null;
        }

        _logger.Info("Level loaded: {Path}, {Count} exports", filePath, asset.Exports.Count);

        // Log all export types for debugging
        var exportTypeCounts = new Dictionary<string, int>();
        foreach (var export in asset.Exports)
        {
            var className = GetExportClassName(export, asset);
            exportTypeCounts.TryGetValue(className, out var count);
            exportTypeCounts[className] = count + 1;
        }
        foreach (var (typeName, count) in exportTypeCounts.OrderByDescending(kv => kv.Value).Take(20))
        {
            _logger.Debug("  Export type: {Type} x{Count}", typeName, count);
        }

        // Find the LevelExport
        var levelExport = asset.Exports.OfType<LevelExport>().FirstOrDefault();
        if (levelExport == null)
        {
            _logger.Warning("No LevelExport found in: {Path}", filePath);
            // Fall back to scanning all exports for actor-like types
            return ExtractActorsFromExports(asset, filePath, progress, ct);
        }

        _logger.Info("LevelExport found with {Count} actors", levelExport.Actors?.Count ?? 0);
        return ExtractActorsFromLevelExport(asset, levelExport, filePath, progress, ct);
    }

    private LevelExtractionResult ExtractActorsFromLevelExport(
        UAsset asset, LevelExport levelExport, string filePath,
        IProgress<(int loaded, int total)>? progress, CancellationToken ct)
    {
        var actors = new List<ActorData>();
        var actorRefs = levelExport.Actors ?? new List<FPackageIndex>();
        int total = actorRefs.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var actorRef = actorRefs[i];
            if (actorRef == null || actorRef.IsNull()) continue;

            var actorData = ExtractActorFromRef(asset, actorRef, i);
            if (actorData != null)
            {
                actors.Add(actorData);
            }

            if (i % 50 == 0)
            {
                progress?.Report((i, total));
            }
        }

        progress?.Report((total, total));
        var levelName = Path.GetFileNameWithoutExtension(filePath);
        _logger.Info("Extracted {Count} actors from level {Name}", actors.Count, levelName);
        return new LevelExtractionResult(levelName, actors.ToArray());
    }

    private LevelExtractionResult ExtractActorsFromExports(
        UAsset asset, string filePath,
        IProgress<(int loaded, int total)>? progress, CancellationToken ct)
    {
        var actors = new List<ActorData>();
        int total = asset.Exports.Count;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            var export = asset.Exports[i];
            var className = GetExportClassName(export, asset);

            if (IsActorClass(className))
            {
                var actorData = ExtractActorFromExport(asset, export, i, className);
                if (actorData != null)
                {
                    actors.Add(actorData);
                }
            }

            if (i % 50 == 0)
            {
                progress?.Report((i, total));
            }
        }

        progress?.Report((total, total));
        var levelName = Path.GetFileNameWithoutExtension(filePath);
        _logger.Info("Extracted {Count} actors from {Total} exports in {Name}",
            actors.Count, total, levelName);
        return new LevelExtractionResult(levelName, actors.ToArray());
    }

    private ActorData? ExtractActorFromRef(UAsset asset, FPackageIndex actorRef, int index)
    {
        if (!actorRef.IsExport()) return null;

        var exportIndex = actorRef.Index - 1;
        if (exportIndex < 0 || exportIndex >= asset.Exports.Count) return null;

        var export = asset.Exports[exportIndex];
        var className = GetExportClassName(export, asset);
        return ExtractActorFromExport(asset, export, index, className);
    }

    private ActorData? ExtractActorFromExport(UAsset asset, Export export, int index, string className)
    {
        var name = export.ObjectName?.Value?.Value ?? $"Actor_{index}";
        var exportIndex = asset.Exports.IndexOf(export);

        string? meshPath = null;
        Transform3D? bestTransform = null;

        // Find child components (exports whose OuterIndex points to this actor)
        for (int j = 0; j < asset.Exports.Count; j++)
        {
            var child = asset.Exports[j];
            if (!child.OuterIndex.IsExport() || child.OuterIndex.Index - 1 != exportIndex)
                continue;

            var childClass = GetExportClassName(child, asset);

            // Extract mesh path from mesh components
            if (IsStaticMeshComponent(childClass) || IsSkeletalMeshComponent(childClass))
            {
                meshPath ??= ExtractMeshPathFromComponent(asset, child);
            }

            // Extract transform from any SceneComponent-derived child
            if (bestTransform == null && child is NormalExport componentExport)
            {
                var transform = ExtractTransformFromProperties(componentExport.Data);
                if (transform.HasValue)
                {
                    bestTransform = transform.Value;
                }
            }
        }

        // Fallback: try getting transform from the actor export itself
        if (bestTransform == null && export is NormalExport normalExport)
        {
            var extracted = ExtractTransformFromProperties(normalExport.Data);
            if (extracted.HasValue)
            {
                bestTransform = extracted.Value;
            }
        }

        return new ActorData(
            $"actor-{index}",
            name,
            className,
            meshPath,
            bestTransform ?? Transform3D.Identity
        );
    }

    /// <summary>
    /// Extracts a Godot Transform3D from UE property data.
    /// Handles both component properties (RelativeLocation/Rotation/Scale3D) and
    /// actor properties (ActorLocation/Rotation). Supports VectorPropertyData (FVector),
    /// RotatorPropertyData (FRotator), and StructPropertyData with child floats.
    /// Applies UE4→Godot coordinate conversion: position (X, Z, -Y), scale (X, Z, Y).
    /// </summary>
    private Transform3D? ExtractTransformFromProperties(List<PropertyData> properties)
    {
        float locX = 0, locY = 0, locZ = 0;
        float rotPitch = 0, rotYaw = 0, rotRoll = 0;
        float scaleX = 1, scaleY = 1, scaleZ = 1;
        bool hasLocation = false;

        foreach (var prop in properties)
        {
            var propName = prop.Name?.Value?.Value;
            if (propName == null) continue;

            // Location: RelativeLocation (components) or ActorLocation (actors)
            if (propName is "RelativeLocation" or "ActorLocation")
            {
                if (TryReadVector(prop, ref locX, ref locY, ref locZ))
                    hasLocation = true;
            }
            // Rotation: RelativeRotation (components) or ActorRotation (actors)
            else if (propName is "RelativeRotation" or "ActorRotation")
            {
                TryReadRotator(prop, ref rotPitch, ref rotYaw, ref rotRoll);
            }
            // Scale
            else if (propName is "RelativeScale3D" or "ActorScale3D")
            {
                TryReadVector(prop, ref scaleX, ref scaleY, ref scaleZ);
            }
        }

        if (!hasLocation) return null;

        // UE4 → Godot coordinate conversion
        // Position: UE(X,Y,Z) → Godot(X,Z,-Y) — same as MeshExtractor
        var godotPos = new Vector3(locX, locZ, -locY);

        // Scale: UE(X,Y,Z) → Godot(X,Z,Y) — swap Y and Z
        var godotScale = new Vector3(scaleX, scaleZ, scaleY);

        // Rotation: UE FRotator is (Pitch, Yaw, Roll) in degrees
        var pitchRad = Mathf.DegToRad(rotPitch);
        var yawRad = Mathf.DegToRad(rotYaw);
        var rollRad = Mathf.DegToRad(rotRoll);

        // UE rotation order: Yaw (Z) → Pitch (Y) → Roll (X)
        // In Godot axes: Yaw around Godot-Y, Pitch around Godot-Z, Roll around Godot-X
        var basis = Basis.Identity;
        basis = new Basis(Vector3.Up, yawRad) * basis;
        basis = new Basis(Vector3.Back, pitchRad) * basis;
        basis = new Basis(Vector3.Right, rollRad) * basis;

        basis = basis * Basis.FromScale(godotScale);

        return new Transform3D(basis, godotPos);
    }

    /// <summary>
    /// Reads a vector from a property. Supports VectorPropertyData (FVector) and
    /// StructPropertyData with child float/double properties named X, Y, Z.
    /// </summary>
    private static bool TryReadVector(PropertyData prop, ref float x, ref float y, ref float z)
    {
        // Direct VectorPropertyData
        if (prop is VectorPropertyData vecProp)
        {
            x = (float)vecProp.Value.X;
            y = (float)vecProp.Value.Y;
            z = (float)vecProp.Value.Z;
            return true;
        }

        // StructPropertyData with StructType "Vector" — UAssetAPI uses custom serialization
        // that stores a single VectorPropertyData child in the Value list
        if (prop is StructPropertyData structProp && structProp.Value != null)
        {
            // Check for custom-serialized VectorPropertyData child
            foreach (var child in structProp.Value)
            {
                if (child is VectorPropertyData innerVec)
                {
                    x = (float)innerVec.Value.X;
                    y = (float)innerVec.Value.Y;
                    z = (float)innerVec.Value.Z;
                    return true;
                }
            }

            // Fallback: named float children (X, Y, Z)
            bool found = false;
            foreach (var child in structProp.Value)
            {
                var name = child.Name?.Value?.Value;
                if (name == null) continue;

                float value = 0;
                if (child is FloatPropertyData fp) value = fp.Value;
                else if (child is DoublePropertyData dp) value = (float)dp.Value;
                else continue;

                switch (name)
                {
                    case "X": x = value; found = true; break;
                    case "Y": y = value; found = true; break;
                    case "Z": z = value; found = true; break;
                }
            }
            return found;
        }

        return false;
    }

    /// <summary>
    /// Reads a rotator from a property. Supports RotatorPropertyData (FRotator) and
    /// StructPropertyData with child float/double properties named Pitch, Yaw, Roll.
    /// </summary>
    private static bool TryReadRotator(PropertyData prop, ref float pitch, ref float yaw, ref float roll)
    {
        // Direct RotatorPropertyData
        if (prop is RotatorPropertyData rotProp)
        {
            pitch = (float)rotProp.Value.Pitch;
            yaw = (float)rotProp.Value.Yaw;
            roll = (float)rotProp.Value.Roll;
            return true;
        }

        // StructPropertyData with StructType "Rotator" — UAssetAPI uses custom serialization
        // that stores a single RotatorPropertyData child in the Value list
        if (prop is StructPropertyData structProp && structProp.Value != null)
        {
            // Check for custom-serialized RotatorPropertyData child
            foreach (var child in structProp.Value)
            {
                if (child is RotatorPropertyData innerRot)
                {
                    pitch = (float)innerRot.Value.Pitch;
                    yaw = (float)innerRot.Value.Yaw;
                    roll = (float)innerRot.Value.Roll;
                    return true;
                }
            }

            // Fallback: named float children (Pitch, Yaw, Roll)
            bool found = false;
            foreach (var child in structProp.Value)
            {
                var name = child.Name?.Value?.Value;
                if (name == null) continue;

                float value = 0;
                if (child is FloatPropertyData fp) value = fp.Value;
                else if (child is DoublePropertyData dp) value = (float)dp.Value;
                else continue;

                switch (name)
                {
                    case "Pitch": pitch = value; found = true; break;
                    case "Yaw": yaw = value; found = true; break;
                    case "Roll": roll = value; found = true; break;
                }
            }
            return found;
        }

        return false;
    }

    /// <summary>
    /// Extracts the mesh asset path from a StaticMeshComponent or SkeletalMeshComponent.
    /// Returns a game path suitable for CUE4Parse LoadPackageObject.
    /// </summary>
    private string? ExtractMeshPathFromComponent(UAsset asset, Export componentExport)
    {
        if (componentExport is not NormalExport normalExport) return null;

        foreach (var prop in normalExport.Data)
        {
            var propName = prop.Name?.Value?.Value;
            if (propName is not ("StaticMesh" or "SkeletalMesh")) continue;

            if (prop is ObjectPropertyData objProp)
            {
                return ResolveObjectPath(asset, objProp.Value);
            }
            else if (prop is SoftObjectPropertyData softProp)
            {
                return softProp.Value.AssetPath.AssetName.Value?.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a FPackageIndex to a full game path by following the import chain.
    /// </summary>
    private string? ResolveObjectPath(UAsset asset, FPackageIndex? packageIndex)
    {
        if (packageIndex == null || packageIndex.IsNull()) return null;

        if (packageIndex.IsImport())
        {
            return BuildImportPath(asset, packageIndex);
        }
        else if (packageIndex.IsExport())
        {
            // Mesh is in the same package — use the package path + export name
            var exportIndex = packageIndex.Index - 1;
            if (exportIndex >= 0 && exportIndex < asset.Exports.Count)
            {
                return asset.Exports[exportIndex].ObjectName?.Value?.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the package game path from an import reference by walking up to
    /// the outermost import (the package). CUE4Parse LoadPackageObject expects
    /// just the package path (e.g. "/Game/Environment/SM_Rock"), not the full
    /// object path (e.g. "/Game/Environment/SM_Rock/SM_Rock").
    /// </summary>
    private string? BuildImportPath(UAsset asset, FPackageIndex importRef)
    {
        var current = importRef;
        string? packageName = null;

        // Walk up the outer chain to find the topmost import (the package)
        for (int i = 0; i < 20; i++)
        {
            if (current == null || current.IsNull()) break;
            if (!current.IsImport()) break;

            var importIndex = -current.Index - 1;
            if (importIndex < 0 || importIndex >= asset.Imports.Count) break;

            var import = asset.Imports[importIndex];
            var name = import.ObjectName?.Value?.Value;
            if (string.IsNullOrEmpty(name)) break;

            packageName = name;
            current = import.OuterIndex;
        }

        // packageName is the outermost import's ObjectName — the package path
        // e.g. "/Game/Environment/Meshes/SM_Rock"
        return packageName;
    }

    private static string GetExportClassName(Export export, UAsset asset)
    {
        if (export.ClassIndex.IsNull())
        {
            return export.ObjectName?.Value?.Value ?? "Unknown";
        }

        if (export.ClassIndex.IsImport())
        {
            var importIndex = -export.ClassIndex.Index - 1;
            if (importIndex >= 0 && importIndex < asset.Imports.Count)
            {
                return asset.Imports[importIndex].ObjectName?.Value?.Value ?? "Unknown";
            }
        }
        else if (export.ClassIndex.IsExport())
        {
            var classExportIndex = export.ClassIndex.Index - 1;
            if (classExportIndex >= 0 && classExportIndex < asset.Exports.Count)
            {
                return asset.Exports[classExportIndex].ObjectName?.Value?.Value ?? "Unknown";
            }
        }

        return "Unknown";
    }

    private static bool IsActorClass(string className)
    {
        return className.Contains("Actor")
            || className.EndsWith("_C")
            || className == "PointLight"
            || className == "SpotLight"
            || className == "DirectionalLight"
            || className == "CameraActor"
            || className == "PlayerStart"
            || className == "Brush";
    }

    private static bool IsStaticMeshComponent(string className)
    {
        return className is "StaticMeshComponent"
            or "InstancedStaticMeshComponent"
            or "HierarchicalInstancedStaticMeshComponent";
    }

    private static bool IsSkeletalMeshComponent(string className)
    {
        return className is "SkeletalMeshComponent"
            or "SkinnedMeshComponent";
    }
}
