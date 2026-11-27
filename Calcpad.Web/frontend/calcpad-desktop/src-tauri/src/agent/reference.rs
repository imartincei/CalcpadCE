//! The Calcpad rules and the bundled docs, served by the calcpad_reference tool.

use std::path::PathBuf;

use serde::Deserialize;

pub const TOOL: &str = "calcpad_reference";
/// Pages larger than this return an outline unless a section is asked for.
const LARGE_PAGE: usize = 40_000;
const SKILL: &str = include_str!("../../../../../../.claude/skills/calcpad-generator/SKILL.md");
const APP_WORKFLOW: &str = include_str!("app-workflow.md");
/// SKILL.md sections about working in the repo, which agents in the app can't use.
const REPO_ONLY: &[&str] = &["Language reference", "Working in the repo"];

#[derive(Deserialize)]
struct Manifest {
    version: String,
    pages: Vec<Page>,
}

#[derive(Deserialize)]
struct Page {
    topic: String,
    file: String,
    title: String,
    #[serde(default)]
    group: String,
    #[serde(default)]
    bytes: usize,
}

pub struct Reference {
    docs_dir: PathBuf,
    app_version: String,
}

impl Reference {
    pub fn new(docs_dir: PathBuf, app_version: String) -> Self {
        Self {
            docs_dir,
            app_version,
        }
    }

    pub fn lookup(&self, topic: Option<&str>, section: Option<&str>) -> Result<String, String> {
        let Some(topic) = topic.map(normalize).filter(|t| !t.is_empty()) else {
            return Ok(self.overview());
        };
        let text = self.page(&topic)?;
        match section.map(str::trim).filter(|s| !s.is_empty()) {
            Some(section) => find_section(&text, section).ok_or_else(|| {
                format!("No section \"{section}\" in {topic}.\n\n{}", outline(&text))
            }),
            None if text.len() > LARGE_PAGE => Ok(format!(
                "{topic} is {} KB. Call again with `section` set to one of these headings.\n\n{}",
                text.len() / 1024,
                outline(&text)
            )),
            None => Ok(text),
        }
    }

    fn read(&self, file: &str) -> Result<String, String> {
        std::fs::read_to_string(self.docs_dir.join(file))
            .map_err(|e| format!("Could not read {file}: {e}"))
    }

    fn manifest(&self) -> Option<Manifest> {
        let manifest: Manifest = serde_json::from_str(&self.read("manifest.json").ok()?).ok()?;
        if manifest.version != self.app_version {
            log::warn!(
                "Bundled agent docs are for {}, but the app is {}",
                manifest.version,
                self.app_version
            );
        }
        Some(manifest)
    }

    fn page(&self, topic: &str) -> Result<String, String> {
        if !is_topic(topic) {
            return Err(unknown(topic));
        }
        let manifest = self
            .manifest()
            .ok_or("The CalcpadCE docs are not bundled with this build.")?;
        let page = manifest
            .pages
            .iter()
            .find(|p| p.topic == topic)
            .ok_or_else(|| unknown(topic))?;
        self.read(&page.file)
    }

    fn overview(&self) -> String {
        let mut out = format!("{}\n\n{}", app_rules(SKILL), APP_WORKFLOW.trim());
        match self.manifest() {
            Some(manifest) => {
                out.push_str(&format!(
                    "\n\n# calcpad_reference topics\n\nCalcpadCE {} documentation, by `topic`:\n",
                    manifest.version
                ));
                let mut group = None;
                for page in &manifest.pages {
                    if group != Some(&page.group) {
                        group = Some(&page.group);
                        out.push_str(&format!("\n## {}\n\n", page.group));
                    }
                    out.push_str(&format!(
                        "- `{}` — {} ({} KB)\n",
                        page.topic,
                        page.title,
                        page.bytes.div_ceil(1024)
                    ));
                }
            }
            None => out.push_str("\n\nThe CalcpadCE docs are not bundled with this build.\n"),
        }
        out
    }
}

/// SKILL.md without its front matter or the sections about working in the repo.
fn app_rules(skill: &str) -> String {
    let text = strip_front_matter(skill);
    let mut out = text.to_string();
    for (level, title, start) in headings(text).into_iter().rev() {
        if level == 2 && REPO_ONLY.contains(&title.as_str()) {
            let end = headings(text)
                .into_iter()
                .find(|(l, _, o)| *o > start && *l <= 2)
                .map_or(text.len(), |(_, _, o)| o);
            out.replace_range(start..end, "");
        }
    }
    out.trim().to_string()
}

fn normalize(topic: &str) -> String {
    let topic = topic.trim().to_lowercase();
    topic.strip_suffix(".md").unwrap_or(&topic).to_string()
}

fn is_topic(topic: &str) -> bool {
    !topic.is_empty()
        && topic
            .chars()
            .all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-')
}

fn unknown(topic: &str) -> String {
    format!("Unknown topic \"{topic}\". Call calcpad_reference with no arguments to list topics.")
}

