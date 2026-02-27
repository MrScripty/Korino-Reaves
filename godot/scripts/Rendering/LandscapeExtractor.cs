// Landscape Extractor - Heightmap to Terrain Mesh
//
// Loads ULandscapeComponent exports from UE level files via CUE4Parse,
// extracts heightmap data, and generates ArrayMesh terrain geometry.
// Supports both UE4 (ushort[] HeightData) and UE5 (byte[] HeightWeightData) formats.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Landscape;
using Godot;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Extracts landscape components from UE level files and generates terrain meshes.
/// </summary>
public sealed class LandscapeExtractor
{
    private const float DefaultQuadSpacing = 100.0f; // UE default landscape quad size in UE units
    private const int HeightMidpoint = 32768;         // ushort midpoint (zero height)
    private const float HeightScale = 128.0f;         // UE height normalization factor
    private const int DefaultComponentSize = 63;       // Default ComponentSizeQuads

    private readonly IAppLogger _logger;

    public LandscapeExtractor(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads all landscape components from a level package and generates terrain meshes.
    /// </summary>
    /// <param name="provider">CUE4Parse file provider with the project mounted.</param>
    /// <param name="levelLoadPath">Game path to the .umap (without extension).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of generated terrain meshes with component names.</returns>
    public async Task<List<(ArrayMesh Mesh, string Name)>> ExtractLandscapeMeshesAsync(
        DefaultFileProvider provider,
        string levelLoadPath,
        CancellationToken ct = default)
    {
        var results = new List<(ArrayMesh Mesh, string Name)>();

        var components = await Task.Run(() =>
        {
            try
            {
                var package = provider.LoadPackage(levelLoadPath);
                return package.GetExports()
                    .OfType<ULandscapeComponent>()
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to load landscape components from {Path}: {Error}",
                    levelLoadPath, ex.Message);
                return Array.Empty<ULandscapeComponent>();
            }
        }, ct);

        if (components.Length == 0)
        {
            _logger.Debug("No landscape components found in {Path}", levelLoadPath);
            return results;
        }

        _logger.Info("Found {Count} landscape components in {Path}", components.Length, levelLoadPath);

        foreach (var component in components)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var meshResult = BuildComponentMesh(component);
                if (meshResult != null)
                {
                    results.Add(meshResult.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to build terrain mesh for component: {Error}", ex.Message);
            }
        }

        _logger.Info("Generated {Count} terrain meshes from {Total} components",
            results.Count, components.Length);

        return results;
    }

    private (ArrayMesh Mesh, string Name)? BuildComponentMesh(ULandscapeComponent component)
    {
        // Read component properties
        var sectionBaseX = component.GetOrDefault("SectionBaseX", 0);
        var sectionBaseY = component.GetOrDefault("SectionBaseY", 0);
        var componentSizeQuads = component.GetOrDefault("ComponentSizeQuads", DefaultComponentSize);
        var componentName = component.Name ?? $"Landscape_{sectionBaseX}_{sectionBaseY}";

        // Extract height data (try UE4 path first, then UE5)
        var heightData = ExtractHeightData(component, componentSizeQuads);
        if (heightData == null)
        {
            _logger.Debug("No height data for component {Name} at ({X},{Y})",
                componentName, sectionBaseX, sectionBaseY);
            return null;
        }

        int gridSize = componentSizeQuads + 1;
        int expectedLength = gridSize * gridSize;

        if (heightData.Length < expectedLength)
        {
            _logger.Warning("Height data too small for component {Name}: {Actual} < {Expected}",
                componentName, heightData.Length, expectedLength);
            return null;
        }

        var mesh = BuildTerrainMesh(heightData, sectionBaseX, sectionBaseY, gridSize, componentName);
        return (mesh, componentName);
    }

