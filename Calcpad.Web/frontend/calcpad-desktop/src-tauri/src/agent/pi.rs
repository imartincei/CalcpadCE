//! pi: drives `pi --mode rpc` with an extension for MCP and approvals.

use std::sync::Arc;

use serde_json::{json, Value};

use super::harness::{Launch, RESTRICTED_PROMPT, SYSTEM_PROMPT};
use super::mcp::auto_approved_tools;
use super::process::{self, truncate, Link};
use super::{AgentBackend, AgentEvent, ModelOption};

const EXTENSION: &str = include_str!("pi_extension.ts");
const APPROVAL_TITLE: &str = "calcpad:approve";
const MAX_TOOL_RESULT_CHARS: usize = 4000;
// Built-in edits write to disk underneath unsaved editor buffers; edits go through propose_edit.
const ALWAYS_EXCLUDED: &[&str] = &["edit"];
const RESTRICTED_EXCLUDED: &[&str] = &["bash", "powershell", "write"];

pub struct PiSession {
    link: Arc<Link>,
}

impl AgentBackend for PiSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        self.link
            .write(&json!({"id": "prompt", "type": "prompt", "message": text}))
    }

    fn interrupt(&self) -> Result<(), String> {
        self.link.write(&json!({"type": "abort"}))
    }

    fn set_model(&self, model: &str) -> Result<(), String> {
        let Some((provider, id)) = model.split_once('/') else {
            return Ok(());
        };
        self.link.write(
            &json!({"id": "set_model", "type": "set_model", "provider": provider, "modelId": id}),
        )
    }

    /// Modes that change the tool set restart the process instead.
    fn set_permission_mode(&self, _mode: &str) -> Result<(), String> {
        Ok(())
    }

    fn stop(&self) {
        self.link.stop();
    }
}

pub fn build_args(launch: &Launch, extension: &std::path::Path) -> Vec<String> {
    let restricted = launch.is_restricted();
    let mut excluded = ALWAYS_EXCLUDED.to_vec();
    let mut prompt = SYSTEM_PROMPT.to_string();
    if restricted {
        excluded.extend_from_slice(RESTRICTED_EXCLUDED);
        prompt.push_str(RESTRICTED_PROMPT);
    }
    let session = launch
        .options
        .resume
        .clone()
        .filter(|s| !s.is_empty())
        .unwrap_or_else(|| launch.key.conversation.clone());
    let mut args: Vec<String> = vec![
        "--mode".into(),
        "rpc".into(),
        "--session-dir".into(),
        launch
            .data_dir
            .join("sessions")
            .to_string_lossy()
            .into_owned(),
        "--session-id".into(),
        session,
        "-e".into(),
        extension.to_string_lossy().into_owned(),
        "--exclude-tools".into(),
        excluded.join(","),
        "--append-system-prompt".into(),
        prompt,
    ];
    if let Some(model) = launch
        .options
        .model
        .as_deref()
        .map(str::trim)
        .filter(|m| !m.is_empty())
    {
        args.push("--model".into());
        args.push(model.into());
    }
    args
}

fn content_text(content: &Value) -> String {
    match content {
        Value::String(s) => s.clone(),
        Value::Array(blocks) => blocks
            .iter()
            .filter_map(|b| b.get("text").and_then(Value::as_str))
            .collect::<Vec<_>>()
            .join("\n"),
        _ => String::new(),
    }
}

fn model_value(model: &Value) -> Option<String> {
    Some(format!(
        "{}/{}",
        model["provider"].as_str()?,
        model["id"].as_str()?
    ))
}

/// Per-run state for turning pi's events into UI events.
#[derive(Default)]
pub struct Mapper {
    cost: f64,
    error: Option<String>,
}

