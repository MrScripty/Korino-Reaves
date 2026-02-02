# Cef

## Purpose

CEF (Chromium Embedded Framework) integration for rendering the Svelte UI overlay.
Provides offscreen rendering, input forwarding, and IPC communication.

## Contents

- `CefManager.cs` - Singleton managing CEF lifecycle and message pump
- `CefBrowserWrapper.cs` - High-level browser wrapper with IPC support
- `CefClient.cs` - CEF client combining render and display handlers
- `CefRenderHandler.cs` - Offscreen rendering capture (BGRA format)
- `CefDisplayHandler.cs` - Console.log interception for IPC messages
- `SharedState.cs` - Thread-safe framebuffer with dirty flag

## Design Decisions

- **Offscreen Rendering**: CEF renders to a BGRA buffer that we copy to a Godot texture.
  This avoids window management complexity and allows the UI to overlay the 3D viewport.

- **IPC via console.log**: Following Pentimento's pattern, IPC messages are sent via
  console.log with a special prefix (`__UASSET_IPC__:`). The DisplayHandler intercepts
  these and routes them to the IpcDispatcher.

- **Dirty Flag Pattern**: The SharedState uses an atomic dirty flag to avoid unnecessary
  texture updates. Only when the framebuffer changes do we copy to the Godot texture.

- **Separate Helper Binary**: CEF requires a subprocess helper to avoid running Godot
  initialization in render/GPU processes. The CefHelper project provides this.

## Dependencies

- Internal: `UAssetViewer.Infrastructure` (logging), `UAssetViewer.Models` (IPC types)
- External: CefGlue.Common, CefGlue

## Usage Examples

```csharp
// Initialize CEF (once on startup)
CefManager.Instance.Initialize();

// Create browser
var browser = new CefBrowserWrapper();
browser.Create("file:///path/to/ui/index.html", 1920, 1080);

// Connect to IPC dispatcher
browser.MessageReceived += (msg) => dispatcher.DispatchAsync(msg);

// In _Process: pump message loop and update texture
CefManager.Instance.DoMessageLoopWork();
var capture = browser.CaptureIfDirty();
if (capture != null)
{
    // Update texture with capture.Value
}

// Cleanup on exit
browser.Dispose();
CefManager.Instance.Dispose();
```
