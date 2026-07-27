use std::backtrace::Backtrace;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use tauri::menu::{Menu, MenuBuilder, MenuItem, PredefinedMenuItem, Submenu};
use tauri::path::BaseDirectory;
use tauri::{AppHandle, Emitter, Manager, RunEvent, State, WindowEvent};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::sync::mpsc;

// Populated at setup(); read from the panic hook (which has no AppHandle).
static CRASH_DIR: OnceLock<PathBuf> = OnceLock::new();
static DRAFTS_DIR: OnceLock<PathBuf> = OnceLock::new();
// Shared with spawn_sidecar so the panic hook can attach the last few KB
// of the child's combined stdio to the dump — same trick the C# server uses.
static SIDECAR_TAIL: OnceLock<Arc<Mutex<String>>> = OnceLock::new();

// Name of the .NET apphost inside the resource dir. All ~200 sibling
// DLLs / native libs / deps.json / runtimeconfig.json land next to it so
// framework-dependent .NET resolves its deps the way it expects.
const SIDECAR_EXE_UNIX: &str = "Calcpad.Server";
const SIDECAR_EXE_WINDOWS: &str = "Calcpad.Server.exe";
const PORT_READY_TIMEOUT_MS: u64 = 30_000;

/// Files handed to us by the OS at launch (double-click on .cpd via the
/// installed file association). Populated in setup() from argv; drained by
/// the frontend once it has registered its open-file listener, avoiding the
/// race between Rust emitting and JS being ready to listen.
#[derive(Default)]
struct PendingLaunchFiles(Mutex<Vec<String>>);

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
}

