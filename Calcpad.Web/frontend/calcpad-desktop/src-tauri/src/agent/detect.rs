//! Finds agent CLIs, including common install folders missing from a GUI launch's PATH.

use std::path::{Path, PathBuf};

pub fn home() -> Option<PathBuf> {
    std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from)
}

/// File names for a command: `.exe` and npm's `.cmd` shim on Windows.
fn file_names(name: &str) -> Vec<String> {
    if cfg!(windows) {
        vec![format!("{name}.exe"), format!("{name}.cmd")]
    } else {
        vec![name.to_string()]
    }
}

/// The newest subfolder of `dir` whose name starts with `prefix`.
pub fn latest_child(dir: &Path, prefix: &str) -> Option<PathBuf> {
    let mut dirs: Vec<PathBuf> = std::fs::read_dir(dir)
        .ok()?
        .flatten()
        .filter(|e| e.file_name().to_string_lossy().starts_with(prefix))
        .map(|e| e.path())
        .collect();
    dirs.sort();
    dirs.pop()
}

fn common_dirs(home: Option<&Path>) -> Vec<PathBuf> {
    let mut dirs = vec![];
    if let Some(home) = home {
        for rel in [".local/bin", ".npm-global/bin", ".bun/bin", ".volta/bin", ".local/share/pnpm", "Library/pnpm"] {
            dirs.push(home.join(rel));
        }
        if let Some(node) = latest_child(&home.join(".nvm").join("versions").join("node"), "v") {
            dirs.push(node.join("bin"));
        }
    }
    if cfg!(windows) {
        for var in ["APPDATA", "LOCALAPPDATA"] {
            if let Some(base) = std::env::var_os(var).map(PathBuf::from) {
                dirs.push(base.join("npm"));
                dirs.push(base.join("pnpm"));
            }
        }
    } else {
        dirs.push(PathBuf::from("/usr/local/bin"));
        dirs.push(PathBuf::from("/opt/homebrew/bin"));
    }
    dirs
}

/// What `extra` may hold besides install folders.
pub enum Extra {
    Dir(PathBuf),
    /// An exact file, such as an editor extension's bundled binary.
    File(PathBuf),
}

/// Candidate paths in priority order: PATH, then `extra`, then common install folders.
pub fn candidates(name: &str, path_var: Option<&str>, extra: Vec<Extra>, home: Option<&Path>) -> Vec<PathBuf> {
    let names = file_names(name);
    let in_dir = |dir: &Path| names.iter().map(|n| dir.join(n)).collect::<Vec<_>>();
    let mut out: Vec<PathBuf> = path_var
        .map(|p| std::env::split_paths(p).flat_map(|d| in_dir(&d)).collect())
        .unwrap_or_default();
    for item in extra {
        match item {
            Extra::Dir(dir) => out.extend(in_dir(&dir)),
            Extra::File(file) => out.push(file),
        }
    }
    out.extend(common_dirs(home).iter().flat_map(|d| in_dir(d)));
    out
}

/// A configured path wins; otherwise the first candidate that exists.
pub fn find(configured: Option<&str>, name: &str, extra: Vec<Extra>) -> Option<PathBuf> {
    if let Some(path) = configured.map(str::trim).filter(|p| !p.is_empty()) {
        return Some(PathBuf::from(path));
    }
    let path_var = std::env::var("PATH").ok();
    candidates(name, path_var.as_deref(), extra, home().as_deref())
        .into_iter()
        .find(|p| p.is_file())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn exe(name: &str) -> String {
        file_names(name).remove(0)
    }

    #[test]
    fn path_first_then_extras_then_common() {
        let path = std::env::join_paths([Path::new("/x/bin")]).unwrap();
        let found = candidates(
            "tool",
            path.to_str(),
            vec![Extra::File(PathBuf::from("/ext/bundled/tool.bin")), Extra::Dir(PathBuf::from("/ext/dir"))],
            Some(Path::new("/home/u")),
        );
        assert_eq!(found[0], Path::new("/x/bin").join(exe("tool")));
        let at = |p: PathBuf| found.iter().position(|f| *f == p).unwrap();
        let bundled = at(PathBuf::from("/ext/bundled/tool.bin"));
        let extra_dir = at(Path::new("/ext/dir").join(exe("tool")));
        let local = at(Path::new("/home/u/.local/bin").join(exe("tool")));
        assert!(0 < bundled && bundled < extra_dir && extra_dir < local);
    }

    #[test]
    fn configured_path_wins_even_if_missing() {
        assert_eq!(find(Some(" /opt/x/tool "), "tool", vec![]), Some(PathBuf::from("/opt/x/tool")));
        assert_eq!(find(Some(""), "definitely-not-a-real-tool-xyz", vec![]), None);
    }
}
