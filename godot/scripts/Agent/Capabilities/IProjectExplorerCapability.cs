// Project Explorer Capability
//
// Capability contract for traversing project files represented as tree nodes.

using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability for project tree exploration.
/// </summary>
public interface IProjectExplorerCapability
{
    /// <summary>
    /// Gets the path of the currently open project, or null when no project is open.
    /// </summary>
    string? CurrentProjectPath { get; }

    /// <summary>
    /// Gets root-level tree nodes for the current project.
    /// </summary>
    TreeNode[] GetRootNodes();

    /// <summary>
    /// Gets direct children for a tree node.
    /// </summary>
    TreeNode[] GetChildren(string nodeId);

    /// <summary>
    /// Searches nodes by display name and node ID.
    /// </summary>
    TreeNode[] Search(string query, int limit = 100);

    /// <summary>
    /// Resolves a node by ID.
    /// </summary>
    TreeNode? GetNode(string nodeId);
}
