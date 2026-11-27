//! Coding-agent chat: an agent CLI process per conversation tab, plus an MCP server
//! that exposes the live editor to it.

mod acp;
mod claude_code;
mod codex;
mod detect;
mod harness;
pub mod history;
mod mcp;
mod pi;
mod process;
mod reference;
mod rpc;

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use serde::{Deserialize, Serialize};
use serde_json::Value;
use tauri::{AppHandle, Emitter, Manager, State};
use tokio::sync::{oneshot, OnceCell};

pub const EVENT_NAME: &str = "agent-event";
const TOOL_REQUEST_EVENT: &str = "agent-tool-request";
const WINDOW_CLOSED_EVENT: &str = "agent-window-closed";

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum AgentEvent {
    SessionStarted {
        session_id: String,
        model: Option<String>,
        version: Option<String>,
        permission_mode: Option<String>,
    },
    TextDelta {
        text: String,
    },
    AssistantText {
        text: String,
    },
    ToolUse {
        id: String,
        name: String,
        input: Value,
    },
    ToolResult {
        tool_use_id: String,
        is_error: bool,
        content: String,
    },
    TurnComplete {
        is_error: bool,
        subtype: String,
        cost_usd: Option<f64>,
        error: Option<String>,
    },
    Status {
        text: String,
    },
    Exited {
        code: Option<i32>,
        output: String,
    },
    Error {
        message: String,
    },
    /// Models the running agent offers, for harnesses without a fixed list.
    Models {
        models: Vec<ModelOption>,
    },
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
pub struct ModelOption {
    pub value: String,
    pub label: String,
}

/// One running agent conversation.
pub trait AgentBackend: Send + Sync {
    fn send_user(&self, text: &str) -> Result<(), String>;
    fn interrupt(&self) -> Result<(), String>;
    /// Empty selects the backend's default model.
    fn set_model(&self, model: &str) -> Result<(), String>;
    fn set_permission_mode(&self, mode: &str) -> Result<(), String>;
    fn stop(&self);
}

#[derive(Debug, Default, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StartOptions {
    pub harness: String,
    /// Overrides auto-detection.
    pub executable: Option<String>,
    pub cwd: Option<String>,
    pub model: Option<String>,
    pub permission_mode: Option<String>,
    pub resume: Option<String>,
}

/// A conversation tab in a window.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct SessionKey {
    pub window: String,
    pub conversation: String,
}

impl SessionKey {
    fn new(window: &tauri::WebviewWindow, conversation: String) -> Result<Self, String> {
        crate::validate_draft_id(&conversation)
            .map_err(|_| "invalid conversation id".to_string())?;
        Ok(Self {
            window: window.label().to_string(),
            conversation,
        })
    }
}

struct Session {
    generation: u64,
    harness: harness::Harness,
    backend: Box<dyn AgentBackend>,
}

struct Pending {
    key: SessionKey,
    tx: oneshot::Sender<Value>,
}

#[derive(Default)]
pub struct AgentState {
    sessions: Mutex<HashMap<SessionKey, Session>>,
    pending: Mutex<HashMap<u64, Pending>>,
    next_id: AtomicU64,
    mcp: OnceCell<mcp::McpEndpoint>,
}

#[derive(Clone, Serialize)]
struct EventPayload {
    conversation: String,
    event: AgentEvent,
}

#[derive(Clone, Serialize)]
struct ToolRequest<'a> {
    id: u64,
    conversation: &'a str,
    tool: &'a str,
    args: Value,
}

impl AgentState {
    fn is_current(&self, key: &SessionKey, generation: u64) -> bool {
        self.sessions
            .lock()
            .expect("agent sessions mutex poisoned")
            .get(key)
            .is_some_and(|s| s.generation == generation)
    }

