// Sub-Level Discoverer
//
// Discovers related .umap sub-levels for a given primary level file.
// Uses three strategies: WorldTileInfo, directory prefix naming, and import references.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Rendering;

/// <summary>
/// Discovers related .umap sub-levels for a given primary level file.
/// </summary>
public sealed class SubLevelDiscoverer
{
    private readonly IAppLogger _logger;

    public SubLevelDiscoverer(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Given a primary .umap file, discovers all related sub-levels.
    /// Returns a list including the primary level itself (first entry, with zero offset).
    /// </summary>
    public SubLevelInfo[] DiscoverSubLevels(
        string primaryFullPath,
        string projectRootPath,
        EngineVersion? version)
    {
        var results = new List<SubLevelInfo>();
        var primaryRelative = Path.GetRelativePath(projectRootPath, primaryFullPath);
        var primaryName = Path.GetFileNameWithoutExtension(primaryFullPath);

        // Always include the primary level with zero offset
        results.Add(new SubLevelInfo(primaryRelative, primaryName, Vector3.Zero, SubLevelDiscoverySource.Primary));

        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryFullPath };

        // Strategy 1: WorldTileInfo scanning
        DiscoverViaWorldTileInfo(primaryFullPath, projectRootPath, version, results, discovered);

        // Strategy 2: Directory prefix scanning
        DiscoverViaDirectoryPrefix(primaryFullPath, projectRootPath, version, results, discovered);

        // Strategy 3: Import reference scanning (LevelStreaming)
        DiscoverViaImportReferences(primaryFullPath, projectRootPath, version, results, discovered);

        if (results.Count > 1)
        {
            _logger.Info("Discovered {Count} sub-levels for {Primary}: {Names}",
                results.Count - 1, primaryName,
                string.Join(", ", results.Skip(1).Select(r => $"{r.LevelName} ({r.Source})")));
        }

        return results.ToArray();
    }

