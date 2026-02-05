// Shared state between CEF render/display handlers and the GDExtension node
//
// Manages:
// - BGRA framebuffer with Arc for zero-copy sharing
// - AtomicBool dirty flag for efficient change detection
// - Viewport size for CEF's GetViewRect
// - IPC message queue from console.log interception

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

pub struct SharedState {
    /// BGRA pixel data wrapped in Arc for zero-copy sharing.
    /// OnPaint copies CEF's buffer once, then Arc::clone is ~20ns.
    pub framebuffer: Mutex<Option<Arc<Vec<u8>>>>,
    /// Dimensions of the current framebuffer
    pub framebuffer_size: Mutex<(u32, u32)>,
    /// Set by OnPaint, cleared by capture_if_dirty
    pub dirty: Arc<AtomicBool>,
    /// Current viewport size (read by GetViewRect)
    pub size: Mutex<(u32, u32)>,
    /// IPC messages received from console.log interception
    pub ipc_messages: Mutex<Vec<String>>,
}

impl SharedState {
    pub fn new(width: u32, height: u32) -> Self {
        Self {
            framebuffer: Mutex::new(None),
            framebuffer_size: Mutex::new((0, 0)),
            dirty: Arc::new(AtomicBool::new(false)),
            size: Mutex::new((width, height)),
            ipc_messages: Mutex::new(Vec::new()),
        }
    }

    /// Check if framebuffer has been updated and return the data if so.
    /// Clears the dirty flag atomically.
    pub fn capture_if_dirty(&self) -> Option<(Arc<Vec<u8>>, u32, u32)> {
        if !self.dirty.swap(false, Ordering::SeqCst) {
            return None;
        }
        let buffer = self.framebuffer.lock().unwrap().clone()?;
        let (w, h) = *self.framebuffer_size.lock().unwrap();
        Some((buffer, w, h))
    }

    /// Check if any framebuffer data exists
    pub fn has_framebuffer(&self) -> bool {
        self.framebuffer.lock().unwrap().is_some()
    }

    /// Drain all pending IPC messages
    pub fn drain_ipc_messages(&self) -> Vec<String> {
        let mut msgs = self.ipc_messages.lock().unwrap();
        std::mem::take(&mut *msgs)
    }
}
