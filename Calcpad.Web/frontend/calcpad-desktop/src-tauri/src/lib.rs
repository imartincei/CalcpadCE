use std::backtrace::Backtrace;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use tauri::menu::{IsMenuItem, Menu, MenuItem, MenuItemKind, PredefinedMenuItem, Submenu};
use tauri::path::BaseDirectory;
use tauri::{AppHandle, Emitter, Manager, RunEvent, State};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};
use tauri_plugin_fs::FsExt;
use tauri_plugin_window_state::StateFlags;
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::sync::mpsc;

mod control;
mod navigation_history;
use navigation_history::{
    complete_navigation, navigate_history, record_navigation, remove_navigation,
    NavigationHistoryState,
};
#[cfg(target_os = "linux")]
mod linux_file_dialog;

// Populated at setup(); read from the panic hook (which has no AppHandle).
static CRASH_DIR: OnceLock<PathBuf> = OnceLock::new();
static DRAFTS_DIR: OnceLock<PathBuf> = OnceLock::new();
// Shared with spawn_sidecar so the panic hook can attach the last few KB
// of the child's combined stdio to the dump — same trick the C# server uses.
static SIDECAR_TAIL: OnceLock<Arc<Mutex<String>>> = OnceLock::new();
// Per-launch bearer for the sidecar's /api routes. See api_token().
static API_TOKEN: OnceLock<String> = OnceLock::new();
static NEXT_WINDOW_NUMBER: AtomicU64 = AtomicU64::new(2);

// Name of the .NET apphost inside the resource dir. All ~200 sibling
// DLLs / native libs / deps.json / runtimeconfig.json land next to it so
// framework-dependent .NET resolves its deps the way it expects.
const SIDECAR_EXE_UNIX: &str = "Calcpad.Server";
const SIDECAR_EXE_WINDOWS: &str = "Calcpad.Server.exe";
const PORT_READY_TIMEOUT_MS: u64 = 30_000;
const MAIN_WINDOW_LABEL: &str = "main";
const CANCEL_LABEL: &str = "Cancel";

/// Files handed to us by the OS at launch (double-click on .cpd or .cpdz via the
/// installed file associations). Populated in setup() from argv; drained by
/// the frontend once it has registered its open-file listener, avoiding the
/// race between Rust emitting and JS being ready to listen.
#[derive(Default)]
struct PendingLaunchFiles(Mutex<Vec<String>>);

/// Label of the window that most recently took focus, so a menu click can be delivered
/// to it alone. Tracked here because `get_focused_window` needs tauri's `unstable` feature.
struct FocusedWindow(Mutex<String>);

impl Default for FocusedWindow {
    fn default() -> Self {
        Self(Mutex::new(MAIN_WINDOW_LABEL.to_string()))
    }
}

struct PrimaryWindow(Mutex<String>);

#[derive(Default)]
struct OpenFiles(Mutex<HashMap<String, Vec<String>>>);

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct EditorPosition {
    line_number: u32,
    column: u32,
}

#[derive(Clone, Serialize)]
struct FileNavigation {
    path: String,
    position: Option<EditorPosition>,
}

impl Default for PrimaryWindow {
    fn default() -> Self {
        Self(Mutex::new(MAIN_WINDOW_LABEL.to_string()))
    }
}

#[derive(Default)]
struct MenuState {
    recent_files: Mutex<Vec<String>>,
}

#[derive(Default)]
struct ServerState {
    // Send () to signal the running sidecar to shut down. Owned here so the
    // Server menu and window-close flow can trigger a kill without owning
    // the Child directly — the wait task inside spawn_sidecar owns the
    // Child and calls start_kill()+wait() when it receives.
    kill_tx: Mutex<Option<mpsc::Sender<()>>>,
    url: Mutex<Option<String>>,
    // Bumped on every spawn. Each wait task captures the generation it belongs
    // to and only mutates shared state if it's still current — otherwise a
    // dying old sidecar (e.g. during restart) would clobber the freshly spawned
    // one's url/kill_tx.
    generation: AtomicU64,
    // Why the server is down, for webviews that were not listening when it failed (boot, or a
    // window opened later). Cleared on every spawn.
    failure: Mutex<Option<ServerFailure>>,
    last_crash: Mutex<Option<ServerCrashPayload>>,
    // The user chose Ignore on a malformed appsettings.json; kept across auto-restarts.
    ignore_settings: AtomicBool,
}

#[derive(Clone, Serialize)]
struct ServerCrashPayload {
    code: Option<i32>,
    tail: String,
}

#[derive(Clone, Serialize)]
struct ServerFailure {
    error: String,
    crash: Option<ServerCrashPayload>,
}

#[derive(Clone, Serialize)]
struct ServerLogLine {
    stream: &'static str,
    line: String,
}

#[derive(Clone, Serialize)]
struct MenuClickPayload {
    id: String,
}

#[derive(Deserialize, Serialize, Clone)]
struct DraftMeta {
    filename: String,
    #[serde(rename = "filePath")]
    file_path: Option<String>,
    #[serde(rename = "savedAt")]
    saved_at: u64,
}

#[derive(Serialize, Clone)]
struct DraftInfo {
    id: String,
    filename: String,
    #[serde(rename = "filePath")]
    file_path: Option<String>,
    #[serde(rename = "savedAt")]
    saved_at: u64,
    size: u64,
}

#[derive(Serialize, Clone)]
struct DraftContent {
    id: String,
    filename: String,
    #[serde(rename = "filePath")]
    file_path: Option<String>,
    #[serde(rename = "savedAt")]
    saved_at: u64,
    content: String,
}

/// Pull out any worksheet paths (.cpd or compiled .cpdz) from a launch argv so we can open
/// them once the frontend is ready. argv[0] is the executable path, and macOS may pass an
/// `-psn_...` process serial arg for double-click launches.
fn extract_launch_files<I: IntoIterator<Item = String>>(args: I) -> Vec<PathBuf> {
    args.into_iter()
        .skip(1)
        .filter(|a| !a.starts_with('-'))
        .map(PathBuf::from)
        .filter(|p| is_worksheet_path(p))
        .filter(|p| p.exists())
        .collect()
}

fn is_worksheet_path(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .is_some_and(|e| e.eq_ignore_ascii_case("cpd") || e.eq_ignore_ascii_case("cpdz"))
}

/// Per-launch shared secret the sidecar requires on every `/api` request, without which any
/// local program would have arbitrary file read as the user — loopback binding keeps remote
/// machines out but not other local processes, and `#include` resolution reads any path it is
/// handed. Generated once and reused across sidecar restarts, and handed to the child through
/// its environment rather than argv, which is world-readable via `/proc/{pid}/cmdline` and WMI.
fn api_token() -> &'static str {
    API_TOKEN.get_or_init(|| {
        let mut bytes = [0u8; 32];
        // A failure here would mean the OS entropy source is unavailable, which is
        // unrecoverable — an all-zero or predictable token would be worse than no
        // server at all, so panic rather than degrade.
        getrandom::getrandom(&mut bytes).expect("OS entropy source unavailable");
        bytes.iter().map(|b| format!("{b:02x}")).collect()
    })
}

fn unix_millis() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn crash_dir() -> Option<&'static Path> {
    CRASH_DIR.get().map(|p| p.as_path())
}

fn write_crash_report(kind: &str, body: &str) {
    // Fall back to the system temp dir if the panic fires before setup()
    // populated CRASH_DIR — better a temp-dir dump than none.
    let base = crash_dir()
        .map(|p| p.to_path_buf())
        .unwrap_or_else(std::env::temp_dir);
    let _ = std::fs::create_dir_all(&base);
    let path = base.join(format!("crash-{}-{}.log", kind, unix_millis()));
    let _ = std::fs::write(&path, body);
    eprintln!("[crash] wrote {}", path.display());
}

