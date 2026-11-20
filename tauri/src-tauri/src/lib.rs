mod commands;
mod models;
mod oss;
mod storage;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(commands::AppState::default())
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            use tauri::Manager;
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.unminimize();
                let _ = window.show();
                let _ = window.set_focus();
            }
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .invoke_handler(tauri::generate_handler![
            commands::load_workspace,
            commands::save_workspace,
            commands::load_credential,
            commands::save_credential,
            commands::delete_credential,
            commands::list_objects,
            commands::list_child_folders,
            commands::create_folder,
            commands::upload_paths,
            commands::download_entries,
            commands::delete_entries,
            commands::copy_entries,
            commands::move_entries,
            commands::get_text_object,
            commands::get_object_bytes,
            commands::put_text_object,
            commands::object_address,
            commands::cancel_task,
        ])
        .run(tauri::generate_context!())
        .expect("error while running OSS Studio");
}