    fn stop_where(&self, matches: impl Fn(&SessionKey) -> bool) {
        let stopped: Vec<Session> = {
            let mut sessions = self.sessions.lock().expect("agent sessions mutex poisoned");
            let keys: Vec<SessionKey> = sessions.keys().filter(|k| matches(k)).cloned().collect();
            keys.iter().filter_map(|k| sessions.remove(k)).collect()
        };
        for session in stopped {
            session.backend.stop();
        }
        self.pending
            .lock()
            .expect("agent pending mutex poisoned")
            .retain(|_, p| !matches(&p.key));
    }
}

/// Emits only while `generation` is still the live session for `key`.
pub(crate) fn emit_event(app: &AppHandle, key: &SessionKey, generation: u64, event: AgentEvent) {
    if app.state::<AgentState>().is_current(key, generation) {
        let payload = EventPayload {
            conversation: key.conversation.clone(),
            event,
        };
        let _ = app.emit_to(key.window.as_str(), EVENT_NAME, payload);
    }
}

/// Called by a backend when its process exits on its own.
pub(crate) fn on_exited(app: &AppHandle, key: &SessionKey, generation: u64, event: AgentEvent) {
    emit_event(app, key, generation, event);
    let state = app.state::<AgentState>();
    let mut sessions = state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned");
    if sessions
        .get(key)
        .is_some_and(|s| s.generation == generation)
    {
        sessions.remove(key);
    }
}

/// Asks the webview that owns `key` to run an editor tool and waits for its answer.
pub(crate) async fn request_window(
    app: &AppHandle,
    key: SessionKey,
    tool: &str,
    args: Value,
    timeout: Option<Duration>,
) -> Result<Value, String> {
    let state = app.state::<AgentState>();
    let id = state.next_id.fetch_add(1, Ordering::Relaxed);
    let (tx, rx) = oneshot::channel();
    state
        .pending
        .lock()
        .expect("agent pending mutex poisoned")
        .insert(
            id,
            Pending {
                key: key.clone(),
                tx,
            },
        );
    let request = ToolRequest {
        id,
        conversation: &key.conversation,
        tool,
        args,
    };
    if let Err(err) = app.emit_to(key.window.as_str(), TOOL_REQUEST_EVENT, request) {
        state
            .pending
            .lock()
            .expect("agent pending mutex poisoned")
            .remove(&id);
        return Err(format!("Calcpad window is unavailable: {err}"));
    }
    let answer = match timeout {
        Some(limit) => match tokio::time::timeout(limit, rx).await {
            Ok(answer) => answer,
            Err(_) => {
                state
                    .pending
                    .lock()
                    .expect("agent pending mutex poisoned")
                    .remove(&id);
                return Err("Calcpad did not respond in time".into());
            }
        },
        None => rx.await,
    };
    let answer = answer.map_err(|_| "The request was cancelled".to_string())?;
    match answer.get("error").and_then(Value::as_str) {
        Some(err) => Err(err.to_string()),
        None => Ok(answer.get("result").cloned().unwrap_or(Value::Null)),
    }
}

struct WindowBridge(AppHandle);

impl mcp::ToolBridge for WindowBridge {
    fn call<'a>(
        &'a self,
        key: SessionKey,
        tool: &'a str,
        args: Value,
        timeout: Option<Duration>,
    ) -> mcp::BridgeFuture<'a> {
        Box::pin(request_window(&self.0, key, tool, args, timeout))
    }
}

fn resolve_cwd(requested: Option<&str>) -> PathBuf {
    requested
        .map(PathBuf::from)
        .filter(|p| p.is_dir())
        .or_else(|| {
            std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from)
        })
        .unwrap_or_else(std::env::temp_dir)
}

pub(crate) fn session_label(key: &SessionKey) -> String {
    let window: String = key
        .window
        .chars()
        .map(|c| if c.is_ascii_alphanumeric() { c } else { '_' })
        .collect();
    format!("{window}-{}", key.conversation)
}