    /// <summary>
    /// Scans sibling .umap files for WorldTileInfo whose ParentTilePackageName matches the primary level.
    /// </summary>
    private void DiscoverViaWorldTileInfo(
        string primaryFullPath, string projectRootPath,
        EngineVersion? version, List<SubLevelInfo> results, HashSet<string> discovered)
    {
        var directory = Path.GetDirectoryName(primaryFullPath);
        if (directory == null) return;

        var primaryName = Path.GetFileNameWithoutExtension(primaryFullPath);

        string[] umapFiles;
        try
        {
            umapFiles = Directory.GetFiles(directory, "*.umap");
        }
        catch (Exception ex)
        {
            _logger.Debug("Could not scan directory for WorldTileInfo: {Error}", ex.Message);
            return;
        }

        foreach (var umapPath in umapFiles)
        {
            if (discovered.Contains(umapPath)) continue;

            try
            {
                var asset = new UAsset(umapPath, version ?? EngineVersion.VER_UE4_27);
                if (asset.WorldTileInfo == null) continue;

                var tileInfo = asset.WorldTileInfo;
                var parentName = tileInfo.ParentTilePackageName?.Value;

                // Check if this tile's parent matches the primary level
                if (parentName != null && MatchesParentReference(parentName, primaryName))
                {
                    var offset = ConvertTilePosition(tileInfo.Position);
                    var relativePath = Path.GetRelativePath(projectRootPath, umapPath);
                    var levelName = Path.GetFileNameWithoutExtension(umapPath);

                    results.Add(new SubLevelInfo(relativePath, levelName, offset, SubLevelDiscoverySource.WorldTileInfo));
                    discovered.Add(umapPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("Could not read WorldTileInfo from {Path}: {Error}", umapPath, ex.Message);
            }
        }
    }

    /// <summary>
    /// Finds sibling .umap files whose name starts with the primary level's name + underscore.
    /// </summary>
    private void DiscoverViaDirectoryPrefix(
        string primaryFullPath, string projectRootPath,
        EngineVersion? version, List<SubLevelInfo> results, HashSet<string> discovered)
    {
        var directory = Path.GetDirectoryName(primaryFullPath);
        if (directory == null) return;

        var primaryName = Path.GetFileNameWithoutExtension(primaryFullPath);
        var prefix = primaryName + "_";

        string[] umapFiles;
        try
        {
            umapFiles = Directory.GetFiles(directory, "*.umap");
        }
        catch (Exception ex)
        {
            _logger.Debug("Could not scan directory for prefix matches: {Error}", ex.Message);
            return;
        }

        foreach (var umapPath in umapFiles)
        {
            if (discovered.Contains(umapPath)) continue;

            var siblingName = Path.GetFileNameWithoutExtension(umapPath);
            if (!siblingName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            // Try to read WorldTileInfo for position offset
            var offset = Vector3.Zero;
            try
            {
                var asset = new UAsset(umapPath, version ?? EngineVersion.VER_UE4_27);
                if (asset.WorldTileInfo?.Position != null)
                {
                    offset = ConvertTilePosition(asset.WorldTileInfo.Position);
                }
            }
            catch
            {
                // Use zero offset if header read fails
            }

            var relativePath = Path.GetRelativePath(projectRootPath, umapPath);
            results.Add(new SubLevelInfo(relativePath, siblingName, offset, SubLevelDiscoverySource.DirectoryScan));
            discovered.Add(umapPath);
        }
    }

    /// <summary>
    /// Scans the primary .umap's imports for LevelStreaming references to other packages.
    /// </summary>
    private void DiscoverViaImportReferences(
        string primaryFullPath, string projectRootPath,
        EngineVersion? version, List<SubLevelInfo> results, HashSet<string> discovered)
    {
        UAsset primaryAsset;
        try
        {
            primaryAsset = new UAsset(primaryFullPath, version ?? EngineVersion.VER_UE4_27);
        }
        catch (Exception ex)
        {
            _logger.Debug("Could not parse primary asset for import scan: {Error}", ex.Message);
            return;
        }

        var directory = Path.GetDirectoryName(primaryFullPath)!;

        foreach (var import in primaryAsset.Imports)
        {
            var className = import.ClassName?.Value?.Value;
            if (className is not ("LevelStreamingKismet" or "LevelStreamingDynamic" or "LevelStreaming"))
                continue;

            var referencedName = import.ObjectName?.Value?.Value;
            if (string.IsNullOrEmpty(referencedName)) continue;

            // Extract the last segment of the package path
            var lastSegment = referencedName.Split('/').Last();
            var candidatePath = Path.Combine(directory, lastSegment + ".umap");

            if (!File.Exists(candidatePath))
            {
                // Try searching more broadly in the project
                candidatePath = FindUmapByName(projectRootPath, lastSegment);
            }

            if (candidatePath == null || discovered.Contains(candidatePath)) continue;

            var offset = Vector3.Zero;
            try
            {
                var subAsset = new UAsset(candidatePath, version ?? EngineVersion.VER_UE4_27);
                if (subAsset.WorldTileInfo?.Position != null)
                    offset = ConvertTilePosition(subAsset.WorldTileInfo.Position);
            }
            catch
            {
                // Use zero offset
            }

            var relativePath = Path.GetRelativePath(projectRootPath, candidatePath);
            var levelName = Path.GetFileNameWithoutExtension(candidatePath);
            results.Add(new SubLevelInfo(relativePath, levelName, offset, SubLevelDiscoverySource.ImportReference));
            discovered.Add(candidatePath);
        }
    }

    /// <summary>
    /// Converts FWorldTileInfo.Position (int[3] in UE units) to Godot Vector3.
    /// UE(X,Y,Z) → Godot(X,Z,-Y), same as LevelExtractor.
    /// </summary>
    private static Vector3 ConvertTilePosition(int[] position)
    {
        if (position == null || position.Length < 3)
            return Vector3.Zero;
        return new Vector3(position[0], position[2], -position[1]);
    }

    /// <summary>
    /// Checks if a ParentTilePackageName references the given primary level name.
    /// ParentTilePackageName might be "/Game/Maps/MyMap" or just "MyMap".
    /// </summary>
    private static bool MatchesParentReference(string parentPackageName, string primaryLevelName)
    {
        var lastSegment = parentPackageName.Split('/').Last();
        return string.Equals(lastSegment, primaryLevelName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Searches the project directory for a .umap file matching the given name.
    /// </summary>
    private static string? FindUmapByName(string projectRootPath, string levelName)
    {
        try
        {
            var targetFilename = levelName + ".umap";
            var files = Directory.GetFiles(projectRootPath, targetFilename, SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }
        catch
        {
            return null;
        }
    }
}
