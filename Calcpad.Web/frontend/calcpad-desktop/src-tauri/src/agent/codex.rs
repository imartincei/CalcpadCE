//! Codex: drives `codex app-server` (JSON-RPC over stdio).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};

use super::harness::{Launch, RESTRICTED_PROMPT, SYSTEM_PROMPT};
use super::mcp::SERVER_NAME;
use super::process::{self, truncate};
use super::rpc::{content_text, Incoming, Rpc};
use super::{AgentBackend, AgentEvent, ModelOption};

const MAX_TOOL_RESULT_CHARS: usize = 4000;
const TOKEN_VAR: &str = "CALCPAD_MCP_TOKEN";

#[derive(Default)]
struct Thread {
    id: Option<String>,
    turn: Option<String>,
    queued: Vec<String>,
    next_model: Option<String>,
    /// fileChange items by id, to show their files when approval is asked.
    changes: HashMap<String, Value>,
}

struct State {
    rpc: Arc<Rpc>,
    thread: Mutex<Thread>,
    restricted: bool,
}

pub struct CodexSession {
    state: Arc<State>,
}

impl State {
    fn thread(&self) -> std::sync::MutexGuard<'_, Thread> {
        self.thread.lock().expect("codex thread mutex poisoned")
    }

    fn emit(&self, event: AgentEvent) {
        self.rpc.link().emit(event);
    }

    fn start_turn(self: &Arc<Self>, thread_id: String, text: String) -> Result<(), String> {
        let mut params = json!({
            "threadId": thread_id,
            "input": [{"type": "text", "text": text, "text_elements": []}],
        });
        if let Some(model) = self.thread().next_model.take() {
            params["model"] = json!(model);
        }
        let state = self.clone();
        self.rpc
            .request("turn/start", params, move |reply| match reply {
                Ok(result) => {
                    if let Some(id) = result.pointer("/turn/id").and_then(Value::as_str) {
                        state.thread().turn = Some(id.into());
                    }
                }
                Err(message) => state.emit(failed_turn(message)),
            })
    }

    fn on_thread(self: &Arc<Self>, result: Value) {
        let Some(id) = result
            .pointer("/thread/id")
            .and_then(Value::as_str)
            .map(str::to_string)
        else {
            self.emit(failed_turn("Codex did not start a thread".into()));
            return;
        };
        self.emit(AgentEvent::SessionStarted {
            session_id: id.clone(),
            model: result
                .get("model")
                .and_then(Value::as_str)
                .map(str::to_string),
            version: None,
            permission_mode: None,
        });
        let queued = {
            let mut thread = self.thread();
            thread.id = Some(id.clone());
            std::mem::take(&mut thread.queued)
        };
        for text in queued {
            if let Err(message) = self.start_turn(id.clone(), text) {
                self.emit(failed_turn(message));
            }
        }
    }

    fn on_request(self: &Arc<Self>, id: Value, method: &str, params: Value) {
        let (tool, input) = match method {
            "item/commandExecution/requestApproval" => (
                "shell".to_string(),
                json!({"command": params["command"], "cwd": params["cwd"], "reason": params["reason"]}),
            ),
            "item/fileChange/requestApproval" => {
                let item = params["itemId"].as_str().unwrap_or_default();
                let changes = self
                    .thread()
                    .changes
                    .get(item)
                    .cloned()
                    .unwrap_or(Value::Null);
                (
                    "apply_patch".to_string(),
                    json!({"changes": changes, "reason": params["reason"]}),
                )
            }
            "mcpServer/elicitation/request" => {
                let server = params["serverName"]
                    .as_str()
                    .or(params["server_name"].as_str())
                    .unwrap_or_default();
                if server == SERVER_NAME {
                    self.rpc
                        .respond(id, json!({"action": "accept", "content": {}}));
                    return;
                }
                (format!("mcp__{server}"), params.clone())
            }
            _ => {
                self.rpc
                    .respond_error(id, &format!("CalcpadCE does not support {method}"));
                return;
            }
        };
        let elicitation = method == "mcpServer/elicitation/request";
        if self.restricted && !elicitation {
            self.rpc.respond(id, json!({"decision": "decline"}));
            return;
        }
        let state = self.clone();
        tauri::async_runtime::spawn(async move {
            let allowed = state.rpc.link().approve(&tool, input).await;
            let result = match (elicitation, allowed) {
                (true, true) => json!({"action": "accept", "content": {}}),
                (true, false) => json!({"action": "decline"}),
                (false, true) => json!({"decision": "accept"}),
                (false, false) => json!({"decision": "decline"}),
            };
            state.rpc.respond(id, result);
        });
    }

    fn on_notification(&self, method: &str, params: &Value) {
        if method == "item/started" && params["item"]["type"] == "fileChange" {
            if let Some(id) = params["item"]["id"].as_str() {
                self.thread()
                    .changes
                    .insert(id.into(), params["item"]["changes"].clone());
            }
        }
        match method {
            "turn/started" => {
                self.thread().turn = params
                    .pointer("/turn/id")
                    .and_then(Value::as_str)
                    .map(str::to_string);
            }
            "turn/completed" => {
                self.thread().turn = None;
            }
            _ => {}
        }
        for event in map_notification(method, params) {
            self.emit(event);
        }
    }
}