fn install_panic_hook() {
    // Force full backtraces even if the user didn't set RUST_BACKTRACE.
    // std::env::set_var is safe here — no other threads read it before we set it.
    if std::env::var_os("RUST_BACKTRACE").is_none() {
        std::env::set_var("RUST_BACKTRACE", "full");
    }
    let default_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let payload = info
            .payload()
            .downcast_ref::<&'static str>()
            .map(|s| (*s).to_string())
            .or_else(|| info.payload().downcast_ref::<String>().cloned())
            .unwrap_or_else(|| "<non-string panic payload>".to_string());
        let location = info
            .location()
            .map(|l| format!("{}:{}:{}", l.file(), l.line(), l.column()))
            .unwrap_or_else(|| "<unknown>".to_string());
        let backtrace = Backtrace::force_capture();
        let thread = std::thread::current()
            .name()
            .unwrap_or("<unnamed>")
            .to_string();
        let tail = SIDECAR_TAIL
            .get()
            .and_then(|m| m.lock().ok().map(|g| g.clone()))
            .unwrap_or_default();
        let body = format!(
            "=== CalcpadCE Desktop panic ===\n\
             Time (unix ms): {ms}\n\
             Thread: {thread}\n\
             Location: {location}\n\
             Payload: {payload}\n\n\
             Backtrace:\n{backtrace}\n\n\
             --- Sidecar stdio tail ---\n{tail}\n",
            ms = unix_millis(),
        );
        write_crash_report("panic", &body);
        default_hook(info);
    }));
}

fn drafts_dir() -> Result<PathBuf, String> {
    DRAFTS_DIR
        .get()
        .cloned()
        .ok_or_else(|| "drafts dir not initialized".to_string())
}

// Some characters (\, /, .., NUL) in a caller-supplied id would let the
// draft commands read/write outside the drafts dir. Restrict ids to a
// conservative alphanumeric-plus-dash-underscore set — matches crypto.randomUUID().
fn validate_draft_id(id: &str) -> Result<(), String> {
    if id.is_empty() || id.len() > 128 {
        return Err("invalid draft id".into());
    }
    if !id
        .chars()
        .all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
    {
        return Err("invalid draft id".into());
    }
    Ok(())
}

fn draft_paths(id: &str) -> Result<(PathBuf, PathBuf), String> {
    validate_draft_id(id)?;
    let base = drafts_dir()?;
    Ok((
        base.join(format!("{id}.cpd")),
        base.join(format!("{id}.meta.json")),
    ))
}

fn atomic_write(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    let tmp = path.with_extension({
        let mut e = path
            .extension()
            .and_then(|s| s.to_str())
            .unwrap_or("")
            .to_string();
        e.push_str(".tmp");
        e
    });
    std::fs::write(&tmp, bytes)?;
    std::fs::rename(&tmp, path)
}

#[tauri::command]
fn draft_write(
    id: String,
    filename: String,
    file_path: Option<String>,
    content: String,
) -> Result<(), String> {
    let (content_path, meta_path) = draft_paths(&id)?;
    let base = drafts_dir()?;
    std::fs::create_dir_all(&base).map_err(|e| format!("create drafts dir: {e}"))?;
    let meta = DraftMeta {
        filename,
        file_path,
        saved_at: unix_millis(),
    };
    let meta_json = serde_json::to_vec_pretty(&meta).map_err(|e| format!("serialize meta: {e}"))?;
    atomic_write(&content_path, content.as_bytes())
        .map_err(|e| format!("write draft content: {e}"))?;
    atomic_write(&meta_path, &meta_json).map_err(|e| format!("write draft meta: {e}"))?;
    Ok(())
}

fn read_draft_meta(meta_path: &Path) -> Option<DraftMeta> {
    let bytes = std::fs::read(meta_path).ok()?;
    serde_json::from_slice::<DraftMeta>(&bytes).ok()
}

#[tauri::command]
fn draft_list() -> Result<Vec<DraftInfo>, String> {
    let base = match DRAFTS_DIR.get() {
        Some(p) => p,
        None => return Ok(Vec::new()),
    };
    if !base.exists() {
        return Ok(Vec::new());
    }
    let mut out = Vec::new();
    let entries = std::fs::read_dir(base).map_err(|e| format!("read drafts dir: {e}"))?;
    for entry in entries.flatten() {
        let path = entry.path();
        if path.extension().and_then(|s| s.to_str()) != Some("cpd") {
            continue;
        }
        let id = match path.file_stem().and_then(|s| s.to_str()) {
            Some(s) => s.to_string(),
            None => continue,
        };
        if validate_draft_id(&id).is_err() {
            continue;
        }
        let meta_path = base.join(format!("{id}.meta.json"));
        let meta = read_draft_meta(&meta_path).unwrap_or(DraftMeta {
            filename: format!("{id}.cpd"),
            file_path: None,
            saved_at: 0,
        });
        let size = std::fs::metadata(&path).map(|m| m.len()).unwrap_or(0);
        out.push(DraftInfo {
            id,
            filename: meta.filename,
            file_path: meta.file_path,
            saved_at: meta.saved_at,
            size,
        });
    }
    out.sort_by_key(|d| std::cmp::Reverse(d.saved_at));
    Ok(out)
}

#[tauri::command]
fn draft_read(id: String) -> Result<Option<DraftContent>, String> {
    let (content_path, meta_path) = draft_paths(&id)?;
    if !content_path.exists() {
        return Ok(None);
    }
    let content =
        std::fs::read_to_string(&content_path).map_err(|e| format!("read draft content: {e}"))?;
    let meta = read_draft_meta(&meta_path).unwrap_or(DraftMeta {
        filename: format!("{id}.cpd"),
        file_path: None,
        saved_at: 0,
    });
    Ok(Some(DraftContent {
        id,
        filename: meta.filename,
        file_path: meta.file_path,
        saved_at: meta.saved_at,
        content,
    }))
}

#[tauri::command]
fn draft_delete(id: String) -> Result<(), String> {
    let (content_path, meta_path) = draft_paths(&id)?;
    let _ = std::fs::remove_file(&content_path);
    let _ = std::fs::remove_file(&meta_path);
    Ok(())
}

#[tauri::command]
fn server_url(state: State<'_, ServerState>) -> Option<String> {
    state.url.lock().ok().and_then(|g| g.clone())
}

#[tauri::command]
fn server_failure(state: State<'_, ServerState>) -> Option<ServerFailure> {
    state.failure.lock().ok().and_then(|g| g.clone())
}

/// Records why the server is down, with the current generation's crash if there was one.
fn record_failure(app: &AppHandle, error: String) {
    let state: State<'_, ServerState> = app.state();
    let crash = state.last_crash.lock().ok().and_then(|g| g.clone());
    if let Ok(mut g) = state.failure.lock() {
        *g = Some(ServerFailure { error, crash });
    };
}

/// Hands the webview the token it must send as `X-Calcpad-Token` on every API call. Safe to
/// expose: the webview is the app's own frontend served from `tauri://`, while worksheet
/// content renders inside a sandboxed opaque-origin frame with no IPC access.
#[tauri::command]
fn server_token() -> &'static str {
    api_token()
}

#[tauri::command]
fn take_pending_launch_files(state: State<'_, PendingLaunchFiles>) -> Vec<String> {
    state
        .0
        .lock()
        .map(|mut g| std::mem::take(&mut *g))
        .unwrap_or_default()
}

/// Grants read access to `path` and the directory holding it, which is what worksheets need
/// since image `src`s resolve relative to the document's folder and the dialog plugin grants
/// only the clicked file. Non-recursive, so picking a file in a large tree does not hand over
/// the whole subtree.
pub(crate) fn allow_file_and_parent(app: &AppHandle, path: &Path) {
    let scope = app.fs_scope();
    let _ = scope.allow_file(path);
    if let Some(parent) = path.parent() {
        let _ = scope.allow_directory(parent, false);
    }
}

/// Extends the read scope to the folder of a document the webview may already read, gated on
/// the runtime scope already allowing `path` — only true once the user picked it through a
/// dialog — so webview script cannot grant itself a directory it was never given. The
/// capability's `fs:deny-default` entries are unaffected.
#[tauri::command]
fn allow_document_dir(app: AppHandle, path: String) -> Result<(), String> {
    let path = PathBuf::from(&path);
    if !app.fs_scope().is_allowed(&path) {
        return Err(format!(
            "outside the current read scope: {}",
            path.display()
        ));
    }
    let parent = path
        .parent()
        .ok_or_else(|| format!("no parent directory: {}", path.display()))?;
    app.fs_scope()
        .allow_directory(parent, false)
        .map_err(|e| e.to_string())
}

