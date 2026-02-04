// Mesh Extractor - CUE4Parse to Godot ArrayMesh
//
// Extracts static and skeletal meshes from Unreal Engine assets using CUE4Parse
// and converts them to Godot ArrayMesh format for 3D preview.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Meshes.PSK;
using Godot;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Information about an extracted mesh.
/// </summary>
public sealed record MeshInfo(
    int VertexCount,
    int TriangleCount,
    int LodCount,
    int MaterialCount,
    bool HasNormals,
    bool HasUvs,
    bool HasColors
);

/// <summary>
/// Extracts meshes from UE assets and converts to Godot ArrayMesh.
/// </summary>
public sealed class MeshExtractor
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Rendering.Mesh");

    private readonly IAppLogger _logger;

    public MeshExtractor(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts a static mesh from a PAK file and converts to Godot ArrayMesh.
    /// </summary>
    public async Task<ArrayMesh?> ExtractStaticMeshFromPakAsync(
        DefaultFileProvider provider,
        string assetPath,
        int lodIndex = 0)
    {
        using var activity = ActivitySource.StartActivity("ExtractStaticMeshFromPak");
        activity?.SetTag("mesh.path", assetPath);
        activity?.SetTag("mesh.lod", lodIndex);

        _logger.Debug("Extracting static mesh: {Path}", assetPath);

        try
        {
            var mesh = await Task.Run(() => provider.LoadPackageObject<UStaticMesh>(assetPath));

            if (mesh == null)
            {
                _logger.Warning("Static mesh not found: {Path}", assetPath);
                return null;
            }

            return await ExtractStaticMeshAsync(mesh, lodIndex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract static mesh: {Path}", assetPath);
            throw;
        }
    }

    /// <summary>
    /// Extracts a Godot ArrayMesh from a CUE4Parse UStaticMesh.
    /// </summary>
    public async Task<ArrayMesh?> ExtractStaticMeshAsync(UStaticMesh mesh, int lodIndex = 0)
    {
        using var activity = ActivitySource.StartActivity("ExtractStaticMesh");
        activity?.SetTag("mesh.name", mesh.Name);

        try
        {
            // Convert to exportable format
            var exported = await Task.Run(() =>
            {
                mesh.TryConvert(out var converted);
                return converted;
            });

            if (exported == null)
            {
                _logger.Warning("Failed to convert static mesh: {Name}", mesh.Name);
                return null;
            }

            // Get LOD data
            if (lodIndex >= exported.LODs.Count)
            {
                _logger.Warning("LOD {Index} not available, using LOD 0", lodIndex);
                lodIndex = 0;
            }

            var lod = exported.LODs[lodIndex];
            var arrayMesh = BuildArrayMesh(lod, mesh.Name);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return arrayMesh;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract static mesh: {Name}", mesh.Name);
            throw;
        }
    }

    /// <summary>
    /// Extracts a skeletal mesh from a PAK file and converts to Godot ArrayMesh.
    /// Note: This extracts geometry only, not skeleton/animation data.
    /// </summary>
    public async Task<ArrayMesh?> ExtractSkeletalMeshFromPakAsync(
        DefaultFileProvider provider,
        string assetPath,
        int lodIndex = 0)
    {
        using var activity = ActivitySource.StartActivity("ExtractSkeletalMeshFromPak");
        activity?.SetTag("mesh.path", assetPath);

        _logger.Debug("Extracting skeletal mesh: {Path}", assetPath);

        try
        {
            var mesh = await Task.Run(() => provider.LoadPackageObject<USkeletalMesh>(assetPath));

            if (mesh == null)
            {
                _logger.Warning("Skeletal mesh not found: {Path}", assetPath);
                return null;
            }

            return await ExtractSkeletalMeshAsync(mesh, lodIndex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract skeletal mesh: {Path}", assetPath);
            throw;
        }
    }

    /// <summary>
    /// Extracts a Godot ArrayMesh from a CUE4Parse USkeletalMesh.
    /// </summary>
    public async Task<ArrayMesh?> ExtractSkeletalMeshAsync(USkeletalMesh mesh, int lodIndex = 0)
    {
        using var activity = ActivitySource.StartActivity("ExtractSkeletalMesh");
        activity?.SetTag("mesh.name", mesh.Name);

        try
        {
            // Convert to exportable format
            var exported = await Task.Run(() =>
            {
                mesh.TryConvert(out var converted);
                return converted;
            });

            if (exported == null)
            {
                _logger.Warning("Failed to convert skeletal mesh: {Name}", mesh.Name);
                return null;
            }

            // Get LOD data
            if (lodIndex >= exported.LODs.Count)
            {
                _logger.Warning("LOD {Index} not available, using LOD 0", lodIndex);
                lodIndex = 0;
            }

            var lod = exported.LODs[lodIndex];
            var arrayMesh = BuildArrayMeshFromSkeletal(lod, mesh.Name);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return arrayMesh;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to extract skeletal mesh: {Name}", mesh.Name);
            throw;
        }
    }

    /// <summary>
    /// Gets information about a static mesh without fully extracting it.
    /// </summary>
    public MeshInfo? GetStaticMeshInfo(UStaticMesh mesh)
    {
        if (mesh == null)
        {
            return null;
        }

        mesh.TryConvert(out var converted);
        if (converted == null || converted.LODs.Count == 0)
        {
            return null;
        }

        var lod = converted.LODs[0];
        var sections = lod.Sections?.Value;
        var section = sections?.FirstOrDefault();

        return new MeshInfo(
            VertexCount: lod.NumVerts,
            TriangleCount: section?.NumFaces ?? 0,
            LodCount: converted.LODs.Count,
            MaterialCount: sections?.Length ?? 0,
            HasNormals: true,
            HasUvs: lod.NumTexCoords > 0,
            HasColors: lod.VertexColors != null
        );
    }

    private ArrayMesh BuildArrayMesh(CStaticMeshLod lod, string meshName)
    {
        var arrayMesh = new ArrayMesh();

        // Process each section (material slot)
        var sections = lod.Sections?.Value;
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var arrays = BuildSurfaceArrays(
                    lod.Verts,
                    lod.Indices!.Value,
                    section,
                    lod.NumTexCoords,
                    lod.VertexColors
                );

                if (arrays != null)
                {
                    arrayMesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                }
            }
        }

        arrayMesh.ResourceName = meshName;
        return arrayMesh;
    }

    private ArrayMesh BuildArrayMeshFromSkeletal(CSkelMeshLod lod, string meshName)
    {
        var arrayMesh = new ArrayMesh();

        // Process each section
        var sections = lod.Sections?.Value;
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var arrays = BuildSurfaceArraysFromSkeletal(
                    lod.Verts,
                    lod.Indices!.Value,
                    section,
                    lod.NumTexCoords
                );

                if (arrays != null)
                {
                    arrayMesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
                }
            }
        }

        arrayMesh.ResourceName = meshName;
        return arrayMesh;
    }

    private Godot.Collections.Array? BuildSurfaceArrays(
        CMeshVertex[]? vertices,
        FRawStaticIndexBuffer indices,
        CMeshSection section,
        int numTexCoords,
        CUE4Parse.UE4.Objects.Core.Math.FColor[]? vertexColors)
    {
        if (vertices == null)
        {
            return null;
        }

        var firstIndex = section.FirstIndex;
        var numFaces = section.NumFaces;

        if (numFaces == 0)
        {
            return null;
        }

        // Collect unique vertex indices for this section
        var indexList = new List<int>();
        var vertexMap = new Dictionary<int, int>();
        var mappedVertices = new List<CMeshVertex>();

        for (int i = 0; i < numFaces * 3; i++)
        {
            var originalIndex = indices[firstIndex + i];

            if (!vertexMap.TryGetValue(originalIndex, out var mappedIndex))
            {
                mappedIndex = mappedVertices.Count;
                vertexMap[originalIndex] = mappedIndex;
                mappedVertices.Add(vertices[originalIndex]);
            }

            indexList.Add(mappedIndex);
        }

        // Build Godot arrays
        var positionArray = new Vector3[mappedVertices.Count];
        var normalArray = new Vector3[mappedVertices.Count];
        var uvArray = numTexCoords > 0 ? new Vector2[mappedVertices.Count] : null;
        var colorArray = vertexColors != null ? new Color[mappedVertices.Count] : null;

        for (int i = 0; i < mappedVertices.Count; i++)
        {
            var vert = mappedVertices[i];

            // Position (swap Y and Z for Godot coordinate system)
            positionArray[i] = new Vector3(
                vert.Position.X,
                vert.Position.Z,
                -vert.Position.Y
            );

            // Normal
            normalArray[i] = new Vector3(
                vert.Normal.X,
                vert.Normal.Z,
                -vert.Normal.Y
            );

            // UV
            if (uvArray != null && numTexCoords > 0)
            {
                uvArray[i] = new Vector2(vert.UV.U, vert.UV.V);
            }

            // Vertex color
            if (colorArray != null && vertexColors != null)
            {
                var originalIndex = vertexMap.First(kv => kv.Value == i).Key;
                if (originalIndex < vertexColors.Length)
                {
                    var c = vertexColors[originalIndex];
                    colorArray[i] = new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
                }
            }
        }

        // Build index array
        var indexArray = indexList.ToArray();

        // Create Godot surface array
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);

        arrays[(int)Godot.Mesh.ArrayType.Vertex] = positionArray;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normalArray;

        if (uvArray != null)
        {
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvArray;
        }

        if (colorArray != null)
        {
            arrays[(int)Godot.Mesh.ArrayType.Color] = colorArray;
        }

        arrays[(int)Godot.Mesh.ArrayType.Index] = indexArray;

        return arrays;
    }

    private Godot.Collections.Array? BuildSurfaceArraysFromSkeletal(
        CSkelMeshVertex[]? vertices,
        FRawStaticIndexBuffer indices,
        CMeshSection section,
        int numTexCoords)
    {
        if (vertices == null)
        {
            return null;
        }

        var firstIndex = section.FirstIndex;
        var numFaces = section.NumFaces;

        if (numFaces == 0)
        {
            return null;
        }

        // Collect unique vertex indices
        var indexList = new List<int>();
        var vertexMap = new Dictionary<int, int>();
        var mappedVertices = new List<CSkelMeshVertex>();

        for (int i = 0; i < numFaces * 3; i++)
        {
            var originalIndex = indices[firstIndex + i];

            if (!vertexMap.TryGetValue(originalIndex, out var mappedIndex))
            {
                mappedIndex = mappedVertices.Count;
                vertexMap[originalIndex] = mappedIndex;
                mappedVertices.Add(vertices[originalIndex]);
            }

            indexList.Add(mappedIndex);
        }

        // Build arrays
        var positionArray = new Vector3[mappedVertices.Count];
        var normalArray = new Vector3[mappedVertices.Count];
        var uvArray = numTexCoords > 0 ? new Vector2[mappedVertices.Count] : null;

        for (int i = 0; i < mappedVertices.Count; i++)
        {
            var vert = mappedVertices[i];

            positionArray[i] = new Vector3(
                vert.Position.X,
                vert.Position.Z,
                -vert.Position.Y
            );

            normalArray[i] = new Vector3(
                vert.Normal.X,
                vert.Normal.Z,
                -vert.Normal.Y
            );

            if (uvArray != null && numTexCoords > 0)
            {
                uvArray[i] = new Vector2(vert.UV.U, vert.UV.V);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);

        arrays[(int)Godot.Mesh.ArrayType.Vertex] = positionArray;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normalArray;

        if (uvArray != null)
        {
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvArray;
        }

        arrays[(int)Godot.Mesh.ArrayType.Index] = indexList.ToArray();

        return arrays;
    }
}
