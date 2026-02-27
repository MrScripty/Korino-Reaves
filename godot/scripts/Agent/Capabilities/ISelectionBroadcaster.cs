// Selection Broadcaster
//
// Abstraction for publishing selection updates to observers (e.g. UI via IPC).

using UAssetViewer.Models;

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Publishes selection state updates.
/// </summary>
public interface ISelectionBroadcaster
{
    /// <summary>
    /// Broadcasts the latest selection state.
    /// </summary>
    void Broadcast(SelectionState state);
}