fn failed_turn(message: String) -> AgentEvent {
    AgentEvent::TurnComplete {
        is_error: true,
        subtype: "error".into(),
        cost_usd: None,
        error: Some(message),
    }
}

fn str_at(value: &Value, pointer: &str) -> Option<String> {
    value
        .pointer(pointer)
        .and_then(Value::as_str)
        .map(str::to_string)
}

/// Maps one app-server notification to UI events.
pub fn map_notification(method: &str, params: &Value) -> Vec<AgentEvent> {
    let item = &params["item"];
    let id = str_at(item, "/id").unwrap_or_default();
    match method {
        "item/agentMessage/delta" => str_at(params, "/delta")
            .map(|text| vec![AgentEvent::TextDelta { text }])
            .unwrap_or_default(),
        "item/started" => {
            let (name, input) = match item["type"].as_str() {
                Some("commandExecution") => (
                    "shell".to_string(),
                    json!({"command": item["command"], "cwd": item["cwd"]}),
                ),
                Some("fileChange") => (
                    "apply_patch".to_string(),
                    json!({"changes": item["changes"]}),
                ),
                Some("mcpToolCall") => (
                    format!(
                        "mcp__{}__{}",
                        item["server"].as_str().unwrap_or("?"),
                        item["tool"].as_str().unwrap_or("?")
                    ),
                    item["arguments"].clone(),
                ),
                Some("webSearch") => ("web_search".to_string(), json!({"query": item["query"]})),
                _ => return vec![],
            };
            vec![AgentEvent::ToolUse { id, name, input }]
        }
        "item/completed" => {
            let failed = matches!(item["status"].as_str(), Some("failed" | "declined"));
            let content = match item["type"].as_str() {
                Some("agentMessage") => {
                    return str_at(item, "/text")
                        .map(|text| vec![AgentEvent::AssistantText { text }])
                        .unwrap_or_default()
                }
                Some("commandExecution") => str_at(item, "/aggregatedOutput").unwrap_or_default(),
                Some("mcpToolCall") => str_at(item, "/error/message")
                    .unwrap_or_else(|| content_text(&item["result"]["content"])),
                Some("fileChange") => item["status"].as_str().unwrap_or_default().to_string(),
                Some("webSearch") => String::new(),
                _ => return vec![],
            };
            let is_error = failed || item["exitCode"].as_i64().is_some_and(|c| c != 0);
            vec![AgentEvent::ToolResult {
                tool_use_id: id,
                is_error,
                content: truncate(content, MAX_TOOL_RESULT_CHARS),
            }]
        }
        "turn/completed" => {
            let turn = &params["turn"];
            match turn["status"].as_str() {
                Some("interrupted") => vec![AgentEvent::TurnComplete {
                    is_error: false,
                    subtype: "interrupted".into(),
                    cost_usd: None,
                    error: None,
                }],
                Some("failed") => vec![failed_turn(
                    str_at(turn, "/error/message").unwrap_or_else(|| "The turn failed".into()),
                )],
                _ => vec![AgentEvent::TurnComplete {
                    is_error: false,
                    subtype: "success".into(),
                    cost_usd: None,
                    error: None,
                }],
            }
        }
        "error" => {
            if params["willRetry"] == true {
                return vec![AgentEvent::Status {
                    text: "Retrying the model request…".into(),
                }];
            }
            let mut message = str_at(params, "/error/message")
                .or_else(|| str_at(params, "/message"))
                .unwrap_or_else(|| "Codex reported an error".into());
            if params.to_string().contains("unauthorized") {
                message.push_str(" Run `codex login` in a terminal.");
            }
            vec![AgentEvent::Error { message }]
        }
        _ => vec![],
    }
}

