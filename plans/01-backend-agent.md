# Backend Agent (Godot C#)

**Phase**: 1 - Foundations
**Depends on**: Shared Contracts (00)

## Scope

CEF integration, IPC handling, core services infrastructure.

## Reference Materials

- **Pentimento** (`/media/jeremy/OrangeCream/Linux Software/Pentimento/`):
  - `browser.rs` → CEF lifecycle patterns
  - `capture.rs` → Dirty-flag framebuffer pattern
  - `bridge.ts` → IPC message patterns

- **Godot Mono** (`/media/jeremy/OrangeCream/Linux Software/godot/modules/mono/`):
  - C# runtime integration patterns

## Files to Create

```
godot/scripts/
├── Cef/
│   ├── CefManager.cs          # CEF lifecycle singleton
│   ├── CefBrowser.cs          # Browser wrapper
│   ├── CefRenderHandler.cs    # Offscreen rendering
│   ├── CefDisplayHandler.cs   # IPC interception
│   └── SharedState.cs         # Thread-safe framebuffer
│   └── README.md
├── Bridge/
│   ├── IpcDispatcher.cs       # Route messages to handlers
│   ├── MessageTypes.cs        # C# message enums (from contracts)
│   └── handlers/
│       ├── IMessageHandler.cs # Handler interface
│       ├── AssetHandler.cs    # Stub for asset operations
│       ├── TreeHandler.cs     # Stub for tree operations
│       └── PropertyHandler.cs # Stub for property operations
│   └── README.md
├── Services/
│   ├── IAssetService.cs       # Interface (Godot-agnostic)
│   ├── ITreeService.cs        # Interface
│   └── README.md
├── Infrastructure/
│   ├── IAppLogger.cs          # Logging interface
│   ├── SerilogAppLogger.cs    # Serilog implementation
│   └── README.md
└── README.md
```

## Tasks

### 1. Project Setup
- [ ] Create Godot C# project structure
- [ ] Add NuGet references:
  - CefGlue.Common
  - Serilog
  - OpenTelemetry
  - System.Text.Json
- [ ] Configure .csproj for .NET 8.0

### 2. CEF Manager (CefManager.cs)
```csharp
public sealed class CefManager
{
    private static CefManager? _instance;
    public static CefManager Instance => _instance ??= new CefManager();

    public void Initialize(string cefHelperPath);
    public void DoMessageLoopWork();  // Call from _Process()
    public void Shutdown();
}
```

- [ ] Initialize CEF with offscreen rendering
- [ ] Configure subprocess path
- [ ] Run message pump integration
- [ ] Implement shutdown

### 3. CEF Browser (CefBrowser.cs)
```csharp
public class CefBrowser
{
    public void Navigate(string url);
    public void ExecuteJavaScript(string code);
    public byte[] GetFramebuffer();
    public bool IsDirty { get; }
}
```

- [ ] Create offscreen browser
- [ ] Implement RenderHandler for paint capture
- [ ] Implement dirty-flag pattern
- [ ] Implement DisplayHandler for IPC

### 4. IPC Dispatcher (IpcDispatcher.cs)
```csharp
public class IpcDispatcher
{
    public void RegisterHandler(string type, IMessageHandler handler);
    public void Dispatch(IpcMessage message);
    public void Send(string type, string action, object payload);
}
```

- [ ] Parse incoming messages from CEF
- [ ] Route to appropriate handlers
- [ ] Send responses back to JavaScript
- [ ] Log all messages for debugging

### 5. Handler Stubs
- [ ] Create IMessageHandler interface
- [ ] Create stub handlers that return mock data
- [ ] Each handler logs when invoked

### 6. Logging Infrastructure
```csharp
public interface IAppLogger
{
    void Debug(string message, params object[] args);
    void Info(string message, params object[] args);
    void Warning(string message, params object[] args);
    void Error(Exception ex, string message, params object[] args);
    IDisposable BeginScope(string operationName);
}
```

- [ ] Create Serilog configuration
- [ ] Implement structured logging
- [ ] Add activity tracing with OpenTelemetry

### 7. Testing
- [ ] Write unit tests for IpcDispatcher
- [ ] Write integration test for CEF initialization
- [ ] Create IPC ping/pong test

## Coding Standards

### Separation of Concerns
```
Godot Nodes (scenes)    - Scene tree, input, rendering
Controllers             - Orchestration, no business logic
Services                - Business logic, Godot-agnostic
Models/DTOs             - Data structures, no behavior
Infrastructure          - CEF, file I/O, external APIs
```

### Rules
- Services MUST NOT depend on Godot types
- All external dependencies behind interfaces
- No magic numbers (use Constants.cs)
- Every directory has README.md
- Max ~500 lines per file

### Error Handling
- Exceptions for exceptional cases only
- Catch at boundaries (IPC handlers)
- Log with correlation IDs
- Return Result<T> for expected failures

## Outputs for Other Agents

1. **IPC message format implemented** - Frontend can send/receive
2. **Handler interfaces defined** - Asset/Diff/AI agents implement
3. **Test harness for IPC** - Can test handlers in isolation
4. **Logging infrastructure** - All agents use same logging

## Acceptance Criteria

- [ ] Godot opens window with CEF rendering
- [ ] Svelte UI loads from file:// or localhost
- [ ] IPC ping/pong test passes
- [ ] All handlers log incoming messages
- [ ] Serilog outputs structured logs
- [ ] Unit tests pass
- [ ] All directories have README.md

## Sync Point

**End of Phase 1**: Must pass IPC integration test with Frontend Agent before Phase 2 begins.
