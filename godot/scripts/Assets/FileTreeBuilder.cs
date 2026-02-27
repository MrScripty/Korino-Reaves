// File Tree Builder - Project Directory Structure
//
// Builds a navigable tree of files and folders from an extracted PAK
// project directory. Groups UE asset triads (.uasset/.uexp/.ubulk)
// into single proxy nodes for a cleaner tree view.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Assets;

/// <summary>
/// Builds a file tree from a project directory, grouping UE asset
/// companions (.uexp, .ubulk) under their primary (.uasset, .umap).
/// </summary>
public sealed class FileTreeBuilder
{
    /// <summary>
    /// Extensions for the primary asset file in a UE asset group.
    /// </summary>
    private static readonly HashSet<string> PrimaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uasset", ".umap"
    };

    /// <summary>
    /// Extensions for companion files that accompany a primary asset.
    /// These are grouped with their primary and hidden from the tree.
    /// </summary>
    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".uexp", ".ubulk"
    };

    private readonly IAppLogger _logger;

    public FileTreeBuilder(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds a file tree from a directory.
    /// </summary>
    public TreeNode[] BuildFileTree(string rootPath)
    {
        var rootNodes = new List<TreeNode>();

        try
        {
            // Get top-level directories
            foreach (var dir in Directory.GetDirectories(rootPath).OrderBy(d => d))
            {
                var node = BuildDirectoryNode(dir, rootPath);
                rootNodes.Add(node);
            }

            // Get top-level files (group asset triads into proxy nodes)
            rootNodes.AddRange(BuildFileNodes(rootPath, rootPath));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error building file tree for: {Path}", rootPath);
        }

        return rootNodes.ToArray();
    }

    private TreeNode BuildDirectoryNode(string dirPath, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, dirPath);
        var name = Path.GetFileName(dirPath);
        var id = $"folder:{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";

        // Check if has children
        var hasChildren = Directory.EnumerateFileSystemEntries(dirPath).Any();

        // Build children (lazy - only immediate children)
        var children = new List<TreeNode>();

        foreach (var subDir in Directory.GetDirectories(dirPath).OrderBy(d => d))
        {
            children.Add(BuildDirectoryNode(subDir, rootPath));
        }

        children.AddRange(BuildFileNodes(dirPath, rootPath));

        return new TreeNode(
            id,
            name,
            TreeNodeTypes.Folder,
            hasChildren,
            children.Count > 0 ? children.ToArray() : null,
            null
        );
    }

    /// <summary>
    /// Checks whether a file has a known UE asset-related extension
    /// (primary or companion), including compound extensions like .uexp.bak.
    /// </summary>
    private static bool IsAssetRelatedFile(string filePath)
    {
        if (filePath.EndsWith(".uexp.bak", StringComparison.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(filePath);
        return PrimaryExtensions.Contains(ext) || CompanionExtensions.Contains(ext);
    }

    /// <summary>
    /// Gets the asset base name for grouping, stripping known extensions.
    /// Handles compound extensions like .uexp.bak.
    /// </summary>
    private static string GetAssetBaseName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".uexp.bak", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - ".uexp.bak".Length);
        return Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>
    /// Builds file nodes for a directory, grouping UE asset triads
    /// (.uasset + .uexp + .ubulk) into single proxy nodes.
    /// </summary>
    private List<TreeNode> BuildFileNodes(string dirPath, string rootPath)
    {
        var allFiles = Directory.GetFiles(dirPath).OrderBy(f => f).ToArray();

        // Separate asset-related files from other files
        var assetGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var standaloneFiles = new List<string>();

        foreach (var file in allFiles)
        {
            if (IsAssetRelatedFile(file))
            {
                var baseName = GetAssetBaseName(file);
                if (!assetGroups.TryGetValue(baseName, out var group))
                {
                    group = new List<string>();
                    assetGroups[baseName] = group;
                }
                group.Add(file);
            }
            else
            {
                standaloneFiles.Add(file);
            }
        }

        var nodes = new List<TreeNode>();

        // Build proxy nodes for asset groups
        foreach (var (baseName, group) in assetGroups)
        {
            var primary = group.FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f);
                return PrimaryExtensions.Contains(ext);
            });

            if (primary != null)
            {
                var companionCount = group.Count - 1;
                nodes.Add(BuildAssetGroupNode(primary, baseName, companionCount, rootPath));
            }
            else
            {
                // Orphaned companions without a primary — show individually
                foreach (var file in group)
                {
                    nodes.Add(BuildSingleFileNode(file, rootPath));
                }
            }
        }

        // Build individual nodes for non-asset files
        foreach (var file in standaloneFiles)
        {
            nodes.Add(BuildSingleFileNode(file, rootPath));
        }

        // Sort all file nodes alphabetically by display name
        nodes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return nodes;
    }

    /// <summary>
    /// Builds a single proxy node representing a UE asset group.
    /// The ID points to the primary file so selection/loading works unchanged.
    /// </summary>
    private TreeNode BuildAssetGroupNode(
        string primaryFile, string baseName, int companionCount, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, primaryFile);
        var id = $"file:{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
        var primaryExt = Path.GetExtension(primaryFile);

        var typeLabel = companionCount > 0
            ? $"{primaryExt} +{companionCount}"
            : primaryExt;

        return new TreeNode(
            id,
            baseName,
            TreeNodeTypes.File,
            false,
            null,
            new TreeNodeMetadata(null, typeLabel, null, null, null)
        );
    }

    /// <summary>
    /// Builds a tree node for a single non-grouped file.
    /// </summary>
    private TreeNode BuildSingleFileNode(string filePath, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath);
        var name = Path.GetFileName(filePath);
        var id = $"file:{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
        var extension = Path.GetExtension(filePath).TrimStart('.');

        return new TreeNode(
            id,
            name,
            TreeNodeTypes.File,
            false,
            null,
            new TreeNodeMetadata(null, extension, null, null, null)
        );
    }
}
