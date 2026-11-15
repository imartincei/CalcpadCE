//! Agent provider extensions: `extensions/<id>/extension.json` plus an adapter executable
//! that speaks JSON lines on stdio (commands in, `AgentEvent`s out).

use std::path::{Component, Path, PathBuf};
use std::process::Stdio;
use std::sync::{Arc, Mutex};

use serde::Deserialize;
use serde_json::{json, Value};
use tauri::{AppHandle, Manager};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::sync::mpsc;

use super::{AgentBackend, AgentEvent, SessionKey};

pub const PROTOCOL_VERSION: u32 = 1;
const MANIFEST: &str = "extension.json";
/// Extra extension directories for development, separated like PATH.
const DEV_ENV: &str = "CALCPAD_EXTENSIONS";
const OUTPUT_TAIL_BYTES: usize = 4096;
/// Time the adapter gets to stop its agent before it is killed.
const STOP_GRACE: std::time::Duration = std::time::Duration::from_secs(3);

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Manifest {
    id: String,
    kind: String,
    command: String,
    #[serde(default)]
    permission_modes: Vec<ModeValue>,
}

#[derive(Deserialize)]
struct ModeValue {
    value: String,
}

pub struct Provider {
    pub id: String,
    pub dir: PathBuf,
    command: PathBuf,
    modes: Vec<String>,
    /// The manifest without `command`, for the frontend.
    pub info: Value,
}

impl Provider {
    pub fn check_mode(&self, mode: &str) -> Result<(), String> {
        if self.modes.iter().any(|m| m == mode) {
            Ok(())
        } else {
            Err(format!("Unsupported permission mode {mode}"))
        }
    }
}

fn valid_id(id: &str) -> bool {
    !id.is_empty() && id.len() <= 64 && id.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
}

fn resolve_command(dir: &Path, command: &str) -> Option<PathBuf> {
    let relative = Path::new(command);
    if !relative.components().all(|c| matches!(c, Component::Normal(_))) {
        return None;
    }
    let path = dir.join(relative);
    if cfg!(windows) && path.extension().is_none() {
        return Some(path.with_extension("exe"));
    }
    Some(path)
}

pub fn load(dir: &Path) -> Result<Provider, String> {
    let text = std::fs::read_to_string(dir.join(MANIFEST)).map_err(|e| e.to_string())?;
    let mut info: Value = serde_json::from_str(&text).map_err(|e| e.to_string())?;
    let manifest: Manifest = serde_json::from_value(info.clone()).map_err(|e| e.to_string())?;
    if manifest.kind != "agent" {
        return Err(format!("unsupported kind {}", manifest.kind));
    }
    if !valid_id(&manifest.id) {
        return Err(format!("invalid id {}", manifest.id));
    }
    if manifest.permission_modes.is_empty() {
        return Err("no permission modes".into());
    }
    let command = resolve_command(dir, &manifest.command).ok_or("command must be a path inside the extension")?;
    if let Some(map) = info.as_object_mut() {
        map.remove("command");
    }
    Ok(Provider {
        id: manifest.id,
        dir: dir.to_path_buf(),
        command,
        modes: manifest.permission_modes.into_iter().map(|m| m.value).collect(),
        info,
    })
}

fn subdirs(root: PathBuf) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(root) else {
        return vec![];
    };
    let mut dirs: Vec<PathBuf> = entries.flatten().map(|e| e.path()).filter(|p| p.is_dir()).collect();
    dirs.sort();
    dirs
}

/// Dev paths first, then user-installed, then bundled; the first of each id wins.
pub fn discover(app: &AppHandle) -> Vec<Arc<Provider>> {
    let mut dirs: Vec<PathBuf> = std::env::var_os(DEV_ENV)
        .map(|v| std::env::split_paths(&v).collect())
        .unwrap_or_default();
    if let Ok(data) = app.path().app_data_dir() {
        dirs.extend(subdirs(data.join("extensions")));
    }
    if let Ok(resources) = app.path().resource_dir() {
        dirs.extend(subdirs(resources.join("extensions")));
    }
    let mut out: Vec<Arc<Provider>> = vec![];
    for dir in dirs.into_iter().filter(|d| d.join(MANIFEST).is_file()) {
        match load(&dir) {
            Ok(provider) if out.iter().all(|p| p.id != provider.id) => {
                log::info!("Loaded agent extension {} from {}", provider.id, dir.display());
                out.push(Arc::new(provider));
            }
            Ok(_) => {}
            Err(err) => log::warn!("Skipped extension {}: {err}", dir.display()),
        }
    }
    out
}

pub struct ExtensionSession {
    stdin_tx: mpsc::Sender<String>,
    kill_tx: mpsc::Sender<()>,
}

impl ExtensionSession {
    fn write(&self, line: Value) -> Result<(), String> {
        self.stdin_tx
            .try_send(line.to_string())
            .map_err(|_| "The agent is not accepting input".to_string())
    }
}

impl AgentBackend for ExtensionSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        self.write(json!({"type": "send", "text": text}))
    }

    fn interrupt(&self) -> Result<(), String> {
        self.write(json!({"type": "interrupt"}))
    }

    fn set_model(&self, model: &str) -> Result<(), String> {
        self.write(json!({"type": "setModel", "model": model}))
    }

    fn set_permission_mode(&self, mode: &str) -> Result<(), String> {
        self.write(json!({"type": "setPermissionMode", "mode": mode}))
    }

    fn stop(&self) {
        let _ = self.write(json!({"type": "stop"}));
        let _ = self.kill_tx.try_send(());
    }
}

