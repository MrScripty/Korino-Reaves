# Scripts

## Purpose

C# source code for the UAsset Viewer application.
Organized by responsibility following clean architecture principles.

## Contents

- `MainController.cs` - Entry point, manages CEF lifecycle and Godot integration
- `Cef/` - CEF integration (browser, rendering, IPC)
- `Bridge/` - IPC message routing and handlers
- `Services/` - Business logic interfaces (Godot-agnostic)
- `Infrastructure/` - Cross-cutting concerns (logging, telemetry)
- `Models/` - Shared data models and IPC types (owned by Shared Contracts Agent)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Godot Nodes (scenes)    - Scene tree, input, rendering    │
├─────────────────────────────────────────────────────────────┤
│  Controllers             - Orchestration, no business logic│
│  (MainController.cs)                                        │
├─────────────────────────────────────────────────────────────┤
│  Bridge                  - IPC routing and handlers        │
│  (IpcDispatcher, Handlers)                                 │
├─────────────────────────────────────────────────────────────┤
│  Services                - Business logic, Godot-agnostic  │
│  (IAssetService, ITreeService)                             │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure          - CEF, logging, file I/O          │
│  (CefManager, AppLogger)                                   │
├─────────────────────────────────────────────────────────────┤
│  Models                  - Data structures, no behavior    │
│  (IpcMessage, TreeNode, PropertyValue, etc.)               │
└─────────────────────────────────────────────────────────────┘
```

## Design Decisions

- **Services Don't Depend on Godot**: All business logic in Services/ is pure C#
  with no Godot dependencies, enabling unit testing without the engine.

- **Controllers Translate**: MainController and IPC handlers translate between
  Godot types (InputEvent, Node) and service types.

- **Single Source of Truth**: C# owns ALL application data. The Svelte frontend
  is a pure presentation layer that displays what C# sends via IPC.

- **Max 500 Lines**: Files approaching 500 lines should be split by responsibility.

## Dependencies

- **NuGet Packages**:
  - CefGlue.Common, CefGlue (CEF integration)
  - Serilog, Serilog.Sinks.* (logging)
  - OpenTelemetry (tracing)
  - System.Text.Json (serialization)

- **Future**:
  - UAssetAPI (asset parsing, by Asset Agent)
  - CUE4Parse (texture/mesh extraction, by Asset Agent)
  - Microsoft.SemanticKernel (AI integration, by AI Agent)