/// `ignore_settings` left out keeps the current mode, which is what auto-restart wants.
#[tauri::command]
async fn restart_server(app: AppHandle, ignore_settings: Option<bool>) -> Result<String, String> {
    if let Some(ignore) = ignore_settings {
        app.state::<ServerState>()
            .ignore_settings
            .store(ignore, Ordering::SeqCst);
    }
    stop_sidecar(&app);
    let result = spawn_sidecar(&app).await;
    if let Err(err) = &result {
        record_failure(&app, err.clone());
    }
    result
}

#[tauri::command]
async fn stop_server(app: AppHandle) -> Result<(), String> {
    stop_sidecar(&app);
    Ok(())
}

/// True for the handful of variables the frontend legitimately expands: `expandEnvVars` feeds
/// this names lifted straight out of worksheet text, so an unrestricted `get_env` would turn any
/// `$AWS_SECRET_ACCESS_KEY` in a document into a value the webview can see. Only names that name
/// a location are answered; everything else reads as unset.
fn is_readable_env(name: &str) -> bool {
    // Only ever set on the sidecar's own env block, so this process should never
    // hold it — denied anyway so an inherited one can't be expanded out of a
    // worksheet and into the rendered preview, which has network egress.
    if name == "CALCPAD_API_TOKEN" {
        return false;
    }
    matches!(name, "HOME" | "USERPROFILE" | "APPDATA")
        || name.starts_with("XDG_")
        || name.starts_with("CALCPAD_")
}

#[tauri::command]
fn get_env(name: String) -> Option<String> {
    if !is_readable_env(&name) {
        return None;
    }
    std::env::var(name).ok()
}

/// Rebuilds one window's menu. Per-window rather than `app.set_menu`, which pushes the
/// same menu to every window and so leaked one window's View state into the others.
fn refresh_menu(app: &AppHandle, label: &str) -> Result<(), String> {
    let Some(window) = app.get_webview_window(label) else {
        return Ok(());
    };
    let menu = build_menu(app).map_err(|e| e.to_string())?;
    window.set_menu(menu).map_err(|e| e.to_string())?;
    window.hide_menu().map_err(|e| e.to_string())?;
    Ok(())
}

#[tauri::command]
fn show_app_menu(window: tauri::WebviewWindow, x: f64, y: f64) -> Result<(), String> {
    window
        .popup_menu_at(
            &window.menu().expect("window menu exists"),
            tauri::LogicalPosition::new(x, y),
        )
        .map_err(|e| e.to_string())
}

#[derive(Serialize)]
struct AppMenuEntry {
    id: String,
    text: String,
    enabled: bool,
    children: Vec<AppMenuEntry>,
}

fn app_menu_entries(items: Vec<MenuItemKind<tauri::Wry>>) -> tauri::Result<Vec<AppMenuEntry>> {
    items
        .into_iter()
        .map(|item| {
            let id = item.id().0.clone();
            let (text, enabled, children) = match item {
                MenuItemKind::MenuItem(item) => (item.text()?, item.is_enabled()?, vec![]),
                MenuItemKind::Submenu(item) => (
                    item.text()?,
                    item.is_enabled()?,
                    app_menu_entries(item.items()?)?,
                ),
                MenuItemKind::Predefined(_) => (String::new(), false, vec![]),
                MenuItemKind::Check(item) => (item.text()?, item.is_enabled()?, vec![]),
                MenuItemKind::Icon(item) => (item.text()?, item.is_enabled()?, vec![]),
            };
            Ok(AppMenuEntry {
                id,
                text,
                enabled,
                children,
            })
        })
        .collect()
}

#[tauri::command]
fn get_app_menu(window: tauri::WebviewWindow) -> Result<Vec<AppMenuEntry>, String> {
    let menu = window.menu().expect("window menu exists");
    app_menu_entries(menu.items().map_err(|e| e.to_string())?).map_err(|e| e.to_string())
}

#[tauri::command]
fn create_export_path(app: AppHandle, filename: String) -> Result<String, String> {
    let filename = Path::new(&filename)
        .file_name()
        .ok_or("export filename is required")?;
    let extension = Path::new(filename).extension().and_then(|e| e.to_str());
    if !matches!(extension, Some("pdf" | "docx")) {
        return Err("unsupported temporary export format".into());
    }
    let mut random = [0u8; 16];
    getrandom::getrandom(&mut random).map_err(|e| e.to_string())?;
    let id: String = random.iter().map(|b| format!("{b:02x}")).collect();
    let directory = std::env::temp_dir().join(format!("calcpad-export-{id}"));
    std::fs::create_dir(&directory).map_err(|e| e.to_string())?;
    let path = directory.join(filename);
    app.fs_scope()
        .allow_file(&path)
        .map_err(|e| e.to_string())?;
    Ok(path.to_string_lossy().into_owned())
}

fn refresh_all_menus(app: &AppHandle) -> Result<(), String> {
    for label in app.webview_windows().into_keys() {
        refresh_menu(app, &label)?;
    }
    Ok(())
}

/// Replaces the File → Open Recent entries, most recent first. The list itself is
/// owned by the frontend's plugin-store; this only mirrors it into the native menu.
#[tauri::command]
fn set_recent_files(app: AppHandle, paths: Vec<String>) -> Result<(), String> {
    *app.state::<MenuState>()
        .recent_files
        .lock()
        .expect("menu state mutex poisoned") = paths;
    refresh_all_menus(&app)
}

/// Labels of every open window.
#[tauri::command]
fn window_labels(app: AppHandle) -> Vec<String> {
    app.webview_windows().into_keys().collect()
}

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct TabDragTarget {
    window_id: String,
    x: f64,
    y: f64,
}

#[cfg(feature = "window-tests")]
static TEST_TAB_DRAG_TARGET: Mutex<Option<Option<TabDragTarget>>> = Mutex::new(None);

#[cfg(feature = "window-tests")]
#[tauri::command]
fn test_set_tab_drag_target(target: Option<TabDragTarget>) {
    *TEST_TAB_DRAG_TARGET.lock().unwrap() = Some(target);
}

#[tauri::command]
async fn use_system_tab_drag(window: tauri::WebviewWindow) -> Result<bool, String> {
    #[cfg(target_os = "linux")]
    {
        let (sender, receiver) = tokio::sync::oneshot::channel();
        window
            .with_webview(move |platform| {
                use gtk::prelude::*;
                let wayland = platform.inner().display().type_().name() == "GdkWaylandDisplay";
                let _ = sender.send(wayland);
            })
            .map_err(|error| error.to_string())?;
        receiver.await.map_err(|error| error.to_string())
    }
    #[cfg(not(target_os = "linux"))]
    {
        let _ = window;
        Ok(false)
    }
}