fn strip_front_matter(text: &str) -> &str {
    text.strip_prefix("---")
        .and_then(|rest| rest.find("\n---").map(|end| &rest[end + 4..]))
        .unwrap_or(text)
}

/// Headings outside code fences, as (level, text, byte offset).
fn headings(text: &str) -> Vec<(usize, String, usize)> {
    let mut out = Vec::new();
    let mut fenced = false;
    let mut offset = 0;
    for line in text.split_inclusive('\n') {
        let trimmed = line.trim_end();
        if trimmed.trim_start().starts_with("```") {
            fenced = !fenced;
        } else if !fenced {
            let level = trimmed.chars().take_while(|&c| c == '#').count();
            if (1..=6).contains(&level) && trimmed[level..].starts_with(' ') {
                let title: String = trimmed[level..]
                    .chars()
                    .filter(|c| !matches!(c, '*' | '`'))
                    .collect();
                out.push((level, title.trim().to_string(), offset));
            }
        }
        offset += line.len();
    }
    out
}

fn find_section(text: &str, query: &str) -> Option<String> {
    let query = query.trim_start_matches('#').trim().to_lowercase();
    let all = headings(text);
    let index = all
        .iter()
        .position(|(_, t, _)| t.to_lowercase() == query)
        .or_else(|| {
            all.iter()
                .position(|(_, t, _)| t.to_lowercase().contains(&query))
        })?;
    let (level, _, start) = all[index];
    let end = all[index + 1..]
        .iter()
        .find(|(l, _, _)| *l <= level)
        .map_or(text.len(), |(_, _, o)| *o);
    Some(text[start..end].trim_end().to_string())
}

fn outline(text: &str) -> String {
    headings(text)
        .into_iter()
        .filter(|(level, _, _)| *level <= 3)
        .map(|(level, title, _)| format!("{}- {title}", "  ".repeat(level.saturating_sub(1))))
        .collect::<Vec<_>>()
        .join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fixture(name: &str) -> Reference {
        let dir = std::env::temp_dir().join(format!("calcpad-reference-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(
            dir.join("units.md"),
            "# Units\n\n## Metric\n\nm, kg\n\n```\n## not a heading\n```\n\n### Length\n\nm\n\n## Imperial\n\nft\n",
        )
        .unwrap();
        std::fs::write(dir.join("stray.md"), "Not in the manifest.").unwrap();
        std::fs::write(
            dir.join("manifest.json"),
            r#"{"version":"1.0.0","pages":[{"topic":"units","file":"units.md","title":"Units","group":"Docs","bytes":80}]}"#,
        )
        .unwrap();
        Reference::new(dir, "1.0.0".into())
    }

    #[test]
    fn app_rules_drop_front_matter_and_repo_sections() {
        let skill = "---\nname: x\n---\n\nIntro.\n\n## Language reference\n\ndocs/\n\n## Non-negotiables\n\nUnits.\n\n## Working in the repo\n\n### Lint\n\nscript\n\n## Last\n\nKept.\n";
        assert_eq!(
            app_rules(skill),
            "Intro.\n\n## Non-negotiables\n\nUnits.\n\n## Last\n\nKept."
        );
    }

    #[test]
    fn real_skill_keeps_rules_and_drops_repo_workflow() {
        let rules = app_rules(SKILL);
        for kept in [
            "## Non-negotiables",
            "## Common mistakes",
            "## Reviewing the report",
        ] {
            assert!(rules.contains(kept), "{kept} missing");
        }
        assert!(!rules.contains("calcpad-server.sh"));
        assert!(!rules.contains("## Language reference"));
    }

    #[test]
    fn overview_has_rules_workflow_and_topics() {
        let text = fixture("overview").lookup(None, None).unwrap();
        assert!(text.contains("## Non-negotiables"));
        assert!(text.contains("propose_edit"));
        assert!(text.contains("- `units` — Units (1 KB)"));
    }

    #[test]
    fn pages_resolve_by_topic_from_the_manifest_only() {
        let r = fixture("pages");
        assert!(r
            .lookup(Some("Units.md"), None)
            .unwrap()
            .starts_with("# Units"));
        assert!(r.lookup(Some("stray"), None).is_err());
        assert!(r.lookup(Some("../manifest"), None).is_err());
    }

    #[test]
    fn sections_stop_at_the_next_peer_heading() {
        let r = fixture("sections");
        let metric = r.lookup(Some("units"), Some("metric")).unwrap();
        assert!(metric.starts_with("## Metric"));
        assert!(metric.contains("### Length"));
        assert!(!metric.contains("Imperial"));
        let missing = r.lookup(Some("units"), Some("SI")).unwrap_err();
        assert!(missing.contains("- Imperial"));
        assert!(!missing.contains("not a heading"));
    }

    #[test]
    fn missing_docs_still_serve_the_rules() {
        let r = Reference::new(
            std::env::temp_dir().join("calcpad-reference-none"),
            "1".into(),
        );
        assert!(r.lookup(None, None).unwrap().contains("not bundled"));
        assert!(r.lookup(Some("units"), None).is_err());
    }
}
