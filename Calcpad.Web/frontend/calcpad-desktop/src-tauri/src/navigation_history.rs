use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, Manager, State};

use crate::FocusedWindow;

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct Position {
    line_number: u32,
    column: u32,
}

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
struct Selection {
    selection_start_line_number: u32,
    selection_start_column: u32,
    position_line_number: u32,
    position_column: u32,
}

#[derive(Clone, Debug, Deserialize, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NavigationLocation {
    #[serde(default)]
    window_id: String,
    group_id: String,
    tab_id: String,
    position: Position,
    selection: Option<Selection>,
}

impl NavigationLocation {
    fn line(&self) -> u32 {
        self.selection
            .as_ref()
            .map_or(self.position.line_number, |selection| {
                selection
                    .selection_start_line_number
                    .min(selection.position_line_number)
            })
    }

    fn same_tab(&self, other: &Self) -> bool {
        self.window_id == other.window_id
            && self.group_id == other.group_id
            && self.tab_id == other.tab_id
    }
}

#[derive(Clone, Serialize)]
struct NavigationRequest {
    id: u64,
    location: NavigationLocation,
}

struct PendingNavigation {
    request: NavigationRequest,
    index: usize,
    source_window: String,
}

#[derive(Default)]
struct NavigationHistory {
    entries: Vec<NavigationLocation>,
    index: Option<usize>,
    next_request: u64,
    pending: Option<PendingNavigation>,
}

impl NavigationHistory {
    fn record(&mut self, location: NavigationLocation, jump: bool) {
        if self.pending.is_some() {
            return;
        }
        if let Some(index) = self.index {
            let current = &self.entries[index];
            if current.same_tab(&location)
                && (current.line() == location.line()
                    || (!jump && current.line().abs_diff(location.line()) < 10))
            {
                self.entries[index] = location;
                return;
            }
            self.entries.truncate(index + 1);
        } else {
            self.entries.clear();
        }
        self.entries.push(location);
        if self.entries.len() > 50 {
            self.entries.remove(0);
        }
        self.index = Some(self.entries.len() - 1);
    }

    fn begin(&mut self, direction: i32, source_window: String) -> Option<NavigationRequest> {
        if self.pending.is_some() {
            return None;
        }
        assert!(direction == -1 || direction == 1);
        let index = self.index?.checked_add_signed(direction as isize)?;
        let location = self.entries.get(index)?.clone();
        self.next_request += 1;
        let request = NavigationRequest {
            id: self.next_request,
            location,
        };
        self.pending = Some(PendingNavigation {
            request: request.clone(),
            index,
            source_window,
        });
        Some(request)
    }

    fn finish(&mut self, id: u64, window_id: &str, accepted: bool) -> Option<String> {
        let pending = self.pending.as_ref()?;
        if pending.request.id != id || pending.request.location.window_id != window_id {
            return None;
        }
        let pending = self.pending.take().unwrap();
        if accepted {
            self.index = Some(pending.index);
            Some(pending.request.location.window_id)
        } else {
            Some(pending.source_window)
        }
    }

    fn retain(&mut self, keep: impl Fn(&NavigationLocation) -> bool) {
        self.index = self.index.and_then(|index| {
            self.entries[..=index]
                .iter()
                .filter(|entry| keep(entry))
                .count()
                .checked_sub(1)
        });
        if let Some(pending) = &mut self.pending {
            if keep(&pending.request.location) {
                pending.index = self.entries[..=pending.index]
                    .iter()
                    .filter(|entry| keep(entry))
                    .count()
                    - 1;
            } else {
                self.pending = None;
            }
        }
        self.entries.retain(keep);
    }
}

#[derive(Default)]
pub struct NavigationHistoryState(Mutex<NavigationHistory>);

impl NavigationHistoryState {
    pub fn remove_window(&self, window_id: &str) {
        self.0
            .lock()
            .expect("navigation history mutex poisoned")
            .retain(|location| location.window_id != window_id);
    }
}

#[tauri::command]
pub fn record_navigation(
    window: tauri::Window,
    state: State<'_, NavigationHistoryState>,
    focused: State<'_, FocusedWindow>,
    mut location: NavigationLocation,
    jump: bool,
) {
    if *focused.0.lock().expect("focused window mutex poisoned") != window.label() {
        return;
    }
    location.window_id = window.label().to_string();
    state
        .0
        .lock()
        .expect("navigation history mutex poisoned")
        .record(location, jump);
}

#[tauri::command]
pub fn remove_navigation(
    window: tauri::Window,
    state: State<'_, NavigationHistoryState>,
    group_id: String,
    tab_id: String,
) {
    state
        .0
        .lock()
        .expect("navigation history mutex poisoned")
        .retain(|location| {
            location.window_id != window.label()
                || location.group_id != group_id
                || location.tab_id != tab_id
        });
}

