//! Local control channel for the desktop app.
//!
//! The app is a GUI; the only way to drive it from a script is the same
//! single-instance trick that opens files, which cannot return a result. This
//! module adds a per-user Unix socket under `$XDG_RUNTIME_DIR` (mode 0600) so a
//! terminal client can ask the running window to open/save/export/read state
//! and receive a JSON answer. The webview owns all editor logic; Rust only
//! transports requests and responses.

use std::collections::{HashMap, HashSet};
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use tauri::{Emitter, Manager};

#[cfg(unix)]
const SOCKET_NAME: &str = "calcpad-desktop.sock";
#[cfg(unix)]
const READY_TIMEOUT: Duration = Duration::from_secs(30);
#[cfg(unix)]
const RESPONSE_TIMEOUT: Duration = Duration::from_secs(120);
#[cfg(unix)]
const MAX_REQUEST_BYTES: usize = 2 * 1024 * 1024;
#[cfg(unix)]
const MAX_CONNECTIONS: usize = 32;
const USAGE: &str = "\
calcpadctl <command> [arguments]

  open <file.cpd>                        Open a file in a tab.
  close [file] [--force]                 Close the active tab or the file's tab.
  save                                   Save the active tab (requires a path).
  save-as <file>                         Save the active tab to the given path.
  menu <id>                              Trigger a native menu action.
  refresh                                Recalculate and refresh the preview.
  state                                  Return tabs, active file and state as JSON.
  content [file]                         Return the active/specified tab text.
  set-content <content|-> [file]         Set the text (read from a file or stdin).
  reload [file] [--force]                Reload the tab from disk.
  result                                 Return the current preview HTML.
  export <pdf|html|docx|cpdz|zip> <out> [mode]
                                         Export without a dialog (mode: report, preview, input, unwrapped).
  plots                                  List the document's plots.
  save-plot <index> <out>                Save one plot.
  save-plots <out.zip>                   Save all plots as a ZIP.
  editor info|focus|undo|redo            Editor state/focus; undo/redo.
  editor goto <line> [column]            Move the cursor.
  editor select <l1> <c1> <l2> <c2>      Select a range (line/column).
  editor insert <text|->                 Insert at the cursor or selection.
  editor find <text> [--regex --case --word]
  editor replace-all <text> <new> [--regex --case --word]
  clipboard read|write <text|->          Read/write the clipboard.
  window <action> [--window=<label>]     minimize|maximize|unmaximize|show|hide|close|destroy.
  message <json> [--wait=<type>]         Send a GUI message and wait for the response.
  eval <expression|->                    Run JS in the window (requires CALCPAD_CONTROL_EVAL=1).
  windows                                List open windows.
  focus                                  Bring the app window to the front.
  ping                                   Check whether the app is running.

