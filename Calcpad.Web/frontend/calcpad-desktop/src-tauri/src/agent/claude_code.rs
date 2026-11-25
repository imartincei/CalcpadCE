//! Claude Code: drives `claude -p` over its stream-json protocol.

use std::path::Path;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use serde_json::{json, Value};

use super::harness::{Launch, RESTRICTED_PROMPT, SKILL_PROMPT, SYSTEM_PROMPT};
use super::mcp::{auto_approved_tools, APPROVAL_TOOL, SERVER_NAME};
use super::process::{self, truncate, Link};
use super::{AgentBackend, AgentEvent, StartOptions};

const MAX_TOOL_RESULT_CHARS: usize = 4000;
// Built-in editing tools write to disk underneath unsaved editor buffers; edits go through propose_edit.
const DISALLOWED_TOOLS: &[&str] = &["Edit", "MultiEdit", "NotebookEdit"];
// Automatic modes may only change open worksheets, through propose_edit.
const RESTRICTED_TOOLS: &[&str] = &["Write", "Bash"];

fn is_restricted(options: &StartOptions) -> bool {
    matches!(
        options.permission_mode.as_deref(),
        Some("acceptEdits" | "auto")
    )
}

pub struct ClaudeCodeSession {
    link: Arc<Link>,
    next_request: AtomicU64,
}

impl AgentBackend for ClaudeCodeSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        self.link.write(&json!({
            "type": "user",
            "message": {"role": "user", "content": [{"type": "text", "text": text}]},
            "parent_tool_use_id": null
        }))
    }

    fn interrupt(&self) -> Result<(), String> {
        self.control(json!({"subtype": "interrupt"}))
    }

    fn set_model(&self, model: &str) -> Result<(), String> {
        let model = if model.is_empty() { "default" } else { model };
        self.control(json!({"subtype": "set_model", "model": model}))
    }

    fn set_permission_mode(&self, mode: &str) -> Result<(), String> {
        self.control(json!({"subtype": "set_permission_mode", "mode": mode}))
    }

    fn stop(&self) {
        self.link.stop();
    }
}

impl ClaudeCodeSession {
    fn control(&self, request: Value) -> Result<(), String> {
        let id = self.next_request.fetch_add(1, Ordering::Relaxed);
        self.link.write(&json!({
            "type": "control_request",
            "request_id": format!("calcpad-{id}"),
            "request": request
        }))
    }
}

pub fn build_args(
    options: &StartOptions,
    mcp_config: &Path,
    plugin_dir: Option<&Path>,
) -> Vec<String> {
    let mut args: Vec<String> = [
        "-p",
        "--input-format",
        "stream-json",
        "--output-format",
        "stream-json",
        "--verbose",
        "--include-partial-messages",
    ]
    .iter()
    .map(|s| s.to_string())
    .collect();
    args.push("--mcp-config".into());
    args.push(mcp_config.to_string_lossy().into_owned());
    args.push("--permission-prompt-tool".into());
    args.push(format!("mcp__{SERVER_NAME}__{APPROVAL_TOOL}"));
    args.push("--allowedTools".into());
    args.push(
        auto_approved_tools()
            .iter()
            .map(|t| format!("mcp__{SERVER_NAME}__{t}"))
            .collect::<Vec<_>>()
            .join(","),
    );
    let restricted = is_restricted(options);
    let mut disallowed = DISALLOWED_TOOLS.to_vec();
    if restricted {
        disallowed.extend_from_slice(RESTRICTED_TOOLS);
        // The user's own MCP servers could change things outside Calcpad.
        args.push("--strict-mcp-config".into());
    }
    args.push("--disallowedTools".into());
    args.push(disallowed.join(","));
    let mut prompt = SYSTEM_PROMPT.to_string();
    if plugin_dir.is_some() {
        prompt.push_str(SKILL_PROMPT);
    }
    if restricted {
        prompt.push_str(RESTRICTED_PROMPT);
    }
    args.push("--append-system-prompt".into());
    args.push(prompt);
    if let Some(dir) = plugin_dir {
        args.push("--plugin-dir".into());
        args.push(dir.to_string_lossy().into_owned());
    }
    if let Some(model) = options.model.as_deref().filter(|m| !m.trim().is_empty()) {
        args.push("--model".into());
        args.push(model.trim().into());
    }
    if let Some(mode) = options
        .permission_mode
        .as_deref()
        .filter(|m| *m != "default")
    {
        args.push("--permission-mode".into());
        args.push(mode.into());
    }
    if let Some(session) = options.resume.as_deref().filter(|s| !s.is_empty()) {
        args.push("--resume".into());
        args.push(session.into());
    }
    args
}