/// The docs bundled for the calcpad_reference tool.
fn skill_reference(app: &AppHandle) -> Option<reference::Reference> {
    let dir = app.path().resource_dir().ok()?.join("agent-docs");
    Some(reference::Reference::new(
        dir,
        app.package_info().version.to_string(),
    ))
}

/// Every supported harness, whether it was found, and what the chat panel offers for it.
/// `paths` holds user-set executable paths by harness id.
#[tauri::command]
pub async fn agent_harnesses(paths: HashMap<String, String>) -> Vec<Value> {
    tauri::async_runtime::spawn_blocking(move || {
        harness::Harness::ALL
            .iter()
            .map(|h| h.info(paths.get(h.id()).map(String::as_str)))
            .collect()
    })
    .await
    .unwrap_or_default()
}

#[tauri::command]
pub async fn agent_start(
    app: AppHandle,
    window: tauri::WebviewWindow,
    conversation: String,
    options: StartOptions,
) -> Result<(), String> {
    let key = SessionKey::new(&window, conversation)?;
    let harness = harness::Harness::from_id(&options.harness)
        .ok_or_else(|| format!("Unknown agent harness {}", options.harness))?;
    if let Some(mode) = options.permission_mode.as_deref() {
        harness.mode(mode)?;
    }
    let exe = harness
        .find(options.executable.as_deref())
        .filter(|p| p.is_file())
        .ok_or_else(|| harness.not_found())?;
    let state = app.state::<AgentState>();
    // Another window may hand the conversation over.
    state.stop_where(|k| k.conversation == key.conversation);

    let endpoint = state
        .mcp
        .get_or_try_init(|| {
            mcp::start(
                std::sync::Arc::new(WindowBridge(app.clone())),
                skill_reference(&app),
            )
        })
        .await?
        .clone();
    let generation = state.next_id.fetch_add(1, Ordering::Relaxed);
    let data_dir = app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("agent")
        .join(harness.id());
    std::fs::create_dir_all(&data_dir).map_err(|e| e.to_string())?;
    let launch = harness::Launch {
        app: app.clone(),
        key: key.clone(),
        generation,
        exe,
        cwd: resolve_cwd(options.cwd.as_deref()),
        options,
        mcp: endpoint,
        data_dir,
    };
    let backend = harness.start(launch)?;
    state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned")
        .insert(
            key,
            Session {
                generation,
                harness,
                backend,
            },
        );
    Ok(())
}

fn with_session<T>(
    state: &AgentState,
    key: &SessionKey,
    f: impl FnOnce(&dyn AgentBackend) -> Result<T, String>,
) -> Result<T, String> {
    let sessions = state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned");
    let session = sessions.get(key).ok_or("No agent session is running")?;
    f(session.backend.as_ref())
}

#[tauri::command]
pub fn agent_send(
    state: State<'_, AgentState>,
    window: tauri::WebviewWindow,
    conversation: String,
    text: String,
) -> Result<(), String> {
    with_session(&state, &SessionKey::new(&window, conversation)?, |b| {
        b.send_user(&text)
    })
}

/// Works from any window, wherever the conversation runs.
#[tauri::command]
pub fn agent_interrupt(state: State<'_, AgentState>, conversation: String) -> Result<(), String> {
    let sessions = state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned");
    let session = sessions
        .iter()
        .find(|(k, _)| k.conversation == conversation)
        .map(|(_, s)| s)
        .ok_or("No agent session is running")?;
    session.backend.interrupt()
}

/// Applies to the running session, if any; the next start picks the model from its options.
#[tauri::command]
pub fn agent_set_model(
    state: State<'_, AgentState>,
    window: tauri::WebviewWindow,
    conversation: String,
    model: String,
) -> Result<(), String> {
    let key = SessionKey::new(&window, conversation)?;
    let sessions = state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned");
    match sessions.get(&key) {
        Some(session) => session.backend.set_model(model.trim()),
        None => Ok(()),
    }
}

