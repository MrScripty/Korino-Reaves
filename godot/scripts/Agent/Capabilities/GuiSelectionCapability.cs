// GUI Selection Capability
//
// Updates backend selection state and mirrors the result to observers.

using System;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Agent capability implementation for GUI selection and expansion.
/// </summary>
public sealed class GuiSelectionCapability : IGuiSelectionCapability
{
    private readonly ISelectionStateController _controller;
    private readonly ISelectionBroadcaster _broadcaster;
    private readonly IAppLogger _logger;

    public GuiSelectionCapability(
        ISelectionStateController controller,
        ISelectionBroadcaster broadcaster,
        IAppLogger logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public SelectionState GetState()
    {
        return _controller.CurrentState;
    }

    /// <inheritdoc />
    public SelectionState SelectNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node ID cannot be empty.", nameof(nodeId));
        }

        var state = _controller.SelectNode(nodeId);
        _broadcaster.Broadcast(state);
        _logger.Info("Agent selected node: {NodeId}", nodeId);
        return state;
    }

    /// <inheritdoc />
    public SelectionState ExpandNodes(string[] nodeIds)
    {
        if (nodeIds == null || nodeIds.Length == 0)
        {
            return _controller.CurrentState;
        }

        var state = _controller.ExpandNodes(nodeIds);
        _broadcaster.Broadcast(state);
        _logger.Info("Agent expanded {Count} node(s)", nodeIds.Length);
        return state;
    }

    /// <inheritdoc />
    public SelectionState CollapseNodes(string[] nodeIds)
    {
        if (nodeIds == null || nodeIds.Length == 0)
        {
            return _controller.CurrentState;
        }

        var state = _controller.CollapseNodes(nodeIds);
        _broadcaster.Broadcast(state);
        _logger.Info("Agent collapsed {Count} node(s)", nodeIds.Length);
        return state;
    }

    /// <inheritdoc />
    public SelectionState CollapseAll()
    {
        var state = _controller.CollapseAll();
        _broadcaster.Broadcast(state);
        _logger.Info("Agent collapsed all nodes");
        return state;
    }
}
