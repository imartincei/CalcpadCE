//! Claude Code backend: drives `claude -p` over its stream-json protocol.

use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use serde_json::{json, Value};
use tauri::AppHandle;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::sync::mpsc;

use super::mcp::{auto_approved_tools, APPROVAL_TOOL, SERVER_NAME};
use super::{AgentBackend, AgentEvent, SessionKey, StartOptions};

const NOT_FOUND: &str = "Claude Code was not found. Install it (https://code.claude.com/docs/en/setup), sign in by running `claude` once in a terminal, or set its path in the chat settings.";
const OUTPUT_TAIL_BYTES: usize = 4096;
// Set when the app is launched from inside a Claude Code session; the child must not inherit them.
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
    "VSCODE_INSPECTOR_OPTIONS",
];
const MAX_TOOL_RESULT_CHARS: usize = 4000;
// Built-in editing tools write to disk underneath unsaved editor buffers; edits go through propose_edit.
const DISALLOWED_TOOLS: &[&str] = &["Edit", "MultiEdit", "NotebookEdit"];
// Automatic modes may only change open worksheets, through propose_edit.
const RESTRICTED_TOOLS: &[&str] = &["Write", "Bash"];
const RESTRICTED_PROMPT: &str = " You are in an automatic mode: you cannot run shell commands or write files; change only worksheets open in Calcpad, with propose_edit.";

fn is_restricted(options: &StartOptions) -> bool {
    matches!(options.permission_mode.as_deref(), Some("acceptEdits" | "auto"))
}
const SYSTEM_PROMPT: &str = "You are running inside the CalcpadCE desktop app, next to the user's open worksheets. \
Use the calcpad MCP tools to work with them: read open worksheets with get_document (it includes unsaved changes) rather than from disk, \
check get_diagnostics after changes, and change open worksheets only with propose_edit, which shows the user a diff to accept. \
Use the calcpad-generator skill for Calcpad syntax.";

pub struct Launch {
    pub options: StartOptions,
    pub mcp_config: Value,
    pub plugin_dir: Option<PathBuf>,
    pub config_dir: PathBuf,
}

pub struct ClaudeCodeSession {
    stdin_tx: mpsc::Sender<String>,
    kill_tx: mpsc::Sender<()>,
    next_request: AtomicU64,
}

impl AgentBackend for ClaudeCodeSession {
    fn send_user(&self, text: &str) -> Result<(), String> {
        self.write(user_message(text))
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
        let _ = self.kill_tx.try_send(());
    }
}

impl ClaudeCodeSession {
    fn control(&self, request: Value) -> Result<(), String> {
        let id = self.next_request.fetch_add(1, Ordering::Relaxed);
        self.write(json!({
            "type": "control_request",
            "request_id": format!("calcpad-{id}"),
            "request": request
        }))
    }

    fn write(&self, line: Value) -> Result<(), String> {
        self.stdin_tx
            .try_send(line.to_string())
            .map_err(|_| "Claude Code is not accepting input".to_string())
    }
}

fn user_message(text: &str) -> Value {
    json!({
        "type": "user",
        "message": {"role": "user", "content": [{"type": "text", "text": text}]},
        "parent_tool_use_id": null
    })
}

pub fn build_args(options: &StartOptions, mcp_config: &Path, plugin_dir: Option<&Path>) -> Vec<String> {
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
    args.push(auto_approved_tools().join(","));
    let restricted = is_restricted(options);
    let mut disallowed = DISALLOWED_TOOLS.to_vec();
    if restricted {
        disallowed.extend_from_slice(RESTRICTED_TOOLS);
        // The user's own MCP servers could change things outside Calcpad.
        args.push("--strict-mcp-config".into());
    }
    args.push("--disallowedTools".into());
    args.push(disallowed.join(","));
    args.push("--append-system-prompt".into());
    args.push(if restricted {
        format!("{SYSTEM_PROMPT}{RESTRICTED_PROMPT}")
    } else {
        SYSTEM_PROMPT.into()
    });
    if let Some(dir) = plugin_dir {
        args.push("--plugin-dir".into());
        args.push(dir.to_string_lossy().into_owned());
    }
    if let Some(model) = options.model.as_deref().filter(|m| !m.trim().is_empty()) {
        args.push("--model".into());
        args.push(model.trim().into());
    }
    if let Some(mode) = options.permission_mode.as_deref().filter(|m| *m != "default") {
        args.push("--permission-mode".into());
        args.push(mode.into());
    }
    if let Some(session) = options.resume.as_deref().filter(|s| !s.is_empty()) {
        args.push("--resume".into());
        args.push(session.into());
    }
    args
}

