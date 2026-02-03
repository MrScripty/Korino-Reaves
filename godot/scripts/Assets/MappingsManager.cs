// Mappings Manager - .usmap Support
//
// Handles loading and caching of .usmap mapping files for unversioned assets.
// Mappings provide type information for assets that don't embed version data.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UAssetAPI.Unversioned;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Assets;

/// <summary>
/// Manages .usmap mappings files for unversioned asset support.
/// </summary>
public sealed class MappingsManager
{
    private static readonly ActivitySource ActivitySource = new("UAssetViewer.Assets.Mappings");

    private readonly IAppLogger _logger;
    private readonly Dictionary<string, Usmap> _cache = new();

    public MappingsManager(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads mappings from a .usmap file.
    /// </summary>
    public async Task<Usmap> LoadAsync(string path)
    {
        using var activity = ActivitySource.StartActivity("LoadMappings");
        activity?.SetTag("mappings.path", path);

        // Check cache first
        if (_cache.TryGetValue(path, out var cached))
        {
            _logger.Debug("Using cached mappings: {Path}", path);
            return cached;
        }

        _logger.Info("Loading mappings: {Path}", path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Mappings file not found", path);
        }

        try
        {
            var mappings = await Task.Run(() => new Usmap(path));

            _cache[path] = mappings;
            _logger.Info("Mappings loaded: {SchemaCount} schemas",
                mappings.Schemas.Count);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("mappings.schemaCount", mappings.Schemas.Count);

            return mappings;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.Error(ex, "Failed to load mappings: {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Attempts to find and load mappings for an asset.
    /// Searches for .usmap files in common locations relative to the asset.
    /// </summary>
    public async Task<Usmap?> LoadMappingsForAssetAsync(string assetPath)
    {
        using var activity = ActivitySource.StartActivity("LoadMappingsForAsset");
        activity?.SetTag("asset.path", assetPath);

        var directory = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        // Search for mappings files in order of preference
        var searchPaths = GetMappingsSearchPaths(directory);

        foreach (var searchPath in searchPaths)
        {
            if (File.Exists(searchPath))
            {
                _logger.Debug("Found mappings file: {Path}", searchPath);
                try
                {
                    return await LoadAsync(searchPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Failed to load mappings from {Path}: {Message}",
                        searchPath, ex.Message);
                }
            }
        }

        _logger.Debug("No mappings file found for asset: {Path}", assetPath);
        return null;
    }

    /// <summary>
    /// Clears the mappings cache.
    /// </summary>
    public void ClearCache()
    {
        _logger.Debug("Clearing mappings cache ({Count} entries)", _cache.Count);
        _cache.Clear();
    }

    /// <summary>
    /// Removes a specific mappings file from the cache.
    /// </summary>
    public void Invalidate(string path)
    {
        if (_cache.Remove(path))
        {
            _logger.Debug("Invalidated cached mappings: {Path}", path);
        }
    }

    /// <summary>
    /// Gets the number of cached mappings.
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Gets information about loaded mappings.
    /// </summary>
    public MappingsInfo? GetInfo(string path)
    {
        if (!_cache.TryGetValue(path, out var mappings))
        {
            return null;
        }

        return new MappingsInfo(
            Path: path,
            SchemaCount: mappings.Schemas.Count,
            EnumCount: mappings.Enums.Count
        );
    }

    private static IEnumerable<string> GetMappingsSearchPaths(string assetDirectory)
    {
        // Common locations for .usmap files

        // 1. Same directory as asset
        foreach (var file in GetUsmapFilesIn(assetDirectory))
        {
            yield return file;
        }

        // 2. Parent directory
        var parent = Path.GetDirectoryName(assetDirectory);
        if (!string.IsNullOrEmpty(parent))
        {
            foreach (var file in GetUsmapFilesIn(parent))
            {
                yield return file;
            }
        }

        // 3. Common game locations
        var current = assetDirectory;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
        {
            // Check for Mappings subdirectory
            var mappingsDir = Path.Combine(current, "Mappings");
            if (Directory.Exists(mappingsDir))
            {
                foreach (var file in GetUsmapFilesIn(mappingsDir))
                {
                    yield return file;
                }
            }

            // Check for common game structure directories
            var contentDir = Path.Combine(current, "Content");
            if (Directory.Exists(contentDir))
            {
                foreach (var file in GetUsmapFilesIn(current))
                {
                    yield return file;
                }
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static IEnumerable<string> GetUsmapFilesIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.usmap");
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            yield return file;
        }
    }
}

/// <summary>
/// Information about a loaded mappings file.
/// </summary>
public sealed record MappingsInfo(
    string Path,
    int SchemaCount,
    int EnumCount
);