The client also accepts `--ctl-json '<json>'` with {\"command\":...,\"args\":{...}}.
Use `--window=<label>` with any command to choose a window.";

#[derive(Default)]
pub struct ControlState {
    pending: Mutex<HashMap<u64, (String, mpsc::Sender<Value>)>>,
    next_id: AtomicU64,
    /// Windows whose webview has registered its control listener. Requests that
    /// arrive while a window is still booting wait here instead of being lost.
    ready: Mutex<HashSet<String>>,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct ControlEvent {
    id: u64,
    command: String,
    args: Value,
}

#[derive(Deserialize, Serialize)]
struct ClientRequest {
    command: String,
    #[serde(default)]
    args: Value,
    /// Window label to act on; defaults to the focused window.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    window: Option<String>,
}

#[cfg(unix)]
fn current_uid() -> u32 {
    unsafe { libc::geteuid() }
}

#[cfg(unix)]
fn socket_path() -> Result<std::path::PathBuf, String> {
    use std::os::unix::fs::{MetadataExt, PermissionsExt};

    if let Some(dir) = std::env::var_os("XDG_RUNTIME_DIR").filter(|dir| !dir.is_empty()) {
        let dir = std::path::PathBuf::from(dir);
        let meta = std::fs::metadata(&dir)
            .map_err(|err| format!("cannot inspect {}: {err}", dir.display()))?;
        if !meta.is_dir() {
            return Err(format!("{} is not a directory", dir.display()));
        }
        if meta.uid() != current_uid() {
            return Err(format!(
                "{} is not owned by the current user",
                dir.display()
            ));
        }
        if meta.permissions().mode() & 0o077 != 0 {
            return Err(format!("{} is not private (mode 0700)", dir.display()));
        }
        return Ok(dir.join(SOCKET_NAME));
    }

    let dir = std::env::temp_dir().join(format!("calcpad-desktop-{}", current_uid()));
    std::fs::create_dir_all(&dir)
        .map_err(|err| format!("cannot create {}: {err}", dir.display()))?;
    std::fs::set_permissions(&dir, std::fs::Permissions::from_mode(0o700))
        .map_err(|err| format!("cannot protect {}: {err}", dir.display()))?;
    let meta = std::fs::metadata(&dir)
        .map_err(|err| format!("cannot inspect {}: {err}", dir.display()))?;
    if meta.uid() != current_uid() || meta.permissions().mode() & 0o077 != 0 {
        return Err(format!(
            "{} is not a private directory owned by the user",
            dir.display()
        ));
    }
    Ok(dir.join(SOCKET_NAME))
}

#[cfg(unix)]
pub fn start(app: tauri::AppHandle) {
    use std::os::unix::fs::{MetadataExt, PermissionsExt};
    use std::os::unix::net::{UnixListener, UnixStream};

    std::thread::spawn(move || {
        let path = match socket_path() {
            Ok(path) => path,
            Err(err) => {
                eprintln!("[control] refusing to start: {err}");
                return;
            }
        };
        // A live server owns the path. Single-instance should prevent a second
        // process, but a stale socket from a crash must not block startup.
        if path.exists() {
            match std::fs::symlink_metadata(&path) {
                Ok(meta) if meta.uid() == current_uid() => {}
                Ok(_) => {
                    eprintln!(
                        "[control] refusing to use a socket owned by another user: {}",
                        path.display()
                    );
                    return;
                }
                Err(err) => {
                    eprintln!("[control] cannot inspect {}: {err}", path.display());
                    return;
                }
            }
            if let Ok(stream) = UnixStream::connect(&path) {
                drop(stream);
                return;
            }
            let _ = std::fs::remove_file(&path);
        }
        let listener = match UnixListener::bind(&path) {
            Ok(listener) => listener,
            Err(err) => {
                eprintln!("[control] cannot bind {}: {err}", path.display());
                return;
            }
        };
        if let Err(err) = std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o600)) {
            eprintln!("[control] cannot protect {}: {err}", path.display());
            let _ = std::fs::remove_file(&path);
            return;
        }
        if let Ok(meta) = std::fs::metadata(&path) {
            if meta.uid() != current_uid() {
                eprintln!("[control] socket owner mismatch at {}", path.display());
                let _ = std::fs::remove_file(&path);
                return;
            }
        }
        let active = Arc::new(AtomicUsize::new(0));
        let mut consecutive_errors = 0;
        for stream in listener.incoming() {
            match stream {
                Ok(stream) => {
                    consecutive_errors = 0;
                    if active.fetch_add(1, Ordering::SeqCst) >= MAX_CONNECTIONS {
                        active.fetch_sub(1, Ordering::SeqCst);
                        let _ = write_response(
                            stream,
                            json!({"ok": false, "error": "too many control connections"}),
                            0,
                        );
                        continue;
                    }
                    let app = app.clone();
                    let active = active.clone();
                    std::thread::spawn(move || {
                        handle_connection(app, stream);
                        active.fetch_sub(1, Ordering::SeqCst);
                    });
                }
                Err(err) => {
                    consecutive_errors += 1;
                    eprintln!("[control] accept failed: {err}");
                    if consecutive_errors >= 10 {
                        eprintln!("[control] stopping after repeated accept failures");
                        break;
                    }
                    std::thread::sleep(Duration::from_millis(100));
                }
            }
        }
    });
}
#[cfg(not(unix))]
pub fn start(_app: tauri::AppHandle) {}

fn lexical_normalize(path: &std::path::Path) -> std::path::PathBuf {
    use std::path::Component;

    let mut normalized = std::path::PathBuf::new();
    for component in path.components() {
        match component {
            Component::Prefix(_) | Component::RootDir => normalized.push(component.as_os_str()),
            Component::CurDir => {}
            Component::ParentDir => {
                normalized.pop();
            }
            Component::Normal(part) => normalized.push(part),
        }
    }
    normalized
}

#[cfg(unix)]
fn normalized_path(raw: &str) -> Result<std::path::PathBuf, String> {
    let path = std::path::Path::new(raw);
    if !path.is_absolute() {
        return Err(format!("path must be absolute: {raw}"));
    }
    if let Ok(canonical) = std::fs::canonicalize(path) {
        return Ok(canonical);
    }
    if let (Some(parent), Some(name)) = (path.parent(), path.file_name()) {
        if let Ok(parent) = std::fs::canonicalize(parent) {
            return Ok(parent.join(name));
        }
    }
    Ok(lexical_normalize(path))
}

