//! Saved chat conversations: `<id>.json` (opaque, owned by the frontend) plus `<id>.meta.json`.

use std::path::PathBuf;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HistoryMeta {
    pub id: String,
    pub title: String,
    pub created_at: u64,
    pub updated_at: u64,
}

fn history_dir(app: &AppHandle) -> Result<PathBuf, String> {
    Ok(app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("agent")
        .join("conversations"))
}

fn paths(app: &AppHandle, id: &str) -> Result<(PathBuf, PathBuf), String> {
    crate::validate_draft_id(id).map_err(|_| "invalid conversation id".to_string())?;
    let dir = history_dir(app)?;
    Ok((dir.join(format!("{id}.json")), dir.join(format!("{id}.meta.json"))))
}

#[tauri::command]
pub fn agent_history_list(app: AppHandle) -> Result<Vec<HistoryMeta>, String> {
    let Ok(entries) = std::fs::read_dir(history_dir(&app)?) else {
        return Ok(vec![]);
    };
    let mut out: Vec<HistoryMeta> = entries
        .flatten()
        .filter(|e| e.file_name().to_string_lossy().ends_with(".meta.json"))
        .filter_map(|e| std::fs::read(e.path()).ok())
        .filter_map(|bytes| serde_json::from_slice(&bytes).ok())
        .collect();
    out.sort_by_key(|m| std::cmp::Reverse(m.updated_at));
    Ok(out)
}

#[tauri::command]
pub fn agent_history_read(app: AppHandle, id: String) -> Result<Option<String>, String> {
    let (data, _) = paths(&app, &id)?;
    match std::fs::read_to_string(data) {
        Ok(text) => Ok(Some(text)),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

#[tauri::command]
pub fn agent_history_write(app: AppHandle, meta: HistoryMeta, data: String) -> Result<(), String> {
    let (data_path, meta_path) = paths(&app, &meta.id)?;
    std::fs::create_dir_all(history_dir(&app)?).map_err(|e| e.to_string())?;
    let meta_json = serde_json::to_vec_pretty(&meta).map_err(|e| e.to_string())?;
    crate::atomic_write(&data_path, data.as_bytes()).map_err(|e| e.to_string())?;
    crate::atomic_write(&meta_path, &meta_json).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn agent_history_delete(app: AppHandle, id: String) -> Result<(), String> {
    let (data, meta) = paths(&app, &id)?;
    for path in [data, meta] {
        match std::fs::remove_file(path) {
            Ok(()) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(e.to_string()),
        }
    }
    Ok(())
}
