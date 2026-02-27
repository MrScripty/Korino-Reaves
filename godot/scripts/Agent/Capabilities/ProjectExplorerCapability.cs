// Project Explorer Capability
//
// Wraps the existing file-tree builder to expose project traversal operations
// for agent plugins without coupling to IPC handlers.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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

    private readonly IProjectPathProvider _projectPathProvider;
    private readonly FileTreeBuilder _fileTreeBuilder;
    private readonly IAppLogger _logger;
    private readonly AgentExecutionPolicy _policy;

    public ProjectExplorerCapability(
        IProjectPathProvider projectPathProvider,
        FileTreeBuilder fileTreeBuilder,
        IAppLogger logger,
        AgentExecutionPolicy? policy = null)
    {
        _projectPathProvider = projectPathProvider ?? throw new ArgumentNullException(nameof(projectPathProvider));
        _fileTreeBuilder = fileTreeBuilder ?? throw new ArgumentNullException(nameof(fileTreeBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? AgentExecutionPolicy.ReadOnlyDefault;
    }

    /// <inheritdoc />
    public string? CurrentProjectPath => _projectPathProvider.CurrentProjectPath;

    /// <inheritdoc />
    public TreeNode[] GetRootNodes(CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var nodes = BuildTree(ct);
            LogTelemetry("getRootNodes", started.ElapsedMilliseconds, resultCount: nodes.Length);
            return nodes;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getRootNodes", started.ElapsedMilliseconds, cancelled: true);
            throw;
        }
    }

    /// <inheritdoc />
    public TreeNode[] GetChildren(string nodeId, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            LogTelemetry("getChildren", started.ElapsedMilliseconds, resultCount: 0);
            return Array.Empty<TreeNode>();
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var node = GetNode(nodeId, ct);
            var children = node?.Children ?? Array.Empty<TreeNode>();
            LogTelemetry("getChildren", started.ElapsedMilliseconds, resultCount: children.Length);
            return children;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getChildren", started.ElapsedMilliseconds, cancelled: true);
            throw;
        }
    }

    /// <inheritdoc />
    public TreeNode[] Search(string query, int limit = DefaultSearchLimit, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(query))
        {
            LogTelemetry("search", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: 0, resultCount: 0);
            return Array.Empty<TreeNode>();
        }

        var boundedLimit = _policy.ClampProjectSearchLimit(limit, DefaultSearchLimit);
        try
        {
            ct.ThrowIfCancellationRequested();
            var tree = BuildTree(ct);
            var matches = new List<TreeNode>(Math.Min(boundedLimit, DefaultSearchLimit));
            var normalizedQuery = query.Trim();

            foreach (var node in Traverse(tree, ct))
            {
                ct.ThrowIfCancellationRequested();
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

            var result = matches.ToArray();
            LogTelemetry("search", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: boundedLimit, resultCount: result.Length);
            return result;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("search", started.ElapsedMilliseconds, requestedLimit: limit, boundedLimit: boundedLimit, cancelled: true);
            throw;
        }
    }

    /// <inheritdoc />
    public TreeNode? GetNode(string nodeId, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            LogTelemetry("getNode", started.ElapsedMilliseconds, resultCount: 0);
            return null;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var tree = BuildTree(ct);
            foreach (var node in Traverse(tree, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(node.Id, nodeId, StringComparison.Ordinal))
                {
                    LogTelemetry("getNode", started.ElapsedMilliseconds, resultCount: 1);
                    return node;
                }
            }

            LogTelemetry("getNode", started.ElapsedMilliseconds, resultCount: 0);
            return null;
        }
        catch (OperationCanceledException)
        {
            LogTelemetry("getNode", started.ElapsedMilliseconds, cancelled: true);
            throw;
        }
    }

    private TreeNode[] BuildTree(CancellationToken ct)
    {
        var projectPath = _projectPathProvider.CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Array.Empty<TreeNode>();
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            return _fileTreeBuilder.BuildFileTree(projectPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Project explorer failed to build tree for path: {Path}", projectPath);
            return Array.Empty<TreeNode>();
        }
    }

    private static IEnumerable<TreeNode> Traverse(TreeNode[] nodes, CancellationToken ct)
    {
        var stack = new Stack<TreeNode>(nodes);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
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

    private void LogTelemetry(
        string operation,
        long durationMs,
        int? requestedLimit = null,
        int? boundedLimit = null,
        int? resultCount = null,
        bool cancelled = false)
    {
        var safeRequestedLimit = requestedLimit ?? -1;
        var safeBoundedLimit = boundedLimit ?? -1;
        var safeResultCount = resultCount ?? -1;

        _logger.Info(
            "Agent capability telemetry: capability={Capability} operation={Operation} durationMs={DurationMs} requestedLimit={RequestedLimit} boundedLimit={BoundedLimit} resultCount={ResultCount} cancelled={Cancelled}",
            nameof(ProjectExplorerCapability),
            operation,
            durationMs,
            safeRequestedLimit,
            safeBoundedLimit,
            safeResultCount,
            cancelled);
    }
}
