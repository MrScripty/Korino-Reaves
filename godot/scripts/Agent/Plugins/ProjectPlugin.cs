// Project Plugin - Semantic Kernel functions for project file exploration
//
// Exposes project tree traversal and search operations through the
// capability layer.

using System.ComponentModel;
using System.Threading;
using Microsoft.SemanticKernel;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for project file tree exploration.
/// </summary>
public sealed class ProjectPlugin
{
    private readonly IProjectExplorerCapability _projectExplorer;

    public ProjectPlugin(IProjectExplorerCapability projectExplorer)
    {
        _projectExplorer = projectExplorer;
    }

    [KernelFunction("get_current_project_path")]
    [Description("Gets the absolute path to the currently open project, or null if no project is open.")]
    public string? GetCurrentProjectPath()
    {
        return _projectExplorer.CurrentProjectPath;
    }

    [KernelFunction("get_project_root_nodes")]
    [Description("Gets root nodes of the current project file tree.")]
    public TreeNode[] GetProjectRootNodes(CancellationToken ct = default)
    {
        return _projectExplorer.GetRootNodes(ct);
    }

    [KernelFunction("get_project_children")]
    [Description("Gets child nodes for a project tree node ID.")]
    public TreeNode[] GetProjectChildren(
        [Description("Tree node ID, e.g. 'folder:Content'")] string nodeId,
        CancellationToken ct = default)
    {
        return _projectExplorer.GetChildren(nodeId, ct);
    }

    [KernelFunction("search_project_nodes")]
    [Description("Searches project tree nodes by name or node ID.")]
    public TreeNode[] SearchProjectNodes(
        [Description("Search query")] string query,
        [Description("Maximum results")] int limit = 100,
        CancellationToken ct = default)
    {
        return _projectExplorer.Search(query, limit, ct);
    }

    [KernelFunction("get_project_node")]
    [Description("Gets a project tree node by ID.")]
    public TreeNode? GetProjectNode(
        [Description("Tree node ID")] string nodeId,
        CancellationToken ct = default)
    {
        return _projectExplorer.GetNode(nodeId, ct);
    }
}