fn exe_name() -> &'static str {
    if cfg!(windows) {
        "claude.exe"
    } else {
        "claude"
    }
}

/// Candidate locations in priority order. GUI launches often lack the shell's PATH, so
/// the installer's default locations and the VS Code extension's bundled binary are listed too.
pub fn candidates(path_var: Option<&str>, home: Option<&Path>) -> Vec<PathBuf> {
    let mut out: Vec<PathBuf> = path_var
        .map(|p| std::env::split_paths(p).map(|d| d.join(exe_name())).collect())
        .unwrap_or_default();
    let Some(home) = home else {
        return out;
    };
    out.push(home.join(".local").join("bin").join(exe_name()));
    out.push(home.join(".claude").join("local").join(exe_name()));
    if !cfg!(windows) {
        out.push(PathBuf::from("/usr/local/bin/claude"));
        out.push(PathBuf::from("/opt/homebrew/bin/claude"));
    }
    for editor in [".vscode", ".vscode-oss", ".vscode-insiders", ".cursor"] {
        let Ok(entries) = std::fs::read_dir(home.join(editor).join("extensions")) else {
            continue;
        };
        let mut dirs: Vec<PathBuf> = entries
            .flatten()
            .filter(|e| e.file_name().to_string_lossy().starts_with("anthropic.claude-code-"))
            .map(|e| e.path())
            .collect();
        dirs.sort();
        if let Some(latest) = dirs.pop() {
            out.push(latest.join("resources").join("native-binary").join(exe_name()));
        }
    }
    out
}

fn find_executable(configured: Option<&str>) -> Option<PathBuf> {
    if let Some(path) = configured.map(str::trim).filter(|p| !p.is_empty()) {
        return Some(PathBuf::from(path));
    }
    let path_var = std::env::var("PATH").ok();
    let home = std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from);
    candidates(path_var.as_deref(), home.as_deref())
        .into_iter()
        .find(|p| p.is_file())
}

fn resolve_cwd(requested: Option<&str>) -> PathBuf {
    requested
        .map(PathBuf::from)
        .filter(|p| p.is_dir())
        .or_else(|| std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from))
        .unwrap_or_else(std::env::temp_dir)
}

/// VS Code's debugger auto-attach injects these; Claude Code then exits 1 without output.
fn is_debugger_hook(node_options: &str) -> bool {
    node_options.contains("--inspect") || node_options.contains("--require") || node_options.contains("-r ")
}

