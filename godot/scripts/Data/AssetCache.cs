// Asset Cache Manifest Manager
//
// Manages a .res pre-extraction cache so Godot's ResourceLoader can handle
// asset deduplication instead of loading from CUE4Parse every time.
// Stores a JSON manifest alongside extracted .res files in usr/cache/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Data;

// -----------------------------------------------------------------
// Entry types for manifest serialization
// -----------------------------------------------------------------

internal sealed record TextureEntry(
    [property: JsonPropertyName("resPath")] string ResPath,
    [property: JsonPropertyName("normalResPath")] string? NormalResPath
);

internal sealed record MeshEntry(
    [property: JsonPropertyName("resPath")] string ResPath,
    [property: JsonPropertyName("surfaceMaterials")] string?[] SurfaceMaterials
);

// -----------------------------------------------------------------
// Manifest JSON structure
// -----------------------------------------------------------------

internal sealed class CacheManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("eGameVersion")]
    public string EGameVersion { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("textures")]
    public Dictionary<string, TextureEntry> Textures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("meshes")]
    public Dictionary<string, MeshEntry> Meshes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("materials")]
    public Dictionary<string, string> Materials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// -----------------------------------------------------------------
// Cache manager
// -----------------------------------------------------------------

/// <summary>
/// Manages a .res pre-extraction cache with a JSON manifest.
/// Provides fast lookups for previously extracted textures, meshes,
/// and materials so Godot's ResourceLoader can load them directly.
/// </summary>
public sealed class AssetCache : IDisposable
{
    private readonly IAppLogger _logger;
    private bool _disposed;

    private CacheManifest? _manifest;
    private Dictionary<string, TextureEntry> _textures = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, MeshEntry> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _materials = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// True if a manifest is loaded with at least one entry.
    /// </summary>
    public bool HasCache =>
        _manifest != null &&
        (_textures.Count > 0 || _meshes.Count > 0 || _materials.Count > 0);

    /// <summary>
    /// Absolute path to the usr/cache/ directory.
    /// </summary>
    public string CacheDirectory { get; private set; } = "";

