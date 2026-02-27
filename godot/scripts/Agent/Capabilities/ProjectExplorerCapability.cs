// Project Explorer Capability
//
// Wraps the existing file-tree builder to expose project traversal operations
// for agent plugins without coupling to IPC handlers.

using System;
using System.Collections.Generic;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for project tree exploration.
/// </summary>
public sealed class ProjectExplorerCapability : IProjectExplorerCapability
{
    private const int DefaultSearchLimit = 100;
    private const int MaxSearchLimit = 1000;

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly FileTreeBuilder _fileTreeBuilder;
    private readonly IAppLogger _logger;

    public ProjectExplorerCapability(
        IProjectPathProvider projectPathProvider,
        FileTreeBuilder fileTreeBuilder,
        IAppLogger logger)
    {
        _projectPathProvider = projectPathProvider ?? throw new ArgumentNullException(nameof(projectPathProvider));
        _fileTreeBuilder = fileTreeBuilder ?? throw new ArgumentNullException(nameof(fileTreeBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string? CurrentProjectPath => _projectPathProvider.CurrentProjectPath;

    /// <inheritdoc />
    public TreeNode[] GetRootNodes()
    {
        return BuildTree();
    }

    /// <inheritdoc />
    public TreeNode[] GetChildren(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return Array.Empty<TreeNode>();
        }

        var node = GetNode(nodeId);
        return node?.Children ?? Array.Empty<TreeNode>();
    }

    /// <inheritdoc />
    public TreeNode[] Search(string query, int limit = DefaultSearchLimit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<TreeNode>();
        }

        var boundedLimit = ClampLimit(limit, DefaultSearchLimit, MaxSearchLimit);
        var tree = BuildTree();
        var matches = new List<TreeNode>(Math.Min(boundedLimit, DefaultSearchLimit));
        var normalizedQuery = query.Trim();

        foreach (var node in Traverse(tree))
        {
            if (matches.Count >= boundedLimit)
            {
                break;
            }

            if (ContainsIgnoreCase(node.Name, normalizedQuery) ||
                ContainsIgnoreCase(node.Id, normalizedQuery))
            {
                matches.Add(node);
            }
        }

        return matches.ToArray();
    }

    /// <inheritdoc />
    public TreeNode? GetNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        var tree = BuildTree();
        foreach (var node in Traverse(tree))
        {
            if (string.Equals(node.Id, nodeId, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    private TreeNode[] BuildTree()
    {
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Array.Empty<TreeNode>();
        }

        try
        {
            return _fileTreeBuilder.BuildFileTree(projectPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Project explorer failed to build tree for path: {Path}", projectPath);
            return Array.Empty<TreeNode>();
        }
    }

    private static IEnumerable<TreeNode> Traverse(TreeNode[] nodes)
    {
        var stack = new Stack<TreeNode>(nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            if (node.Children == null || node.Children.Length == 0)
            {
                continue;
            }

            for (int i = node.Children.Length - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private static bool ContainsIgnoreCase(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int ClampLimit(int requested, int fallback, int max)
    {
        if (requested <= 0)
        {
            return fallback;
        }

        return Math.Min(requested, max);
    }
}