#[cfg(unix)]
fn grant_request_paths(app: &tauri::AppHandle, request: &ClientRequest) -> Result<(), String> {
    let keys: &[&str] = match request.command.as_str() {
        "open" | "save_as" | "content" | "reload" | "set_content" | "export" | "save_plot"
        | "save_plots" => &["path"],
        _ => &[],
    };
    let mut paths = Vec::new();
    for key in keys {
        if let Some(Value::String(path)) = request.args.get(key) {
            if !path.is_empty() {
                paths.push(path.as_str());
            }
        }
    }
    if request.command == "message" {
        if let Some(message) = request.args.get("message").and_then(Value::as_object) {
            let kind = message
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or_default();
            if matches!(
                kind,
                "readDirectory" | "openFileByPath" | "openContainingFolder"
            ) {
                if let Some(Value::String(path)) = message.get("path") {
                    if !path.is_empty() {
                        paths.push(path.as_str());
                    }
                }
            }
        }
    }
    for raw in paths {
        let path = normalized_path(raw)?;
        super::allow_file_and_parent(app, &path);
    }
    Ok(())
}

#[cfg(unix)]
fn handle_connection(app: tauri::AppHandle, stream: std::os::unix::net::UnixStream) {
    use std::io::{BufRead, BufReader, Read};

    // The socket lives in the per-user runtime dir and is created mode 0600,
    // which keeps other users out.
    let _ = stream.set_read_timeout(Some(Duration::from_secs(10)));
    let _ = stream.set_write_timeout(Some(RESPONSE_TIMEOUT));

    let mut line = String::new();
    let reader = match stream.try_clone() {
        Ok(clone) => BufReader::new(clone),
        Err(_) => return,
    };
    let read = match reader
        .take(MAX_REQUEST_BYTES as u64 + 1)
        .read_line(&mut line)
    {
        Ok(read) => read,
        Err(_) => return,
    };
    if read > MAX_REQUEST_BYTES {
        let _ = write_response(
            stream,
            json!({"ok": false, "error": "control request is too large"}),
            0,
        );
        return;
    }
    let request: ClientRequest = match serde_json::from_str(line.trim()) {
        Ok(request) => request,
        Err(err) => {
            let _ = write_response(
                stream,
                json!({"ok": false, "error": format!("invalid request: {err}")}),
                0,
            );
            return;
        }
    };

    if request.command == "ping" {
        let version = app.package_info().version.to_string();
        let _ = write_response(
            stream,
            json!({"ok": true, "result": {"pid": std::process::id(), "version": version}}),
            0,
        );
        return;
    }

    if request.command == "windows" {
        let windows: Vec<Value> = app
            .webview_windows()
            .into_iter()
            .map(|(label, window)| {
                json!({
                    "label": label,
                    "focused": window.is_focused().unwrap_or(false),
                    "visible": window.is_visible().unwrap_or(false),
                })
            })
            .collect();
        let _ = write_response(
            stream,
            json!({"ok": true, "result": {"windows": windows}}),
            0,
        );
        return;
    }

    if request.command == "window" {
        let action = request
            .args
            .get("action")
            .and_then(Value::as_str)
            .unwrap_or_default();
        let focused = app
            .state::<super::FocusedWindow>()
            .0
            .lock()
            .map(|label| label.clone())
            .unwrap_or_else(|_| super::MAIN_WINDOW_LABEL.to_string());
        let label = request.window.as_deref().unwrap_or(&focused);
        let Some(window) = app.get_webview_window(label) else {
            let _ = write_response(
                stream,
                json!({"ok": false, "error": format!("window not found: {label}")}),
                0,
            );
            return;
        };
        let result = match action {
            "minimize" => window.minimize(),
            "maximize" => window.maximize(),
            "unmaximize" => window.unmaximize(),
            "show" => window.show(),
            "hide" => window.hide(),
            "close" => window.close(),
            "destroy" => window.destroy(),
            other => {
                let _ = write_response(
                    stream,
                    json!({"ok": false, "error": format!("unknown window action: {other}")}),
                    0,
                );
                return;
            }
        };
        match result {
            Ok(()) => {
                let mut payload = json!({"action": action, "label": label});
                if action == "close" {
                    std::thread::sleep(Duration::from_millis(100));
                    let closed = app.get_webview_window(label).is_none();
                    payload["closed"] = json!(closed);
                    payload["requested"] = json!(!closed);
                } else if action == "destroy" {
                    payload["closed"] = json!(true);
                }
                let _ = write_response(stream, json!({"ok": true, "result": payload}), 0);
            }
            Err(err) => {
                let _ = write_response(
                    stream,
                    json!({"ok": false, "error": format!("window action failed: {err}")}),
                    0,
                );
            }
        }
        return;
    }

    if request.command == "eval" && std::env::var("CALCPAD_CONTROL_EVAL").as_deref() != Ok("1") {
        let _ = write_response(
            stream,
            json!({
                "ok": false,
                "error": "eval is disabled; start the app with CALCPAD_CONTROL_EVAL=1"
            }),
            0,
        );
        return;
    }

    // Only commands that actually consume a filesystem path may widen the
    // webview's fs scope, and only after the command/eval gates above.
    if let Err(err) = grant_request_paths(&app, &request) {
        let _ = write_response(stream, json!({"ok": false, "error": err}), 0);
        return;
    }

    let target = match request.window.as_deref() {
        Some(label) => {
            if app.get_webview_window(label).is_none() {
                let _ = write_response(
                    stream,
                    json!({"ok": false, "error": format!("window not found: {label}")}),
                    0,
                );
                return;
            }
            label.to_string()
        }
        None => {
            let focused = app
                .state::<super::FocusedWindow>()
                .0
                .lock()
                .map(|label| label.clone())
                .unwrap_or_else(|_| super::MAIN_WINDOW_LABEL.to_string());
            if app.get_webview_window(&focused).is_some() {
                focused
            } else {
                super::MAIN_WINDOW_LABEL.to_string()
            }
        }
    };

    if request.command == "focus" {
        let Some(window) = app.get_webview_window(&target) else {
            let _ = write_response(
                stream,
                json!({"ok": false, "error": format!("window not found: {target}")}),
                0,
            );
            return;
        };
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
        let _ = write_response(stream, json!({"ok": true, "result": {"focused": true}}), 0);
        return;
    }

    let state = app.state::<ControlState>();
    let deadline = Instant::now() + READY_TIMEOUT;
    loop {
        let ready = state
            .ready
            .lock()
            .map(|ready| ready.contains(&target))
            .unwrap_or(false);
        if ready {
            break;
        }
        if Instant::now() >= deadline {
            let _ = write_response(
                stream,
                json!({"ok": false, "error": format!("window {target} is still starting")}),
                0,
            );
            return;
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    let id = state.next_id.fetch_add(1, Ordering::SeqCst) + 1;
    let (tx, rx) = mpsc::channel();
    if let Ok(mut pending) = state.pending.lock() {
        pending.insert(id, (target.clone(), tx));
    } else {
        let _ = write_response(
            stream,
            json!({"ok": false, "error": "control state is poisoned"}),
            id,
        );
        return;
    }

    let event = ControlEvent {
        id,
        command: request.command,
        args: request.args,
    };
    if let Err(err) = app.emit_to(&target, "control-request", event) {
        remove_pending(&app, id);
        let _ = write_response(
            stream,
            json!({"ok": false, "error": format!("cannot reach the window: {err}")}),
            id,
        );
        return;
    }

    let response = match rx.recv_timeout(RESPONSE_TIMEOUT) {
        Ok(response) => response,
        Err(_) => {
            remove_pending(&app, id);
            json!({"ok": false, "error": "the app did not answer in time"})
        }
    };
    let _ = write_response(stream, response, id);
}

#[cfg(unix)]
fn remove_pending(app: &tauri::AppHandle, id: u64) {
    if let Ok(mut pending) = app.state::<ControlState>().pending.lock() {
        pending.remove(&id);
    }
}

#[cfg(unix)]
fn write_response(
    mut stream: std::os::unix::net::UnixStream,
    mut response: Value,
    id: u64,
) -> std::io::Result<()> {
    use std::io::Write;

    if let Value::Object(ref mut map) = response {
        if id != 0 {
            map.insert("id".to_string(), json!(id));
        }
        map.insert("pid".to_string(), json!(std::process::id()));
    }
    let body = serde_json::to_string(&response)
        .unwrap_or_else(|_| "{\"ok\":false,\"error\":\"cannot serialize response\"}".to_string());
    stream.write_all(body.as_bytes())?;
    stream.write_all(b"\n")
}

#[tauri::command]
pub fn control_ready(window: tauri::WebviewWindow, state: tauri::State<'_, ControlState>) {
    if let Ok(mut ready) = state.ready.lock() {
        ready.insert(window.label().to_string());
    }
}

#[tauri::command]
pub fn control_respond(
    window: tauri::WebviewWindow,
    state: tauri::State<'_, ControlState>,
    id: u64,
    response: Value,
) {
    if let Ok(mut pending) = state.pending.lock() {
        let from_target = pending
            .get(&id)
            .map(|(label, _)| label == window.label())
            .unwrap_or(false);
        if !from_target {
            return;
        }
        if let Some((_, tx)) = pending.remove(&id) {
            let _ = tx.send(response);
        }
    }
}

/// Drops readiness and pending work for a window that no longer exists.
pub fn mark_window_closed(app: &tauri::AppHandle, label: &str) {
    let state = app.state::<ControlState>();
    if let Ok(mut ready) = state.ready.lock() {
        ready.remove(label);
    }
    if let Ok(mut pending) = state.pending.lock() {
        let ids: Vec<u64> = pending
            .iter()
            .filter(|(_, (target, _))| target == label)
            .map(|(id, _)| *id)
            .collect();
        for id in ids {
            if let Some((_, tx)) = pending.remove(&id) {
                let _ = tx.send(json!({
                    "ok": false,
                    "error": format!("window {label} closed")
                }));
            }
        }
    };
}

#[cfg(unix)]
pub fn cleanup() {
    if let Ok(path) = socket_path() {
        let _ = std::fs::remove_file(path);
    }
}

#[cfg(not(unix))]
pub fn cleanup() {}

/// Intercepts `calcpad-desktop --ctl ...` before Tauri boots and returns the
/// process exit code. `None` means this is a normal GUI launch.
pub fn try_run_client() -> Option<i32> {
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    let window = args
        .iter()
        .position(|arg| arg.starts_with("--window="))
        .map(|index| {
            args.remove(index)
                .trim_start_matches("--window=")
                .to_string()
        });
    let index = args
        .iter()
        .position(|arg| arg == "--ctl" || arg == "--ctl-json")?;
    let json_mode = args[index] == "--ctl-json";
    let parsed = if json_mode {
        args.get(index + 1)
            .ok_or_else(|| "missing JSON after --ctl-json".to_string())
            .and_then(|raw| {
                serde_json::from_str::<ClientRequest>(raw).map_err(|err| err.to_string())
            })
    } else {
        parse_friendly(&args[index + 1..])
    };
    Some(match parsed {
        Ok(mut request) => {
            if request.window.is_none() {
                request.window = window;
            }
            send(request)
        }
        Err(err) => {
            println!("{}", json!({"ok": false, "error": err}));
            eprintln!("{USAGE}");
            2
        }
    })
}

fn parse_friendly(args: &[String]) -> Result<ClientRequest, String> {
    let (command, rest) = args
        .split_first()
        .ok_or_else(|| "missing command".to_string())?;
    let force = rest.iter().any(|arg| arg == "--force");
    let regex = rest.iter().any(|arg| arg == "--regex");
    let case_sensitive = rest.iter().any(|arg| arg == "--case");
    let whole_word = rest.iter().any(|arg| arg == "--word");
    let wait_for = rest
        .iter()
        .find_map(|arg| arg.strip_prefix("--wait=").map(str::to_string));
    let positional: Vec<&str> = rest
        .iter()
        .filter(|arg| {
            !matches!(arg.as_str(), "--force" | "--regex" | "--case" | "--word")
                && !arg.starts_with("--wait=")
        })
        .map(String::as_str)
        .collect();
    let mut map = serde_json::Map::new();
    let command = match command.as_str() {
        "open" => {
            map.insert(
                "path".into(),
                json!(absolute(require(&positional, 0, "path")?)),
            );
            "open"
        }
        "close" => {
            if let Some(path) = positional.first() {
                map.insert("path".into(), json!(absolute(path)));
            }
            map.insert("force".into(), json!(force));
            "close"
        }
        "save" => "save",
        "save-as" => {
            let path = require(&positional, 0, "path")?;
            let path = if std::path::Path::new(path).extension().is_none() {
                format!("{path}.cpd")
            } else {
                path.to_string()
            };
            map.insert("path".into(), json!(absolute(&path)));
            "save_as"
        }
        "menu" => {
            map.insert("id".into(), json!(require(&positional, 0, "id")?));
            "menu"
        }
        "refresh" => "refresh",
        "state" => "state",
        "content" => {
            if let Some(path) = positional.first() {
                map.insert("path".into(), json!(absolute(path)));
            }
            "content"
        }
        "set-content" => {
            let source = require(&positional, 0, "content-file or -")?;
            let content = if source == "-" {
                use std::io::Read;
                let mut buffer = String::new();
                std::io::stdin()
                    .read_to_string(&mut buffer)
                    .map_err(|err| format!("cannot read stdin: {err}"))?;
                buffer
            } else {
                std::fs::read_to_string(source)
                    .map_err(|err| format!("cannot read {source}: {err}"))?
            };
            map.insert("content".into(), json!(content));
            if let Some(path) = positional.get(1) {
                map.insert("path".into(), json!(absolute(path)));
            }
            "set_content"
        }
        "reload" => {
            if let Some(path) = positional.first() {
                map.insert("path".into(), json!(absolute(path)));
            }
            map.insert("force".into(), json!(force));
            "reload"
        }
        "result" => "result",
        "export" => {
            let format = require(&positional, 0, "format")?;
            if !matches!(format, "pdf" | "html" | "docx" | "cpdz" | "zip") {
                return Err(format!("unknown export format: {format}"));
            }
            let output = absolute(require(&positional, 1, "output")?);
            let variant = positional.get(2).copied().unwrap_or("report");
            if !matches!(variant, "report" | "preview" | "input" | "unwrapped") {
                return Err(format!("unknown export variant: {variant}"));
            }
            map.insert("format".into(), json!(format));
            map.insert("path".into(), json!(output));
            map.insert("variant".into(), json!(variant));
            "export"
        }
        "plots" => "plots",
        "save-plot" => {
            let index = require(&positional, 0, "index")?;
            let index: usize = index
                .parse()
                .map_err(|_| format!("invalid plot index: {index}"))?;
            map.insert("index".into(), json!(index));
            map.insert(
                "path".into(),
                json!(absolute(require(&positional, 1, "path")?)),
            );
            "save_plot"
        }
        "save-plots" => {
            map.insert(
                "path".into(),
                json!(absolute(require(&positional, 0, "path")?)),
            );
            "save_plots"
        }
        "editor" => {
            let action = require(&positional, 0, "action")?;
            match action {
                "info" | "focus" | "undo" | "redo" => {}
                "goto" => {
                    map.insert(
                        "line".into(),
                        json!(parse_number(require(&positional, 1, "line")?)?),
                    );
                    if let Some(column) = positional.get(2) {
                        map.insert("column".into(), json!(parse_number(column)?));
                    }
                }
                "select" => {
                    map.insert(
                        "startLine".into(),
                        json!(parse_number(require(&positional, 1, "startLine")?)?),
                    );
                    map.insert(
                        "startColumn".into(),
                        json!(parse_number(require(&positional, 2, "startColumn")?)?),
                    );
                    map.insert(
                        "endLine".into(),
                        json!(parse_number(require(&positional, 3, "endLine")?)?),
                    );
                    map.insert(
                        "endColumn".into(),
                        json!(parse_number(require(&positional, 4, "endColumn")?)?),
                    );
                }
                "insert" => {
                    let source = require(&positional, 1, "text or -")?;
                    let text = if source == "-" {
                        use std::io::Read;
                        let mut buffer = String::new();
                        std::io::stdin()
                            .read_to_string(&mut buffer)
                            .map_err(|err| format!("cannot read stdin: {err}"))?;
                        buffer
                    } else {
                        source.to_string()
                    };
                    map.insert("text".into(), json!(text));
                }
                "find" => {
                    map.insert("query".into(), json!(require(&positional, 1, "query")?));
                    map.insert("regex".into(), json!(regex));
                    map.insert("caseSensitive".into(), json!(case_sensitive));
                    map.insert("wholeWord".into(), json!(whole_word));
                }
                "replace-all" => {
                    map.insert("query".into(), json!(require(&positional, 1, "query")?));
                    map.insert(
                        "replacement".into(),
                        json!(require(&positional, 2, "replacement")?),
                    );
                    map.insert("regex".into(), json!(regex));
                    map.insert("caseSensitive".into(), json!(case_sensitive));
                    map.insert("wholeWord".into(), json!(whole_word));
                }
                other => return Err(format!("unknown editor action: {other}")),
            }
            map.insert("action".into(), json!(action.replace('-', "_")));
            "editor"
        }
        "window" => {
            let action = require(&positional, 0, "action")?;
            if !matches!(
                action,
                "minimize" | "maximize" | "unmaximize" | "show" | "hide" | "close" | "destroy"
            ) {
                return Err(format!("unknown window action: {action}"));
            }
            map.insert("action".into(), json!(action));
            "window"
        }
        "clipboard" => {
            let action = require(&positional, 0, "action")?;
            match action {
                "read" => "clipboard_read",
                "write" => {
                    let source = require(&positional, 1, "text or -")?;
                    let text = if source == "-" {
                        use std::io::Read;
                        let mut buffer = String::new();
                        std::io::stdin()
                            .read_to_string(&mut buffer)
                            .map_err(|err| format!("cannot read stdin: {err}"))?;
                        buffer
                    } else {
                        source.to_string()
                    };
                    map.insert("text".into(), json!(text));
                    "clipboard_write"
                }
                other => return Err(format!("unknown clipboard action: {other}")),
            }
        }
        "message" => {
            let source = require(&positional, 0, "json or -")?;
            let raw = if source == "-" {
                use std::io::Read;
                let mut buffer = String::new();
                std::io::stdin()
                    .read_to_string(&mut buffer)
                    .map_err(|err| format!("cannot read stdin: {err}"))?;
                buffer
            } else {
                source.to_string()
            };
            let message: Value =
                serde_json::from_str(&raw).map_err(|err| format!("invalid message JSON: {err}"))?;
            map.insert("message".into(), message);
            if let Some(wait_for) = wait_for {
                map.insert("waitFor".into(), json!(wait_for));
            }
            "message"
        }
        "eval" => {
            let source = require(&positional, 0, "expression or -")?;
            let expression = if source == "-" {
                use std::io::Read;
                let mut buffer = String::new();
                std::io::stdin()
                    .read_to_string(&mut buffer)
                    .map_err(|err| format!("cannot read stdin: {err}"))?;
                buffer
            } else {
                source.to_string()
            };
            map.insert("expression".into(), json!(expression));
            "eval"
        }
        "windows" => "windows",
        "focus" => "focus",
        "ping" => "ping",
        other => return Err(format!("unknown command: {other}")),
    };
    Ok(ClientRequest {
        command: command.to_string(),
        args: Value::Object(map),
        window: None,
    })
}

fn require<'a>(args: &'a [&str], index: usize, name: &str) -> Result<&'a str, String> {
    args.get(index)
        .copied()
        .ok_or_else(|| format!("missing argument: {name}"))
}