    public AssetCache(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =================================================================
    // Lifecycle
    // =================================================================

    /// <summary>
    /// Loads an existing manifest from disk if it exists.
    /// </summary>
    public void Open(string projectPath)
    {
        CacheDirectory = GetCacheDirectory(projectPath);
        var manifestPath = GetManifestPath();

        if (!File.Exists(manifestPath))
        {
            _logger.Debug("No cache manifest found at: {Path}", manifestPath);
            _manifest = null;
            _textures.Clear();
            _meshes.Clear();
            _materials.Clear();
            return;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            _manifest = JsonSerializer.Deserialize<CacheManifest>(json, JsonOptions);

            if (_manifest != null)
            {
                _textures = _manifest.Textures;
                _meshes = _manifest.Meshes;
                _materials = _manifest.Materials;
                _logger.Info(
                    "Cache manifest loaded: {Textures} textures, {Meshes} meshes, {Materials} materials",
                    _textures.Count, _meshes.Count, _materials.Count);
            }
            else
            {
                _textures.Clear();
                _meshes.Clear();
                _materials.Clear();
                _logger.Warning("Cache manifest deserialized as null: {Path}", manifestPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load cache manifest: {Path}", manifestPath);
            _manifest = null;
            _textures.Clear();
            _meshes.Clear();
            _materials.Clear();
        }
    }

    /// <summary>
    /// Creates a fresh cache directory and empty manifest.
    /// Deletes any existing cache contents.
    /// </summary>
    public void Create(string projectPath, string eGameVersion)
    {
        CacheDirectory = GetCacheDirectory(projectPath);

        // Delete existing cache directory to start fresh
        if (Directory.Exists(CacheDirectory))
        {
            Directory.Delete(CacheDirectory, true);
        }

        Directory.CreateDirectory(CacheDirectory);

        _textures = new Dictionary<string, TextureEntry>(StringComparer.OrdinalIgnoreCase);
        _meshes = new Dictionary<string, MeshEntry>(StringComparer.OrdinalIgnoreCase);
        _materials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _manifest = new CacheManifest
        {
            Version = 1,
            EGameVersion = eGameVersion,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            Textures = _textures,
            Meshes = _meshes,
            Materials = _materials
        };

        SaveManifest();
        _logger.Info("Cache created for version {Version} at: {Path}", eGameVersion, CacheDirectory);
    }

    /// <summary>
    /// Writes the current manifest state to disk.
    /// </summary>
    public void SaveManifest()
    {
        if (_manifest == null)
        {
            _logger.Warning("Cannot save manifest: no manifest loaded");
            return;
        }

        var manifestPath = GetManifestPath();
        var dir = Path.GetDirectoryName(manifestPath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(_manifest, JsonOptions);
        File.WriteAllText(manifestPath, json);
        _logger.Debug("Cache manifest saved: {Path}", manifestPath);
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _manifest = null;
            _textures.Clear();
            _meshes.Clear();
            _materials.Clear();
            _disposed = true;
        }
    }

    // =================================================================
    // Invalidation
    // =================================================================

    /// <summary>
    /// Checks whether the cache is valid for the given engine version.
    /// Returns true if a manifest exists and the version matches.
    /// </summary>
    public bool IsValid(string eGameVersion)
    {
        return _manifest != null &&
               string.Equals(_manifest.EGameVersion, eGameVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deletes the entire cache directory and clears in-memory state.
    /// </summary>
    public void Invalidate()
    {
        if (Directory.Exists(CacheDirectory))
        {
            Directory.Delete(CacheDirectory, true);
            _logger.Info("Cache invalidated: {Path}", CacheDirectory);
        }

        _manifest = null;
        _textures.Clear();
        _meshes.Clear();
        _materials.Clear();
    }

    // =================================================================
    // Lookups
    // =================================================================

    /// <summary>
    /// Gets the absolute .res path for a texture, or null on miss.
    /// </summary>
    public string? GetTexturePath(string assetPath)
    {
        if (_textures.TryGetValue(assetPath, out var entry))
        {
            return Path.Combine(CacheDirectory, entry.ResPath);
        }
        return null;
    }

    /// <summary>
    /// Gets the absolute .res path for a normal map variant, or null on miss.
    /// </summary>
    public string? GetNormalTexturePath(string assetPath)
    {
        if (_textures.TryGetValue(assetPath, out var entry) && entry.NormalResPath != null)
        {
            return Path.Combine(CacheDirectory, entry.NormalResPath);
        }
        return null;
    }

    /// <summary>
    /// Gets the absolute .res path for a mesh, or null on miss.
    /// </summary>
    public string? GetMeshPath(string assetPath)
    {
        if (_meshes.TryGetValue(assetPath, out var entry))
        {
            return Path.Combine(CacheDirectory, entry.ResPath);
        }
        return null;
    }

    /// <summary>
    /// Gets the absolute .res path for a material, or null on miss.
    /// </summary>
    public string? GetMaterialPath(string assetPath)
    {
        if (_materials.TryGetValue(assetPath, out var value))
        {
            return Path.Combine(CacheDirectory, value);
        }
        return null;
    }

    /// <summary>
    /// Gets the material asset paths per surface index for a mesh, or null on miss.
    /// </summary>
    public string?[]? GetMeshSurfaceMaterials(string assetPath)
    {
        if (_meshes.TryGetValue(assetPath, out var entry))
        {
            return entry.SurfaceMaterials;
        }
        return null;
    }

    // =================================================================
    // Registration
    // =================================================================

    /// <summary>
    /// Registers a texture in the cache manifest.
    /// </summary>
    public void RegisterTexture(string assetPath, string relativeResPath)
    {
        if (_textures.TryGetValue(assetPath, out var existing))
        {
            _textures[assetPath] = existing with { ResPath = relativeResPath };
        }
        else
        {
            _textures[assetPath] = new TextureEntry(relativeResPath, null);
        }
    }

    /// <summary>
    /// Registers a normal map texture variant in the cache manifest.
    /// </summary>
    public void RegisterNormalTexture(string assetPath, string relativeResPath)
    {
        if (_textures.TryGetValue(assetPath, out var existing))
        {
            _textures[assetPath] = existing with { NormalResPath = relativeResPath };
        }
        else
        {
            _textures[assetPath] = new TextureEntry(relativeResPath, relativeResPath);
        }
    }

    /// <summary>
    /// Registers a mesh with its surface material references in the cache manifest.
    /// </summary>
    public void RegisterMesh(string assetPath, string relativeResPath, string?[] surfaceMaterialPaths)
    {
        _meshes[assetPath] = new MeshEntry(relativeResPath, surfaceMaterialPaths);
    }

    /// <summary>
    /// Registers a material in the cache manifest.
    /// </summary>
    public void RegisterMaterial(string assetPath, string relativeResPath)
    {
        _materials[assetPath] = relativeResPath;
    }

    // =================================================================
    // Path helpers
    // =================================================================

    private string GetManifestPath()
    {
        return Path.Combine(CacheDirectory, "manifest.json");
    }

    private static string GetCacheDirectory(string projectPath)
    {
        var projectRoot = projectPath;
        var dirName = Path.GetFileName(projectPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "UE_data", StringComparison.OrdinalIgnoreCase))
        {
            projectRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
        }

        return Path.Combine(projectRoot, "usr", "cache");
    }
}
