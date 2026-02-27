// GUI Plugin - Semantic Kernel functions for GUI selection control
//
// Exposes selection and expansion operations through the capability layer.

using System.ComponentModel;
using Microsoft.SemanticKernel;
using UAssetViewer.Agent;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for GUI selection state operations.
/// </summary>
public sealed class GuiPlugin
{
    private readonly IGuiSelectionCapability _guiSelection;
    private readonly AgentExecutionPolicy _policy;

    public GuiPlugin(IGuiSelectionCapability guiSelection, AgentExecutionPolicy policy)
    {
        _guiSelection = guiSelection;
        _policy = policy;
    }

    [KernelFunction("get_selection_state")]
    [Description("Gets the current GUI selection and expansion state.")]
    public SelectionState GetSelectionState()
    {
        return _guiSelection.GetState();
    }

    [KernelFunction("select_node")]
    [Description("Selects a node in the GUI by node ID.")]
    public SelectionState SelectNode(
        [Description("Node ID, e.g. 'file:Content/Foo.uasset'")] string nodeId)
    {
        _policy.EnsureGuiMutationAllowed("select_node");
        return _guiSelection.SelectNode(nodeId);
    }

    [KernelFunction("expand_node")]
    [Description("Expands one node in the GUI by node ID.")]
    public SelectionState ExpandNode(
        [Description("Node ID")] string nodeId)
    {
        _policy.EnsureGuiMutationAllowed("expand_node");
        return _guiSelection.ExpandNodes(new[] { nodeId });
    }

    [KernelFunction("collapse_node")]
    [Description("Collapses one node in the GUI by node ID.")]
    public SelectionState CollapseNode(
        [Description("Node ID")] string nodeId)
    {
        _policy.EnsureGuiMutationAllowed("collapse_node");
        return _guiSelection.CollapseNodes(new[] { nodeId });
    }

    [KernelFunction("collapse_all_nodes")]
    [Description("Collapses all expanded nodes in the GUI.")]
    public SelectionState CollapseAllNodes()
    {
        _policy.EnsureGuiMutationAllowed("collapse_all_nodes");
        return _guiSelection.CollapseAll();
    }
}
