// Asset Loader - UAssetAPI Wrapper
//
// Handles loading and saving of .uasset files using UAssetAPI.
// Supports multiple Unreal Engine versions and .usmap mappings.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets;

/// <summary>
/// Wraps UAssetAPI for loading and saving .uasset files.
/// </summary>
public sealed class AssetLoader
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Assets.Loader");

    private readonly IAppLogger _logger;

    public AssetLoader(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads an asset from the specified path.
    /// </summary>
    /// <param name="path">Path to the .uasset file</param>
    /// <param name="mappings">Optional .usmap mappings</param>
    /// <returns>The loaded UAsset</returns>
    public Task<UAsset> LoadAsync(string path, Usmap? mappings = null, EngineVersion? version = null)
    {
        return Task.Run(() => Load(path, mappings, version));
    }

    /// <summary>
    /// Loads an asset synchronously.
    /// </summary>
    public UAsset Load(string path, Usmap? mappings = null, EngineVersion? version = null)
    {
        using var activity = ActivitySource.StartActivity("Load");
        activity?.SetTag("asset.path", path);

        _logger.Debug("Loading asset from: {Path}", path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Asset file not found: {path}", path);
        }

        try
        {
            // Use provided version or fall back to detection heuristic
            var effectiveVersion = version ?? DetectEngineVersion(path);
            activity?.SetTag("asset.version", effectiveVersion.ToString());

            _logger.Debug("Using engine version: {Version}", effectiveVersion);

            // Create UAsset with mappings if provided
            UAsset asset;
            if (mappings != null)
            {
                asset = new UAsset(path, effectiveVersion, mappings);
                _logger.Debug("Loaded with mappings");
            }
            else
            {
                asset = new UAsset(path, effectiveVersion);
            }

            // Handle .uexp file if present
            var uexpPath = Path.ChangeExtension(path, ".uexp");
            if (File.Exists(uexpPath))
            {
                _logger.Debug("Found companion .uexp file");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("asset.exports", asset.Exports.Count);
            activity?.SetTag("asset.imports", asset.Imports.Count);

            return asset;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to load asset from: {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Saves an asset to the specified path.
    /// </summary>
    public Task SaveAsync(UAsset asset, string path)
    {
        return Task.Run(() => Save(asset, path));
    }

    /// <summary>
    /// Saves an asset synchronously.
    /// </summary>
    public void Save(UAsset asset, string path)
    {
        using var activity = ActivitySource.StartActivity("Save");
        activity?.SetTag("asset.path", path);

        if (asset == null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        _logger.Debug("Saving asset to: {Path}", path);

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save the asset
            asset.Write(path);

            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.Debug("Asset saved successfully");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to save asset to: {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Loads an asset from PAK file contents.
    /// </summary>
    public UAsset LoadFromBytes(byte[] data, string name, EngineVersion version, Usmap? mappings = null)
    {
        using var activity = ActivitySource.StartActivity("LoadFromBytes");
        activity?.SetTag("asset.name", name);
        activity?.SetTag("asset.size", data.Length);

        _logger.Debug("Loading asset from bytes: {Name} ({Size} bytes)", name, data.Length);

        try
        {
            using var stream = new MemoryStream(data);
            using var reader = new AssetBinaryReader(stream);

            UAsset asset;
            if (mappings != null)
            {
                asset = new UAsset(reader, version, mappings);
            }
            else
            {
                asset = new UAsset(reader, version);
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return asset;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to load asset from bytes: {Name}", name);
            throw;
        }
    }

    /// <summary>
    /// Attempts to detect the engine version from the asset file.
    /// </summary>
    private EngineVersion DetectEngineVersion(string path)
    {
        try
        {
            // Read just the header to detect version
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            // UAsset files start with magic number
            var magic = reader.ReadUInt32();
            if (magic != 0x9E2A83C1) // PACKAGE_FILE_TAG
            {
                _logger.Warning("Invalid magic number in asset file, defaulting to UE5");
                return EngineVersion.VER_UE5_3;
            }

            // Skip to version info
            var legacyFileVersion = reader.ReadInt32();
            if (legacyFileVersion < 0)
            {
                // Unversioned - need to use mappings
                _logger.Debug("Detected unversioned asset");
            }

            // Try to determine version from legacy version number
            // This is a simplified heuristic
            // LegacyFileVersion indicates serialization format, not exact engine version:
            // -8 and below = UE5 (has ObjectVersionUE5 field)
            // -7 = UE4.26-4.27 (licensee versioning changes)
            // -6 = UE4.14-4.25 (optimized custom version format)
            // -5 = UE4.8-4.13 (graceful fail support)
            // -4 and above = early UE4
            if (legacyFileVersion <= -8)
            {
                return EngineVersion.VER_UE5_3;
            }
            else if (legacyFileVersion == -7)
            {
                return EngineVersion.VER_UE4_27;
            }
            else if (legacyFileVersion == -6)
            {
                return EngineVersion.VER_UE4_25;
            }
            else if (legacyFileVersion == -5)
            {
                return EngineVersion.VER_UE4_13;
            }
            else
            {
                return EngineVersion.VER_UE4_0;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Failed to detect engine version, defaulting to UE5.3: {Message}", ex.Message);
            return EngineVersion.VER_UE5_3;
        }
    }

    /// <summary>
    /// Validates that an asset can be loaded from the given path.
    /// </summary>
    public bool CanLoad(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 4)
            {
                return false;
            }

            var magic = reader.ReadUInt32();
            return magic == 0x9E2A83C1; // PACKAGE_FILE_TAG
        }
        catch
        {
            return false;
        }
    }
}
