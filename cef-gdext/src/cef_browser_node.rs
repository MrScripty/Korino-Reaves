// CefBrowserNode — GDExtension class exposing CEF to Godot/C#
//
// This node manages the CEF lifecycle and provides methods callable from C#:
// - initialize(helper_path, cef_path): Init CEF runtime
// - create_browser(url, width, height): Create offscreen browser
// - navigate(url): Load a URL
// - execute_javascript(code): Eval JS in the browser
// - send_mouse_move/button/wheel: Forward input events
// - send_key_event: Forward keyboard events
// - get_texture(): Get the current framebuffer as ImageTexture
//
// Signals emitted:
// - ipc_message_received(json: String): IPC message from Svelte UI
// - framebuffer_updated(): New frame available

use crate::app::AppBuilder;
use crate::display_handler::{DisplayHandlerBuilder, OsrDisplayHandler, IPC_PREFIX};
use crate::render_handler::{OsrRenderHandler, RenderHandlerBuilder};
use crate::shared_state::SharedState;
use cef::args::Args;
use cef::rc::Rc;
use cef::{
    api_hash, sys, wrap_client, Browser, BrowserSettings, CefStringUtf16, Client, ImplBrowser,
    ImplBrowserHost, ImplClient, ImplFrame, KeyEvent, KeyEventType, MouseButtonType, Settings,
    WindowInfo, WrapClient,
};
use godot::classes::{Image, ImageTexture};
use godot::prelude::*;
use std::ffi::c_int;
use std::mem::size_of;
use std::sync::{Arc, OnceLock};

static CEF_INITIALIZED: OnceLock<bool> = OnceLock::new();

// Client wraps render + display handlers
wrap_client! {
    struct ClientBuilder {
        render_handler: cef::RenderHandler,
        display_handler: cef::DisplayHandler,
    }

    impl Client {
        fn render_handler(&self) -> Option<cef::RenderHandler> {
            Some(self.render_handler.clone())
        }

        fn display_handler(&self) -> Option<cef::DisplayHandler> {
            Some(self.display_handler.clone())
        }
    }
}

impl ClientBuilder {
    fn build_client(shared: Arc<SharedState>) -> Client {
        let render_handler = RenderHandlerBuilder::build(OsrRenderHandler::new(Arc::clone(&shared)));
        let display_handler =
            DisplayHandlerBuilder::build(OsrDisplayHandler::new(shared));
        Self::new(render_handler, display_handler)
    }
}

#[derive(GodotClass)]
#[class(base=Node)]
pub struct CefBrowserNode {
    base: Base<Node>,
    shared: Option<Arc<SharedState>>,
    browser: Option<Browser>,
    texture: Option<Gd<ImageTexture>>,
    image: Option<Gd<Image>>,
    initialized: bool,
    ready: bool,
    update_count: u32,
}

#[godot_api]
impl INode for CefBrowserNode {
    fn init(base: Base<Node>) -> Self {
        Self {
            base,
            shared: None,
            browser: None,
            texture: None,
            image: None,
            initialized: false,
            ready: false,
            update_count: 0,
        }
    }

    fn process(&mut self, _delta: f64) {
        if !self.initialized {
            return;
        }

        // Pump CEF message loop
        cef::do_message_loop_work();

        // Check if first paint arrived (transition to ready)
        if !self.ready {
            if let Some(shared) = &self.shared {
                if shared.has_framebuffer() {
                    self.ready = true;
                    godot_print!("[cef-gdext] Browser ready");
                    self.inject_ipc_bridge();
                }
            }
        }

        // Emit queued IPC messages as signals
        if let Some(shared) = &self.shared {
            let messages = shared.drain_ipc_messages();
            for json in messages {
                self.base_mut()
                    .emit_signal("ipc_message_received", &[json.to_variant()]);
            }
        }

        // Update texture if framebuffer changed
        if let Some(shared) = &self.shared {
            if let Some((buffer, width, height)) = shared.capture_if_dirty() {
                self.update_count += 1;
                if self.update_count <= 5 {
                    // Log first few bytes to verify non-zero pixel data
                    let preview: Vec<u8> = buffer.iter().take(16).cloned().collect();
                    godot_print!(
                        "[cef-gdext] Texture update #{}: {}x{}, {} bytes, first 16: {:?}",
                        self.update_count, width, height, buffer.len(), preview
                    );
                }
                self.update_texture(&buffer, width, height);
                self.base_mut()
                    .emit_signal("framebuffer_updated", &[]);
            }
        }
    }
}

#[godot_api]
impl CefBrowserNode {
    #[signal]
    fn ipc_message_received(json: GString);

    #[signal]
    fn framebuffer_updated();