fn parse_number(value: &str) -> Result<u32, String> {
    value
        .parse::<u32>()
        .map_err(|_| format!("invalid number: {value}"))
}

fn absolute(path: &str) -> String {
    let path = std::path::Path::new(path);
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()
            .map(|current| current.join(path))
            .unwrap_or_else(|_| path.to_path_buf())
    };
    let normalized = if let Ok(canonical) = std::fs::canonicalize(&absolute) {
        canonical
    } else if let (Some(parent), Some(name)) = (absolute.parent(), absolute.file_name()) {
        std::fs::canonicalize(parent)
            .map(|parent| parent.join(name))
            .unwrap_or_else(|_| lexical_normalize(&absolute))
    } else {
        lexical_normalize(&absolute)
    };
    normalized.to_string_lossy().into_owned()
}

fn send(request: ClientRequest) -> i32 {
    #[cfg(unix)]
    {
        use std::io::{BufRead, BufReader, Write};
        use std::os::unix::net::UnixStream;

        let path = match socket_path() {
            Ok(path) => path,
            Err(err) => {
                println!("{}", json!({"ok": false, "error": err}));
                return 1;
            }
        };
        let mut stream = match UnixStream::connect(&path) {
            Ok(stream) => stream,
            Err(err) => {
                println!(
                    "{}",
                    json!({
                        "ok": false,
                        "error": format!("CalcpadCE is not running or the control socket is unavailable ({}): {err}", path.display())
                    })
                );
                return 1;
            }
        };
        let _ = stream.set_read_timeout(Some(
            READY_TIMEOUT + RESPONSE_TIMEOUT + Duration::from_secs(10),
        ));
        let _ = stream.set_write_timeout(Some(Duration::from_secs(10)));
        let body = match serde_json::to_string(&request) {
            Ok(body) => body,
            Err(err) => {
                println!(
                    "{}",
                    json!({"ok": false, "error": format!("cannot encode request: {err}")})
                );
                return 2;
            }
        };
        if body.len() > MAX_REQUEST_BYTES {
            println!(
                "{}",
                json!({"ok": false, "error": "control request is too large"})
            );
            return 1;
        }
        if stream.write_all(body.as_bytes()).is_err() || stream.write_all(b"\n").is_err() {
            println!(
                "{}",
                json!({"ok": false, "error": "cannot write to the control socket"})
            );
            return 1;
        }
        let mut line = String::new();
        if BufReader::new(stream).read_line(&mut line).is_err() {
            println!(
                "{}",
                json!({"ok": false, "error": "cannot read the control response"})
            );
            return 1;
        }
        if line.trim().is_empty() {
            // Closing the last window succeeds by ending the process, so there is
            // no chance to write a response. Treat that as the expected outcome.
            let closes_app = (request.command == "menu"
                && request.args.get("id").and_then(Value::as_str) == Some("quit"))
                || (request.command == "window"
                    && matches!(
                        request.args.get("action").and_then(Value::as_str),
                        Some("close" | "destroy")
                    ));
            if closes_app {
                println!(
                    "{}",
                    serde_json::to_string_pretty(&json!({"ok": true, "result": {"closed": true}}))
                        .unwrap()
                );
                return 0;
            }
            println!(
                "{}",
                json!({"ok": false, "error": "the app closed the connection without a response"})
            );
            return 1;
        }
        let response: Value = serde_json::from_str(line.trim()).unwrap_or_else(
            |_| json!({"ok": false, "error": format!("invalid response: {}", line.trim())}),
        );
        println!(
            "{}",
            serde_json::to_string_pretty(&response).unwrap_or(line)
        );
        if response.get("ok").and_then(Value::as_bool).unwrap_or(false) {
            0
        } else {
            1
        }
    }
    #[cfg(not(unix))]
    {
        let _ = request;
        println!(
            "{}",
            json!({"ok": false, "error": "the control channel is only implemented on Unix"})
        );
        2
    }
}

