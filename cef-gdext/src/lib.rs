// cef-gdext — Rust GDExtension providing CEF 143 integration for Godot 4
//
// Exposes CefBrowserNode as a Godot node that C# code can interact with
// via Call() and signals.

mod app;
mod cef_browser_node;
mod display_handler;
mod render_handler;
mod shared_state;

use godot::prelude::*;

struct CefGdExt;

#[gdextension]
unsafe impl ExtensionLibrary for CefGdExt {}