    /// Initialize CEF runtime. Must be called before create_browser.
    #[func]
    fn initialize(&mut self, helper_path: GString, _cef_path: GString) -> bool {
        let helper = helper_path.to_string();

        let success = *CEF_INITIALIZED.get_or_init(|| {
            godot_print!("[cef-gdext] Initializing CEF...");

            let mut settings = Settings::default();
            settings.windowless_rendering_enabled = 1;
            settings.no_sandbox = 1;
            settings.external_message_pump = 1;
            settings.multi_threaded_message_loop = 0;
            settings.remote_debugging_port = 9222;  // Enable remote DevTools

            if !helper.is_empty() {
                settings.browser_subprocess_path = helper.as_str().into();
                godot_print!("[cef-gdext] Helper binary: {}", helper);
            }
            godot_print!("[cef-gdext] Remote debugging available at http://localhost:9222");

            let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
            let args = Args::new();
            let mut app = AppBuilder::build();

            let exec_result = cef::execute_process(
                Some(args.as_main_args()),
                Some(&mut app),
                std::ptr::null_mut(),
            );
            godot_print!("[cef-gdext] execute_process returned: {}", exec_result);

            let result = cef::initialize(
                Some(args.as_main_args()),
                Some(&settings),
                Some(&mut app),
                std::ptr::null_mut(),
            );

            if result == 0 {
                godot_error!("[cef-gdext] Failed to initialize CEF");
                return false;
            }

            godot_print!("[cef-gdext] CEF initialized successfully");
            true
        });

        self.initialized = success;
        success
    }

    /// Create an offscreen browser and load the given URL.
    #[func]
    fn create_browser(&mut self, url: GString, width: i32, height: i32) -> bool {
        if !self.initialized {
            godot_error!("[cef-gdext] CEF not initialized");
            return false;
        }

        let w = width as u32;
        let h = height as u32;

        let shared = Arc::new(SharedState::new(w, h));
        let mut client = ClientBuilder::build_client(Arc::clone(&shared));

        let mut window_info = WindowInfo::default();
        window_info.windowless_rendering_enabled = 1;
        window_info.bounds.width = width as c_int;
        window_info.bounds.height = height as c_int;

        let mut browser_settings = BrowserSettings::default();

        let url_str = url.to_string();
        let mut cef_url: CefStringUtf16 = url_str.as_str().into();

        godot_print!("[cef-gdext] Creating browser {}x{}: {}", w, h, url_str);

        let browser = cef::browser_host_create_browser_sync(
            Some(&mut window_info),
            Some(&mut client),
            Some(&mut cef_url),
            Some(&mut browser_settings),
            None,
            None,
        );

        match browser {
            Some(b) => {
                self.browser = Some(b);
                self.shared = Some(shared);

                // Texture will be created on first OnPaint callback in update_texture()
                self.image = None;
                self.texture = None;

                godot_print!("[cef-gdext] Browser created");
                true
            }
            None => {
                godot_error!("[cef-gdext] Failed to create browser");
                false
            }
        }
    }

    /// Navigate to a URL.
    #[func]
    fn navigate(&self, url: GString) {
        if let Some(browser) = &self.browser {
            if let Some(frame) = browser.main_frame() {
                let url_str = url.to_string();
                let cef_url: CefStringUtf16 = url_str.as_str().into();
                frame.load_url(Some(&cef_url));
            }
        }
    }

    /// Execute JavaScript in the browser's main frame.
    #[func]
    fn execute_javascript(&self, code: GString) {
        if let Some(browser) = &self.browser {
            if let Some(frame) = browser.main_frame() {
                let js: CefStringUtf16 = code.to_string().as_str().into();
                let empty: CefStringUtf16 = "".into();
                frame.execute_java_script(Some(&js), Some(&empty), 0);
            }
        }
    }

    /// Send an IPC message to the browser by evaluating JS.
    #[func]
    fn send_ipc_message(&self, json: GString) {
        let escaped = json
            .to_string()
            .replace('\\', "\\\\")
            .replace('\'', "\\'");
        let js = format!(
            "if (window.__UASSET_RECV__) {{ window.__UASSET_RECV__('{}'); }}",
            escaped
        );
        self.execute_javascript(GString::from(js));
    }

