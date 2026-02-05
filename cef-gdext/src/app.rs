// Minimal CefApp implementation for offscreen rendering
//
// CEF requires an App to be passed to initialize(). We don't need
// any custom behavior for offscreen rendering.

use cef::rc::Rc;
use cef::{wrap_app, App, CefString, CefStringUtf16, CommandLine, ImplApp, ImplCommandLine, WrapApp};

#[derive(Clone)]
pub struct OsrApp;

wrap_app! {
    pub struct AppBuilder {
        app: OsrApp,
    }

    impl App {
        fn on_before_command_line_processing(
            &self,
            _process_type: Option<&CefString>,
            command_line: Option<&mut CommandLine>,
        ) {
            if let Some(cmd) = command_line {
                // Enable file:// access for ES module loading (fixes CORS)
                let allow_file_access: CefStringUtf16 = "allow-file-access-from-files".into();
                let allow_universal: CefStringUtf16 = "allow-universal-access-from-files".into();
                let disable_security: CefStringUtf16 = "disable-web-security".into();

                cmd.append_switch(Some(&allow_file_access));
                cmd.append_switch(Some(&allow_universal));
                cmd.append_switch(Some(&disable_security));

                use godot::prelude::godot_print;
                godot_print!("[cef-gdext] Applied CORS-bypass switches to command line");
            }
        }
    }
}

impl AppBuilder {
    pub fn build() -> App {
        Self::new(OsrApp)
    }
}