impl Mapper {
    pub fn map(&mut self, line: &Value) -> Vec<AgentEvent> {
        let str_at = |v: &Value, key: &str| v.get(key).and_then(Value::as_str).map(str::to_string);
        match line["type"].as_str() {
            Some("response") => self.map_response(line),
            Some("message_update") => {
                let update = &line["assistantMessageEvent"];
                match update["type"].as_str() {
                    Some("text_delta") => str_at(update, "delta")
                        .map(|text| vec![AgentEvent::TextDelta { text }])
                        .unwrap_or_default(),
                    _ => vec![],
                }
            }
            Some("message_end") if line["message"]["role"] == "assistant" => {
                let message = &line["message"];
                self.cost += message["usage"]["cost"]["total"].as_f64().unwrap_or(0.0);
                if message["stopReason"] == "error" {
                    self.error = str_at(message, "errorMessage")
                        .or_else(|| Some("The model request failed".into()));
                }
                let text: Vec<&str> = message["content"]
                    .as_array()
                    .map(|blocks| {
                        blocks
                            .iter()
                            .filter(|b| b["type"] == "text")
                            .filter_map(|b| b["text"].as_str())
                            .collect()
                    })
                    .unwrap_or_default();
                if text.is_empty() {
                    vec![]
                } else {
                    vec![AgentEvent::AssistantText {
                        text: text.join("\n\n"),
                    }]
                }
            }
            Some("tool_execution_start") => vec![AgentEvent::ToolUse {
                id: str_at(line, "toolCallId").unwrap_or_default(),
                name: str_at(line, "toolName").unwrap_or_default(),
                input: line.get("args").cloned().unwrap_or(Value::Null),
            }],
            Some("tool_execution_end") => vec![AgentEvent::ToolResult {
                tool_use_id: str_at(line, "toolCallId").unwrap_or_default(),
                is_error: line["isError"].as_bool().unwrap_or(false),
                content: truncate(
                    content_text(&line["result"]["content"]),
                    MAX_TOOL_RESULT_CHARS,
                ),
            }],
            Some("auto_retry_start") => vec![AgentEvent::Status {
                text: "Retrying the model request…".into(),
            }],
            Some("agent_settled") => {
                let aborted = line["aborted"].as_bool().unwrap_or(false);
                let error = self.error.take().filter(|_| !aborted);
                let cost = std::mem::take(&mut self.cost);
                vec![AgentEvent::TurnComplete {
                    is_error: error.is_some(),
                    subtype: if aborted {
                        "interrupted"
                    } else if error.is_some() {
                        "error"
                    } else {
                        "success"
                    }
                    .into(),
                    cost_usd: (cost > 0.0).then_some(cost),
                    error,
                }]
            }
            _ => vec![],
        }
    }

    fn map_response(&mut self, line: &Value) -> Vec<AgentEvent> {
        let command = line["command"].as_str().unwrap_or_default();
        if line["success"] == false {
            let message = line["error"]
                .as_str()
                .unwrap_or("pi rejected the request")
                .to_string();
            return vec![if command == "prompt" {
                AgentEvent::TurnComplete {
                    is_error: true,
                    subtype: "error".into(),
                    cost_usd: None,
                    error: Some(message),
                }
            } else {
                AgentEvent::Error { message }
            }];
        }
        let data = &line["data"];
        match command {
            "get_state" => vec![AgentEvent::SessionStarted {
                session_id: data["sessionId"].as_str().unwrap_or_default().to_string(),
                model: model_value(&data["model"]),
                version: None,
                permission_mode: None,
            }],
            "get_available_models" => vec![AgentEvent::Models {
                models: data["models"]
                    .as_array()
                    .map(|models| {
                        models
                            .iter()
                            .filter_map(|m| {
                                let value = model_value(m)?;
                                let name = m["name"].as_str().unwrap_or(&value);
                                let provider = m["provider"].as_str().unwrap_or_default();
                                Some(ModelOption {
                                    label: format!("{name} ({provider})"),
                                    value,
                                })
                            })
                            .collect()
                    })
                    .unwrap_or_default(),
            }],
            "prompt" if data["disposition"] == "handled" => vec![AgentEvent::TurnComplete {
                is_error: false,
                subtype: "success".into(),
                cost_usd: None,
                error: None,
            }],
            _ => vec![],
        }
    }
}

/// The question card for an extension dialog, or None for calls that need no answer.
pub fn dialog_question(request: &Value) -> Option<Value> {
    let title = request["title"].as_str().unwrap_or_default();
    let message = request["message"].as_str().filter(|m| !m.is_empty());
    let question = match message {
        Some(m) => format!("{title}\n{m}"),
        None => title.to_string(),
    };
    Some(match request["method"].as_str()? {
        "select" => {
            json!({"question": question, "options": request["options"], "allowOther": false})
        }
        "confirm" => json!({"question": question, "options": ["Yes", "No"], "allowOther": false}),
        "input" | "editor" => json!({
            "question": question,
            "options": [],
            "placeholder": request["placeholder"].as_str().or(request["prefill"].as_str()).unwrap_or_default(),
        }),
        _ => return None,
    })
}