#[derive(Clone, Serialize)]
struct ServerCrashPayload {
    code: Option<i32>,
    tail: String,
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

/// Pull out any file paths (with .cpd extension) from a launch argv so we can
/// open them once the frontend is ready. argv[0] is the executable path — skip
/// it. macOS may pass an `-psn_...` process serial arg for double-click launches.
fn extract_launch_files<I: IntoIterator<Item = String>>(args: I) -> Vec<PathBuf> {
    args.into_iter()
        .skip(1)
        .filter(|a| !a.starts_with('-'))
        .map(PathBuf::from)
        .filter(|p| {
            p.extension()
                .and_then(|e| e.to_str())
                .map(|e| e.eq_ignore_ascii_case("cpd"))
                .unwrap_or(false)
        })
        .filter(|p| p.exists())
        .collect()
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
    let meta_json =
        serde_json::to_vec_pretty(&meta).map_err(|e| format!("serialize meta: {e}"))?;
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
    out.sort_by(|a, b| b.saved_at.cmp(&a.saved_at));
    Ok(out)
}

#[tauri::command]
fn draft_read(id: String) -> Result<Option<DraftContent>, String> {
    let (content_path, meta_path) = draft_paths(&id)?;
    if !content_path.exists() {
        return Ok(None);
    }
    let content = std::fs::read_to_string(&content_path)
        .map_err(|e| format!("read draft content: {e}"))?;
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
fn take_pending_launch_files(state: State<'_, PendingLaunchFiles>) -> Vec<String> {
    state.0.lock().map(|mut g| std::mem::take(&mut *g)).unwrap_or_default()
}

#[tauri::command]
async fn restart_server(app: AppHandle) -> Result<String, String> {
    stop_sidecar(&app);
    spawn_sidecar(&app).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn stop_server(app: AppHandle) -> Result<(), String> {
    stop_sidecar(&app);
    Ok(())
}

#[tauri::command]
fn get_env(name: String) -> Option<String> {
    std::env::var(name).ok()
}

// Inside a linuxdeploy-generated AppImage, AppRun exports LD_LIBRARY_PATH so
// the bundled binary can find its libs. If we spawn xdg-open through the
// opener plugin, the child inherits that env, and any glib/dbus tools it
// invokes crash trying to load the AppImage's bundled libc/libglib against
// the host system. Strip those vars (plus the archived originals AppRun
// stashes) so xdg-open sees a pristine environment.
#[tauri::command]
fn open_path_native(path: String) -> Result<(), String> {
    #[cfg(target_os = "linux")]
    {
        let mut cmd = std::process::Command::new("xdg-open");
        cmd.arg(&path);
        for key in [
            "LD_LIBRARY_PATH",
            "LD_PRELOAD",
            "GTK_DATA_PREFIX",
            "GTK_THEME",
            "GTK_EXE_PREFIX",
            "GTK_PATH",
            "GTK_IM_MODULE_FILE",
            "GDK_PIXBUF_MODULE_FILE",
            "GIO_EXTRA_MODULES",
            "GSETTINGS_SCHEMA_DIR",
            "XDG_DATA_DIRS",
            "PYTHONHOME",
            "PYTHONPATH",
            "PERLLIB",
            "QT_PLUGIN_PATH",
        ] {
            let orig = format!("APPDIR_ORIG_{key}");
            match std::env::var(&orig) {
                Ok(v) if !v.is_empty() => { cmd.env(key, v); }
                _ => { cmd.env_remove(key); }
            }
            cmd.env_remove(orig);
        }
        cmd.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
        cmd.spawn().map(|_| ()).map_err(|e| e.to_string())
    }
    #[cfg(not(target_os = "linux"))]
    {
        let _ = path;
        Err("open_path_native is Linux-only".to_string())
    }
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

// Writable log directory shared with the .NET sidecar. On an AppImage the
// resource dir is a read-only FUSE mount, so the server can't create logs/
// beside its apphost. Point both sides at app_data_dir/logs instead.
fn resolve_log_dir(app: &AppHandle) -> Option<PathBuf> {
    let dir = app.path().app_data_dir().ok()?.join("logs");
    let _ = std::fs::create_dir_all(&dir);
    Some(dir)
}

#[tauri::command]
fn log_dir(app: AppHandle) -> Result<String, String> {
    resolve_log_dir(&app)
        .map(|p| p.to_string_lossy().to_string())
        .ok_or_else(|| "app_data_dir unresolved".to_string())
}

/// Launch the .NET calculation server as a background child process.
///
/// **Why not `tauri_plugin_shell::sidecar()`?** Framework-dependent .NET
/// publishes need ~200 sibling DLLs / native libs / deps.json in the same
/// directory as the apphost. Tauri's `externalBin` places binaries in
/// `usr/bin/` on Linux `.deb`, `Contents/MacOS/` on macOS `.app`, and the
/// install root on Windows — all read-only at runtime — while
/// `bundle.resources` land in a separate resource dir. There is no config
/// path that puts both in the same directory, and Tauri v2 has no post-bundle
/// hook (`beforeBundleCommand` runs before packaging). Spawning directly
/// from the resource dir via `tokio::process::Command` sidesteps the layout
/// mismatch entirely — the apphost and its siblings all live under
/// `BaseDirectory::Resource`.
///
/// **macOS limitation** — dropping `externalBin` also drops Tauri's automatic
/// codesigning of the child binary and its ~200 `.dylib` siblings. On macOS,
/// notarization will reject an unsigned bundle. macOS is not a primary
/// target for this project today, so this is deferred. When it becomes one,
/// either (a) add a `codesign --deep --force --sign "$IDENTITY" --options
/// runtime` pass over the publish tree in `beforeBundleCommand`, or (b)
/// revisit once Tauri v2 supports codesigning resource files directly.
/// Tracking upstream: tauri-apps/tauri#8501 (per-arch resources), #11992
/// (macOS notarization + externalBin bugs).
async fn spawn_sidecar(app: &AppHandle) -> Result<String, String> {
    let state: State<'_, ServerState> = app.state();
    let my_gen = state.generation.fetch_add(1, Ordering::SeqCst) + 1;

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

    let exe_name = if cfg!(windows) { SIDECAR_EXE_WINDOWS } else { SIDECAR_EXE_UNIX };
    let exe_path = app
        .path()
        .resolve(exe_name, BaseDirectory::Resource)
        .map_err(|e| format!("resource path lookup failed for {exe_name}: {e}"))?;
    let exe_dir = exe_path
        .parent()
        .ok_or_else(|| format!("resolved apphost {exe_path:?} has no parent"))?
        .to_path_buf();

    let spawn_started = Instant::now();
    eprintln!("[sidecar-timing] spawning {:?}", exe_path);
    let log_dir = resolve_log_dir(app);
    let mut command = tokio::process::Command::new(&exe_path);
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

    // Enable the .NET runtime's on-crash minidump (createdump). StackOverflow,
    // FailFast and access violations bypass AppDomain.UnhandledException, so the
    // server's own FileLogger never sees them — the runtime-level dump is the
    // only trace. These vars must be set before the runtime boots (setting them
    // from inside the server is too late), which is why this lives here and not
    // in Program.cs. Mirrors the VS Code extension's spawn config.
    //
    // The dump goes to the writable logs/ dir (CRASH_DIR, falling back to the
    // locally-resolved log dir or temp) because on an AppImage the resource dir
    // beside the apphost is read-only. Fixed filename — each crash overwrites the
    // last, matching the one-server-at-a-time model.
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

    // Stdout/stderr line readers — replaces the shell plugin's CommandEvent
    // stream. Each stream drains on its own task so a chatty stderr doesn't
    // starve stdout. Both feed the shared tail buffer and scan for Kestrel's
    // "Now listening on:" marker as a fallback for the port file.
    //
    // `stdout` and `stderr` are different concrete types (ChildStdout /
    // ChildStderr), so we can't share a single non-generic closure. Type-erase
    // both to `Box<dyn AsyncRead + Send + Unpin>` and hand them to one reader
    // function; keeps the drain logic in one place.
    use tokio::io::AsyncRead;
    fn spawn_stream_reader(
        stream: Box<dyn AsyncRead + Send + Unpin>,
        label: &'static str,
        spawn_started: Instant,
        tx_url: Arc<Mutex<Option<tokio::sync::oneshot::Sender<Result<String, String>>>>>,
        app: AppHandle,
        tail: Arc<Mutex<String>>,
        saw_first: Arc<std::sync::atomic::AtomicBool>,
    ) {
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stream).lines();
            loop {
                match lines.next_line().await {
                    Ok(Some(line)) => {
                        if !saw_first.swap(true, std::sync::atomic::Ordering::Relaxed) {
                            eprintln!(
                                "[sidecar-timing] first stdio byte ({}) after {}ms",
                                label,
                                spawn_started.elapsed().as_millis()
                            );
                        }
                        if let Ok(mut t) = tail.lock() {
                            append_tail(&mut t, &line);
                            append_tail(&mut t, "\n");
                        }
                        let _ = app.emit(
                            "server-log",
                            ServerLogLine {
                                stream: label,
                                line: line.clone(),
                            },
                        );
                        if let Some(url) = extract_listening_url(&line) {
                            let state: State<'_, ServerState> = app.state();
                            if let Ok(mut g) = state.url.lock() {
                                *g = Some(url.clone());
                            }
                            if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                                let _ = tx.send(Ok(url.clone()));
                            }
                            let _ = app.emit("server-url", url);
                        }
                    }
                    Ok(None) | Err(_) => break,
                }
            }
        });
    }
    if let Some(s) = stdout {
        spawn_stream_reader(
            Box::new(s),
            "stdout",
            spawn_started,
            tx_url.clone(),
            app.clone(),
            tail.clone(),
            saw_first_output.clone(),
        );
    }
    if let Some(s) = stderr {
        spawn_stream_reader(
            Box::new(s),
            "stderr",
            spawn_started,
            tx_url.clone(),
            app.clone(),
            tail.clone(),
            saw_first_output.clone(),
        );
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
            // `killed` distinguishes an explicit stop (kill_rx fired — restart,
            // menu Stop, window close) from an unexpected exit. We must NOT
            // infer this from a shared `intentional_stop` flag: a concurrent
            // spawn during restart resets any such flag before the old process
            // finishes dying, so its intentional kill gets misreported as a
            // crash (start_kill exits with code 1 on Windows), which triggers a
            // JS auto-restart storm. The branch that fired is the ground truth.
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
                let tail_snapshot = tail
                    .lock()
                    .ok()
                    .map(|t| t.clone())
                    .unwrap_or_default();
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
                let _ = app_for_wait.emit(
                    "server-crashed",
                    ServerCrashPayload {
                        code: exit_code,
                        tail: tail_snapshot,
                    },
                );
                if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                    let _ = tx.send(Err(format!(
                        "sidecar exited before port ready (code {:?})",
                        exit_code
                    )));
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
        AssignProcessToJobObject, CreateJobObjectW, SetInformationJobObject,
        JobObjectExtendedLimitInformation,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
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

fn build_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let sep = || PredefinedMenuItem::separator(app);

    let file = Submenu::with_items(
        app,
        "File",
        true,
        &[
            &MenuItem::with_id(app, "new", "New Tab", true, Some("CmdOrCtrl+N"))?,
            &MenuItem::with_id(app, "open", "Open...", true, Some("CmdOrCtrl+O"))?,
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
            &MenuItem::with_id(app, "export-pdf", "Export PDF...", true, None::<&str>)?,
            &MenuItem::with_id(app, "export-html", "Export HTML...", true, None::<&str>)?,
            &MenuItem::with_id(app, "export-docx", "Export Word...", true, None::<&str>)?,
            &sep()?,
            &MenuItem::with_id(app, "quit", "Quit", true, Some("CmdOrCtrl+Q"))?,
        ],
    )?;

    let edit = Submenu::with_items(
        app,
        "Edit",
        true,
        &[
            &MenuItem::with_id(app, "undo", "Undo", true, Some("CmdOrCtrl+Z"))?,
            &MenuItem::with_id(app, "redo", "Redo", true, Some("CmdOrCtrl+Shift+Z"))?,
            &sep()?,
            &MenuItem::with_id(app, "cut", "Cut", true, Some("CmdOrCtrl+X"))?,
            &MenuItem::with_id(app, "copy", "Copy", true, Some("CmdOrCtrl+C"))?,
            &MenuItem::with_id(app, "paste", "Paste", true, Some("CmdOrCtrl+V"))?,
            &sep()?,
            &MenuItem::with_id(app, "select-all", "Select All", true, Some("CmdOrCtrl+A"))?,
            &MenuItem::with_id(app, "find", "Find", true, Some("CmdOrCtrl+F"))?,
            &MenuItem::with_id(app, "replace", "Replace", true, Some("CmdOrCtrl+H"))?,
        ],
    )?;

    let view = Submenu::with_items(
        app,
        "View",
        true,
        &[
            &MenuItem::with_id(
                app,
                "toggle-sidebar",
                "Toggle Sidebar",
                true,
                Some("CmdOrCtrl+Shift+B"),
            )?,
            &MenuItem::with_id(
                app,
                "toggle-preview",
                "Toggle Preview",
                true,
                Some("CmdOrCtrl+P"),
            )?,
            &MenuItem::with_id(
                app,
                "toggle-word-wrap",
                "Toggle Word Wrap",
                true,
                Some("Alt+Z"),
            )?,
            &sep()?,
            &MenuItem::with_id(
                app,
                "split-editor",
                "Split Editor Down",
                true,
                Some("CmdOrCtrl+\\"),
            )?,
            &MenuItem::with_id(
                app,
                "unsplit-editor",
                "Merge Editor Groups",
                true,
                None::<&str>,
            )?,
            &sep()?,
            &MenuItem::with_id(
                app,
                "preview-mode:wrapped",
                "Preview Mode: Wrapped",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(
                app,
                "preview-mode:unwrapped",
                "Preview Mode: Unwrapped",
                true,
                None::<&str>,
            )?,
        ],
    )?;

    let server = Submenu::with_items(
        app,
        "Server",
        true,
        &[
            &MenuItem::with_id(app, "refresh", "Refresh", true, Some("CmdOrCtrl+Alt+X"))?,
            &MenuItem::with_id(
                app,
                "show-server-log",
                "Show Server Log",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(app, "stop-server", "Stop Server", true, None::<&str>)?,
            &MenuItem::with_id(
                app,
                "restart-server",
                "Restart Server",
                true,
                None::<&str>,
            )?,
        ],
    )?;

    let help = Submenu::with_items(
        app,
        "Help",
        true,
        &[&MenuItem::with_id(
            app,
            "help-documentation",
            "Documentation",
            true,
            None::<&str>,
        )?],
    )?;

    MenuBuilder::new(app)
        .items(&[&file, &edit, &view, &server, &help])
        .build()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    install_panic_hook();
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, argv, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
                let _ = w.unminimize();
            }
            for path in extract_launch_files(argv) {
                let _ = app.emit("open-file-request", path.to_string_lossy().to_string());
            }
        }))
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_os::init())
        .plugin(tauri_plugin_log::Builder::default().build())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .manage(ServerState::default())
        .manage(PendingLaunchFiles::default())
        .invoke_handler(tauri::generate_handler![
            server_url,
            restart_server,
            stop_server,
            get_env,
            server_dir,
            draft_write,
            draft_list,
            draft_read,
            draft_delete,
            open_path_native,
            log_dir,
            take_pending_launch_files,
        ])
        .setup(|app| {
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

            let menu = build_menu(app.handle())?;
            app.set_menu(menu)?;
            let handle_for_menu = app.handle().clone();
            app.on_menu_event(move |_app, event| {
                let _ = handle_for_menu.emit(
                    "menu-click",
                    MenuClickPayload {
                        id: event.id().0.clone(),
                    },
                );
            });

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
                        let _ = handle_for_drafts.emit("drafts-recovered", drafts);
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
                let mut guard = pending.0.lock().expect("pending launch files mutex poisoned");
                for path in launch_files {
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
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { .. } = event {
                stop_sidecar(&window.app_handle().clone());
            }
        })
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| {
            if let RunEvent::ExitRequested { .. } = event {
                stop_sidecar(app);
            }
            if let RunEvent::Exit = event {
                stop_sidecar(app);
            }
        });
}
