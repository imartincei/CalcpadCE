//! The agent CLIs the chat can drive, what each supports, and how to find it.

use std::path::{Path, PathBuf};

use serde_json::{json, Value};
use tauri::AppHandle;

use super::detect::{self, Extra};
use super::mcp::McpEndpoint;
use super::{acp, claude_code, codex, pi, AgentBackend, SessionKey, StartOptions};

pub const SYSTEM_PROMPT: &str = "You are in the CalcpadCE desktop app. \
Work on the user's worksheets (.cpd) only through the calcpad MCP tools: read them with get_document, not from disk, and change them only with propose_edit, never by writing files. \
Before writing or reviewing Calcpad, call calcpad_reference once with no arguments.";

pub const RESTRICTED_PROMPT: &str =
    " In this mode you cannot run commands or write files; propose_edit is the only way to make changes.";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Harness {
    ClaudeCode,
    Codex,
    Copilot,
    Gemini,
    Opencode,
    Pi,
}

/// How a mode treats propose_edit: diff review, applied with a backup, or refused.
#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Edits {
    Review,
    Auto,
    Blocked,
}

pub struct Mode {
    pub value: &'static str,
    label: &'static str,
    hint: &'static str,
    pub edits: Edits,
    /// Switching between modes with different profiles restarts the agent (the session resumes).
    profile: Option<&'static str>,
}

const MANUAL: Mode = Mode {
    value: "default",
    label: "catalog.manual",
    hint: "catalog.askBeforeRunningCommandsOrEditing",
    edits: Edits::Review,
    profile: None,
};

const AUTO: Mode = Mode {
    value: "auto",
    label: "catalog.autoMode",
    hint: "catalog.workWithoutAskingChangingOnlyOpenWorksheetsEvery",
    edits: Edits::Auto,
    profile: Some("restricted"),
};

const CLAUDE_MODES: &[Mode] = &[
    MANUAL,
    Mode {
        value: "acceptEdits",
        label: "catalog.editAutomatically",
        hint: "catalog.applyEditsToOpenWorksheetsWithoutReviewNo",
        edits: Edits::Auto,
        profile: Some("restricted"),
    },
    Mode {
        value: "plan",
        label: "catalog.planMode",
        hint: "catalog.exploreAndProposeAPlanWithoutMakingChanges",
        edits: Edits::Blocked,
        profile: None,
    },
    AUTO,
];

const fn standard_modes(profile: Option<&'static str>) -> [Mode; 3] {
    [
        MANUAL,
        Mode {
            value: "acceptEdits",
            label: "catalog.editAutomatically",
            hint: "catalog.applyEditsToOpenWorksheetsWithoutReviewNo",
            edits: Edits::Auto,
            profile,
        },
        Mode {
            value: "plan",
            label: "catalog.planMode",
            hint: "catalog.readAndProposeAPlanNoEditsCommands",
            edits: Edits::Blocked,
            profile,
        },
    ]
}

/// Codex picks its sandbox at launch.
const RESTARTING_MODES: &[Mode] = &standard_modes(Some("restricted"));
/// pi has no plan mode; its tool set is picked at launch.
const PI_MODES: &[Mode] = &[MANUAL, AUTO];
/// ACP agents ask the app for permission, so the app applies the mode itself.
const LIVE_MODES: &[Mode] = &standard_modes(None);

pub struct Launch {
    pub app: AppHandle,
    pub key: SessionKey,
    pub generation: u64,
    pub exe: PathBuf,
    pub cwd: PathBuf,
    pub options: StartOptions,
    pub mcp: McpEndpoint,
    pub data_dir: PathBuf,
}

impl Launch {
    pub fn is_restricted(&self) -> bool {
        matches!(
            self.options.permission_mode.as_deref(),
            Some("acceptEdits" | "auto" | "plan")
        )
    }
}

impl Harness {
    /// Dropdown order; the first installed one is the default.
    pub const ALL: [Harness; 6] = [
        Harness::ClaudeCode,
        Harness::Codex,
        Harness::Copilot,
        Harness::Gemini,
        Harness::Opencode,
        Harness::Pi,
    ];

    pub fn from_id(id: &str) -> Option<Harness> {
        Self::ALL.into_iter().find(|h| h.id() == id)
    }