#[tauri::command]
pub fn agent_set_permission_mode(
    state: State<'_, AgentState>,
    window: tauri::WebviewWindow,
    conversation: String,
    mode: String,
) -> Result<(), String> {
    let key = SessionKey::new(&window, conversation)?;
    let sessions = state
        .sessions
        .lock()
        .expect("agent sessions mutex poisoned");
    match sessions.get(&key) {
        Some(session) => {
            session.harness.mode(&mode)?;
            session.backend.set_permission_mode(&mode)
        }
        None => Ok(()),
    }
}

/// Works from any window, wherever the conversation runs.
#[tauri::command]
pub fn agent_stop(state: State<'_, AgentState>, conversation: String) -> Result<(), String> {
    state.stop_where(|k| k.conversation == conversation);
    Ok(())
}

/// Lets the chat tell a new worksheet from one it cannot read.
#[tauri::command]
pub fn agent_path_exists(path: String) -> bool {
    std::path::Path::new(&path).exists()
}

#[tauri::command]
pub fn agent_tool_response(state: State<'_, AgentState>, id: u64, answer: Value) {
    let pending = state
        .pending
        .lock()
        .expect("agent pending mutex poisoned")
        .remove(&id);
    if let Some(pending) = pending {
        let _ = pending.tx.send(answer);
    }
}

pub fn on_window_destroyed(app: &AppHandle, window: &str) {
    app.state::<AgentState>().stop_where(|k| k.window == window);
    let _ = app.emit(WINDOW_CLOSED_EVENT, window);
}

pub fn stop_all(app: &AppHandle) {
    app.state::<AgentState>().stop_where(|_| true);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Lines of a transcript recorded from a real CLI, when `CALCPAD_LIVE_DIR` is set.
    pub(crate) fn live_transcript(file: &str) -> Option<Vec<Value>> {
        let dir = std::env::var_os("CALCPAD_LIVE_DIR")?;
        let text = std::fs::read_to_string(std::path::Path::new(&dir).join(file)).ok()?;
        Some(
            text.lines()
                .filter_map(|l| serde_json::from_str(l).ok())
                .collect(),
        )
    }

    /// Checks a mapped live run: editor tools used, every tool finished, turn succeeded.
    pub(crate) fn check_live_run(events: &[AgentEvent]) {
        let tools: Vec<&str> = events
            .iter()
            .filter_map(|e| match e {
                AgentEvent::ToolUse { name, .. } => Some(name.as_str()),
                _ => None,
            })
            .collect();
        for expected in ["mcp__calcpad__get_document", "mcp__calcpad__propose_edit"] {
            assert!(
                tools.contains(&expected),
                "{expected} missing from {tools:?}"
            );
        }
        for event in events {
            if let AgentEvent::ToolUse { id, .. } = event {
                assert!(
                    events.iter().any(|e| matches!(e, AgentEvent::ToolResult { tool_use_id, .. } if tool_use_id == id)),
                    "tool {id} has no result"
                );
            }
        }
        assert!(events
            .iter()
            .any(|e| matches!(e, AgentEvent::TextDelta { .. })));
        assert!(
            matches!(
                events.last(),
                Some(AgentEvent::TurnComplete { is_error: false, subtype, .. }) if subtype == "success"
            ),
            "last event: {:?}",
            events.last()
        );
    }

    #[test]
    fn events_round_trip_with_camel_case_fields() {
        let event = AgentEvent::ToolResult {
            tool_use_id: "t1".into(),
            is_error: false,
            content: "ok".into(),
        };
        let json = serde_json::json!({"kind":"toolResult","toolUseId":"t1","isError":false,"content":"ok"});
        assert_eq!(
            serde_json::to_value(EventPayload {
                conversation: "c1".into(),
                event: event.clone()
            })
            .unwrap(),
            serde_json::json!({"conversation":"c1","event":json})
        );
        assert_eq!(serde_json::from_value::<AgentEvent>(json).unwrap(), event);
    }
}