#[tauri::command]
async fn tab_drag_target(app: AppHandle) -> Result<Option<TabDragTarget>, String> {
    #[cfg(feature = "window-tests")]
    if let Some(target) = &*TEST_TAB_DRAG_TARGET.lock().unwrap() {
        return Ok(target.clone());
    }
    let windows: Vec<_> = app.webview_windows().into_values().collect();
    #[cfg(not(any(windows, target_os = "linux")))]
    let windows = {
        let mut windows = windows;
        let focused = app.state::<FocusedWindow>();
        let label = focused.0.lock().expect("focused window mutex poisoned");
        windows.sort_by_key(|window| window.label() != *label);
        windows
    };

    #[cfg(windows)]
    let (cursor, root) = {
        use windows_sys::Win32::Foundation::POINT;
        use windows_sys::Win32::UI::WindowsAndMessaging::{
            GetAncestor, GetCursorPos, WindowFromPoint, GA_ROOT,
        };
        let mut point = POINT { x: 0, y: 0 };
        unsafe {
            if GetCursorPos(&mut point) == 0 {
                return Err(std::io::Error::last_os_error().to_string());
            }
            (point, GetAncestor(WindowFromPoint(point), GA_ROOT))
        }
    };

    for window in windows {
        if !window.is_visible().map_err(|e| e.to_string())?
            || window.is_minimized().map_err(|e| e.to_string())?
        {
            continue;
        }
        #[cfg(target_os = "linux")]
        {
            let (sender, receiver) = tokio::sync::oneshot::channel();
            let label = window.label().to_string();
            window
                .with_webview(move |platform| {
                    use gtk::prelude::*;
                    let view = platform.inner();
                    let target = (|| {
                        if !view.is_drawable() {
                            return None;
                        }
                        let pointer = view.display().default_seat().unwrap().pointer().unwrap();
                        let hit = pointer.window_at_position_double().0?;
                        let toplevel = view.toplevel().unwrap();
                        let native = toplevel.window().unwrap();
                        if hit.effective_toplevel() != native.effective_toplevel() {
                            return None;
                        }
                        let (_, root_x, root_y) = pointer.position_double();
                        let (_, origin_x, origin_y) = native.origin();
                        let (offset_x, offset_y) =
                            view.translate_coordinates(&toplevel, 0, 0).unwrap();
                        let x = root_x - f64::from(origin_x + offset_x);
                        let y = root_y - f64::from(origin_y + offset_y);
                        let allocation = view.allocation();
                        (x >= 0.0
                            && y >= 0.0
                            && x < f64::from(allocation.width())
                            && y < f64::from(allocation.height()))
                        .then_some(TabDragTarget {
                            window_id: label,
                            x,
                            y,
                        })
                    })();
                    let _ = sender.send(target);
                })
                .map_err(|error| error.to_string())?;
            if let Some(target) = receiver.await.map_err(|error| error.to_string())? {
                return Ok(Some(target));
            }
        }
        #[cfg(not(target_os = "linux"))]
        {
            #[cfg(windows)]
            if !std::ptr::eq(window.hwnd().map_err(|e| e.to_string())?.0, root) {
                continue;
            }
            #[cfg(not(windows))]
            let cursor = window.cursor_position().map_err(|e| e.to_string())?;
            let origin = window.inner_position().map_err(|e| e.to_string())?;
            let size = window.inner_size().map_err(|e| e.to_string())?;
            let x = cursor.x as f64 - origin.x as f64;
            let y = cursor.y as f64 - origin.y as f64;
            if x >= 0.0 && y >= 0.0 && x < size.width as f64 && y < size.height as f64 {
                let scale = window.scale_factor().map_err(|e| e.to_string())?;
                return Ok(Some(TabDragTarget {
                    window_id: window.label().to_string(),
                    x: x / scale,
                    y: y / scale,
                }));
            }
        }
    }
    Ok(None)
}

#[tauri::command]
fn primary_window(state: State<'_, PrimaryWindow>) -> String {
    state
        .0
        .lock()
        .expect("primary window mutex poisoned")
        .clone()
}

#[tauri::command]
fn set_open_files(window: tauri::WebviewWindow, state: State<'_, OpenFiles>, paths: Vec<String>) {
    state
        .0
        .lock()
        .expect("open files mutex poisoned")
        .insert(window.label().to_string(), paths);
}

fn file_path_key(path: &str) -> String {
    #[cfg(windows)]
    {
        path.replace('\\', "/").to_lowercase()
    }
    #[cfg(not(windows))]
    {
        path.to_string()
    }
}

#[tauri::command]
fn navigate_to_open_file(
    app: AppHandle,
    window: tauri::WebviewWindow,
    state: State<'_, OpenFiles>,
    path: String,
    position: Option<EditorPosition>,
) -> Result<bool, String> {
    let key = file_path_key(&path);
    let target = state
        .0
        .lock()
        .expect("open files mutex poisoned")
        .iter()
        .filter_map(|(label, paths)| {
            paths
                .iter()
                .find(|path| file_path_key(path) == key)
                .map(|path| (label.clone(), path.clone()))
        })
        .min_by_key(|(label, _)| (label != window.label(), label.clone()));
    let Some((label, path)) = target else {
        return Ok(false);
    };
    let target = app
        .get_webview_window(&label)
        .expect("open file window exists");
    target
        .emit_to(
            &label,
            "navigate-to-file",
            FileNavigation { path, position },
        )
        .map_err(|e| e.to_string())?;
    target.unminimize().map_err(|e| e.to_string())?;
    target.set_focus().map_err(|e| e.to_string())?;
    target.as_ref().set_focus().map_err(|e| e.to_string())?;
    Ok(true)
}

/// Opens another window in this process — one sidecar, one store, one drafts dir. Cloning
/// the configured window carries its CSP, min sizes and browser args over. Async because
/// building a webview from a sync command deadlocks on Windows.
#[tauri::command]
async fn new_window(app: AppHandle) -> Result<String, String> {
    let mut config = app
        .config()
        .app
        .windows
        .first()
        .ok_or("no window configured")?
        .clone();

    let label = format!(
        "{MAIN_WINDOW_LABEL}-{}",
        NEXT_WINDOW_NUMBER.fetch_add(1, Ordering::Relaxed)
    );
    config.label = label.clone();

    let menu = build_menu(&app).map_err(|e| e.to_string())?;
    let window = tauri::WebviewWindowBuilder::from_config(&app, &config)
        .map_err(|e| e.to_string())?
        .menu(menu)
        .focused(true)
        .build()
        .map_err(|e| e.to_string())?;
    window.set_focus().map_err(|e| e.to_string())?;
    window.as_ref().set_focus().map_err(|e| e.to_string())?;
    Ok(label)
}

// Windows may prevent the existing instance from taking focus after a repeat launch.
// The user-launched process grants it foreground permission before exiting.
#[cfg(windows)]
fn allow_existing_instance_foreground(identifier: &str) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        AllowSetForegroundWindow, FindWindowW, GetWindowThreadProcessId,
    };

    let class: Vec<u16> = format!("{identifier}-sic")
        .encode_utf16()
        .chain(Some(0))
        .collect();
    unsafe {
        let window = FindWindowW(class.as_ptr(), std::ptr::null());
        if !window.is_null() {
            let mut process_id = 0;
            GetWindowThreadProcessId(window, &mut process_id);
            AllowSetForegroundWindow(process_id);
        }
    }
}

/// Native Save / Don't Save / Cancel prompt. The dialog plugin's JS API tops out at two
/// buttons, so the three-way unsaved-changes question has to be asked from Rust.
#[tauri::command]
async fn confirm_three_way(
    app: AppHandle,
    window: tauri::Window,
    title: String,
    message: String,
    yes_label: String,
    no_label: String,
) -> Result<String, String> {
    let (tx, rx) = tokio::sync::oneshot::channel();
    let (yes, no) = (yes_label.clone(), no_label.clone());
    app.dialog()
        .message(message)
        .title(title)
        .kind(MessageDialogKind::Warning)
        .parent(&window)
        .buttons(MessageDialogButtons::YesNoCancelCustom(
            yes_label,
            no_label,
            CANCEL_LABEL.to_string(),
        ))
        .show_with_result(move |res| {
            let _ = tx.send(res);
        });

    let result = rx.await.map_err(|e| e.to_string())?;
    // Linux reports the pressed button as Custom(label); dismissing via the window
    // manager lands on Cancel.
    Ok(match result {
        tauri_plugin_dialog::MessageDialogResult::Yes
        | tauri_plugin_dialog::MessageDialogResult::Ok => "yes".to_string(),
        tauri_plugin_dialog::MessageDialogResult::No => "no".to_string(),
        tauri_plugin_dialog::MessageDialogResult::Custom(s) if s == yes => "yes".to_string(),
        tauri_plugin_dialog::MessageDialogResult::Custom(s) if s == no => "no".to_string(),
        _ => "cancel".to_string(),
    })
}

#[tauri::command]
fn server_dir(app: AppHandle) -> Result<String, String> {
    // Directory where the sidecar was extracted at install time. Calcpad.Server
    // writes its logs, port file, and cached Chromium download here — the JS
    // bridge needs the path to surface log tails in the Output panel.
    app.path()
        .resolve("", BaseDirectory::Resource)
        .map(|p| p.to_string_lossy().to_string())
        .map_err(|e| e.to_string())
}