    /// Resize the browser viewport.
    #[func]
    fn resize(&mut self, width: i32, height: i32) {
        if let Some(shared) = &self.shared {
            *shared.size.lock() = (width as u32, height as u32);
        }
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                host.was_resized();
            }
        }
    }

    #[func]
    fn send_mouse_move(&self, x: i32, y: i32) {
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                let event = cef::MouseEvent {
                    x: x as c_int,
                    y: y as c_int,
                    modifiers: 0,
                };
                host.send_mouse_move_event(Some(&event), 0);
            }
        }
    }

    /// button: 0=Left, 1=Middle, 2=Right
    #[func]
    fn send_mouse_button(&self, x: i32, y: i32, button: i32, pressed: bool, click_count: i32) {
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                let event = cef::MouseEvent {
                    x: x as c_int,
                    y: y as c_int,
                    modifiers: 0,
                };
                let cef_button = match button {
                    1 => MouseButtonType::MIDDLE,
                    2 => MouseButtonType::RIGHT,
                    _ => MouseButtonType::LEFT,
                };
                let mouse_up = if pressed { 0 } else { 1 };
                host.send_mouse_click_event(Some(&event), cef_button, mouse_up, click_count as c_int);
            }
        }
    }

    #[func]
    fn send_mouse_wheel(&self, x: i32, y: i32, delta_x: i32, delta_y: i32) {
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                let event = cef::MouseEvent {
                    x: x as c_int,
                    y: y as c_int,
                    modifiers: 0,
                };
                host.send_mouse_wheel_event(Some(&event), delta_x as c_int, delta_y as c_int);
            }
        }
    }

    /// event_type: 0=KeyDown, 1=KeyUp, 2=Char
    #[func]
    fn send_key_event(
        &self,
        event_type: i32,
        key_code: i32,
        native_code: i32,
        modifiers: i32,
        character: i32,
    ) {
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                let cef_type = match event_type {
                    1 => KeyEventType::KEYUP,
                    2 => KeyEventType::CHAR,
                    _ => KeyEventType::RAWKEYDOWN,
                };
                let key_event = KeyEvent {
                    size: size_of::<KeyEvent>(),
                    type_: cef_type,
                    modifiers: modifiers as u32,
                    windows_key_code: key_code as c_int,
                    native_key_code: native_code as c_int,
                    is_system_key: 0,
                    character: character as u16,
                    unmodified_character: character as u16,
                    focus_on_editable_field: 0,
                };
                host.send_key_event(Some(&key_event));
            }
        }
    }

    /// Get the current framebuffer as an ImageTexture.
    #[func]
    fn get_texture(&self) -> Option<Gd<ImageTexture>> {
        self.texture.clone()
    }

    /// Check if the browser is ready (first paint received).
    #[func]
    fn is_ready(&self) -> bool {
        self.ready
    }

    /// Show DevTools in a new window.
    #[func]
    fn show_dev_tools(&self) {
        if let Some(browser) = &self.browser {
            if let Some(host) = browser.host() {
                let window_info = WindowInfo::default();
                let browser_settings = BrowserSettings::default();

                host.show_dev_tools(
                    Some(&window_info),
                    None,
                    Some(&browser_settings),
                    None,
                );

                godot_print!("[cef-gdext] Opening DevTools");
            }
        }
    }

    /// Shut down CEF. Call on application exit.
    #[func]
    fn shutdown(&mut self) {
        if let Some(browser) = self.browser.take() {
            if let Some(host) = browser.host() {
                host.close_browser(1);
            }
        }
        self.shared = None;
        self.texture = None;
        self.image = None;
        self.ready = false;
        godot_print!("[cef-gdext] Browser closed");
    }
}

impl CefBrowserNode {
    fn update_texture(&mut self, bgra_data: &[u8], width: u32, height: u32) {
        let w = width as i32;
        let h = height as i32;
        let expected_size = (width * height * 4) as usize;

        if bgra_data.len() != expected_size {
            godot_warn!(
                "[cef-gdext] Buffer size mismatch: got {} expected {} for {}x{}",
                bgra_data.len(), expected_size, w, h
            );
            return;
        }

        // Create a fresh Image from the BGRA buffer each frame.
        // Pass BGRA bytes as Rgba8 — the bgra_swizzle shader handles R↔B on the GPU.
        let data = PackedByteArray::from(bgra_data);
        let new_image = Image::create_from_data(
            w,
            h,
            false,
            godot::classes::image::Format::RGBA8,
            &data,
        );

        match new_image {
            Some(img) => {
                // Check if we need a new texture (first frame or size changed)
                let need_new_texture = match &self.texture {
                    None => true,
                    Some(tex) => tex.get_width() != w || tex.get_height() != h,
                };

                if need_new_texture {
                    self.texture = ImageTexture::create_from_image(&img);
                    godot_print!(
                        "[cef-gdext] Created new ImageTexture {}x{} (id={:?})",
                        w, h,
                        self.texture.as_ref().map(|t| t.instance_id())
                    );
                } else if let Some(ref mut tex) = self.texture {
                    tex.update(&img);
                }

                self.image = Some(img);
            }
            None => {
                godot_warn!("[cef-gdext] Failed to create Image from buffer data");
            }
        }
    }

    fn inject_ipc_bridge(&self) {
        let js = format!(
            r#"
            (function() {{
                if (window.ipc) return;
                window.ipc = {{
                    postMessage: function(message) {{
                        console.log('{}' + message);
                    }}
                }};
                window.ipc.postMessage(JSON.stringify({{ type: 'ui', action: 'ready' }}));
                console.log('UAssetViewer IPC bridge initialized');
            }})();
            "#,
            IPC_PREFIX
        );
        self.execute_javascript(GString::from(js));
    }
}
