# CEF GDExtension Source

## Purpose
Rust source for the Godot-side CEF embedding layer. This directory owns offscreen rendering, shared framebuffer state, and the signal bridge that moves IPC messages between the browser and Godot.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `lib.rs` | GDExtension module entry point and exports. |
| `cef_browser_node.rs` | Godot-facing node implementation used by the C# runtime. |
| `render_handler.rs` | CEF render handler that captures framebuffer updates. |
| `display_handler.rs` | Console-message interception for inbound IPC. |
| `shared_state.rs` | Shared framebuffer and message queues between handlers and node code. |

## Problem
CEF expects native handlers and shared state, while Godot expects a GDExtension node. This directory provides the adapter layer between those models.

## Constraints
- CEF rendering callbacks run on native threads.
- Godot consumes data through GDExtension APIs.
- Framebuffer handoff must stay cheap enough for interactive previews.

## Decision
Keep CEF-specific handlers and shared state in Rust and expose a single Godot node to the rest of the app.

## Alternatives Rejected
- Push CEF integration into C#: rejected because the existing native bindings already live in Rust.

## Invariants
- IPC payloads remain raw JSON until the C# boundary validates them.
- Shared framebuffer access stays synchronized across render/display callbacks.

## Revisit Triggers
- Godot stops requiring the current GDExtension integration model.
- Frame timing shows the shared-state design is too expensive.

## Dependencies
**Internal:** `cef-helper-rs/`.
**External:** `godot`, `cef`, `parking_lot`, `serde`, `serde_json`.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this native boundary is stable but not yet tracked in ADRs.
- Revisit trigger: the browser embedding model changes.

## Usage Examples
```rust
let shared = Arc::new(SharedState::new(1280, 720));
let render = OsrRenderHandler::new(shared.clone());
```

## API Consumer Contract
- `cef_browser_node.rs` exposes the Godot-facing API consumed by the C# runtime.
- Consumers must initialize the helper/runtime before creating a browser.
- IPC messages are emitted as raw JSON strings through Godot signals.

## Structured Producer Contract
- Shared-state fields are private implementation details except for their current semantic roles in `SharedState`.
- Console messages prefixed with `__UASSET_IPC__:` are treated as IPC payload producers.
- Contract changes must stay compatible with `godot/scripts/Bridge/IpcDispatcher.cs`.
