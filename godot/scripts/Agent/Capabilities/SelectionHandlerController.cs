// Selection Handler Controller
//
// Adapter that bridges selection capability operations to SelectionHandler.

using System;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Selection controller backed by <see cref="SelectionHandler"/>.
/// </summary>
public sealed class SelectionHandlerController : ISelectionStateController
{
    private readonly SelectionHandler _selectionHandler;

    public SelectionHandlerController(SelectionHandler selectionHandler)
    {
        _selectionHandler = selectionHandler ?? throw new ArgumentNullException(nameof(selectionHandler));
    }

    /// <inheritdoc />
    public SelectionState CurrentState => _selectionHandler.CurrentState;

    /// <inheritdoc />
    public SelectionState SelectNode(string? nodeId)
    {
        return _selectionHandler.SelectNode(nodeId);
    }

    /// <inheritdoc />
    public SelectionState ExpandNodes(string[] nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        return _selectionHandler.ExpandIds(nodeIds);
    }

    /// <inheritdoc />
    public SelectionState CollapseNodes(string[] nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        return _selectionHandler.CollapseIds(nodeIds);
    }

    /// <inheritdoc />
    public SelectionState CollapseAll()
    {
        return _selectionHandler.CollapseAll();
    }
}
