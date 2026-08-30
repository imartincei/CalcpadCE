// Prevents additional console window on Windows in release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // WebKitGTK's DMABUF renderer crashes with "Error 71 (Protocol error) dispatching to
    // Wayland display" on many current compositors, so the DMABUF and compositing fast paths
    // are disabled in favour of the software renderer. Must be set before GTK/WebKit
    // initializes, which makes main.rs the earliest place to do it.
    #[cfg(target_os = "linux")]
    {
        if std::env::var_os("WEBKIT_DISABLE_DMABUF_RENDERER").is_none() {
            std::env::set_var("WEBKIT_DISABLE_DMABUF_RENDERER", "1");
        }
        if std::env::var_os("WEBKIT_DISABLE_COMPOSITING_MODE").is_none() {
            std::env::set_var("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
        }
    }
    calcpad_desktop_lib::run();
}