/// Answers extension dialogs: calcpad approvals become approval cards, other dialogs question cards.
fn on_ui_request(link: &Arc<Link>, request: &Value) {
    let id = request["id"].clone();
    let method = request["method"].as_str().unwrap_or_default().to_string();
    if method == "confirm" && request["title"] == APPROVAL_TITLE {
        let ask: Value = request["message"]
            .as_str()
            .and_then(|m| serde_json::from_str(m).ok())
            .unwrap_or(Value::Null);
        let link = link.clone();
        tauri::async_runtime::spawn(async move {
            let tool = ask["tool_name"].as_str().unwrap_or_default().to_string();
            let allowed = link.approve(&tool, ask["input"].clone()).await;
            let _ = link
                .write(&json!({"type": "extension_ui_response", "id": id, "confirmed": allowed}));
        });
        return;
    }
    if method == "notify" {
        if let Some(text) = request["message"].as_str() {
            link.emit(AgentEvent::Status { text: text.into() });
        }
        return;
    }
    let Some(question) = dialog_question(request) else {
        return;
    };
    let link = link.clone();
    tauri::async_runtime::spawn(async move {
        let key = question["question"]
            .as_str()
            .unwrap_or_default()
            .to_string();
        let answer = link.ask(json!([question])).await.and_then(|answers| {
            answers
                .get(&key)
                .and_then(Value::as_str)
                .map(str::to_string)
        });
        let response = match (answer, method.as_str()) {
            (None, _) => json!({"type": "extension_ui_response", "id": id, "cancelled": true}),
            (Some(a), "confirm") => {
                json!({"type": "extension_ui_response", "id": id, "confirmed": a == "Yes"})
            }
            (Some(a), _) => json!({"type": "extension_ui_response", "id": id, "value": a}),
        };
        let _ = link.write(&response);
    });
}

