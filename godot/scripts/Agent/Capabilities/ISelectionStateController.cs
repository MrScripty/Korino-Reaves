// Selection State Controller
//
// Abstraction for mutating and reading tree selection state.

using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Controls selection and expansion state.
/// </summary>
public interface ISelectionStateController
{
    /// <summary>
    /// Gets the current selection state.
    /// </summary>
    SelectionState CurrentState { get; }

    /// <summary>
    /// Selects a node.
    /// </summary>
    SelectionState SelectNode(string? nodeId);

    /// <summary>
    /// Expands node IDs.
    /// </summary>
    SelectionState ExpandNodes(string[] nodeIds);

    /// <summary>
    /// Collapses node IDs.
    /// </summary>
    SelectionState CollapseNodes(string[] nodeIds);

    /// <summary>
    /// Collapses all nodes.
    /// </summary>
    SelectionState CollapseAll();
}
