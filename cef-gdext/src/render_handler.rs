// CEF render handler for offscreen rendering
//
// Captures BGRA framebuffer from CEF's OnPaint callback and stores
// in SharedState for consumption by the GDExtension node.

use crate::shared_state::SharedState;
use cef::rc::Rc;
use cef::{
    wrap_render_handler, Browser, ImplRenderHandler, PaintElementType, Rect, RenderHandler,
    WrapRenderHandler,
};
use std::ffi::c_int;
use std::sync::atomic::Ordering;
use std::sync::Arc;

#[derive(Clone)]
pub struct OsrRenderHandler {
    pub shared: Arc<SharedState>,
}

impl OsrRenderHandler {
    pub fn new(shared: Arc<SharedState>) -> Self {
        Self { shared }
    }
}

wrap_render_handler! {
    pub struct RenderHandlerBuilder {
        handler: OsrRenderHandler,
    }

    impl RenderHandler {
        fn view_rect(&self, _browser: Option<&mut Browser>, rect: Option<&mut Rect>) {
            if let Some(rect) = rect {
                let size = self.handler.shared.size.lock().unwrap();
                rect.x = 0;
                rect.y = 0;
                rect.width = size.0 as c_int;
                rect.height = size.1 as c_int;
            }
        }

        fn on_paint(
            &self,
            _browser: Option<&mut Browser>,
            type_: PaintElementType,
            _dirty_rects: Option<&[Rect]>,
            buffer: *const u8,
            width: c_int,
            height: c_int,
        ) {
            if type_ != PaintElementType::VIEW {
                return;
            }

            if buffer.is_null() || width <= 0 || height <= 0 {
                return;
            }

            let width = width as u32;
            let height = height as u32;
            let buffer_size = (width * height * 4) as usize;

            // Safety: CEF guarantees the buffer is valid for the duration of on_paint
            let bgra = unsafe { std::slice::from_raw_parts(buffer, buffer_size) };

            // Copy BGRA buffer and wrap in Arc for zero-copy sharing
            let buffer_copy = Arc::new(bgra.to_vec());

            *self.handler.shared.framebuffer.lock().unwrap() = Some(buffer_copy);
            *self.handler.shared.framebuffer_size.lock().unwrap() = (width, height);
            self.handler.shared.dirty.store(true, Ordering::SeqCst);
        }
    }
}

impl RenderHandlerBuilder {
    pub fn build(handler: OsrRenderHandler) -> RenderHandler {
        Self::new(handler)
    }
}