pub fn start(launch: Launch) -> Result<PiSession, String> {
    let extension = launch.data_dir.join("calcpad-extension.ts");
    process::write_private(&extension, EXTENSION).map_err(|e| e.to_string())?;
    let headers: serde_json::Map<String, Value> = launch
        .mcp
        .headers(&launch.key, false)
        .into_iter()
        .filter(|(k, _)| *k != "Authorization")
        .map(|(k, v)| (k.to_string(), Value::String(v)))
        .collect();

    let mut command = process::command(&launch.exe, &launch.cwd);
    command
        .args(build_args(&launch, &extension))
        .env("CALCPAD_MCP_URL", launch.mcp.url())
        .env("CALCPAD_MCP_TOKEN", launch.mcp.token())
        .env("CALCPAD_MCP_HEADERS", Value::Object(headers).to_string())
        .env("CALCPAD_AUTO_TOOLS", auto_approved_tools().join(","));
    let mut mapper = Mapper::default();
    let link = process::spawn(
        &launch.app,
        &launch.key,
        launch.generation,
        command,
        "pi",
        None,
        move |link, line| {
            let Ok(value) = serde_json::from_str::<Value>(line) else {
                return false;
            };
            if value["type"] == "extension_ui_request" {
                on_ui_request(link, &value);
            } else {
                for event in mapper.map(&value) {
                    link.emit(event);
                }
            }
            true
        },
    )?;
    link.write(&json!({"id": "state", "type": "get_state"}))?;
    link.write(&json!({"id": "models", "type": "get_available_models"}))?;
    Ok(PiSession { link })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replays_live_transcript() {
        let Some(lines) = crate::agent::tests::live_transcript("pi.jsonl") else {
            return;
        };
        let mut mapper = Mapper::default();
        let events: Vec<AgentEvent> = lines
            .iter()
            .filter(|l| l["type"] != "extension_ui_request")
            .flat_map(|l| mapper.map(l))
            .collect();
        assert!(events.iter().any(
            |e| matches!(e, AgentEvent::SessionStarted { session_id, .. } if !session_id.is_empty())
        ));
        assert!(events
            .iter()
            .any(|e| matches!(e, AgentEvent::Models { models } if !models.is_empty())));
        let approvals: Vec<&Value> = lines
            .iter()
            .filter(|l| l["title"] == APPROVAL_TITLE)
            .collect();
        assert!(!approvals.is_empty(), "no approval was asked");
        crate::agent::tests::check_live_run(&events);
    }

    #[test]
    fn maps_a_run() {
        let mut m = Mapper::default();
        let delta = json!({"type":"message_update","assistantMessageEvent":{"type":"text_delta","contentIndex":0,"delta":"Hi"}});
        assert_eq!(
            m.map(&delta),
            vec![AgentEvent::TextDelta { text: "Hi".into() }]
        );
        let start = json!({"type":"tool_execution_start","toolCallId":"c1","toolName":"bash","args":{"command":"ls"}});
        assert!(
            matches!(m.map(&start).as_slice(), [AgentEvent::ToolUse { id, name, .. }] if id == "c1" && name == "bash")
        );
        let end = json!({"type":"tool_execution_end","toolCallId":"c1","toolName":"bash","result":{"content":[{"type":"text","text":"a.cpd"}]},"isError":false});
        assert_eq!(
            m.map(&end),
            vec![AgentEvent::ToolResult {
                tool_use_id: "c1".into(),
                is_error: false,
                content: "a.cpd".into()
            }]
        );
        let done = json!({"type":"message_end","message":{"role":"assistant","content":[{"type":"text","text":"Done"}],"usage":{"cost":{"total":0.25}},"stopReason":"stop"}});
        assert_eq!(
            m.map(&done),
            vec![AgentEvent::AssistantText {
                text: "Done".into()
            }]
        );
        assert_eq!(
            m.map(&json!({"type":"agent_settled","aborted":false})),
            vec![AgentEvent::TurnComplete {
                is_error: false,
                subtype: "success".into(),
                cost_usd: Some(0.25),
                error: None
            }]
        );
    }

    #[test]
    fn reports_errors_aborts_and_rejected_prompts() {
        let mut m = Mapper::default();
        m.map(&json!({"type":"message_end","message":{"role":"assistant","content":[],"stopReason":"error","errorMessage":"No API key"}}));
        assert!(matches!(
            m.map(&json!({"type":"agent_settled","aborted":false})).as_slice(),
            [AgentEvent::TurnComplete { is_error: true, error: Some(e), .. }] if e == "No API key"
        ));
        assert!(matches!(
            m.map(&json!({"type":"agent_settled","aborted":true})).as_slice(),
            [AgentEvent::TurnComplete { is_error: false, subtype, .. }] if subtype == "interrupted"
        ));
        let rejected = json!({"type":"response","command":"prompt","success":false,"error":"busy"});
        assert!(matches!(
            m.map(&rejected).as_slice(),
            [AgentEvent::TurnComplete { is_error: true, .. }]
        ));
    }

    #[test]
    fn turns_dialogs_into_questions() {
        let select = json!({"method":"select","title":"Pick one","options":["A","B"]});
        assert_eq!(
            dialog_question(&select).unwrap()["options"],
            json!(["A", "B"])
        );
        let confirm = json!({"method":"confirm","title":"Clear session?","message":"All messages will be lost."});
        assert_eq!(
            dialog_question(&confirm).unwrap()["question"],
            "Clear session?\nAll messages will be lost."
        );
        assert_eq!(
            dialog_question(&json!({"method":"input","title":"Name"})).unwrap()["options"],
            json!([])
        );
        assert!(dialog_question(&json!({"method":"setStatus"})).is_none());
    }

    #[test]
    fn maps_state_and_models() {
        let mut m = Mapper::default();
        let state = json!({"type":"response","command":"get_state","success":true,"data":{"sessionId":"s1","model":{"provider":"anthropic","id":"claude-x"}}});
        assert!(matches!(
            m.map(&state).as_slice(),
            [AgentEvent::SessionStarted { session_id, model: Some(model), .. }] if session_id == "s1" && model == "anthropic/claude-x"
        ));
        let models = json!({"type":"response","command":"get_available_models","success":true,"data":{"models":[{"provider":"openai","id":"gpt-x","name":"GPT X"}]}});
        assert_eq!(
            m.map(&models),
            vec![AgentEvent::Models {
                models: vec![ModelOption {
                    value: "openai/gpt-x".into(),
                    label: "GPT X (openai)".into()
                }]
            }]
        );
    }
}