fn write_private(path: &Path, contents: &str) -> std::io::Result<()> {
    let mut options = std::fs::OpenOptions::new();
    options.write(true).create(true).truncate(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    std::io::Write::write_all(&mut options.open(path)?, contents.as_bytes())
}

fn truncate(text: String, max: usize) -> String {
    match text.char_indices().nth(max) {
        Some((cut, _)) => format!("{}…", &text[..cut]),
        None => text,
    }
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
                        Some("text") => str_at(b, "text").map(|text| AgentEvent::AssistantText { text }),
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
            vec![AgentEvent::TurnComplete {
                is_error,
                subtype: str_at(line, "subtype").unwrap_or_default(),
                cost_usd: line["total_cost_usd"].as_f64(),
                error: if is_error { str_at(line, "result") } else { None },
            }]
        }
        _ => vec![],
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

pub fn spawn(
    app: &AppHandle,
    key: &SessionKey,
    generation: u64,
    launch: Launch,
) -> Result<ClaudeCodeSession, String> {
    let exe = find_executable(launch.options.executable.as_deref()).ok_or(NOT_FOUND)?;
    std::fs::create_dir_all(&launch.config_dir).map_err(|e| e.to_string())?;
    let safe_label: String = key
        .window
        .chars()
        .map(|c| if c.is_ascii_alphanumeric() { c } else { '_' })
        .collect();
    let config_path = launch
        .config_dir
        .join(format!("mcp-{safe_label}-{}.json", key.conversation));
    write_private(&config_path, &launch.mcp_config.to_string()).map_err(|e| e.to_string())?;

    let mut command = tokio::process::Command::new(&exe);
    command
        .args(build_args(&launch.options, &config_path, launch.plugin_dir.as_deref()))
        .current_dir(resolve_cwd(launch.options.cwd.as_deref()))
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .env("CLAUDE_CODE_STARTUP_FAILURE_RESULTS", "1");
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
    let mut child = command
        .spawn()
        .map_err(|e| format!("Could not start Claude Code ({}): {e}", exe.display()))?;
    #[cfg(windows)]
    if let Some(pid) = child.id() {
        crate::assign_to_job_object(pid);
    }

    log::info!("Started Claude Code: {}", exe.display());
    let mut stdin = child.stdin.take().ok_or("Claude Code stdin unavailable")?;
    let stdout = child.stdout.take().ok_or("Claude Code stdout unavailable")?;
    let stderr = child.stderr.take().ok_or("Claude Code stderr unavailable")?;

    let (stdin_tx, mut stdin_rx) = mpsc::channel::<String>(64);
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
    let stdout_tail = tail.clone();
    let exit_tx = event_tx.clone();
    let stdout_task = tauri::async_runtime::spawn(async move {
        let mut lines = BufReader::new(stdout).lines();
        while let Ok(Some(line)) = lines.next_line().await {
            let Ok(value) = serde_json::from_str::<Value>(&line) else {
                append_tail(&stdout_tail, &line);
                continue;
            };
            for event in map_line(&value) {
                let _ = event_tx.try_send(event);
            }
        }
    });
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
    {
        tauri::async_runtime::spawn(async move {
            let code = tokio::select! {
                status = child.wait() => status.ok().and_then(|s| s.code()),
                _ = kill_rx.recv() => {
                    let _ = child.start_kill();
                    child.wait().await.ok().and_then(|s| s.code())
                }
            };
            let _ = stdout_task.await;
            let _ = stderr_task.await;
            let _ = std::fs::remove_file(&config_path);
            let output = tail.lock().map(|t| t.clone()).unwrap_or_default();
            if code != Some(0) {
                log::warn!("Claude Code exited with {code:?}: {output}");
            }
            let _ = exit_tx.send(AgentEvent::Exited { code, output }).await;
        });
    }

    Ok(ClaudeCodeSession {
        stdin_tx,
        kill_tx,
        next_request: AtomicU64::new(0),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn maps_init_and_text_delta() {
        let init = json!({"type":"system","subtype":"init","session_id":"s1","model":"m","claude_code_version":"2.1.318","permissionMode":"auto","parent_tool_use_id":null});
        assert_eq!(
            map_line(&init),
            vec![AgentEvent::SessionStarted {
                session_id: "s1".into(),
                model: Some("m".into()),
                version: Some("2.1.318".into()),
                permission_mode: Some("auto".into())
            }]
        );
        let rejected = json!({"type":"control_response","response":{"subtype":"error","request_id":"calcpad-1","error":"Auto mode is unavailable"}});
        assert_eq!(map_line(&rejected), vec![AgentEvent::Error { message: "Auto mode is unavailable".into() }]);
        let ok = json!({"type":"control_response","response":{"subtype":"success","request_id":"calcpad-1"}});
        assert!(map_line(&ok).is_empty());
        let delta = json!({"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}},"parent_tool_use_id":null});
        assert_eq!(map_line(&delta), vec![AgentEvent::TextDelta { text: "ok".into() }]);
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
                AgentEvent::AssistantText { text: "Checking".into() },
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
        let failed = json!({"type":"result","subtype":"success","is_error":true,"result":"Not logged in"});
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
        let args = build_args(&options, Path::new("/tmp/mcp.json"), Some(Path::new("/res/agent-plugin")));
        let value_after = |flag: &str| {
            let i = args.iter().position(|a| a == flag).unwrap();
            args[i + 1].clone()
        };
        assert_eq!(value_after("--permission-prompt-tool"), "mcp__calcpad__approve_tool");
        assert_eq!(value_after("--model"), "sonnet");
        assert_eq!(value_after("--resume"), "abc");
        assert_eq!(value_after("--permission-mode"), "auto");
        assert!(value_after("--disallowedTools").contains("Bash"));
        assert!(value_after("--disallowedTools").contains("Write"));
        assert!(args.contains(&"--strict-mcp-config".to_string()));
        let manual = build_args(&StartOptions::default(), Path::new("/tmp/mcp.json"), None);
        let i = manual.iter().position(|a| a == "--disallowedTools").unwrap();
        assert!(!manual[i + 1].contains("Bash"));
        assert!(!manual.contains(&"--permission-mode".to_string()));
        assert!(!manual.contains(&"--strict-mcp-config".to_string()));
        assert_eq!(value_after("--plugin-dir"), "/res/agent-plugin");
        assert!(value_after("--disallowedTools").contains("Edit"));
        assert!(!value_after("--allowedTools").contains("approve_tool"));
    }

    #[test]
    fn path_candidates_come_first() {
        let path = std::env::join_paths([Path::new("/x/bin")]).unwrap();
        let found = candidates(path.to_str(), Some(Path::new("/nonexistent-home")));
        assert_eq!(found[0], Path::new("/x/bin").join(exe_name()));
        assert!(found.contains(&Path::new("/nonexistent-home/.local/bin").join(exe_name())));
    }

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
