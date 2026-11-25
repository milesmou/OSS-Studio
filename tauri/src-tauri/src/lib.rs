mod commands;
mod models;
mod oss;
mod storage;

use tauri::{webview::Color, Theme, WebviewWindowBuilder};

fn system_uses_dark_theme() -> bool {
    #[cfg(windows)]
    {
        windows_registry::CURRENT_USER
            .open(r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            .and_then(|key| key.get_u32("AppsUseLightTheme"))
            .is_ok_and(|light| light == 0)
    }
    #[cfg(not(windows))]
    {
        false
    }
}

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
        .setup(|app| {
            let workspace = storage::WorkspaceStore::new().load();
            let window_theme = match workspace.theme_mode.as_str() {
                "System" => None,
                "Dark" => Some(Theme::Dark),
                _ => Some(Theme::Light),
            };
            let dark = match window_theme {
                Some(theme) => theme == Theme::Dark,
                None => system_uses_dark_theme(),
            };
            // Match the --window colors in styles.css before the page loads.
            let (theme, background) = if dark {
                ("dark", Color(9, 20, 22, 255))
            } else {
                ("light", Color(234, 241, 237, 255))
            };
            let startup_script = format!(
                r#"(() => {{
                    const applyTheme = () => {{
                        document.documentElement.dataset.theme = '{theme}';
                    }};
                    if (document.documentElement) {{
                        applyTheme();
                    }} else {{
                        const observer = new MutationObserver(() => {{
                            if (!document.documentElement) return;
                            observer.disconnect();
                            applyTheme();
                        }});
                        observer.observe(document, {{ childList: true }});
                    }}
                }})();"#
            );

            WebviewWindowBuilder::from_config(app, &app.config().app.windows[0])?
                .theme(window_theme)
                .background_color(background)
                .initialization_script(startup_script)
                .build()?;
            Ok(())
        })
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
