// IPC Selection Broadcaster
//
// Publishes selection updates to the frontend through IpcDispatcher.

using System;
using UAssetViewer.Bridge;
using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Sends selection state updates via IPC.
/// </summary>
public sealed class IpcSelectionBroadcaster : ISelectionBroadcaster
{
    private readonly IpcDispatcher _dispatcher;

    public IpcSelectionBroadcaster(IpcDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public void Broadcast(SelectionState state)
    {
        _dispatcher.Send(MessageTypes.Selection, "update", state);
    }
}
