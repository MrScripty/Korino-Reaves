# Handlers

## Purpose

IPC message handlers that process specific message types.
Each handler implements `IMessageHandler` and is registered with the IpcDispatcher.

## Contents

- `IMessageHandler.cs` - Handler interface contract
- `TestHandler.cs` - Ping/pong test messages for IPC verification
- `AssetHandler.cs` - Asset operations (stub, returns mock data)
- `TreeHandler.cs` - Tree navigation (stub, returns mock data)
- `PropertyHandler.cs` - Property editing (stub, returns mock data)
- `SelectionHandler.cs` - Selection state management

## Design Decisions

- **One Handler Per Type**: Each message type (asset, tree, property, etc.) has
  exactly one handler. This keeps routing simple and predictable.

- **Async by Default**: All handlers are async to support I/O operations without
  blocking the CEF message pump.

- **Stub Pattern**: Handlers for Asset, Tree, and Property are stubs that return
  mock data. The Asset Agent will replace these with real implementations using
  UAssetAPI. This allows parallel development.

- **Response Optional**: Handlers return `IpcMessage?` - null means no response
  needed. This supports fire-and-forget messages.

## Handler Interface

```csharp
public interface IMessageHandler
{
    // The message type this handler processes
    string MessageType { get; }

    // Check if this handler supports the given action
    bool CanHandle(string action);

    // Process the message and optionally return a response
    Task<IpcMessage?> HandleAsync(IpcMessage message);
}
```

## Adding a New Handler

1. Create a new class implementing `IMessageHandler`
2. Set `MessageType` to your message category
3. Implement `CanHandle` to list supported actions
4. Implement `HandleAsync` with your logic
5. Register in `IpcDispatcher.RegisterDefaultHandlers()` or manually

Example:
```csharp
public sealed class DiffHandler : IMessageHandler
{
    public string MessageType => MessageTypes.Diff;

    public bool CanHandle(string action)
    {
        return action is "compare" or "getChanges";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        // Handle message...
    }
}
```
