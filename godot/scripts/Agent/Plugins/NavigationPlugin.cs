// Navigation Plugin - Semantic Kernel functions for tree browsing
//
// Exposes asset tree navigation to the AI agent.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using UAssetViewer.Models;
using UAssetViewer.Services;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for navigating the asset tree structure.
/// </summary>
public sealed class NavigationPlugin
{
    private readonly ITreeService _treeService;

    public NavigationPlugin(ITreeService treeService)
    {
        _treeService = treeService;
    }

    [KernelFunction("get_root_nodes")]
    [Description("Gets the root nodes of the asset tree. Returns exports, imports, name map, and header sections.")]
    public TreeNode[] GetRootNodes()
    {
        return _treeService.GetRootNodes();
    }

    [KernelFunction("get_children")]
    [Description("Gets the child nodes of a specific tree node. Use this to drill into exports and their properties.")]
    public TreeNode[] GetChildren(
        [Description("ID of the parent node to expand")] string nodeId)
    {
        return _treeService.GetChildren(nodeId);
    }

    [KernelFunction("get_properties")]
    [Description("Gets all properties for a tree node (typically an export). Returns typed property values.")]
    public PropertyValue[] GetProperties(
        [Description("ID of the node to get properties for")] string nodeId)
    {
        return _treeService.GetProperties(nodeId);
    }

    [KernelFunction("search_tree")]
    [Description("Searches the asset tree for nodes matching a text query. Useful for finding specific properties or exports.")]
    public TreeNode[] SearchTree(
        [Description("Search query to match against node names")] string query)
    {
        return _treeService.Search(query);
    }

    [KernelFunction("get_path_to_node")]
    [Description("Gets the full path from the root to a specific node. Useful for understanding where a node lives in the hierarchy.")]
    public string[] GetPathToNode(
        [Description("ID of the target node")] string nodeId)
    {
        return _treeService.GetPathToNode(nodeId);
    }
}