fn append_tail(tail: &Mutex<String>, line: &str) {
    let Ok(mut tail) = tail.lock() else {
        return;
    };
    tail.push_str(line);
    tail.push('\n');
    if tail.len() > OUTPUT_TAIL_BYTES {
        let mut cut = tail.len() - OUTPUT_TAIL_BYTES;
        while !tail.is_char_boundary(cut) {
            cut += 1;
        }
        tail.drain(..cut);
    }
}

/// Parses one adapter line; `exited` is reserved for the host.
pub fn parse_event(line: &str) -> Option<AgentEvent> {
    serde_json::from_str::<AgentEvent>(line)
        .ok()
        .filter(|e| !matches!(e, AgentEvent::Exited { .. }))
}

/// Starts the adapter and hands it `start` (which carries the MCP token) on stdin.
pub fn spawn(
    app: &AppHandle,
    key: &SessionKey,
    generation: u64,
    provider: &Provider,
    start: Value,
    cwd: &Path,
) -> Result<ExtensionSession, String> {
    let mut command = tokio::process::Command::new(&provider.command);
    command
        .current_dir(cwd)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true);
    #[cfg(windows)]
    {
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    let mut child = command
        .spawn()
        .map_err(|e| format!("Could not start the {} extension ({}): {e}", provider.id, provider.command.display()))?;
    #[cfg(windows)]
    if let Some(pid) = child.id() {
        crate::assign_to_job_object(pid);
    }
    log::info!("Started agent extension {}", provider.id);
    let mut stdin = child.stdin.take().ok_or("Extension stdin unavailable")?;
    let stdout = child.stdout.take().ok_or("Extension stdout unavailable")?;
    let stderr = child.stderr.take().ok_or("Extension stderr unavailable")?;

    let (stdin_tx, mut stdin_rx) = mpsc::channel::<String>(64);
    stdin_tx
        .try_send(start.to_string())
        .map_err(|_| "Could not start the extension".to_string())?;
    tauri::async_runtime::spawn(async move {
        while let Some(line) = stdin_rx.recv().await {
            if stdin.write_all(line.as_bytes()).await.is_err()
                || stdin.write_all(b"\n").await.is_err()
                || stdin.flush().await.is_err()
            {
                break;
            }
        }
    });

    // Emitting can stall under UI load; keep it off the reader so the pipe never fills.
    let (event_tx, mut event_rx) = mpsc::channel::<AgentEvent>(4096);
    {
        let app = app.clone();
        let key = key.clone();
        tauri::async_runtime::spawn(async move {
            while let Some(event) = event_rx.recv().await {
                if matches!(event, AgentEvent::Exited { .. }) {
                    super::on_exited(&app, &key, generation, event);
                } else {
                    super::emit_event(&app, &key, generation, event);
                }
            }
        });
    }
    let tail = Arc::new(Mutex::new(String::new()));
    let exit_tx = event_tx.clone();
    let stdout_task = {
        let tail = tail.clone();
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stdout).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                match parse_event(&line) {
                    Some(event) => {
                        let _ = event_tx.try_send(event);
                    }
                    None => append_tail(&tail, &line),
                }
            }
        })
    };
    let stderr_task = {
        let tail = tail.clone();
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stderr).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                append_tail(&tail, &line);
            }
        })
    };

    let (kill_tx, mut kill_rx) = mpsc::channel::<()>(1);
    let id = provider.id.clone();
    tauri::async_runtime::spawn(async move {
        let code = tokio::select! {
            status = child.wait() => status.ok().and_then(|s| s.code()),
            _ = kill_rx.recv() => match tokio::time::timeout(STOP_GRACE, child.wait()).await {
                Ok(status) => status.ok().and_then(|s| s.code()),
                Err(_) => {
                    let _ = child.start_kill();
                    child.wait().await.ok().and_then(|s| s.code())
                }
            }
        };
        let _ = stdout_task.await;
        let _ = stderr_task.await;
        let output = tail.lock().map(|t| t.clone()).unwrap_or_default();
        if code != Some(0) {
            log::warn!("Agent extension {id} exited with {code:?}: {output}");
        }
        let _ = exit_tx.send(AgentEvent::Exited { code, output }).await;
    });

    Ok(ExtensionSession { stdin_tx, kill_tx })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_extension(name: &str, manifest: &str) -> PathBuf {
        let dir = std::env::temp_dir()
            .join(format!("calcpad-ext-test-{}", std::process::id()))
            .join(name);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join(MANIFEST), manifest).unwrap();
        dir
    }

    #[test]
    fn loads_manifest_and_hides_command() {
        let dir = temp_extension("demo", r#"{"id":"demo","kind":"agent","name":"Demo","command":"bin/demo","permissionModes":[{"value":"default"}]}"#);
        let provider = load(&dir).unwrap();
        assert_eq!(provider.id, "demo");
        assert!(provider.command.starts_with(&dir));
        assert!(provider.info.get("command").is_none());
        assert_eq!(provider.info["name"], "Demo");
        assert!(provider.check_mode("default").is_ok());
        assert!(provider.check_mode("bypassPermissions").is_err());
    }

    #[test]
    fn rejects_commands_outside_the_extension() {
        assert!(resolve_command(Path::new("/ext"), "../evil").is_none());
        assert!(resolve_command(Path::new("/ext"), "/usr/bin/evil").is_none());
        assert!(resolve_command(Path::new("/ext"), "bin/ok").is_some());
    }

    #[test]
    fn parses_events_but_not_exited() {
        assert_eq!(
            parse_event(r#"{"kind":"textDelta","text":"hi"}"#),
            Some(AgentEvent::TextDelta { text: "hi".into() })
        );
        assert_eq!(parse_event(r#"{"kind":"exited","code":0,"output":""}"#), None);
        assert_eq!(parse_event("not json"), None);
    }
}
