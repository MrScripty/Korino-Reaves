// Message Handler Interface
//
// Defines the contract for IPC message handlers.
// Each handler is responsible for a specific message type.

using System.Threading.Tasks;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Interface for IPC message handlers.
/// Implementations handle specific message types and return responses.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Gets the message type this handler processes.
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Handles an incoming IPC message.
    /// </summary>
    /// <param name="message">The incoming message</param>
    /// <returns>Response message or null if no response needed</returns>
    Task<IpcMessage?> HandleAsync(IpcMessage message);

    /// <summary>
    /// Checks if this handler can process the given action.
    /// </summary>
    /// <param name="action">The action name</param>
    /// <returns>True if this handler supports the action</returns>
    bool CanHandle(string action);
}