// Writable log directory shared with the .NET sidecar. The resource dir can be read-only,
// so both sides point at app_data_dir/logs instead.
fn resolve_log_dir(app: &AppHandle) -> Option<PathBuf> {
    let dir = app.path().app_data_dir().ok()?.join("logs");
    let _ = std::fs::create_dir_all(&dir);
    Some(dir)
}

/// The saved log level, read straight from the settings file the frontend writes.
///
/// The frontend re-pushes the level over `/api/calcpad/log-level` once it loads, but that is far
/// too late for the server's own startup entries — without this, choosing Verbose still loses
/// everything up to the first bind. Unreadable or unrecognised leaves it to the server's default.
fn stored_log_level(app: &AppHandle) -> Option<String> {
    let path = app
        .path()
        .app_data_dir()
        .ok()?
        .join("settings")
        .join("active-settings.json");
    let bytes = std::fs::read(path).ok()?;
    let parsed: serde_json::Value = serde_json::from_slice(&bytes).ok()?;
    let level = parsed.get("extras")?.get("logLevel")?.as_str()?;
    matches!(level, "error" | "warning" | "information" | "verbose").then(|| level.to_string())
}

#[tauri::command]
fn log_dir(app: AppHandle) -> Result<String, String> {
    resolve_log_dir(&app)
        .map(|p| p.to_string_lossy().to_string())
        .ok_or_else(|| "app_data_dir unresolved".to_string())
}

