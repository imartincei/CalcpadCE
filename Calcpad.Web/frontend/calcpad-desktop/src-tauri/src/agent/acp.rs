//! ACP agents (Copilot, Gemini, opencode); the app applies the permission mode itself.

use std::collections::HashSet;
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};

use super::harness::{Launch, SYSTEM_PROMPT};
use super::mcp::{auto_approved_tools, SERVER_NAME};
use super::process::{self, truncate};
use super::rpc::{content_text, Incoming, Rpc};
use super::{AgentBackend, AgentEvent, ModelOption};

const PROTOCOL_VERSION: u64 = 1;
const MAX_TOOL_RESULT_CHARS: usize = 4000;
/// Tool kinds a restricted mode lets run without asking.
const READ_KINDS: &[&str] = &["read", "search", "think", "fetch"];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Agent {
    Copilot,
    Gemini,
    Opencode,
}

impl Agent {
    fn name(self) -> &'static str {
        match self {
            Agent::Copilot => "GitHub Copilot",
            Agent::Gemini => "Gemini CLI",
            Agent::Opencode => "opencode",
        }
    }

    fn args(self) -> &'static [&'static str] {
        match self {
            Agent::Copilot | Agent::Gemini => &["--acp"],
            Agent::Opencode => &["acp"],
        }
    }

    fn login_hint(self) -> &'static str {
        match self {
            Agent::Copilot => "Run `copilot login` in a terminal.",
            Agent::Gemini => "Run `gemini` once in a terminal to sign in.",
            Agent::Opencode => "Run `opencode auth login` in a terminal.",
        }
    }
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum ModelMethod {
    SetModel,
    ConfigOption,
}

#[derive(Default)]
struct Session {
    id: Option<String>,
    queued: Vec<String>,
    /// History replayed by session/load is already in the chat.
    replaying: bool,
    /// The first prompt of a new session carries the app's instructions.
    needs_preamble: bool,
    mode: String,
    agent_modes: Vec<String>,
    default_agent_mode: Option<String>,
    model_method: Option<ModelMethod>,
    tool_calls: HashSet<String>,
}

struct State {
    agent: Agent,
    rpc: Arc<Rpc>,
    session: Mutex<Session>,
}

pub struct AcpSession {
    state: Arc<State>,
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
    value.pointer(pointer).and_then(Value::as_str).map(str::to_string)
}

fn is_restricted(mode: &str) -> bool {
    matches!(mode, "acceptEdits" | "plan")
}

/// Whether a permission request is for one of the editor's own tools.
fn is_calcpad_tool(tool_call: &Value) -> bool {
    if tool_call.pointer("/_meta/gemini-cli/mcp/server_name").and_then(Value::as_str) == Some(SERVER_NAME) {
        return true;
    }
    let title = tool_call["title"].as_str().unwrap_or_default();
    auto_approved_tools().iter().any(|t| title.contains(t))
}

/// Whether a permission request asks to leave the agent's plan mode.
fn is_plan_exit(tool_call: &Value) -> bool {
    let title = tool_call["title"].as_str().unwrap_or_default().to_lowercase();
    tool_call["kind"] == "switch_mode" || title.contains("exit plan") || title.contains("plan mode")
}

/// The option id answering a permission request, or None to cancel it.
fn pick_option(options: &Value, allow: bool) -> Option<String> {
    let options = options.as_array()?;
    let kinds: &[&str] = if allow { &["allow_once", "allow_always"] } else { &["reject_once", "reject_always"] };
    kinds
        .iter()
        .find_map(|kind| options.iter().find(|o| o["kind"] == *kind))
        .and_then(|o| o["optionId"].as_str())
        .map(str::to_string)
}

fn parse_models(result: &Value) -> (Vec<ModelOption>, Option<String>, Option<ModelMethod>) {
    if let Some(models) = result.pointer("/models/availableModels").and_then(Value::as_array) {
        let list = models
            .iter()
            .filter_map(|m| {
                let value = m["modelId"].as_str()?.to_string();
                Some(ModelOption { label: m["name"].as_str().unwrap_or(&value).to_string(), value })
            })
            .collect();
        return (list, str_at(result, "/models/currentModelId"), Some(ModelMethod::SetModel));
    }
    let option = result["configOptions"]
        .as_array()
        .and_then(|options| options.iter().find(|o| o["id"] == "model" || o["category"] == "model"));
    if let Some(option) = option {
        let mut list = vec![];
        for entry in option["options"].as_array().into_iter().flatten() {
            // Options may be grouped.
            let entries = entry["options"].as_array().cloned().unwrap_or_else(|| vec![entry.clone()]);
            for e in entries {
                if let Some(value) = e["value"].as_str() {
                    list.push(ModelOption { label: e["name"].as_str().unwrap_or(value).to_string(), value: value.into() });
                }
            }
        }
        return (list, str_at(option, "/currentValue"), Some(ModelMethod::ConfigOption));
    }
    (vec![], None, None)
}

