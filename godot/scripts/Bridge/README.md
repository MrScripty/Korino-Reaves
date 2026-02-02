# Bridge

## Purpose

IPC (Inter-Process Communication) bridge between the C# backend and Svelte frontend.
Routes messages to appropriate handlers and manages bidirectional communication.

## Contents

- `IpcDispatcher.cs` - Central message router and handler registry
- `Handlers/` - Directory containing message handlers
  - `IMessageHandler.cs` - Handler interface
  - `TestHandler.cs` - Ping/pong test handler
  - `AssetHandler.cs` - Asset operations (stub)
  - `TreeHandler.cs` - Tree navigation (stub)
  - `PropertyHandler.cs` - Property editing (stub)
  - `SelectionHandler.cs` - Selection state management

## Design Decisions

- **Handler Pattern**: Each message type has a dedicated handler implementing
  `IMessageHandler`. This provides clear separation and allows easy extension.

- **Async Handlers**: Handlers return `Task<IpcMessage?>` to support async operations
  like file I/O without blocking the message pump.

- **Stub Implementations**: Asset, Tree, and Property handlers are stubs returning
  mock data. The Asset Agent will replace these with real UAssetAPI integration.

- **Single Source of Truth**: The C# backend owns ALL data. The SelectionHandler
  tracks selection state, and handlers push updates to the frontend. The frontend
  never mutates state directly.

## Dependencies

- Internal: `UAssetViewer.Cef` (browser wrapper), `UAssetViewer.Models` (IPC types)
- External: None

## Usage Examples

```csharp
// Create and configure dispatcher
var dispatcher = new IpcDispatcher(logger);
dispatcher.RegisterDefaultHandlers();
dispatcher.Connect(browser);

// Send message to frontend
dispatcher.Send("tree", "root", new[] { rootNode });

// Messages from frontend are automatically dispatched
// Handlers return responses that are sent back
```

## Adding New Handlers

1. Create a class implementing `IMessageHandler`
2. Define the `MessageType` and supported actions in `CanHandle`
3. Implement `HandleAsync` with your logic
4. Register with `dispatcher.RegisterHandler(new YourHandler(...))`
