// GUI Selection Capability
//
// Capability contract for selecting and expanding nodes while synchronizing
// selection updates to the UI.

using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability for GUI selection and expansion.
/// </summary>
public interface IGuiSelectionCapability
{
    /// <summary>
    /// Gets the current selection state.
    /// </summary>
    SelectionState GetState();

    /// <summary>
    /// Selects a node and broadcasts the updated state.
    /// </summary>
    SelectionState SelectNode(string nodeId);

    /// <summary>
    /// Expands node IDs and broadcasts the updated state.
    /// </summary>
    SelectionState ExpandNodes(string[] nodeIds);

    /// <summary>
    /// Collapses node IDs and broadcasts the updated state.
    /// </summary>
    SelectionState CollapseNodes(string[] nodeIds);

    /// <summary>
    /// Collapses all nodes and broadcasts the updated state.
    /// </summary>
    SelectionState CollapseAll();
}
