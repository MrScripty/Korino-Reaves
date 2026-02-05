// CEF display handler for IPC via console.log interception
//
// JavaScript in the Svelte UI sends IPC messages by calling:
//   console.log("__UASSET_IPC__:" + JSON.stringify(message))
//
// This handler intercepts those messages, strips the prefix, and
// queues the raw JSON string for the GDExtension node to emit as a signal.

use crate::shared_state::SharedState;
use cef::rc::Rc;
use cef::{
    wrap_display_handler, Browser, CefString, DisplayHandler, ImplDisplayHandler, LogSeverity,
    WrapDisplayHandler,
};
use std::ffi::c_int;
use std::sync::atomic::Ordering;
use std::sync::Arc;

pub const IPC_PREFIX: &str = "__UASSET_IPC__:";

#[derive(Clone)]
pub struct OsrDisplayHandler {
    pub shared: Arc<SharedState>,
}

impl OsrDisplayHandler {
    pub fn new(shared: Arc<SharedState>) -> Self {
        Self { shared }
    }
}

wrap_display_handler! {
    pub struct DisplayHandlerBuilder {
        handler: OsrDisplayHandler,
    }

    impl DisplayHandler {
        fn on_console_message(
            &self,
            _browser: Option<&mut Browser>,
            level: LogSeverity,
            message: Option<&CefString>,
            source: Option<&CefString>,
            line: c_int,
        ) -> c_int {
            if let Some(msg) = message {
                let msg_str = msg.to_string();
                if let Some(json_str) = msg_str.strip_prefix(IPC_PREFIX) {
                    // Queue the raw JSON for the GDExtension node to emit
                    self.handler
                        .shared
                        .ipc_messages
                        .lock()
                        .unwrap()
                        .push(json_str.to_string());

                    // Mark dirty for UI-triggered repaints
                    self.handler.shared.dirty.store(true, Ordering::SeqCst);

                    // Return 1 to suppress the console message
                    return 1;
                } else {
                    // Log non-IPC console messages to help debug
                    let source_str = source.map(|s| s.to_string()).unwrap_or_default();
                    let level_str = match level {
                        LogSeverity::VERBOSE => "VERBOSE",
                        LogSeverity::INFO => "INFO",
                        LogSeverity::WARNING => "WARNING",
                        LogSeverity::ERROR => "ERROR",
                        LogSeverity::FATAL => "FATAL",
                        _ => "DEFAULT",
                    };
                    use godot::prelude::godot_print;
                    godot_print!("[CEF Console {}] {}:{} {}", level_str, source_str, line, msg_str);
                }
            }
            0
        }
    }
}

impl DisplayHandlerBuilder {
    pub fn build(handler: OsrDisplayHandler) -> DisplayHandler {
        Self::new(handler)
    }
}