impl State {
    fn session(&self) -> std::sync::MutexGuard<'_, Session> {
        self.session.lock().expect("acp session mutex poisoned")
    }

    fn emit(&self, event: AgentEvent) {
        self.rpc.link().emit(event);
    }

    fn with_hint(&self, message: String) -> String {
        let lower = message.to_lowercase();
        if lower.contains("auth") || lower.contains("login") || lower.contains("credential") {
            format!("{message} {}", self.agent.login_hint())
        } else {
            message
        }
    }

    fn prompt(self: &Arc<Self>, session_id: String, text: String) -> Result<(), String> {
        let text = {
            let mut session = self.session();
            if std::mem::take(&mut session.needs_preamble) {
                format!("<calcpad-instructions>{SYSTEM_PROMPT}</calcpad-instructions>\n\n{text}")
            } else {
                text
            }
        };
        let state = self.clone();
        self.rpc.request(
            "session/prompt",
            json!({"sessionId": session_id, "prompt": [{"type": "text", "text": text}]}),
            move |reply| {
                state.emit(match reply {
                    Ok(result) => match result["stopReason"].as_str() {
                        Some("cancelled") => AgentEvent::TurnComplete {
                            is_error: false,
                            subtype: "interrupted".into(),
                            cost_usd: None,
                            error: None,
                        },
                        Some("end_turn") | None => AgentEvent::TurnComplete {
                            is_error: false,
                            subtype: "success".into(),
                            cost_usd: None,
                            error: None,
                        },
                        Some(reason) => failed_turn(format!("The turn ended early ({reason}).")),
                    },
                    Err(message) => failed_turn(state.with_hint(message)),
                });
            },
        )
    }

    fn set_agent_mode(&self, mode_id: &str) {
        let Some(session_id) = self.session().id.clone() else { return };
        let _ = self
            .rpc
            .request("session/set_mode", json!({"sessionId": session_id, "modeId": mode_id}), |_| {});
    }

    /// Plan mode also switches the agent into its own plan mode when it has one.
    fn sync_agent_mode(&self) {
        let target = {
            let session = self.session();
            let has_plan = session.agent_modes.iter().any(|m| m == "plan");
            if session.mode == "plan" && has_plan {
                Some("plan".to_string())
            } else if has_plan {
                session.default_agent_mode.clone().filter(|m| m != "plan")
            } else {
                None
            }
        };
        if let Some(mode) = target {
            self.set_agent_mode(&mode);
        }
    }

    fn set_model(&self, model: &str) -> Result<(), String> {
        let (session_id, method) = {
            let session = self.session();
            (session.id.clone(), session.model_method)
        };
        let (Some(session_id), Some(method)) = (session_id, method) else {
            return Ok(());
        };
        let (name, params) = match method {
            ModelMethod::SetModel => ("session/set_model", json!({"sessionId": session_id, "modelId": model})),
            ModelMethod::ConfigOption => (
                "session/set_config_option",
                json!({"sessionId": session_id, "configId": "model", "value": model}),
            ),
        };
        let link = self.rpc.link().clone();
        self.rpc.request(name, params, move |reply| {
            if let Err(message) = reply {
                link.emit(AgentEvent::Error { message });
            }
        })
    }

    fn on_session(self: &Arc<Self>, session_id: String, result: &Value, version: Option<String>, model: Option<String>) {
        let (models, current, method) = parse_models(result);
        let agent_modes: Vec<String> = result
            .pointer("/modes/availableModes")
            .and_then(Value::as_array)
            .map(|modes| modes.iter().filter_map(|m| m["id"].as_str().map(str::to_string)).collect())
            .unwrap_or_default();
        let queued = {
            let mut session = self.session();
            session.id = Some(session_id.clone());
            session.replaying = false;
            session.model_method = method;
            session.default_agent_mode = str_at(result, "/modes/currentModeId");
            session.agent_modes = agent_modes;
            std::mem::take(&mut session.queued)
        };
        self.emit(AgentEvent::SessionStarted {
            session_id: session_id.clone(),
            model: current.clone(),
            version,
            permission_mode: None,
        });
        if !models.is_empty() {
            self.emit(AgentEvent::Models { models });
        }
        if let Some(model) = model.filter(|m| Some(m) != current.as_ref()) {
            let _ = self.set_model(&model);
        }
        self.sync_agent_mode();
        for text in queued {
            if let Err(message) = self.prompt(session_id.clone(), text) {
                self.emit(failed_turn(message));
            }
        }
    }

    fn on_request(self: &Arc<Self>, id: Value, method: &str, params: Value) {
        if method != "session/request_permission" {
            self.rpc.respond_error(id, &format!("CalcpadCE does not support {method}"));
            return;
        }
        let tool_call = &params["toolCall"];
        let options = params["options"].clone();
        let respond = move |state: &State, id: Value, allow: bool| {
            let outcome = match pick_option(&options, allow) {
                Some(option) => json!({"outcome": "selected", "optionId": option}),
                None => json!({"outcome": "cancelled"}),
            };
            state.rpc.respond(id, json!({"outcome": outcome}));
        };
        if is_calcpad_tool(tool_call) {
            respond(self, id, true);
            return;
        }
        let (name, input) = if is_plan_exit(tool_call) {
            let plan = match content_text(&tool_call["content"]) {
                text if text.is_empty() => tool_call["title"].as_str().unwrap_or_default().to_string(),
                text => text,
            };
            // The app shows this as a plan to approve; approving leaves plan mode.
            ("ExitPlanMode".to_string(), json!({"plan": plan}))
        } else {
            let mode = self.session().mode.clone();
            if is_restricted(&mode) {
                let kind = tool_call["kind"].as_str().unwrap_or("other");
                respond(self, id, READ_KINDS.contains(&kind));
                return;
            }
            let name = tool_call["title"].as_str().or(tool_call["kind"].as_str()).unwrap_or("tool").to_string();
            let input = tool_call
                .get("rawInput")
                .cloned()
                .filter(|v| !v.is_null())
                .unwrap_or_else(|| json!({"kind": tool_call["kind"], "locations": tool_call["locations"]}));
            (name, input)
        };
        let state = self.clone();
        tauri::async_runtime::spawn(async move {
            let allowed = state.rpc.link().approve(&name, input).await;
            respond(&state, id, allowed);
        });
    }

    fn on_notification(&self, method: &str, params: &Value) {
        if method != "session/update" || params.pointer("/_meta/opencode~1child-session").is_some() {
            return;
        }
        let update = &params["update"];
        if update.pointer("/_meta/opencode~1child-session").is_some() || self.session().replaying {
            return;
        }
        for event in self.map_update(update) {
            self.emit(event);
        }
    }

    fn map_update(&self, update: &Value) -> Vec<AgentEvent> {
        let id = str_at(update, "/toolCallId").unwrap_or_default();
        let finished = matches!(update["status"].as_str(), Some("completed" | "failed"));
        let result = || AgentEvent::ToolResult {
            tool_use_id: id.clone(),
            is_error: update["status"] == "failed",
            content: truncate(
                match content_text(&update["content"]) {
                    text if text.is_empty() => update.get("rawOutput").map(|v| match v {
                        Value::String(s) => s.clone(),
                        Value::Null => String::new(),
                        other => other.to_string(),
                    }).unwrap_or_default(),
                    text => text,
                },
                MAX_TOOL_RESULT_CHARS,
            ),
        };
        match update["sessionUpdate"].as_str() {
            Some("agent_message_chunk") => match str_at(update, "/content/text") {
                Some(text) if !text.starts_with("[MODE_UPDATE]") => vec![AgentEvent::TextDelta { text }],
                _ => vec![],
            },
            Some("tool_call") => {
                self.session().tool_calls.insert(id.clone());
                let input = tool_input(update).unwrap_or_else(|| json!({"locations": update["locations"]}));
                let mut events = vec![AgentEvent::ToolUse { id: id.clone(), name: tool_name(update), input }];
                if finished {
                    events.push(result());
                }
                events
            }
            Some("tool_call_update") if self.session().tool_calls.contains(&id) => {
                let mut events = vec![];
                // Some agents only fill in the input once the call starts.
                if let Some(input) = tool_input(update) {
                    events.push(AgentEvent::ToolUse { id: id.clone(), name: tool_name(update), input });
                }
                if finished {
                    self.session().tool_calls.remove(&id);
                    events.push(result());
                }
                events
            }
            _ => vec![],
        }
    }
}