#[tauri::command]
pub fn navigate_history(
    app: AppHandle,
    window: tauri::Window,
    state: State<'_, NavigationHistoryState>,
    direction: i32,
) -> Result<(), String> {
    let request = state
        .0
        .lock()
        .expect("navigation history mutex poisoned")
        .begin(direction, window.label().to_string());
    if let Some(request) = request {
        if let Err(error) = app.emit_to(
            &request.location.window_id,
            "navigate-history",
            request.clone(),
        ) {
            state
                .0
                .lock()
                .expect("navigation history mutex poisoned")
                .finish(request.id, &request.location.window_id, false);
            return Err(error.to_string());
        }
    }
    Ok(())
}

#[tauri::command]
pub async fn complete_navigation(
    app: AppHandle,
    window: tauri::Window,
    state: State<'_, NavigationHistoryState>,
    id: u64,
    accepted: bool,
) -> Result<(), String> {
    let focus = state
        .0
        .lock()
        .expect("navigation history mutex poisoned")
        .finish(id, window.label(), accepted);
    if let Some(window) = focus.and_then(|label| app.get_webview_window(&label)) {
        window.set_focus().map_err(|error| error.to_string())?;
        window
            .as_ref()
            .set_focus()
            .map_err(|error| error.to_string())?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn location(window: &str, line: u32) -> NavigationLocation {
        NavigationLocation {
            window_id: window.into(),
            group_id: "g0".into(),
            tab_id: "g0-tab-1".into(),
            position: Position {
                line_number: line,
                column: 1,
            },
            selection: None,
        }
    }

    fn go(history: &mut NavigationHistory, direction: i32) -> NavigationLocation {
        let request = history.begin(direction, "source".into()).unwrap();
        history.finish(request.id, &request.location.window_id, true);
        request.location
    }

    #[test]
    fn windows_share_history_even_with_identical_tab_ids() {
        let mut history = NavigationHistory::default();
        history.record(location("main", 5), false);
        history.record(location("main-2", 6), false);
        history.record(location("main-3", 7), false);
        assert_eq!(go(&mut history, -1), location("main-2", 6));
        assert_eq!(go(&mut history, -1), location("main", 5));
        assert_eq!(go(&mut history, 1), location("main-2", 6));
        assert_eq!(go(&mut history, 1), location("main-3", 7));
    }

    #[test]
    fn nearby_moves_preserve_forward_history_and_explicit_jumps_branch() {
        let mut history = NavigationHistory::default();
        history.record(location("main", 5), false);
        history.record(location("main-2", 20), false);
        go(&mut history, -1);
        history.record(location("main", 8), false);
        assert_eq!(go(&mut history, 1), location("main-2", 20));
        assert_eq!(go(&mut history, -1), location("main", 8));
        history.record(location("main", 9), true);
        assert!(history.begin(1, "main".into()).is_none());
        assert_eq!(go(&mut history, -1), location("main", 8));
    }

    #[test]
    fn cancelled_navigation_keeps_the_index_and_focus_events_do_not_add_stops() {
        let mut history = NavigationHistory::default();
        history.record(location("main", 5), false);
        history.record(location("main-2", 20), false);
        let request = history.begin(-1, "main-2".into()).unwrap();
        history.record(location("main", 1), false);
        assert!(history.begin(-1, "main-2".into()).is_none());
        assert_eq!(
            history.finish(request.id, "main", false),
            Some("main-2".into())
        );
        assert_eq!(go(&mut history, -1), location("main", 5));
        assert_eq!(go(&mut history, 1), location("main-2", 20));
    }

    #[test]
    fn closing_windows_removes_their_stops_and_rebases_pending_navigation() {
        let mut history = NavigationHistory::default();
        history.record(location("main", 5), false);
        history.record(location("main-2", 20), false);
        history.record(location("main-3", 30), false);
        let request = history.begin(-1, "main-3".into()).unwrap();
        history.retain(|entry| entry.window_id != "main");
        history.finish(request.id, "main-2", true);
        assert_eq!(go(&mut history, 1), location("main-3", 30));
        history.retain(|entry| entry.window_id != "main-2");
        assert!(history.begin(-1, "main-3".into()).is_none());
    }

    #[test]
    fn removing_a_pending_destination_allows_further_navigation() {
        let mut history = NavigationHistory::default();
        history.record(location("main", 5), false);
        history.record(location("main-2", 20), false);
        let request = history.begin(-1, "main-2".into()).unwrap();
        history.retain(|entry| entry.window_id != "main");
        assert_eq!(history.finish(request.id, "main", true), None);
        history.record(location("main-2", 40), false);
        assert_eq!(go(&mut history, -1), location("main-2", 20));
    }
}