/// Launch the .NET calculation server as a background child process.
///
/// **Why not `tauri_plugin_shell::sidecar()`?** Framework-dependent .NET publishes need ~200
/// sibling DLLs / native libs / deps.json in the apphost's own directory, and no Tauri config
/// puts `externalBin` and `bundle.resources` in the same place (nor is there a post-bundle
/// hook). Spawning directly from the resource dir via `tokio::process::Command` sidesteps the
/// layout mismatch entirely.
///
/// **macOS limitation** — dropping `externalBin` also drops Tauri's automatic codesigning of
/// the child binary and its `.dylib` siblings, which notarization would reject. Deferred while
/// macOS is not a primary target; the fix is a `codesign` pass in `beforeBundleCommand` or
/// upstream support (tauri-apps/tauri#8501, #11992).
async fn spawn_sidecar(app: &AppHandle) -> Result<String, String> {
    let state: State<'_, ServerState> = app.state();
    let my_gen = state.generation.fetch_add(1, Ordering::SeqCst) + 1;
    if let Ok(mut g) = state.failure.lock() {
        *g = None;
    }
    if let Ok(mut g) = state.last_crash.lock() {
        *g = None;
    }

    let parent_pid = std::process::id().to_string();
    // Explicit port-file path in temp so we don't depend on the child's CWD.
    // Rust polls this file — under piped stdio, ASP.NET Core's ConsoleLogger
    // can buffer "Now listening on:" for hundreds of ms; the port file lands
    // within one Kestrel binding cycle regardless.
    let port_file = std::env::temp_dir().join(format!(
        ".calcpad-server-{}-{}.port",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_millis())
            .unwrap_or(0)
    ));
    let _ = std::fs::remove_file(&port_file);
    let port_file_str = port_file.to_string_lossy().into_owned();

    let exe_name = if cfg!(windows) {
        SIDECAR_EXE_WINDOWS
    } else {
        SIDECAR_EXE_UNIX
    };
    let exe_path = app
        .path()
        .resolve(exe_name, BaseDirectory::Resource)
        .map_err(|e| format!("resource path lookup failed for {exe_name}: {e}"))?;
    let exe_dir = exe_path
        .parent()
        .ok_or_else(|| format!("resolved apphost {exe_path:?} has no parent"))?
        .to_path_buf();

    let spawn_started = Instant::now();
    eprintln!("[sidecar-timing] spawning {exe_path:?}");
    let log_dir = resolve_log_dir(app);
    let mut command = tokio::process::Command::new(&exe_path);
    #[cfg(windows)]
    {
        let runtime_root = exe_dir.join(".dotnet");
        if runtime_root.join("dotnet.exe").is_file() {
            let architecture = if cfg!(target_arch = "aarch64") {
                "ARM64"
            } else {
                "X64"
            };
            command.env(format!("DOTNET_ROOT_{architecture}"), &runtime_root);
        }
    }
    command
        .args([
            "--no-exit-on-stdin-close",
            "--parent-pid",
            parent_pid.as_str(),
            "--port-file",
            port_file_str.as_str(),
        ])
        // CWD must be the apphost's directory so .NET's dependency resolver
        // finds the sibling DLLs regardless of where this process was started.
        .current_dir(&exe_dir)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    if let Some(dir) = &log_dir {
        command.env("CALCPAD_LOG_DIR", dir);
    }
    if let Some(level) = stored_log_level(app) {
        command.env("CALCPAD_LOG_LEVEL", level);
    }
    if state.ignore_settings.load(Ordering::SeqCst) {
        command.env("CALCPAD_IGNORE_APPSETTINGS", "1");
    }
    // Every /api route on the child requires this header value, passed via env rather than argv
    // (see api_token()). ASPNETCORE_ENVIRONMENT is pinned because the child inherits our whole
    // environment: a developer with Development exported would otherwise get Swagger and the
    // debug-crash endpoint on a shipped app.
    command
        .env("CALCPAD_API_TOKEN", api_token())
        .env("ASPNETCORE_ENVIRONMENT", "Production");

    // Enable the .NET runtime's on-crash minidump (createdump) for StackOverflow, FailFast and
    // access violations, which bypass AppDomain.UnhandledException so the server's FileLogger
    // never sees them. These vars must be set before the runtime boots, which is why they live
    // here rather than in Program.cs.
    //
    // The dump goes to the writable logs/ dir because the resource dir beside the apphost can
    // be read-only. Fixed filename — each crash overwrites the last, matching the
    // one-server-at-a-time model.
    let dump_dir = crash_dir()
        .map(|p| p.to_path_buf())
        .or_else(|| log_dir.clone())
        .unwrap_or_else(std::env::temp_dir);
    let _ = std::fs::create_dir_all(&dump_dir);
    let dump_path = dump_dir.join("last-crash.dmp");
    command
        .env("DOTNET_DbgEnableMiniDump", "1")
        .env("DOTNET_DbgMiniDumpType", "2")
        .env("DOTNET_DbgMiniDumpName", &dump_path)
        .env("DOTNET_EnableCrashReport", "1");

    // createdump ships beside the apphost, but resource packaging can drop the
    // executable bit; without +x the runtime fails to spawn it on crash. Re-set
    // it best-effort — a no-op on read-only installs where the bit already
    // survived (the same packaging that keeps the apphost runnable).
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let createdump = exe_dir.join("createdump");
        if let Ok(meta) = std::fs::metadata(&createdump) {
            let mut perms = meta.permissions();
            perms.set_mode(0o755);
            let _ = std::fs::set_permissions(&createdump, perms);
        }
    }

    #[cfg(windows)]
    {
        // The apphost is a console subsystem binary; without this it pops up
        // its own visible console window since it has no console to inherit.
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    let mut child = command
        .spawn()
        .map_err(|e| format!("sidecar spawn failed: {e}"))?;
    eprintln!(
        "[sidecar-timing] spawn() returned after {}ms",
        spawn_started.elapsed().as_millis()
    );

    #[cfg(windows)]
    if let Some(pid) = child.id() {
        assign_to_job_object(pid);
    }

    let stdout = child.stdout.take();
    let stderr = child.stderr.take();

    // One-shot for start()'s port-ready promise; readers and the port-file
    // poller both race to fulfil it, whichever wins takes the tx.
    let (tx_url, rx_url) = tokio::sync::oneshot::channel::<Result<String, String>>();
    let tx_url = Arc::new(Mutex::new(Some(tx_url)));

    // Kill signal — stop_sidecar sends into this; the wait task translates
    // it into start_kill() on the Child.
    let (kill_tx, mut kill_rx) = mpsc::channel::<()>(1);
    *state.kill_tx.lock().unwrap() = Some(kill_tx);

    // Rolling tail of the child's combined stdio, shared with the wait task
    // so it can attach the last N bytes to the server-crashed payload.
    // Also shared globally with the panic hook via SIDECAR_TAIL so a Rust
    // panic can dump the same context.
    let tail = SIDECAR_TAIL
        .get_or_init(|| Arc::new(Mutex::new(String::new())))
        .clone();
    if let Ok(mut t) = tail.lock() {
        t.clear();
    }
    let saw_first_output = Arc::new(std::sync::atomic::AtomicBool::new(false));

    // Port-file poller — resolves as soon as Kestrel binds, independent of
    // stdio buffering.
    {
        let tx_url = tx_url.clone();
        let app_for_poll = app.clone();
        let port_file = port_file.clone();
        tauri::async_runtime::spawn(async move {
            for _ in 0..600 {
                if let Ok(bytes) = std::fs::read(&port_file) {
                    let url = String::from_utf8_lossy(&bytes).trim().to_string();
                    if url.starts_with("http") {
                        eprintln!(
                            "[sidecar-timing] port file appeared after {}ms — {}",
                            spawn_started.elapsed().as_millis(),
                            url
                        );
                        let state: State<'_, ServerState> = app_for_poll.state();
                        if let Ok(mut g) = state.url.lock() {
                            *g = Some(url.clone());
                        }
                        if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                            let _ = tx.send(Ok(url.clone()));
                        }
                        let _ = app_for_poll.emit("server-url", url);
                        eprintln!(
                            "[sidecar-timing] emitted server-url after {}ms",
                            spawn_started.elapsed().as_millis()
                        );
                        break;
                    }
                }
                tokio::time::sleep(Duration::from_millis(50)).await;
            }
        });
    }

    // Relay task — owns every `server-log` emit so the readers below never do one. `emit`
    // dispatches to the webview and can take arbitrarily long under load; doing it inline meant a
    // busy UI stalled the reader, filled the child's 64KB stdout pipe, and blocked the .NET side
    // in `Console.Write` — hanging the whole server with the app still responsive. The channel is
    // bounded and lines are dropped rather than queued, since the tail buffer keeps them anyway.
    let (log_tx, mut log_rx) = mpsc::channel::<ServerLogLine>(1024);
    {
        let app_for_log = app.clone();
        tauri::async_runtime::spawn(async move {
            while let Some(entry) = log_rx.recv().await {
                let _ = app_for_log.emit("server-log", entry);
            }
        });
    }

    // Stdout/stderr line readers — replaces the shell plugin's CommandEvent stream. Each stream
    // drains on its own task so a chatty stderr doesn't starve stdout, both feed the shared tail
    // buffer while scanning for Kestrel's "Now listening on:" marker, and both are type-erased to
    // `Box<dyn AsyncRead + Send + Unpin>` since they are different concrete types.
    use tokio::io::AsyncRead;
    type UrlSender = Arc<Mutex<Option<tokio::sync::oneshot::Sender<Result<String, String>>>>>;

    #[derive(Clone)]
    struct ReaderContext {
        spawn_started: Instant,
        tx_url: UrlSender,
        app: AppHandle,
        tail: Arc<Mutex<String>>,
        saw_first: Arc<std::sync::atomic::AtomicBool>,
        log_tx: mpsc::Sender<ServerLogLine>,
    }

    fn spawn_stream_reader(
        stream: Box<dyn AsyncRead + Send + Unpin>,
        label: &'static str,
        ctx: ReaderContext,
    ) -> tauri::async_runtime::JoinHandle<()> {
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stream).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                if !ctx
                    .saw_first
                    .swap(true, std::sync::atomic::Ordering::Relaxed)
                {
                    eprintln!(
                        "[sidecar-timing] first stdio byte ({}) after {}ms",
                        label,
                        ctx.spawn_started.elapsed().as_millis()
                    );
                }
                if let Ok(mut t) = ctx.tail.lock() {
                    append_tail(&mut t, &line);
                    append_tail(&mut t, "\n");
                }
                // try_send, never send: blocking here is what wedges the sidecar.
                let _ = ctx.log_tx.try_send(ServerLogLine {
                    stream: label,
                    line: line.clone(),
                });
                if let Some(url) = extract_listening_url(&line) {
                    let state: State<'_, ServerState> = ctx.app.state();
                    if let Ok(mut g) = state.url.lock() {
                        *g = Some(url.clone());
                    }
                    if let Some(tx) = ctx.tx_url.lock().ok().and_then(|mut g| g.take()) {
                        let _ = tx.send(Ok(url.clone()));
                    }
                    let _ = ctx.app.emit("server-url", url);
                }
            }
        })
    }
    let reader_ctx = ReaderContext {
        spawn_started,
        tx_url: tx_url.clone(),
        app: app.clone(),
        tail: tail.clone(),
        saw_first: saw_first_output.clone(),
        log_tx: log_tx.clone(),
    };
    let mut readers = Vec::new();
    if let Some(s) = stdout {
        readers.push(spawn_stream_reader(
            Box::new(s),
            "stdout",
            reader_ctx.clone(),
        ));
    }
    if let Some(s) = stderr {
        readers.push(spawn_stream_reader(
            Box::new(s),
            "stderr",
            reader_ctx.clone(),
        ));
    }

    // Wait task — owns the Child, races natural exit against the kill signal,
    // and emits `server-crashed` on unintentional termination. This is the
    // piece the shell plugin used to give us for free via CommandEvent::Terminated.
    {
        let app_for_wait = app.clone();
        let tx_url = tx_url.clone();
        let tail = tail.clone();
        let dump_path = dump_path.clone();
        tauri::async_runtime::spawn(async move {
            // `killed` distinguishes an explicit stop (kill_rx fired) from an unexpected exit.
            // It must NOT be inferred from a shared `intentional_stop` flag: a concurrent
            // spawn during restart resets any such flag before the old process finishes dying,
            // misreporting its intentional kill as a crash and triggering a JS restart storm.
            let mut killed = false;
            let exit_code: Option<i32> = tokio::select! {
                r = child.wait() => r.ok().and_then(|s| s.code()),
                _ = kill_rx.recv() => {
                    killed = true;
                    let _ = child.start_kill();
                    child.wait().await.ok().and_then(|s| s.code())
                }
            };
            // Only clear shared state if a newer spawn hasn't superseded us.
            let state: State<'_, ServerState> = app_for_wait.state();
            let is_current = state.generation.load(Ordering::SeqCst) == my_gen;
            if is_current {
                if let Ok(mut g) = state.url.lock() {
                    *g = None;
                }
                if let Ok(mut g) = state.kill_tx.lock() {
                    *g = None;
                }
            }
            if !killed {
                // Exit can beat the readers to the last lines (e.g. the server's CONFIG ERROR).
                let _ = tokio::time::timeout(Duration::from_millis(500), async {
                    for r in readers {
                        let _ = r.await;
                    }
                })
                .await;
                let tail_snapshot = tail.lock().ok().map(|t| t.clone()).unwrap_or_default();
                // The .NET server writes its own crash log via FileLogger, but
                // duplicate here anyway — if the sidecar died before .NET's
                // AppDomain.UnhandledException could fire (SIGKILL, StackOverflow,
                // FailFast), that's the only trace of the tail we'll have.
                // Point at the runtime minidump if createdump produced one for
                // this crash (StackOverflow/FailFast/AV leave a dump but no tail).
                let dump_note = match std::fs::metadata(&dump_path) {
                    Ok(m) => format!("Minidump: {} ({} bytes)", dump_path.display(), m.len()),
                    Err(_) => format!("Minidump: none at {}", dump_path.display()),
                };
                let body = format!(
                    "=== Calcpad.Server sidecar exited unexpectedly ===\n\
                     Time (unix ms): {ms}\n\
                     Exit code: {code:?}\n\
                     {dump_note}\n\n\
                     --- Sidecar stdio tail ---\n{tail}\n",
                    ms = unix_millis(),
                    code = exit_code,
                    tail = tail_snapshot,
                );
                write_crash_report("sidecar", &body);
                let payload = ServerCrashPayload {
                    code: exit_code,
                    tail: tail_snapshot,
                };
                if is_current {
                    if let Ok(mut g) = state.last_crash.lock() {
                        *g = Some(payload.clone());
                    }
                }
                let _ = app_for_wait.emit("server-crashed", payload);
                if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                    let _ = tx.send(Err(format!(
                        "sidecar exited before port ready (code {exit_code:?})"
                    )));
                } else if is_current {
                    // Ran, then died: no spawn caller is left to record it.
                    let reason = match exit_code {
                        Some(code) => format!("server exited unexpectedly (exit code {code})"),
                        None => "server exited unexpectedly".to_string(),
                    };
                    record_failure(&app_for_wait, reason);
                }
            }
        });
    }

    tokio::time::timeout(Duration::from_millis(PORT_READY_TIMEOUT_MS), rx_url)
        .await
        .map_err(|_| "timed out waiting for server to bind port".to_string())?
        .map_err(|_| "server terminated before reporting url".to_string())?
}

