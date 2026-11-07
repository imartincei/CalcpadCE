//! Loopback MCP server exposing the live editor of each window to agents.

use std::convert::Infallible;
use std::future::Future;
use std::pin::Pin;
use std::sync::Arc;
use std::time::Duration;

use bytes::Bytes;
use http_body_util::{BodyExt, Full};
use hyper::body::Incoming;
use hyper_util::rt::TokioIo;
use rmcp::model::{
    CallToolRequestParams, CallToolResponse, CallToolResult, ContentBlock, Implementation,
    ListToolsResult, PaginatedRequestParams, ServerCapabilities, ServerConfig, Tool,
};
use rmcp::service::{RequestContext, RoleServer};
use rmcp::transport::streamable_http_server::{
    session::local::LocalSessionManager, StreamableHttpServerConfig, StreamableHttpService,
};
use rmcp::{ErrorData, ServerHandler};
use serde_json::{json, Value};
use tower_service::Service;

use super::SessionKey;

pub type BridgeFuture<'a> = Pin<Box<dyn Future<Output = Result<Value, String>> + Send + 'a>>;

/// Runs a tool against the editor of `window`.
pub trait ToolBridge: Send + Sync + 'static {
    fn call<'a>(
        &'a self,
        key: SessionKey,
        tool: &'a str,
        args: Value,
        timeout: Option<Duration>,
    ) -> BridgeFuture<'a>;
}

pub const SERVER_NAME: &str = "calcpad";
const WINDOW_HEADER: &str = "x-calcpad-window";
const CONVERSATION_HEADER: &str = "x-calcpad-conversation";
const DATA_TIMEOUT: Option<Duration> = Some(Duration::from_secs(30));

#[derive(Clone)]
pub struct McpEndpoint {
    url: String,
    token: String,
}

impl McpEndpoint {
    pub fn config_json(&self, key: &SessionKey) -> Value {
        json!({
            "mcpServers": {
                SERVER_NAME: {
                    "type": "http",
                    "url": self.url,
                    "headers": {
                        "Authorization": format!("Bearer {}", self.token),
                        "X-Calcpad-Window": key.window,
                        "X-Calcpad-Conversation": key.conversation,
                    }
                }
            }
        })
    }
}

struct ToolSpec {
    name: &'static str,
    description: &'static str,
    schema: fn() -> Value,
    /// None waits on the user indefinitely.
    timeout: Option<Duration>,
    /// Runs without a permission prompt.
    auto_approve: bool,
}

fn path_arg() -> Value {
    json!({
        "type": "object",
        "properties": {
            "path": {"type": "string", "description": "Absolute path of an open worksheet, or its id from list_open_documents if untitled. Defaults to the active one."}
        }
    })
}

const TOOLS: &[ToolSpec] = &[
    ToolSpec {
        name: "list_open_documents",
        description: "List the worksheets open in Calcpad, with their paths, which one is active, and whether each has unsaved changes.",
        schema: || json!({"type": "object", "properties": {}}),
        timeout: DATA_TIMEOUT,
        auto_approve: true,
    },
    ToolSpec {
        name: "get_document",
        description: "Read an open worksheet's current text from the editor, including unsaved changes. Prefer this over reading the file from disk.",
        schema: path_arg,
        timeout: DATA_TIMEOUT,
        auto_approve: true,
    },
    ToolSpec {
        name: "get_selection",
        description: "Get the user's current selection and cursor position in the active worksheet.",
        schema: || json!({"type": "object", "properties": {}}),
        timeout: DATA_TIMEOUT,
        auto_approve: true,
    },
    ToolSpec {
        name: "get_diagnostics",
        description: "Get the linter and calculation errors/warnings currently shown for an open worksheet (1-based lines).",
        schema: path_arg,
        timeout: DATA_TIMEOUT,
        auto_approve: true,
    },
    ToolSpec {
        name: "open_document",
        description: "Open a worksheet file in the Calcpad editor and make it active.",
        schema: || json!({
            "type": "object",
            "properties": {"path": {"type": "string", "description": "Absolute path of the .cpd file."}},
            "required": ["path"]
        }),
        timeout: DATA_TIMEOUT,
        auto_approve: true,
    },
    ToolSpec {
        name: "propose_edit",
        description: "Propose a change to an open worksheet. The user reviews a diff and accepts or rejects it; accepted changes land in the editor buffer (unsaved, undoable). Give either `edits` (each oldText must match exactly once) or the full new `text`.",
        schema: || json!({
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Absolute path of an open worksheet, or its id from list_open_documents if untitled. Defaults to the active one."},
                "edits": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {"oldText": {"type": "string"}, "newText": {"type": "string"}},
                        "required": ["oldText", "newText"]
                    }
                },
                "text": {"type": "string", "description": "Full replacement text."},
                "summary": {"type": "string", "description": "One line shown to the user above the diff."}
            }
        }),
        timeout: None,
        auto_approve: true,
    },
    ToolSpec {
        name: "approve_tool",
        description: "Internal: asks the Calcpad user to approve another tool call.",
        schema: || json!({
            "type": "object",
            "properties": {
                "tool_name": {"type": "string"},
                "input": {"type": "object"},
                "tool_use_id": {"type": "string"}
            },
            "required": ["tool_name", "input"]
        }),
        timeout: None,
        auto_approve: false,
    },
];

pub const APPROVAL_TOOL: &str = "approve_tool";

/// Fully-qualified names of tools that need no permission prompt.
pub fn auto_approved_tools() -> Vec<String> {
    TOOLS
        .iter()
        .filter(|t| t.auto_approve)
        .map(|t| format!("mcp__{SERVER_NAME}__{}", t.name))
        .collect()
}

