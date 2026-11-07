//! Coding-agent chat: an agent CLI child process per conversation tab, plus an MCP server
//! that exposes the live editor to it.

mod claude_code;
pub mod history;
mod mcp;

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

#[derive(Clone, Debug, Serialize, PartialEq)]
#[serde(tag = "kind", rename_all = "camelCase", rename_all_fields = "camelCase")]
pub enum AgentEvent {
    SessionStarted {
        session_id: String,
        model: Option<String>,
        version: Option<String>,
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
}

/// One running agent conversation. Backends other than Claude Code (e.g. ACP) implement this.
pub trait AgentBackend: Send + Sync {
    fn send_user(&self, text: &str) -> Result<(), String>;
    fn interrupt(&self) -> Result<(), String>;
    /// Empty selects the backend's default model.
    fn set_model(&self, model: &str) -> Result<(), String>;
    fn stop(&self);
}

#[derive(Debug, Default, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StartOptions {
    pub executable: Option<String>,
    pub cwd: Option<String>,
    pub model: Option<String>,
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
        crate::validate_draft_id(&conversation).map_err(|_| "invalid conversation id".to_string())?;
        Ok(Self {
            window: window.label().to_string(),
            conversation,
        })
    }
}

struct Session {
    generation: u64,
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
    let mut sessions = state.sessions.lock().expect("agent sessions mutex poisoned");
    if sessions.get(key).is_some_and(|s| s.generation == generation) {
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
    state.pending.lock().expect("agent pending mutex poisoned").insert(
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
        state.pending.lock().expect("agent pending mutex poisoned").remove(&id);
        return Err(format!("Calcpad window is unavailable: {err}"));
    }
    let answer = match timeout {
        Some(limit) => match tokio::time::timeout(limit, rx).await {
            Ok(answer) => answer,
            Err(_) => {
                state.pending.lock().expect("agent pending mutex poisoned").remove(&id);
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

fn plugin_dir(app: &AppHandle) -> Option<PathBuf> {
    let dir = app.path().resource_dir().ok()?.join("agent-plugin");
    dir.join(".claude-plugin").join("plugin.json").is_file().then_some(dir)
}

#[tauri::command]
pub async fn agent_start(
    app: AppHandle,
    window: tauri::WebviewWindow,
    conversation: String,
    options: StartOptions,
) -> Result<(), String> {
    let key = SessionKey::new(&window, conversation)?;
    let state = app.state::<AgentState>();
    state.stop_where(|k| *k == key);

    let endpoint = state
        .mcp
        .get_or_try_init(|| mcp::start(std::sync::Arc::new(WindowBridge(app.clone()))))
        .await?
        .clone();
    let generation = state.next_id.fetch_add(1, Ordering::Relaxed);
    let launch = claude_code::Launch {
        options,
        mcp_config: endpoint.config_json(&key),
        plugin_dir: plugin_dir(&app),
        config_dir: app.path().app_data_dir().map_err(|e| e.to_string())?.join("agent"),
    };
    let backend = claude_code::spawn(&app, &key, generation, launch)?;
    state.sessions.lock().expect("agent sessions mutex poisoned").insert(
        key,
        Session {
            generation,
            backend: Box::new(backend),
        },
    );
    Ok(())
}

fn with_session<T>(
    state: &AgentState,
    key: &SessionKey,
    f: impl FnOnce(&dyn AgentBackend) -> Result<T, String>,
) -> Result<T, String> {
    let sessions = state.sessions.lock().expect("agent sessions mutex poisoned");
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
    with_session(&state, &SessionKey::new(&window, conversation)?, |b| b.send_user(&text))
}

#[tauri::command]
pub fn agent_interrupt(
    state: State<'_, AgentState>,
    window: tauri::WebviewWindow,
    conversation: String,
) -> Result<(), String> {
    with_session(&state, &SessionKey::new(&window, conversation)?, |b| b.interrupt())
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
    let sessions = state.sessions.lock().expect("agent sessions mutex poisoned");
    match sessions.get(&key) {
        Some(session) => session.backend.set_model(model.trim()),
        None => Ok(()),
    }
}

#[tauri::command]
pub fn agent_stop(
    state: State<'_, AgentState>,
    window: tauri::WebviewWindow,
    conversation: String,
) -> Result<(), String> {
    let key = SessionKey::new(&window, conversation)?;
    state.stop_where(|k| *k == key);
    Ok(())
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
}

pub fn stop_all(app: &AppHandle) {
    app.state::<AgentState>().stop_where(|_| true);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn events_serialize_with_camel_case_fields() {
        let event = AgentEvent::ToolResult {
            tool_use_id: "t1".into(),
            is_error: false,
            content: "ok".into(),
        };
        assert_eq!(
            serde_json::to_value(EventPayload {
                conversation: "c1".into(),
                event
            })
            .unwrap(),
            serde_json::json!({"conversation":"c1","event":{"kind":"toolResult","toolUseId":"t1","isError":false,"content":"ok"}})
        );
    }
}
