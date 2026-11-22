//! Child-process plumbing shared by the harness backends.

use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde_json::{json, Value};
use tauri::AppHandle;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::sync::{mpsc, Notify};

use super::{AgentEvent, SessionKey};

const OUTPUT_TAIL_BYTES: usize = 4096;
const STOP_GRACE: Duration = Duration::from_secs(3);
// Set when the app is launched from inside an agent session; children must not inherit them.
const INHERITED_SESSION_VARS: &[&str] = &[
    "CLAUDECODE",
    "CLAUDE_PID",
    "CLAUDE_CODE_ENTRYPOINT",
    "CLAUDE_CODE_SESSION_ID",
    "CLAUDE_CODE_CHILD_SESSION",
    "CLAUDE_CODE_SESSION_ATTENDED",
    "CLAUDE_CODE_MESSAGING_SOCKET",
    "CLAUDE_CODE_MESSAGING_TOKEN",
    "CLAUDE_CODE_SSE_PORT",
    "CLAUDE_CODE_EXECPATH",
    "CLAUDE_AGENT_SDK_VERSION",
    "CODEX_SANDBOX",
    "CODEX_SANDBOX_NETWORK_DISABLED",
    "VSCODE_INSPECTOR_OPTIONS",
];

/// VS Code's debugger auto-attach injects these; Node-based CLIs then exit without output.
pub fn is_debugger_hook(node_options: &str) -> bool {
    node_options.contains("--inspect") || node_options.contains("--require") || node_options.contains("-r ")
}

pub fn command(exe: &Path, cwd: &Path) -> tokio::process::Command {
    let mut command = tokio::process::Command::new(exe);
    command
        .current_dir(cwd)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true);
    for name in INHERITED_SESSION_VARS {
        command.env_remove(name);
    }
    if std::env::var("NODE_OPTIONS").is_ok_and(|v| is_debugger_hook(&v)) {
        command.env_remove("NODE_OPTIONS");
    }
    #[cfg(windows)]
    {
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    command
}

pub fn write_private(path: &Path, contents: &str) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let mut options = std::fs::OpenOptions::new();
    options.write(true).create(true).truncate(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    std::io::Write::write_all(&mut options.open(path)?, contents.as_bytes())
}

pub fn truncate(text: String, max: usize) -> String {
    match text.char_indices().nth(max) {
        Some((cut, _)) => format!("{}…", &text[..cut]),
        None => text,
    }
}

/// A backend's handle on its running process.
pub struct Link {
    app: AppHandle,
    key: SessionKey,
    events: mpsc::Sender<AgentEvent>,
    stdin: mpsc::Sender<String>,
    close: Arc<Notify>,
    kill: mpsc::Sender<()>,
}

impl Link {
    pub fn emit(&self, event: AgentEvent) {
        let _ = self.events.try_send(event);
    }

    pub fn write(&self, line: &Value) -> Result<(), String> {
        self.stdin
            .try_send(line.to_string())
            .map_err(|_| "The agent is not accepting input".to_string())
    }

    /// Asks the user to approve a tool call in the chat panel; true when allowed.
    pub async fn approve(&self, tool_name: &str, input: Value) -> bool {
        let answer = super::request_window(
            &self.app,
            self.key.clone(),
            "approve_tool",
            json!({"tool_name": tool_name, "input": input}),
            None,
        )
        .await;
        matches!(answer, Ok(v) if v["behavior"] == "allow")
    }

    /// Asks the user questions in the chat panel; answers keyed by question text, or None if skipped.
    pub async fn ask(&self, questions: Value) -> Option<serde_json::Map<String, Value>> {
        let answer = super::request_window(&self.app, self.key.clone(), "ask_user", json!({"questions": questions}), None).await;
        answer.ok()?.get("answers")?.as_object().cloned()
    }

    /// Closes stdin, then kills the process if it is still running after a grace period.
    pub fn stop(&self) {
        self.close.notify_one();
        let _ = self.kill.try_send(());
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

/// Feeds stdout lines to `on_line`; lines it rejects go into the exit error. `cleanup` is deleted on exit.
pub fn spawn(
    app: &AppHandle,
    key: &SessionKey,
    generation: u64,
    mut command: tokio::process::Command,
    name: &str,
    cleanup: Option<PathBuf>,
    mut on_line: impl FnMut(&Arc<Link>, &str) -> bool + Send + 'static,
) -> Result<Arc<Link>, String> {
    let mut child = command.spawn().map_err(|e| format!("Could not start {name}: {e}"))?;
    #[cfg(windows)]
    if let Some(pid) = child.id() {
        crate::assign_to_job_object(pid);
    }
    log::info!("Started {name}");
    let mut stdin = child.stdin.take().ok_or("Agent stdin unavailable")?;
    let stdout = child.stdout.take().ok_or("Agent stdout unavailable")?;
    let stderr = child.stderr.take().ok_or("Agent stderr unavailable")?;

    let (stdin_tx, mut stdin_rx) = mpsc::channel::<String>(256);
    let close = Arc::new(Notify::new());
    {
        let close = close.clone();
        tauri::async_runtime::spawn(async move {
            loop {
                let line = tokio::select! {
                    line = stdin_rx.recv() => line,
                    _ = close.notified() => None,
                };
                let Some(line) = line else { break };
                if stdin.write_all(line.as_bytes()).await.is_err()
                    || stdin.write_all(b"\n").await.is_err()
                    || stdin.flush().await.is_err()
                {
                    break;
                }
            }
        });
    }

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

    let (kill_tx, mut kill_rx) = mpsc::channel::<()>(1);
    let link = Arc::new(Link {
        app: app.clone(),
        key: key.clone(),
        events: event_tx.clone(),
        stdin: stdin_tx,
        close,
        kill: kill_tx,
    });
    let tail = Arc::new(Mutex::new(String::new()));
    let stdout_task = {
        let tail = tail.clone();
        let link = link.clone();
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stdout).lines();
            while let Ok(Some(line)) = lines.next_line().await {
                let line = line.trim_end_matches('\r');
                if !line.is_empty() && !on_line(&link, line) {
                    append_tail(&tail, line);
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

    let name = name.to_string();
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
        if let Some(path) = cleanup {
            let _ = std::fs::remove_file(path);
        }
        let output = tail.lock().map(|t| t.clone()).unwrap_or_default();
        if code != Some(0) {
            log::warn!("{name} exited with {code:?}: {output}");
        }
        let _ = event_tx.send(AgentEvent::Exited { code, output }).await;
    });

    Ok(link)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_debugger_node_options() {
        assert!(is_debugger_hook(" --require /x/js-debug/src/bootloader.js  --inspect-publish-uid=http"));
        assert!(!is_debugger_hook("--max-old-space-size=4096"));
    }

    #[test]
    fn truncate_respects_char_boundaries() {
        assert_eq!(truncate("héllo".into(), 2), "hé…");
        assert_eq!(truncate("hi".into(), 5), "hi");
    }
}