fn stop_sidecar(app: &AppHandle) {
    let state: State<'_, ServerState> = app.state();
    // The wait task recognizes this shutdown by the kill signal itself (its
    // kill_rx branch), so it skips the `server-crashed` emit without any
    // shared flag. See the wait task in spawn_sidecar.
    if let Ok(mut g) = state.url.lock() {
        *g = None;
    }
    let kill_tx = state.kill_tx.lock().ok().and_then(|mut g| g.take());
    if let Some(tx) = kill_tx {
        // try_send is fine: the channel has capacity 1 and the wait task
        // only needs to see a single signal to trigger start_kill().
        let _ = tx.try_send(());
    }
}

fn extract_listening_url(line: &str) -> Option<String> {
    // Kestrel logs: "Now listening on: http://127.0.0.1:12345"
    let marker = "Now listening on:";
    let idx = line.find(marker)?;
    let after = line[idx + marker.len()..].trim_start();
    let end = after
        .find(|c: char| c.is_whitespace())
        .unwrap_or(after.len());
    let url = after[..end].trim_end_matches('/').to_string();
    if url.starts_with("http") {
        Some(url)
    } else {
        None
    }
}

fn append_tail(tail: &mut String, chunk: &str) {
    tail.push_str(chunk);
    const MAX: usize = 8 * 1024;
    if tail.len() > MAX {
        let cut = tail.len() - MAX;
        tail.drain(..cut);
    }
}

#[cfg(windows)]
fn assign_to_job_object(pid: u32) {
    use std::sync::OnceLock;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE};
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
        SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };
    use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_ALL_ACCESS};

    static JOB: OnceLock<isize> = OnceLock::new();
    let job = *JOB.get_or_init(|| unsafe {
        let handle = CreateJobObjectW(std::ptr::null(), std::ptr::null());
        if handle.is_null() {
            return 0;
        }
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(
            handle,
            JobObjectExtendedLimitInformation,
            &info as *const _ as *const _,
            std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        );
        handle as isize
    });
    if job == 0 {
        return;
    }
    unsafe {
        let proc = OpenProcess(PROCESS_ALL_ACCESS, 0, pid);
        if !proc.is_null() {
            AssignProcessToJobObject(job as HANDLE, proc);
            CloseHandle(proc);
        }
    }
}

/// Label for a recent entry: the full path with the user's home collapsed to `~`.
/// The file name on its own repeats across folders too often to identify one.
fn recent_label(app: &AppHandle, path: &str) -> String {
    let Ok(home) = app.path().home_dir() else {
        return path.to_string();
    };
    match path.strip_prefix(home.to_string_lossy().as_ref()) {
        Some(rest) => format!("~{rest}"),
        None => path.to_string(),
    }
}

