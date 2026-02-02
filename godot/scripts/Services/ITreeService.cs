// Tree Service Interface
//
// Defines the contract for building and navigating the asset tree.
// Implementations will use UAssetAPI to extract exports, imports, and properties.

using UAssetViewer.Models;

namespace UAssetViewer.Services;

/// <summary>
/// Service interface for asset tree operations.
/// Implementations are Godot-agnostic and can be unit tested.
/// </summary>
public interface ITreeService
{
    /// <summary>
    /// Gets the root nodes of the asset tree.
    /// </summary>
    /// <returns>Array of root tree nodes</returns>
    TreeNode[] GetRootNodes();

    /// <summary>
    /// Gets the children of a tree node.
    /// </summary>
    /// <param name="nodeId">ID of the parent node</param>
    /// <returns>Array of child tree nodes</returns>
    TreeNode[] GetChildren(string nodeId);

    /// <summary>
    /// Gets the properties for a tree node (typically an export).
    /// </summary>
    /// <param name="nodeId">ID of the node</param>
    /// <returns>Array of property values</returns>
    PropertyValue[] GetProperties(string nodeId);

    /// <summary>
    /// Searches the tree for nodes matching the query.
    /// </summary>
    /// <param name="query">Search query</param>
    /// <returns>Array of matching tree nodes</returns>
    TreeNode[] Search(string query);

    /// <summary>
    /// Gets the path from root to a specific node.
    /// </summary>
    /// <param name="nodeId">ID of the target node</param>
    /// <returns>Array of node IDs from root to target</returns>
    string[] GetPathToNode(string nodeId);
}
