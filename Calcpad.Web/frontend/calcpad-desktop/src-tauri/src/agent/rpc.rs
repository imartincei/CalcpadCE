//! Minimal JSON-RPC client over a harness process's stdio (Codex app-server, ACP).

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use serde_json::{json, Value};

use super::process::Link;

type Reply = Box<dyn FnOnce(Result<Value, String>) + Send>;

pub enum Incoming {
    /// A reply to one of our requests; already handled.
    Reply,
    Request {
        id: Value,
        method: String,
        params: Value,
    },
    Notification {
        method: String,
        params: Value,
    },
    Invalid,
}

pub struct Rpc {
    link: OnceLock<Arc<Link>>,
    next_id: AtomicU64,
    pending: Mutex<HashMap<u64, Reply>>,
    /// Codex omits the `jsonrpc` field.
    versioned: bool,
}

impl Rpc {
    pub fn new(versioned: bool) -> Arc<Rpc> {
        Arc::new(Rpc {
            link: OnceLock::new(),
            next_id: AtomicU64::new(0),
            pending: Mutex::new(HashMap::new()),
            versioned,
        })
    }

    pub fn attach(&self, link: Arc<Link>) {
        let _ = self.link.set(link);
    }

    pub fn link(&self) -> &Arc<Link> {
        self.link
            .get()
            .expect("rpc used before its process started")
    }

    fn send(&self, mut message: Value) -> Result<(), String> {
        if self.versioned {
            message["jsonrpc"] = json!("2.0");
        }
        self.link().write(&message)
    }

    pub fn request(
        &self,
        method: &str,
        params: Value,
        on_reply: impl FnOnce(Result<Value, String>) + Send + 'static,
    ) -> Result<(), String> {
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        self.pending
            .lock()
            .expect("rpc pending mutex poisoned")
            .insert(id, Box::new(on_reply));
        let sent = self.send(json!({"id": id, "method": method, "params": params}));
        if sent.is_err() {
            self.pending
                .lock()
                .expect("rpc pending mutex poisoned")
                .remove(&id);
        }
        sent
    }

    pub fn notify(&self, method: &str, params: Value) -> Result<(), String> {
        self.send(json!({"method": method, "params": params}))
    }

    pub fn respond(&self, id: Value, result: Value) {
        let _ = self.send(json!({"id": id, "result": result}));
    }

    pub fn respond_error(&self, id: Value, message: &str) {
        let _ = self.send(json!({"id": id, "error": {"code": -32601, "message": message}}));
    }

    pub fn dispatch(&self, message: Value) -> Incoming {
        let method = message
            .get("method")
            .and_then(Value::as_str)
            .map(str::to_string);
        let id = message.get("id").cloned().filter(|id| !id.is_null());
        match (method, id) {
            (Some(method), Some(id)) => Incoming::Request {
                id,
                method,
                params: message.get("params").cloned().unwrap_or(Value::Null),
            },
            (Some(method), None) => Incoming::Notification {
                method,
                params: message.get("params").cloned().unwrap_or(Value::Null),
            },
            (None, Some(id)) => {
                let reply = id.as_u64().and_then(|id| {
                    self.pending
                        .lock()
                        .expect("rpc pending mutex poisoned")
                        .remove(&id)
                });
                if let Some(reply) = reply {
                    reply(match message.get("error") {
                        Some(error) => Err(error_text(error)),
                        None => Ok(message.get("result").cloned().unwrap_or(Value::Null)),
                    });
                }
                Incoming::Reply
            }
            (None, None) => Incoming::Invalid,
        }
    }
}

pub fn error_text(error: &Value) -> String {
    let message = error
        .get("message")
        .and_then(Value::as_str)
        .unwrap_or("The agent reported an error");
    match error
        .pointer("/data/details")
        .or_else(|| error.pointer("/data/message"))
        .and_then(Value::as_str)
    {
        Some(detail) if detail != message => format!("{message}: {detail}"),
        _ => message.to_string(),
    }
}

/// Joins the text blocks of MCP or ACP content.
pub fn content_text(content: &Value) -> String {
    match content {
        Value::String(s) => s.clone(),
        Value::Array(items) => items
            .iter()
            .filter_map(|item| {
                item.get("text")
                    .or_else(|| item.pointer("/content/text"))
                    .and_then(Value::as_str)
            })
            .collect::<Vec<_>>()
            .join("\n"),
        Value::Object(_) => content
            .get("text")
            .and_then(Value::as_str)
            .map(str::to_string)
            .unwrap_or_default(),
        _ => String::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn classifies_messages_without_a_process() {
        let rpc = Rpc::new(true);
        assert!(
            matches!(rpc.dispatch(json!({"id":3,"method":"x","params":{}})), Incoming::Request { method, .. } if method == "x")
        );
        assert!(
            matches!(rpc.dispatch(json!({"method":"y"})), Incoming::Notification { method, .. } if method == "y")
        );
        assert!(matches!(
            rpc.dispatch(json!({"id":9,"result":{}})),
            Incoming::Reply
        ));
        assert!(matches!(rpc.dispatch(json!({})), Incoming::Invalid));
    }

    #[test]
    fn extracts_text_from_content_shapes() {
        assert_eq!(
            content_text(
                &json!([{"type":"text","text":"a"},{"type":"content","content":{"type":"text","text":"b"}}])
            ),
            "a\nb"
        );
        assert_eq!(
            error_text(&json!({"message":"Auth required","data":{"details":"run login"}})),
            "Auth required: run login"
        );
    }
}