fn build_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let state = app.state::<MenuState>();
    let recent = state
        .recent_files
        .lock()
        .expect("menu state mutex poisoned")
        .clone();
    let sep = || PredefinedMenuItem::separator(app);

    // Each entry carries its own path in the id rather than a list index, so a click
    // can never land on a different file than the one whose label was read. The
    // frontend strips the `open-recent:` prefix and opens the rest.
    let recent_items = recent
        .iter()
        .map(|path| {
            MenuItem::with_id(
                app,
                format!("open-recent:{path}"),
                recent_label(app, path),
                true,
                None::<&str>,
            )
        })
        .collect::<tauri::Result<Vec<_>>>()?;
    let no_recent = MenuItem::with_id(app, "no-recent", "No Recent Files", false, None::<&str>)?;
    let clear_recent = MenuItem::with_id(app, "clear-recent", "Clear Recent", true, None::<&str>)?;
    let recent_sep = sep()?;
    let mut recent_refs: Vec<&dyn IsMenuItem<tauri::Wry>> = Vec::new();
    if recent_items.is_empty() {
        recent_refs.push(&no_recent);
    } else {
        recent_refs.extend(
            recent_items
                .iter()
                .map(|i| i as &dyn IsMenuItem<tauri::Wry>),
        );
        recent_refs.push(&recent_sep);
        recent_refs.push(&clear_recent);
    }
    let open_recent = Submenu::with_items(app, "Open Recent", true, &recent_refs)?;

    Menu::with_items(
        app,
        &[
            &MenuItem::with_id(app, "new", "New Tab", true, Some("CmdOrCtrl+N"))?,
            // Ctrl+Shift+N and Ctrl+Q are the editor's Numbered List and Toggle Comment,
            // and a menu accelerator beats Monaco — so neither is bound here.
            &MenuItem::with_id(
                app,
                "new-window",
                "New Window",
                true,
                Some("CmdOrCtrl+Alt+W"),
            )?,
            &MenuItem::with_id(app, "open", "Open...", true, Some("CmdOrCtrl+O"))?,
            &open_recent,
            &sep()?,
            &MenuItem::with_id(app, "save", "Save", true, Some("CmdOrCtrl+S"))?,
            &MenuItem::with_id(
                app,
                "save-as",
                "Save As...",
                true,
                Some("CmdOrCtrl+Shift+S"),
            )?,
            &sep()?,
            &MenuItem::with_id(app, "close-tab", "Close Tab", true, Some("CmdOrCtrl+W"))?,
            &sep()?,
            &MenuItem::with_id(
                app,
                "show-server-log",
                "Show Server Log",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(app, "stop-server", "Stop Server", true, None::<&str>)?,
            &MenuItem::with_id(app, "restart-server", "Restart Server", true, None::<&str>)?,
            &sep()?,
            &MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?,
        ],
    )
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    if let Some(code) = control::try_run_client() {
        std::process::exit(code);
    }
    install_panic_hook();
    let context = tauri::generate_context!();
    #[cfg(all(windows, feature = "window-tests"))]
    let context = {
        let mut context = context;
        let port = std::env::var("CALCPAD_TEST_DEBUG_PORT").expect("test debug port is required");
        for window in &mut context.config_mut().app.windows {
            let args = window
                .additional_browser_args
                .get_or_insert_with(String::new);
            args.push_str(&format!(" --remote-debugging-port={port}"));
        }
        context
    };
    #[cfg(windows)]
    allow_existing_instance_foreground(&context.config().identifier);
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            let files = extract_launch_files(argv);
            if files.is_empty() {
                let app = app.clone();
                tauri::async_runtime::spawn(async move {
                    new_window(app)
                        .await
                        .expect("failed to open window for application launch");
                });
                return;
            }

            let target = app
                .state::<FocusedWindow>()
                .0
                .lock()
                .expect("focused window mutex poisoned")
                .clone();
            let window = app
                .get_webview_window(&target)
                .expect("last focused window is unavailable");
            let _ = window.show();
            let _ = window.unminimize();
            let _ = window.set_focus();
            for path in files {
                // The OS handing us this path is the user's consent; no dialog ran.
                allow_file_and_parent(app, &path);
                let _ = app.emit_to(
                    &target,
                    "open-file-request",
                    path.to_string_lossy().to_string(),
                );
            }
        }))
        .plugin(tauri_plugin_fs::init())
        // Must follow the fs plugin: it reaches for that plugin's scope at setup
        // and silently no-ops if it isn't managed yet. Persists the per-pick grants
        // the dialog adds at runtime, so a recent file or restored folder outside
        // $HOME still opens after a restart.
        .plugin(tauri_plugin_persisted_scope::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_os::init())
        .plugin(
            tauri_plugin_log::Builder::default()
                // Suppress Tao's routine debug messages:
                // "NewEvents emitted without explicit RedrawEventsCleared"
                // "RedrawEventsCleared emitted without explicit MainEventsCleared"
                .level_for(
                    "tao::platform_impl::platform::event_loop::runner",
                    log::LevelFilter::Info,
                )
                .build(),
        )
        .plugin(tauri_plugin_opener::init())
        .plugin(
            tauri::plugin::Builder::<tauri::Wry>::new("custom-titlebar")
                .on_window_ready(|window| {
                    window.hide_menu().expect("failed to hide native menu");
                })
                .build(),
        )
        .plugin(
            tauri_plugin_window_state::Builder::default()
                .with_filter(|label| label == MAIN_WINDOW_LABEL)
                .with_state_flags(StateFlags::SIZE | StateFlags::POSITION | StateFlags::MAXIMIZED)
                .build(),
        )
        .manage(ServerState::default())
        .manage(PendingLaunchFiles::default())
        .manage(FocusedWindow::default())
        .manage(PrimaryWindow::default())
        .manage(OpenFiles::default())
        .manage(MenuState::default())
        .manage(NavigationHistoryState::default())
        .manage(control::ControlState::default())
        .menu(build_menu)
        .invoke_handler(tauri::generate_handler![
            server_url,
            server_failure,
            server_token,
            restart_server,
            stop_server,
            get_env,
            server_dir,
            draft_write,
            draft_list,
            draft_read,
            draft_delete,
            log_dir,
            take_pending_launch_files,
            allow_document_dir,
            set_recent_files,
            confirm_three_way,
            window_labels,
            tab_drag_target,
            use_system_tab_drag,
            #[cfg(feature = "window-tests")]
            test_set_tab_drag_target,
            primary_window,
            set_open_files,
            navigate_to_open_file,
            new_window,
            record_navigation,
            remove_navigation,
            navigate_history,
            complete_navigation,
            show_app_menu,
            get_app_menu,
            create_export_path,
            #[cfg(target_os = "linux")]
            linux_file_dialog::linux_file_dialog,
            control::control_respond,
            control::control_ready,
        ])
        .on_window_event(|window, event| match event {
            tauri::WindowEvent::DragDrop(tauri::DragDropEvent::Drop { paths, .. }) => {
                for path in paths.iter().filter(|p| is_worksheet_path(p) && p.is_file()) {
                    allow_file_and_parent(window.app_handle(), path);
                    window
                        .emit_to(
                            window.label(),
                            "open-file-request",
                            path.to_string_lossy().to_string(),
                        )
                        .expect("failed to emit dropped file request");
                }
            }
            tauri::WindowEvent::Focused(true) => {
                *window
                    .state::<FocusedWindow>()
                    .0
                    .lock()
                    .expect("focused window mutex poisoned") = window.label().to_string();
            }
            tauri::WindowEvent::Destroyed => {
                window
                    .state::<OpenFiles>()
                    .0
                    .lock()
                    .expect("open files mutex poisoned")
                    .remove(window.label());
                window
                    .state::<NavigationHistoryState>()
                    .remove_window(window.label());
                let app = window.app_handle();
                control::mark_window_closed(app, window.label());
                let next = app
                    .webview_windows()
                    .into_keys()
                    .filter(|label| label != window.label())
                    .min();
                if let Some(next) = next {
                    let primary = app.state::<PrimaryWindow>();
                    let mut primary = primary.0.lock().expect("primary window mutex poisoned");
                    let changed = *primary == window.label();
                    if changed {
                        *primary = next;
                    }
                    let focused = app.state::<FocusedWindow>();
                    let mut focused = focused.0.lock().expect("focused window mutex poisoned");
                    if *focused == window.label() {
                        *focused = primary.clone();
                    }
                    if changed {
                        let _ = app.emit("primary-window-changed", primary.clone());
                    }
                }
            }
            _ => {}
        })
        .setup(|app| {
            app.get_webview_window(MAIN_WINDOW_LABEL)
                .expect("main window exists during setup")
                .unmaximize()?;

            // Pin the on-disk locations the panic hook + draft commands need.
            // app_data_dir is per-user and writable on all supported platforms.
            if let Ok(data_dir) = app.path().app_data_dir() {
                // Crash artifacts (panic/sidecar reports, the .NET minidump and
                // its crashreport.json, last-crash.txt) go in the same logs/ dir
                // as the server's own logs — one place, matching the VS Code side.
                let logs = data_dir.join("logs");
                let drafts = data_dir.join("drafts");
                let _ = std::fs::create_dir_all(&logs);
                let _ = std::fs::create_dir_all(&drafts);
                let _ = CRASH_DIR.set(logs);
                let _ = DRAFTS_DIR.set(drafts);
            }

            let handle_for_menu = app.handle().clone();
            // Addressed to the focused window: this handler fires for every window's menu,
            // so a broadcast would run each click and accelerator in all of them at once.
            app.on_menu_event(move |_app, event| {
                let target = handle_for_menu
                    .state::<FocusedWindow>()
                    .0
                    .lock()
                    .expect("focused window mutex poisoned")
                    .clone();
                let _ = handle_for_menu.emit_to(
                    target,
                    "menu-click",
                    MenuClickPayload {
                        id: event.id().0.clone(),
                    },
                );
            });

            control::start(app.handle().clone());

            let handle_for_spawn = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                match spawn_sidecar(&handle_for_spawn).await {
                    Ok(url) => {
                        let _ = handle_for_spawn.emit("server-url", url);
                    }
                    Err(err) => {
                        let body = format!(
                            "=== Calcpad.Server failed to start ===\n\
                             Time (unix ms): {ms}\n\
                             Error: {err}\n",
                            ms = unix_millis(),
                        );
                        write_crash_report("startup", &body);
                        record_failure(&handle_for_spawn, err.clone());
                        let _ = handle_for_spawn.emit("server-startup-error", err);
                    }
                }
            });

            // Surface any orphan drafts left by a prior session so the UI can
            // offer a recovery prompt. Emitted on the tick after setup so the
            // JS listener has a chance to register.
            let handle_for_drafts = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                tokio::time::sleep(Duration::from_millis(50)).await;
                if let Ok(drafts) = draft_list() {
                    if !drafts.is_empty() {
                        let _ = handle_for_drafts.emit_to(
                            MAIN_WINDOW_LABEL,
                            "drafts-recovered",
                            drafts,
                        );
                    }
                }
            });

            // Cold-start file associations: if the app was launched by
            // double-clicking a .cpd file, argv contains its path. Stash it
            // in shared state; the frontend drains it via take_pending_launch_files
            // once its listener is up (event emit would race the JS boot).
            let launch_files = extract_launch_files(std::env::args());
            if !launch_files.is_empty() {
                let pending: State<'_, PendingLaunchFiles> = app.state();
                let mut guard = pending
                    .0
                    .lock()
                    .expect("pending launch files mutex poisoned");
                for path in launch_files {
                    allow_file_and_parent(app.handle(), &path);
                    guard.push(path.to_string_lossy().to_string());
                }
            }

            // Main window is configured `visible: true` in tauri.conf.json and
            // intentionally NOT hidden-then-shown here: on GNOME (X11 and
            // Wayland) that pattern leaves titlebar buttons unresponsive until
            // the user double-clicks the titlebar.
            // See tauri-apps/tauri#11856 and #13440.
            Ok(())
        })
        // No CloseRequested handler: the frontend preventDefaults it, so killing there
        // leaves an open window with a dead server.
        .build(context)
        .expect("error while building tauri application")
        .run(|app, event| {
            if let RunEvent::ExitRequested { .. } = event {
                stop_sidecar(app);
            }
            if let RunEvent::Exit = event {
                stop_sidecar(app);
                control::cleanup();
            }
        });
}