#[cfg(all(test, unix))]
mod tests {
    use super::*;

    fn args(values: &[&str]) -> Vec<String> {
        values.iter().map(|value| value.to_string()).collect()
    }

    #[test]
    fn parses_open_path() {
        let request = parse_friendly(&args(&["open", "/tmp/x.cpd"])).unwrap();
        assert_eq!(request.command, "open");
        assert_eq!(request.args["path"], "/tmp/x.cpd");
    }

    #[test]
    fn parses_close_force() {
        let request = parse_friendly(&args(&["close", "--force"])).unwrap();
        assert_eq!(request.command, "close");
        assert_eq!(request.args["force"], true);
    }

    #[test]
    fn parses_export_defaults_to_report() {
        let request = parse_friendly(&args(&["export", "pdf", "/tmp/a.pdf"])).unwrap();
        assert_eq!(request.command, "export");
        assert_eq!(request.args["format"], "pdf");
        assert_eq!(request.args["path"], "/tmp/a.pdf");
        assert_eq!(request.args["variant"], "report");
    }

    #[test]
    fn rejects_unknown_export_format() {
        assert!(parse_friendly(&args(&["export", "tar", "/tmp/a.tar"])).is_err());
    }

    #[test]
    fn accepts_portable_export_formats() {
        assert_eq!(
            parse_friendly(&args(&["export", "cpdz", "/tmp/a.cpdz"]))
                .unwrap()
                .args["format"],
            "cpdz"
        );
        assert_eq!(
            parse_friendly(&args(&["export", "zip", "/tmp/a.zip"]))
                .unwrap()
                .args["format"],
            "zip"
        );
    }