    pub fn id(self) -> &'static str {
        match self {
            Harness::ClaudeCode => "claude-code",
            Harness::Codex => "codex",
            Harness::Copilot => "copilot",
            Harness::Gemini => "gemini",
            Harness::Opencode => "opencode",
            Harness::Pi => "pi",
        }
    }

    fn name(self) -> &'static str {
        match self {
            Harness::ClaudeCode => "Claude Code",
            Harness::Codex => "Codex",
            Harness::Copilot => "GitHub Copilot",
            Harness::Gemini => "Gemini CLI",
            Harness::Opencode => "opencode",
            Harness::Pi => "pi",
        }
    }

    fn assistant_name(self) -> &'static str {
        match self {
            Harness::ClaudeCode => "Claude",
            Harness::Copilot => "Copilot",
            Harness::Gemini => "Gemini",
            _ => self.name(),
        }
    }

    fn command(self) -> &'static str {
        match self {
            Harness::ClaudeCode => "claude",
            Harness::Codex => "codex",
            Harness::Copilot => "copilot",
            Harness::Gemini => "gemini",
            Harness::Opencode => "opencode",
            Harness::Pi => "pi",
        }
    }

    fn install_hint(self) -> &'static str {
        match self {
            Harness::ClaudeCode => "https://code.claude.com/docs/en/setup",
            Harness::Codex => "npm install -g @openai/codex",
            Harness::Copilot => "npm install -g @github/copilot",
            Harness::Gemini => "npm install -g @google/gemini-cli",
            Harness::Opencode => "https://opencode.ai",
            Harness::Pi => "https://pi.dev",
        }
    }

    fn extra_paths(self, home: Option<&Path>) -> Vec<Extra> {
        let Some(home) = home else { return vec![] };
        let exe = |name: &str| {
            if cfg!(windows) {
                format!("{name}.exe")
            } else {
                name.to_string()
            }
        };
        let editor_extension = |prefix: &str| -> Vec<PathBuf> {
            [".vscode", ".vscode-oss", ".vscode-insiders", ".cursor"]
                .iter()
                .filter_map(|editor| {
                    detect::latest_child(&home.join(editor).join("extensions"), prefix)
                })
                .collect()
        };
        match self {
            Harness::ClaudeCode => {
                let mut out = vec![Extra::Dir(home.join(".claude").join("local"))];
                out.extend(
                    editor_extension("anthropic.claude-code-")
                        .into_iter()
                        .map(|d| {
                            Extra::File(
                                d.join("resources")
                                    .join("native-binary")
                                    .join(exe("claude")),
                            )
                        }),
                );
                out
            }
            Harness::Codex => editor_extension("openai.chatgpt-")
                .into_iter()
                .filter_map(|d| std::fs::read_dir(d.join("bin")).ok())
                .flat_map(|entries| {
                    entries
                        .flatten()
                        .map(|e| Extra::File(e.path().join(exe("codex"))))
                })
                .collect(),
            Harness::Opencode => vec![Extra::Dir(home.join(".opencode").join("bin"))],
            Harness::Pi => vec![Extra::Dir(home.join(".pi").join("agent").join("bin"))],
            Harness::Copilot | Harness::Gemini => vec![],
        }
    }

    pub fn find(self, configured: Option<&str>) -> Option<PathBuf> {
        detect::find(
            configured,
            self.command(),
            self.extra_paths(detect::home().as_deref()),
        )
    }

    fn modes(self) -> &'static [Mode] {
        match self {
            Harness::ClaudeCode => CLAUDE_MODES,
            Harness::Codex => RESTARTING_MODES,
            Harness::Pi => PI_MODES,
            Harness::Copilot | Harness::Gemini | Harness::Opencode => LIVE_MODES,
        }
    }

    pub fn mode(self, value: &str) -> Result<&'static Mode, String> {
        self.modes()
            .iter()
            .find(|m| m.value == value)
            .ok_or_else(|| format!("Unsupported permission mode {value}"))
    }

    /// Fixed model aliases, or none when the agent reports its models once it starts.
    fn models(self) -> Value {
        match self {
            Harness::ClaudeCode => json!([
                {"value": "", "label": "Default"},
                {"value": "fable", "label": "Fable"},
                {"value": "opus", "label": "Opus"},
                {"value": "sonnet", "label": "Sonnet"},
                {"value": "haiku", "label": "Haiku"},
            ]),
            _ => json!([]),
        }
    }

    /// What the chat panel shows for this harness.
    pub fn info(self, configured: Option<&str>) -> Value {
        let path = self.find(configured);
        let modes: Vec<Value> = self
            .modes()
            .iter()
            .map(|m| {
                json!({
                    "value": m.value,
                    "label": m.label,
                    "hint": m.hint,
                    "edits": match m.edits { Edits::Review => "review", Edits::Auto => "auto", Edits::Blocked => "blocked" },
                    "profile": m.profile,
                })
            })
            .collect();
        json!({
            "id": self.id(),
            "name": self.name(),
            "assistantName": self.assistant_name(),
            "installed": path.as_ref().is_some_and(|p| p.is_file()),
            "path": path,
            "installHint": self.install_hint(),
            "models": self.models(),
            "dynamicModels": self.models().as_array().is_some_and(|m| m.is_empty()),
            "permissionModes": modes,
        })
    }

    pub fn start(self, launch: Launch) -> Result<Box<dyn AgentBackend>, String> {
        Ok(match self {
            Harness::ClaudeCode => Box::new(claude_code::start(launch)?),
            Harness::Codex => Box::new(codex::start(launch)?),
            Harness::Pi => Box::new(pi::start(launch)?),
            Harness::Copilot => Box::new(acp::start(launch, acp::Agent::Copilot)?),
            Harness::Gemini => Box::new(acp::start(launch, acp::Agent::Gemini)?),
            Harness::Opencode => Box::new(acp::start(launch, acp::Agent::Opencode)?),
        })
    }

    pub fn not_found(self) -> String {
        format!(
            "{} was not found. Install it ({}), sign in once in a terminal, or set its path in the chat settings.",
            self.name(),
            self.install_hint()
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ids_round_trip_and_modes_validate() {
        for h in Harness::ALL {
            assert_eq!(Harness::from_id(h.id()), Some(h));
            assert_eq!(h.modes()[0].value, "default");
            assert!(h.mode("bypassPermissions").is_err());
        }
        assert!(Harness::ClaudeCode.mode("auto").is_ok());
        assert!(Harness::Pi.mode("auto").is_ok());
        assert!(Harness::Pi.mode("plan").is_err());
        assert!(Harness::Codex.mode("auto").is_err());
    }

    #[test]
    fn every_harness_is_told_about_the_reference_tool() {
        assert!(SYSTEM_PROMPT.contains(super::super::reference::TOOL));
    }

    #[test]
    fn only_claude_has_fixed_models() {
        let info = Harness::ClaudeCode.info(Some("/nonexistent/claude"));
        assert_eq!(info["dynamicModels"], false);
        assert_eq!(info["installed"], false);
        assert_eq!(Harness::Codex.info(None)["dynamicModels"], true);
    }
}