fn tools() -> Vec<Tool> {
    TOOLS
        .iter()
        .map(|t| {
            let Value::Object(schema) = (t.schema)() else {
                unreachable!("tool schemas are objects")
            };
            Tool::new(t.name, t.description, Arc::new(schema))
        })
        .collect()
}

fn render(value: Value) -> String {
    match value {
        Value::String(s) => s,
        other => serde_json::to_string_pretty(&other).unwrap_or_default(),
    }
}

#[derive(Clone)]
struct CalcpadMcp {
    bridge: Arc<dyn ToolBridge>,
}

impl ServerHandler for CalcpadMcp {
    fn get_info(&self) -> ServerConfig {
        ServerConfig::new(ServerCapabilities::builder().enable_tools().build())
            .with_server_info(Implementation::new("calcpad", env!("CARGO_PKG_VERSION")))
    }

    async fn list_tools(
        &self,
        _request: Option<PaginatedRequestParams>,
        _context: RequestContext<RoleServer>,
    ) -> Result<ListToolsResult, ErrorData> {
        Ok(ListToolsResult::with_all_items(tools()))
    }

    async fn call_tool(
        &self,
        request: CallToolRequestParams,
        context: RequestContext<RoleServer>,
    ) -> Result<CallToolResponse, ErrorData> {
        let header = |name: &str| {
            context
                .extensions
                .get::<http::request::Parts>()
                .and_then(|p| p.headers.get(name))
                .and_then(|v| v.to_str().ok())
                .map(str::to_string)
                .ok_or_else(|| ErrorData::invalid_request(format!("Missing {name} header"), None))
        };
        let key = SessionKey {
            window: header(WINDOW_HEADER)?,
            conversation: header(CONVERSATION_HEADER)?,
        };
        let spec = TOOLS
            .iter()
            .find(|t| t.name == request.name)
            .ok_or_else(|| ErrorData::invalid_params(format!("Unknown tool {}", request.name), None))?;
        let args = Value::Object(request.arguments.unwrap_or_default());
        let result = self.bridge.call(key, spec.name, args, spec.timeout).await;
        Ok(match result {
            Ok(value) => CallToolResult::success(vec![ContentBlock::text(render(value))]),
            Err(err) => CallToolResult::error(vec![ContentBlock::text(err)]),
        }
        .into())
    }
}

fn random_token() -> Result<String, String> {
    let mut bytes = [0u8; 32];
    getrandom::getrandom(&mut bytes).map_err(|e| e.to_string())?;
    Ok(bytes.iter().map(|b| format!("{b:02x}")).collect())
}

fn authorized(headers: &http::HeaderMap, expected: &str) -> bool {
    let Some(value) = headers.get(http::header::AUTHORIZATION).and_then(|v| v.to_str().ok()) else {
        return false;
    };
    let Some(token) = value.strip_prefix("Bearer ") else {
        return false;
    };
    // Constant-time compare.
    token.len() == expected.len()
        && token
            .bytes()
            .zip(expected.bytes())
            .fold(0u8, |acc, (a, b)| acc | (a ^ b))
            == 0
}

pub async fn start(bridge: Arc<dyn ToolBridge>) -> Result<McpEndpoint, String> {
    let token = random_token()?;
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|e| format!("Could not start the Calcpad MCP server: {e}"))?;
    let addr = listener.local_addr().map_err(|e| e.to_string())?;

    let service = StreamableHttpService::new(
        move || Ok(CalcpadMcp { bridge: bridge.clone() }),
        Arc::new(LocalSessionManager::default()),
        StreamableHttpServerConfig::default(),
    );
    let expected = token.clone();
    tauri::async_runtime::spawn(async move {
        loop {
            let Ok((stream, _)) = listener.accept().await else {
                tokio::time::sleep(Duration::from_millis(50)).await;
                continue;
            };
            let service = service.clone();
            let expected = expected.clone();
            tauri::async_runtime::spawn(async move {
                let handler = hyper::service::service_fn(move |req: http::Request<Incoming>| {
                    let mut service = service.clone();
                    let ok = authorized(req.headers(), &expected);
                    async move {
                        if !ok {
                            let body = Full::new(Bytes::from_static(b"Unauthorized"))
                                .map_err(|never| match never {})
                                .boxed();
                            let mut response = http::Response::new(body);
                            *response.status_mut() = http::StatusCode::UNAUTHORIZED;
                            return Ok::<_, Infallible>(response);
                        }
                        service.call(req).await
                    }
                });
                let _ = hyper::server::conn::http1::Builder::new()
                    .serve_connection(TokioIo::new(stream), handler)
                    .await;
            });
        }
    });

    Ok(McpEndpoint {
        url: format!("http://{addr}/mcp"),
        token,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn auth_requires_exact_bearer() {
        let mut headers = http::HeaderMap::new();
        assert!(!authorized(&headers, "abc"));
        headers.insert(http::header::AUTHORIZATION, "Bearer abd".parse().unwrap());
        assert!(!authorized(&headers, "abc"));
        headers.insert(http::header::AUTHORIZATION, "Bearer abc".parse().unwrap());
        assert!(authorized(&headers, "abc"));
    }

    #[test]
    fn approval_tool_is_never_auto_approved() {
        let names = auto_approved_tools();
        assert!(names.contains(&"mcp__calcpad__get_document".to_string()));
        assert!(!names.iter().any(|n| n.ends_with(APPROVAL_TOOL)));
    }

    #[test]
    fn every_tool_schema_is_an_object() {
        assert_eq!(tools().len(), TOOLS.len());
    }
}