    private ushort[]? ExtractHeightData(ULandscapeComponent component, int componentSizeQuads)
    {
        var grassData = component.GrassData;
        if (grassData == null)
        {
            _logger.Debug("GrassData is null for landscape component");
            return null;
        }

        // UE4 path: direct ushort[] HeightData
        if (grassData.HeightData is { Length: > 0 })
        {
            _logger.Debug("Using UE4 HeightData: {Length} values", grassData.HeightData.Length);
            return grassData.HeightData;
        }

        // UE5 path: interleaved HeightWeightData (byte[])
        if (grassData.HeightWeightData is { Length: > 0 })
        {
            var numElements = grassData.NumElements;
            if (numElements <= 0)
            {
                // Infer from grid size
                int gridSize = componentSizeQuads + 1;
                numElements = gridSize * gridSize;
            }

            int bytesNeeded = numElements * 2;
            if (grassData.HeightWeightData.Length < bytesNeeded)
            {
                _logger.Warning("HeightWeightData too small: {Actual} < {Needed} bytes",
                    grassData.HeightWeightData.Length, bytesNeeded);
                return null;
            }

            _logger.Debug("Using UE5 HeightWeightData: {Elements} elements from {Length} bytes",
                numElements, grassData.HeightWeightData.Length);

            var heights = new ushort[numElements];
            for (int i = 0; i < numElements; i++)
            {
                // Little-endian ushort pairs
                heights[i] = BitConverter.ToUInt16(grassData.HeightWeightData, i * 2);
            }
            return heights;
        }

        _logger.Debug("No height data available in GrassData");
        return null;
    }

    private ArrayMesh BuildTerrainMesh(
        ushort[] heightData,
        int sectionBaseX,
        int sectionBaseY,
        int gridSize,
        string meshName)
    {
        int vertexCount = gridSize * gridSize;
        int quadCount = (gridSize - 1) * (gridSize - 1);

        var positions = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var indices = new int[quadCount * 6];

        // Generate vertex positions in actor-local space
        // Actor Transform (including DrawScale3D) is applied by MeshInstance3D.Transform
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int idx = y * gridSize + x;
                ushort rawHeight = heightData[idx];

                // Local UE coordinates (unscaled — actor transform handles scale)
                float ueX = (sectionBaseX + x) * DefaultQuadSpacing;
                float ueY = (sectionBaseY + y) * DefaultQuadSpacing;
                float ueZ = (rawHeight - HeightMidpoint) / HeightScale;

                // UE → Godot coordinate conversion: (X, Z, -Y)
                positions[idx] = new Vector3(ueX, ueZ, -ueY);

                // UVs normalized across this component
                uvs[idx] = new Vector2(
                    (float)x / (gridSize - 1),
                    (float)y / (gridSize - 1));
            }
        }

        // Generate triangle indices (2 triangles per quad)
        // The -Y flip inherently converts UE CW → Godot CCW winding
        int triIdx = 0;
        for (int y = 0; y < gridSize - 1; y++)
        {
            for (int x = 0; x < gridSize - 1; x++)
            {
                int tl = y * gridSize + x;
                int tr = tl + 1;
                int bl = tl + gridSize;
                int br = bl + 1;

                indices[triIdx++] = tl;
                indices[triIdx++] = bl;
                indices[triIdx++] = tr;

                indices[triIdx++] = tr;
                indices[triIdx++] = bl;
                indices[triIdx++] = br;
            }
        }

        // Compute normals from height grid
        var normals = ComputeNormals(positions, gridSize);

        // Build ArrayMesh (same pattern as MeshExtractor.BuildArrayMesh)
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.ResourceName = meshName;

        _logger.Debug("Built terrain mesh {Name}: {Verts} vertices, {Tris} triangles",
            meshName, vertexCount, quadCount * 2);

        return mesh;
    }

    /// <summary>
    /// Computes smooth vertex normals from a height grid using finite differences.
    /// </summary>
    private static Vector3[] ComputeNormals(Vector3[] positions, int gridSize)
    {
        var normals = new Vector3[positions.Length];

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int idx = y * gridSize + x;

                // Sample neighboring heights using Godot-space Y (which is height)
                float hLeft = x > 0 ? positions[idx - 1].Y : positions[idx].Y;
                float hRight = x < gridSize - 1 ? positions[idx + 1].Y : positions[idx].Y;
                float hDown = y > 0 ? positions[idx - gridSize].Y : positions[idx].Y;
                float hUp = y < gridSize - 1 ? positions[idx + gridSize].Y : positions[idx].Y;

                // Spacing between samples in Godot space
                float spacingX = DefaultQuadSpacing; // X axis
                float spacingZ = DefaultQuadSpacing;  // Z axis (Godot Z = -UE Y)

                // Finite difference gradients
                float dhdx = (hRight - hLeft) / (2.0f * spacingX);
                float dhdz = (hUp - hDown) / (2.0f * spacingZ);

                // Normal vector: cross product of tangent vectors
                // Tangent X = (1, dhdx, 0), Tangent Z = (0, dhdz, 1)
                // Normal = (-dhdx, 1, -dhdz)
                normals[idx] = new Vector3(-dhdx, 1.0f, -dhdz).Normalized();
            }
        }

        return normals;
    }
}