fn parse_models(result: &Value) -> Vec<ModelOption> {
    let list = result
        .get("data")
        .or_else(|| result.get("models"))
        .unwrap_or(result);
    list.as_array()
        .map(|models| {
            models
                .iter()
                .filter(|m| m["hidden"] != true)
                .filter_map(|m| {
                    let value = m["model"].as_str().or(m["id"].as_str())?.to_string();
                    let label = m["displayName"].as_str().unwrap_or(&value).to_string();
                    Some(ModelOption { value, label })
                })
                .collect()
        })
        .unwrap_or_default()
}

/// A TOML basic string; JSON string escapes are valid TOML.
fn toml_str(s: &str) -> String {
    serde_json::to_string(s).unwrap_or_default()
}

pub fn build_args(launch: &Launch) -> Vec<String> {
    let headers: Vec<String> = launch
        .mcp
        .headers(&launch.key, false)
        .into_iter()
        .filter(|(k, _)| *k != "Authorization")
        .map(|(k, v)| format!("{} = {}", toml_str(k), toml_str(&v)))
        .collect();
    let mut config = vec![
        format!(
            "mcp_servers.{SERVER_NAME}.url={}",
            toml_str(launch.mcp.url())
        ),
        format!(
            "mcp_servers.{SERVER_NAME}.bearer_token_env_var={}",
            toml_str(TOKEN_VAR)
        ),
        format!(
            "mcp_servers.{SERVER_NAME}.http_headers={{ {} }}",
            headers.join(", ")
        ),
        format!("mcp_servers.{SERVER_NAME}.default_tools_approval_mode=\"auto\""),
    ];
    if launch.is_restricted() {
        config.push("features.shell_tool=false".into());
        config.push("features.apply_patch_freeform=false".into());
    }
    let mut args = vec!["app-server".to_string()];
    for entry in config {
        args.push("-c".into());
        args.push(entry);
    }
    args
}

fn thread_params(launch: &Launch) -> Value {
    let restricted = launch.is_restricted();
    let mut instructions = SYSTEM_PROMPT.to_string();
    if restricted {
        instructions.push_str(RESTRICTED_PROMPT);
    }
    let mut params = json!({
        "cwd": launch.cwd,
        "approvalPolicy": if restricted { "never" } else { "untrusted" },
        "sandbox": if restricted { "read-only" } else { "workspace-write" },
        "developerInstructions": instructions,
    });
    if let Some(model) = launch
        .options
        .model
        .as_deref()
        .map(str::trim)
        .filter(|m| !m.is_empty())
    {
        params["model"] = json!(model);
    }
    params
}

pub fn start(launch: Launch) -> Result<CodexSession, String> {
    let rpc = Rpc::new(false);
    let state = Arc::new(State {
        rpc: rpc.clone(),
        thread: Mutex::new(Thread::default()),
        restricted: launch.is_restricted(),
    });
    let mut command = process::command(&launch.exe, &launch.cwd);
    command
        .args(build_args(&launch))
        .env(TOKEN_VAR, launch.mcp.token());
    let link = {
        let state = state.clone();
        process::spawn(
            &launch.app,
            &launch.key,
            launch.generation,
            command,
            "Codex",
            None,
            move |_, line| {
                let Ok(message) = serde_json::from_str::<Value>(line) else {
                    return false;
                };
                match state.rpc.dispatch(message) {
                    Incoming::Request { id, method, params } => {
                        state.on_request(id, &method, params)
                    }
                    Incoming::Notification { method, params } => {
                        state.on_notification(&method, &params)
                    }
                    Incoming::Reply | Incoming::Invalid => {}
                }
                true
            },
        )?
    };
    rpc.attach(link);

    let params = thread_params(&launch);
    let resume = launch.options.resume.clone().filter(|s| !s.is_empty());
    let init_state = state.clone();
    rpc.request(
        "initialize",
        json!({"clientInfo": {"name": "calcpad", "title": "CalcpadCE", "version": env!("CARGO_PKG_VERSION")}, "capabilities": {}}),
        move |reply| {
            let state = init_state;
            if let Err(message) = reply {
                state.emit(failed_turn(message));
                return;
            }
            let _ = state.rpc.notify("initialized", json!({}));
            let models_state = state.clone();
            let _ = state.rpc.request("model/list", json!({}), move |reply| {
                if let Ok(result) = reply {
                    models_state.emit(AgentEvent::Models { models: parse_models(&result) });
                }
            });
            let started = state.clone();
            let fresh = params.clone();
            let on_start = move |reply: Result<Value, String>| match reply {
                Ok(result) => started.on_thread(result),
                Err(message) => started.emit(failed_turn(message)),
            };
            let sent = match resume {
                Some(thread_id) => {
                    let mut resume_params = params;
                    resume_params["threadId"] = json!(thread_id);
                    let fallback = state.clone();
                    state.rpc.request("thread/resume", resume_params, move |reply| match reply {
                        Ok(result) => fallback.on_thread(result),
                        Err(message) => {
                            fallback.emit(AgentEvent::Status {
                                text: format!("Could not resume the Codex thread ({message}); starting a new one."),
                            });
                            let _ = fallback.rpc.request("thread/start", fresh, on_start);
                        }
                    })
                }
                None => state.rpc.request("thread/start", params, on_start),
            };
            if let Err(message) = sent {
                state.emit(failed_turn(message));
            }
        },
    )?;
    Ok(CodexSession { state })
}