fn tool_result_text(content: &Value) -> String {
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

/// Maps one stream-json line to UI events. Subagent traffic (non-null parent_tool_use_id) is hidden.
pub fn map_line(line: &Value) -> Vec<AgentEvent> {
    let str_at = |v: &Value, key: &str| v.get(key).and_then(Value::as_str).map(str::to_string);
    if line.get("parent_tool_use_id").is_some_and(|p| !p.is_null()) {
        return vec![];
    }
    match line.get("type").and_then(Value::as_str) {
        Some("system") => match line.get("subtype").and_then(Value::as_str) {
            Some("init") => vec![AgentEvent::SessionStarted {
                session_id: str_at(line, "session_id").unwrap_or_default(),
                model: str_at(line, "model"),
                version: str_at(line, "claude_code_version"),
                permission_mode: str_at(line, "permissionMode"),
            }],
            Some("api_retry") => vec![AgentEvent::Status {
                text: "Retrying the API request…".into(),
            }],
            _ => vec![],
        },
        Some("stream_event") => {
            let event = &line["event"];
            if event["type"] == "content_block_delta" && event["delta"]["type"] == "text_delta" {
                str_at(&event["delta"], "text")
                    .map(|text| vec![AgentEvent::TextDelta { text }])
                    .unwrap_or_default()
            } else {
                vec![]
            }
        }
        Some("assistant") => line["message"]["content"]
            .as_array()
            .map(|blocks| {
                blocks
                    .iter()
                    .filter_map(|b| match b.get("type").and_then(Value::as_str) {
                        Some("text") => {
                            str_at(b, "text").map(|text| AgentEvent::AssistantText { text })
                        }
                        Some("tool_use") => Some(AgentEvent::ToolUse {
                            id: str_at(b, "id").unwrap_or_default(),
                            name: str_at(b, "name").unwrap_or_default(),
                            input: b.get("input").cloned().unwrap_or(Value::Null),
                        }),
                        _ => None,
                    })
                    .collect()
            })
            .unwrap_or_default(),
        Some("user") => line["message"]["content"]
            .as_array()
            .map(|blocks| {
                blocks
                    .iter()
                    .filter(|b| b["type"] == "tool_result")
                    .map(|b| AgentEvent::ToolResult {
                        tool_use_id: str_at(b, "tool_use_id").unwrap_or_default(),
                        is_error: b["is_error"].as_bool().unwrap_or(false),
                        content: truncate(tool_result_text(&b["content"]), MAX_TOOL_RESULT_CHARS),
                    })
                    .collect()
            })
            .unwrap_or_default(),
        Some("control_response") if line["response"]["subtype"] == "error" => {
            let response = &line["response"];
            vec![AgentEvent::Error {
                message: str_at(response, "error")
                    .or_else(|| str_at(response, "message"))
                    .unwrap_or_else(|| "Claude Code rejected the request".into()),
            }]
        }
        Some("result") => {
            let is_error = line["is_error"].as_bool().unwrap_or(false);
            let subtype = match str_at(line, "subtype").unwrap_or_default() {
                s if s == "error_during_execution" && !is_error => "interrupted".to_string(),
                s => s,
            };
            vec![AgentEvent::TurnComplete {
                is_error,
                subtype,
                cost_usd: line["total_cost_usd"].as_f64(),
                error: if is_error {
                    str_at(line, "result")
                } else {
                    None
                },
            }]
        }
        _ => vec![],
    }
}

pub fn start(launch: Launch) -> Result<ClaudeCodeSession, String> {
    let mut servers = serde_json::Map::new();
    let headers: serde_json::Map<String, Value> = launch
        .mcp
        .headers(&launch.key, true)
        .into_iter()
        .map(|(k, v)| (k.to_string(), Value::String(v)))
        .collect();
    servers.insert(
        SERVER_NAME.into(),
        json!({"type": "http", "url": launch.mcp.url(), "headers": headers}),
    );
    let config_path = launch
        .data_dir
        .join(format!("mcp-{}.json", super::session_label(&launch.key)));
    process::write_private(&config_path, &json!({"mcpServers": servers}).to_string())
        .map_err(|e| e.to_string())?;

    let mut command = process::command(&launch.exe, &launch.cwd);
    command
        .args(build_args(
            &launch.options,
            &config_path,
            launch.plugin_dir.as_deref(),
        ))
        .env("CLAUDE_CODE_STARTUP_FAILURE_RESULTS", "1");
    let link = process::spawn(
        &launch.app,
        &launch.key,
        launch.generation,
        command,
        "Claude Code",
        Some(config_path),
        |link, line| {
            let Ok(value) = serde_json::from_str::<Value>(line) else {
                return false;
            };
            for event in map_line(&value) {
                link.emit(event);
            }
            true
        },
    )?;
    Ok(ClaudeCodeSession {
        link,
        next_request: AtomicU64::new(0),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replays_live_transcript() {
        let Some(lines) = crate::agent::tests::live_transcript("claude.jsonl") else {
            return;
        };
        let events: Vec<AgentEvent> = lines.iter().flat_map(map_line).collect();
        assert!(matches!(
            events.first(),
            Some(AgentEvent::SessionStarted { .. })
        ));
        crate::agent::tests::check_live_run(&events);
    }

    #[test]
    fn maps_init_and_control_errors() {
        let init = json!({"type":"system","subtype":"init","session_id":"s1","model":"claude-opus","claude_code_version":"2.1.0","permissionMode":"plan"});
        assert_eq!(
            map_line(&init),
            vec![AgentEvent::SessionStarted {
                session_id: "s1".into(),
                model: Some("claude-opus".into()),
                version: Some("2.1.0".into()),
                permission_mode: Some("plan".into())
            }]
        );
        let err = json!({"type":"control_response","response":{"subtype":"error","request_id":"calcpad-1","error":"Auto mode is unavailable"}});
        assert_eq!(
            map_line(&err),
            vec![AgentEvent::Error {
                message: "Auto mode is unavailable".into()
            }]
        );
        let ok = json!({"type":"control_response","response":{"subtype":"success","request_id":"calcpad-1"}});
        assert!(map_line(&ok).is_empty());
        let delta = json!({"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}},"parent_tool_use_id":null});
        assert_eq!(
            map_line(&delta),
            vec![AgentEvent::TextDelta { text: "ok".into() }]
        );
    }

    #[test]
    fn maps_assistant_blocks_and_tool_results() {
        let assistant = json!({"type":"assistant","parent_tool_use_id":null,"message":{"content":[
            {"type":"thinking","thinking":"hidden"},
            {"type":"text","text":"Checking"},
            {"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/a.cpd"}}
        ]}});
        assert_eq!(
            map_line(&assistant),
            vec![
                AgentEvent::AssistantText {
                    text: "Checking".into()
                },
                AgentEvent::ToolUse {
                    id: "t1".into(),
                    name: "Read".into(),
                    input: json!({"file_path":"/a.cpd"})
                }
            ]
        );
        let user = json!({"type":"user","parent_tool_use_id":null,"message":{"content":[
            {"type":"tool_result","tool_use_id":"t1","is_error":true,"content":[{"type":"text","text":"boom"}]}
        ]}});
        assert_eq!(
            map_line(&user),
            vec![AgentEvent::ToolResult {
                tool_use_id: "t1".into(),
                is_error: true,
                content: "boom".into()
            }]
        );
    }

    #[test]
    fn maps_result_and_hides_subagents() {
        let result = json!({"type":"result","subtype":"success","is_error":false,"total_cost_usd":0.5,"result":"done"});
        assert_eq!(
            map_line(&result),
            vec![AgentEvent::TurnComplete {
                is_error: false,
                subtype: "success".into(),
                cost_usd: Some(0.5),
                error: None
            }]
        );
        let stopped = json!({"type":"result","subtype":"error_during_execution","is_error":false});
        assert!(
            matches!(map_line(&stopped).as_slice(), [AgentEvent::TurnComplete { subtype, .. }] if subtype == "interrupted")
        );
        let failed =
            json!({"type":"result","subtype":"success","is_error":true,"result":"Not logged in"});
        assert!(matches!(
            map_line(&failed).as_slice(),
            [AgentEvent::TurnComplete { error: Some(e), .. }] if e == "Not logged in"
        ));
        let nested = json!({"type":"assistant","parent_tool_use_id":"t9","message":{"content":[{"type":"text","text":"x"}]}});
        assert!(map_line(&nested).is_empty());
        assert!(map_line(&json!({"type":"rate_limit_event"})).is_empty());
    }

    #[test]
    fn args_route_permissions_through_calcpad() {
        let options = StartOptions {
            model: Some(" sonnet ".into()),
            permission_mode: Some("auto".into()),
            resume: Some("abc".into()),
            ..Default::default()
        };
        let args = build_args(
            &options,
            Path::new("/tmp/mcp.json"),
            Some(Path::new("/res/agent-plugins/calcpadce")),
        );
        let value_after = |flag: &str| {
            let i = args.iter().position(|a| a == flag).unwrap();
            args[i + 1].clone()
        };
        assert_eq!(
            value_after("--permission-prompt-tool"),
            "mcp__calcpad__approve_tool"
        );
        assert_eq!(value_after("--model"), "sonnet");
        assert_eq!(value_after("--resume"), "abc");
        assert_eq!(value_after("--permission-mode"), "auto");
        assert!(value_after("--disallowedTools").contains("Bash"));
        assert!(value_after("--disallowedTools").contains("Write"));
        assert!(value_after("--append-system-prompt").contains("calcpad-generator"));
        assert!(args.contains(&"--strict-mcp-config".to_string()));
        assert!(value_after("--allowedTools").contains("mcp__calcpad__get_document"));
        assert!(!value_after("--allowedTools").contains("approve_tool"));
        let manual = build_args(&StartOptions::default(), Path::new("/tmp/mcp.json"), None);
        let i = manual
            .iter()
            .position(|a| a == "--disallowedTools")
            .unwrap();
        assert!(!manual[i + 1].contains("Bash"));
        assert!(!manual.contains(&"--permission-mode".to_string()));
        assert!(!manual.contains(&"--strict-mcp-config".to_string()));
        assert!(value_after("--disallowedTools").contains("Edit"));
    }
}