    #[test]
    fn parses_plot_commands() {
        let plot = parse_friendly(&args(&["save-plot", "2", "/tmp/p.png"])).unwrap();
        assert_eq!(plot.command, "save_plot");
        assert_eq!(plot.args["index"], 2);
        let zip = parse_friendly(&args(&["save-plots", "/tmp/p.zip"])).unwrap();
        assert_eq!(zip.command, "save_plots");
    }

    #[test]
    fn rejects_unknown_command() {
        assert!(parse_friendly(&args(&["nope"])).is_err());
    }

    #[test]
    fn parses_message_with_wait() {
        let request = parse_friendly(&args(&[
            "message",
            r#"{"type":"getSettings"}"#,
            "--wait=settings",
        ]))
        .unwrap();
        assert_eq!(request.command, "message");
        assert_eq!(request.args["message"]["type"], "getSettings");
        assert_eq!(request.args["waitFor"], "settings");
    }

    #[test]
    fn rejects_unknown_export_variant() {
        assert!(parse_friendly(&args(&["export", "html", "/tmp/a.html", "reportt"])).is_err());
    }

    #[test]
    fn parses_eval_expression() {
        let request = parse_friendly(&args(&["eval", "window.calcpadTabs.activeId"])).unwrap();
        assert_eq!(request.command, "eval");
        assert_eq!(request.args["expression"], "window.calcpadTabs.activeId");
    }