fn tool_input(update: &Value) -> Option<Value> {
    update
        .get("rawInput")
        .filter(|v| !v.is_null() && v.as_object().map_or(true, |o| !o.is_empty()))
        .cloned()
}

/// The title, with opencode's `calcpad_<tool>` written like other harnesses' MCP names.
fn tool_name(update: &Value) -> String {
    let title = update["title"].as_str().or(update["kind"].as_str()).unwrap_or("tool");
    match title.strip_prefix(&format!("{SERVER_NAME}_")) {
        Some(tool) if auto_approved_tools().contains(&tool) => format!("mcp__{SERVER_NAME}__{tool}"),
        _ => title.to_string(),
    }
}

fn mcp_servers(launch: &Launch) -> Value {
    let headers: Vec<Value> = launch
        .mcp
        .headers(&launch.key, false)
        .into_iter()
        .map(|(name, value)| json!({"name": name, "value": value}))
        .collect();
    json!([{"type": "http", "name": SERVER_NAME, "url": launch.mcp.url(), "headers": headers}])
}

pub fn start(launch: Launch, agent: Agent) -> Result<AcpSession, String> {
    let rpc = Rpc::new(true);
    let mode = launch.options.permission_mode.clone().unwrap_or_else(|| "default".into());
    let state = Arc::new(State {
        agent,
        rpc: rpc.clone(),
        session: Mutex::new(Session { mode, ..Default::default() }),
    });
    let mut command = process::command(&launch.exe, &launch.cwd);
    command.args(agent.args());
    if agent == Agent::Opencode {
        // opencode allows everything by default; make it ask.
        command.env("OPENCODE_PERMISSION", r#"{"edit":"ask","bash":"ask","webfetch":"ask"}"#);
    }
    let link = {
        let state = state.clone();
        process::spawn(&launch.app, &launch.key, launch.generation, command, agent.name(), None, move |_, line| {
            let Ok(message) = serde_json::from_str::<Value>(line) else {
                return false;
            };
            match state.rpc.dispatch(message) {
                Incoming::Request { id, method, params } => state.on_request(id, &method, params),
                Incoming::Notification { method, params } => state.on_notification(&method, &params),
                Incoming::Reply | Incoming::Invalid => {}
            }
            true
        })?
    };
    rpc.attach(link);

    let servers = mcp_servers(&launch);
    let cwd = launch.cwd.clone();
    let model = launch.options.model.clone().map(|m| m.trim().to_string()).filter(|m| !m.is_empty());
    let resume = launch.options.resume.clone().filter(|s| !s.is_empty());
    let init_state = state.clone();
    rpc.request(
        "initialize",
        json!({
            "protocolVersion": PROTOCOL_VERSION,
            "clientCapabilities": {"fs": {"readTextFile": false, "writeTextFile": false}, "terminal": false},
            "clientInfo": {"name": "calcpad", "title": "CalcpadCE", "version": env!("CARGO_PKG_VERSION")},
        }),
        move |reply| {
            let state = init_state;
            let init = match reply {
                Ok(init) => init,
                Err(message) => {
                    state.emit(failed_turn(state.with_hint(message)));
                    return;
                }
            };
            let version = str_at(&init, "/agentInfo/version");
            let can_load = init.pointer("/agentCapabilities/loadSession") == Some(&json!(true));
            let fresh = {
                let state = state.clone();
                let version = version.clone();
                let model = model.clone();
                move |reply: Result<Value, String>| match reply {
                    Ok(result) => match str_at(&result, "/sessionId") {
                        Some(id) => {
                            state.session().needs_preamble = true;
                            state.on_session(id, &result, version, model);
                        }
                        None => state.emit(failed_turn("The agent did not start a session".into())),
                    },
                    Err(message) => state.emit(failed_turn(state.with_hint(message))),
                }
            };
            let new_params = json!({"cwd": cwd, "mcpServers": servers});
            let sent = match resume.filter(|_| can_load) {
                Some(session_id) => {
                    state.session().replaying = true;
                    let loaded = state.clone();
                    let retry = new_params.clone();
                    state.rpc.request(
                        "session/load",
                        json!({"sessionId": session_id, "cwd": cwd, "mcpServers": servers}),
                        move |reply| match reply {
                            Ok(result) => loaded.on_session(session_id, &result, version, model),
                            Err(message) => {
                                loaded.session().replaying = false;
                                loaded.emit(AgentEvent::Status {
                                    text: format!("Could not resume the session ({message}); starting a new one."),
                                });
                                let _ = loaded.rpc.request("session/new", retry, fresh);
                            }
                        },
                    )
                }
                None => state.rpc.request("session/new", new_params, fresh),
            };
            if let Err(message) = sent {
                state.emit(failed_turn(message));
            }
        },
    )?;
    Ok(AcpSession { state })
}

impl AgentBackend for AcpSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        let session_id = {
            let mut session = self.state.session();
            match session.id.clone() {
                Some(id) => id,
                None => {
                    session.queued.push(text.to_string());
                    return Ok(());
                }
            }
        };
        self.state.prompt(session_id, text.to_string())
    }

    fn interrupt(&self) -> Result<(), String> {
        match self.state.session().id.clone() {
            Some(id) => self.state.rpc.notify("session/cancel", json!({"sessionId": id})),
            None => Ok(()),
        }
    }

    fn set_model(&self, model: &str) -> Result<(), String> {
        if model.is_empty() {
            return Ok(());
        }
        self.state.set_model(model)
    }

    fn set_permission_mode(&self, mode: &str) -> Result<(), String> {
        self.state.session().mode = mode.to_string();
        self.state.sync_agent_mode();
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
    fn replays_live_transcript() {
        let Some(lines) = crate::agent::tests::live_transcript("opencode.jsonl") else { return };
        let state = State { agent: Agent::Opencode, rpc: Rpc::new(true), session: Mutex::new(Session::default()) };
        let session = lines.iter().find_map(|l| l.get("result").filter(|r| r.get("sessionId").is_some())).unwrap();
        let (models, current, method) = parse_models(session);
        assert!(!models.is_empty() && current.is_some() && method == Some(ModelMethod::ConfigOption));
        let mut events = vec![];
        for line in &lines {
            match line["method"].as_str() {
                Some("session/update") => events.extend(state.map_update(&line["params"]["update"])),
                Some("session/request_permission") => {
                    let call = &line["params"]["toolCall"];
                    assert!(!is_calcpad_tool(call));
                    assert_eq!(pick_option(&line["params"]["options"], true).as_deref(), Some("once"));
                    assert_eq!(pick_option(&line["params"]["options"], false).as_deref(), Some("reject"));
                }
                _ if line["result"]["stopReason"] == "end_turn" => events.push(AgentEvent::TurnComplete {
                    is_error: false,
                    subtype: "success".into(),
                    cost_usd: None,
                    error: None,
                }),
                _ => {}
            }
        }
        let get_document = events.iter().rev().find_map(|e| match e {
            AgentEvent::ToolUse { name, input, .. } if name == "mcp__calcpad__get_document" => Some(input),
            _ => None,
        });
        assert_eq!(get_document.unwrap()["path"], "/tmp/beam.cpd", "the later update should carry the real input");
        crate::agent::tests::check_live_run(&events);
    }

    #[test]
    fn picks_permission_options_by_kind() {
        let options = json!([
            {"optionId":"always","kind":"allow_always","name":"Always allow"},
            {"optionId":"once","kind":"allow_once","name":"Allow once"},
            {"optionId":"reject","kind":"reject_once","name":"Reject"}
        ]);
        assert_eq!(pick_option(&options, true).as_deref(), Some("once"));
        assert_eq!(pick_option(&options, false).as_deref(), Some("reject"));
        assert_eq!(pick_option(&json!([{"optionId":"a","kind":"allow_once"}]), false), None);
    }

    #[test]
    fn recognizes_calcpad_tools() {
        assert!(is_calcpad_tool(&json!({"title":"x","_meta":{"gemini-cli":{"mcp":{"server_name":"calcpad"}}}})));
        assert!(is_calcpad_tool(&json!({"title":"calcpad/get_document"})));
        assert!(!is_calcpad_tool(&json!({"title":"Shell: rm -rf build"})));
    }

    #[test]
    fn recognizes_plan_exits() {
        assert!(is_plan_exit(&json!({"kind":"switch_mode","title":"Ready to code?"})));
        assert!(is_plan_exit(&json!({"kind":"other","title":"Exit Plan Mode"})));
        assert!(!is_plan_exit(&json!({"kind":"edit","title":"Write plan.md"})));
    }

    #[test]
    fn parses_models_from_either_shape() {
        let gemini = json!({"models":{"availableModels":[{"modelId":"pro","name":"Pro"}],"currentModelId":"pro"}});
        let (list, current, method) = parse_models(&gemini);
        assert_eq!(list, vec![ModelOption { value: "pro".into(), label: "Pro".into() }]);
        assert_eq!(current.as_deref(), Some("pro"));
        assert!(method == Some(ModelMethod::SetModel));
        let opencode = json!({"configOptions":[{"id":"model","category":"model","currentValue":"a/b","options":[{"value":"a/b","name":"B"}]}]});
        let (list, current, method) = parse_models(&opencode);
        assert_eq!(list.len(), 1);
        assert_eq!(current.as_deref(), Some("a/b"));
        assert!(method == Some(ModelMethod::ConfigOption));
    }
}
