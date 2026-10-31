use std::cell::RefCell;
use std::path::PathBuf;

use gtk::prelude::*;
use serde::Deserialize;
use tauri::Manager;
use tauri_plugin_fs::FsExt;

#[derive(Deserialize)]
pub struct Filter {
    name: String,
    extensions: Vec<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Options {
    title: Option<String>,
    default_path: Option<PathBuf>,
    #[serde(default)]
    filters: Vec<Filter>,
    #[serde(default)]
    directory: bool,
    #[serde(default)]
    multiple: bool,
    #[serde(default)]
    recursive: bool,
}

#[tauri::command]
pub async fn linux_file_dialog(
    window: tauri::WebviewWindow,
    options: Options,
    save: bool,
) -> Result<Option<Vec<PathBuf>>, String> {
    let directory = options.directory;
    let recursive = options.recursive;
    let (sender, receiver) = tokio::sync::oneshot::channel();
    window
        .with_webview(move |platform| {
            let parent = platform
                .inner()
                .toplevel()
                .unwrap()
                .downcast::<gtk::Window>()
                .unwrap();
            let action = if save {
                gtk::FileChooserAction::Save
            } else if directory {
                gtk::FileChooserAction::SelectFolder
            } else {
                gtk::FileChooserAction::Open
            };
            let dialog = gtk::FileChooserNative::new(
                options.title.as_deref(),
                Some(&parent),
                action,
                None,
                None,
            );
            dialog.set_modal(true);
            dialog.set_select_multiple(options.multiple);
            dialog.set_do_overwrite_confirmation(save);
            if let Some(path) = options.default_path {
                if path.is_dir() {
                    dialog.set_current_folder(path);
                } else if save && !path.exists() {
                    if let Some(parent) = path
                        .parent()
                        .filter(|parent| !parent.as_os_str().is_empty())
                    {
                        dialog.set_current_folder(parent);
                    }
                    dialog.set_current_name(path.file_name().unwrap().to_str().unwrap());
                } else {
                    dialog.set_filename(path);
                }
            }
            for filter in options.filters {
                let native_filter = gtk::FileFilter::new();
                native_filter.set_name(Some(&filter.name));
                for extension in filter.extensions {
                    native_filter.add_pattern(&if extension == "*" {
                        "*".to_string()
                    } else {
                        format!("*.{extension}")
                    });
                }
                dialog.add_filter(native_filter);
            }
            gtk::glib::MainContext::default().spawn_local(async move {
                let (response_sender, response_receiver) = tokio::sync::oneshot::channel();
                let response_sender = RefCell::new(Some(response_sender));
                dialog.connect_response(move |_, response| {
                    response_sender
                        .borrow_mut()
                        .take()
                        .unwrap()
                        .send(response)
                        .unwrap();
                });
                dialog.show();
                let response = response_receiver.await.unwrap();
                let paths = (response == gtk::ResponseType::Accept).then(|| dialog.filenames());
                dialog.destroy();
                let _ = sender.send(paths);
            });
        })
        .map_err(|error| error.to_string())?;
    let paths = receiver.await.map_err(|error| error.to_string())?;
    if let Some(paths) = &paths {
        let scope = window.state::<tauri::scope::Scopes>();
        for path in paths {
            if directory {
                window
                    .fs_scope()
                    .allow_directory(path, recursive)
                    .map_err(|error| error.to_string())?;
                scope
                    .allow_directory(path, recursive)
                    .map_err(|error| error.to_string())?;
            } else {
                window
                    .fs_scope()
                    .allow_file(path)
                    .map_err(|error| error.to_string())?;
                scope.allow_file(path).map_err(|error| error.to_string())?;
            }
        }
    }
    Ok(paths)
}