    #[test]
    fn parses_windows_command() {
        let request = parse_friendly(&args(&["windows"])).unwrap();
        assert_eq!(request.command, "windows");
    }

    #[test]
    fn parses_window_and_clipboard() {
        let window = parse_friendly(&args(&["window", "minimize"])).unwrap();
        assert_eq!(window.command, "window");
        assert_eq!(window.args["action"], "minimize");
        let read = parse_friendly(&args(&["clipboard", "read"])).unwrap();
        assert_eq!(read.command, "clipboard_read");
        let write = parse_friendly(&args(&["clipboard", "write", "hello"])).unwrap();
        assert_eq!(write.command, "clipboard_write");
        assert_eq!(write.args["text"], "hello");
    }

    #[test]
    fn parses_editor_commands() {
        let goto = parse_friendly(&args(&["editor", "goto", "12", "3"])).unwrap();
        assert_eq!(goto.command, "editor");
        assert_eq!(goto.args["action"], "goto");
        assert_eq!(goto.args["line"], 12);
        assert_eq!(goto.args["column"], 3);

        let find = parse_friendly(&args(&[
            "editor", "find", "value", "--regex", "--case", "--word",
        ]))
        .unwrap();
        assert_eq!(find.args["action"], "find");
        assert_eq!(find.args["regex"], true);
        assert_eq!(find.args["caseSensitive"], true);
        assert_eq!(find.args["wholeWord"], true);

        let replace = parse_friendly(&args(&["editor", "replace-all", "a", "b"])).unwrap();
        assert_eq!(replace.args["action"], "replace_all");
    }
}
