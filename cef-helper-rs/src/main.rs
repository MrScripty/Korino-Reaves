// CEF subprocess helper binary
//
// CEF uses a multi-process architecture (browser, render, GPU, utility).
// This minimal binary handles subprocess execution so CEF doesn't try
// to re-launch the main Godot executable for subprocesses.

fn main() {
    let args = cef::args::Args::new();
    let exit_code =
        cef::execute_process(Some(args.as_main_args()), None, std::ptr::null_mut());
    std::process::exit(if exit_code >= 0 { exit_code } else { 1 });
}