impl AgentBackend for CodexSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        let thread_id = {
            let mut thread = self.state.thread();
            match thread.id.clone() {
                Some(id) => id,
                None => {
                    thread.queued.push(text.to_string());
                    return Ok(());
                }
            }
        };
        self.state.start_turn(thread_id, text.to_string())
    }

    fn interrupt(&self) -> Result<(), String> {
        let (thread_id, turn_id) = {
            let thread = self.state.thread();
            (thread.id.clone(), thread.turn.clone())
        };
        match (thread_id, turn_id) {
            (Some(thread_id), Some(turn_id)) => self.state.rpc.request(
                "turn/interrupt",
                json!({"threadId": thread_id, "turnId": turn_id}),
                |_| {},
            ),
            _ => Ok(()),
        }
    }

    /// Applies from the next turn.
    fn set_model(&self, model: &str) -> Result<(), String> {
        self.state.thread().next_model = Some(model.to_string()).filter(|m| !m.is_empty());
        Ok(())
    }

    /// Modes that change the sandbox restart the process instead.
    fn set_permission_mode(&self, _mode: &str) -> Result<(), String> {
        Ok(())
    }

    fn stop(&self) {
        self.state.rpc.link().stop();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn maps_items_and_turns() {
        let delta = json!({"threadId":"t","turnId":"u","itemId":"i","delta":"Hi"});
        assert_eq!(
            map_notification("item/agentMessage/delta", &delta),
            vec![AgentEvent::TextDelta { text: "Hi".into() }]
        );
        let started = json!({"item":{"type":"mcpToolCall","id":"i1","server":"calcpad","tool":"get_document","arguments":{"path":"/a.cpd"},"status":"inProgress"}});
        assert!(matches!(
            map_notification("item/started", &started).as_slice(),
            [AgentEvent::ToolUse { id, name, .. }] if id == "i1" && name == "mcp__calcpad__get_document"
        ));
        let done = json!({"item":{"type":"commandExecution","id":"i2","command":"ls","status":"completed","aggregatedOutput":"a.cpd","exitCode":1}});
        assert_eq!(
            map_notification("item/completed", &done),
            vec![AgentEvent::ToolResult {
                tool_use_id: "i2".into(),
                is_error: true,
                content: "a.cpd".into()
            }]
        );
        let message = json!({"item":{"type":"agentMessage","id":"i3","text":"Done"}});
        assert_eq!(
            map_notification("item/completed", &message),
            vec![AgentEvent::AssistantText {
                text: "Done".into()
            }]
        );
        let interrupted = json!({"turn":{"id":"u","status":"interrupted"}});
        assert!(matches!(
            map_notification("turn/completed", &interrupted).as_slice(),
            [AgentEvent::TurnComplete { subtype, is_error: false, .. }] if subtype == "interrupted"
        ));
        let failed = json!({"turn":{"id":"u","status":"failed","error":{"message":"quota"}}});
        assert!(matches!(
            map_notification("turn/completed", &failed).as_slice(),
            [AgentEvent::TurnComplete { error: Some(e), .. }] if e == "quota"
        ));
    }

    #[test]
    fn parses_models_and_hides_hidden() {
        let result = json!({"data":[{"model":"gpt-x","displayName":"GPT X"},{"model":"secret","hidden":true}]});
        assert_eq!(
            parse_models(&result),
            vec![ModelOption {
                value: "gpt-x".into(),
                label: "GPT X".into()
            }]
        );
    }

    #[test]
    fn toml_strings_are_quoted_and_escaped() {
        assert_eq!(toml_str("a\"b"), "\"a\\\"b\"");
    }
}
