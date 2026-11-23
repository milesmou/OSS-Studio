use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Mutex;

use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use tauri::{AppHandle, Emitter, State};
use tokio_util::sync::CancellationToken;

use crate::models::{
    BucketProfile, CopyResult, CredentialInput, DeleteResult, DownloadResult, ObjectEntry,
    ObjectListResult, OssSettings, StoredCredential, TransferProgress, UploadBytes, UploadResult,
    WorkspaceState,
};
use crate::oss::{text_content_type, OssClient};
use crate::storage::{
    delete_credential as remove_credential, load_credential as read_credential, require_credential,
    save_credential as write_credential, WorkspaceStore,
};

#[derive(Default)]
pub struct AppState {
    transfers: Mutex<HashMap<String, CancellationToken>>,
}

impl AppState {
    fn begin(&self, task_id: &str) -> CancellationToken {
        let token = CancellationToken::new();
        let mut transfers = self
            .transfers
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if let Some(previous) = transfers.insert(task_id.to_string(), token.clone()) {
            previous.cancel();
        }
        token
    }

    fn finish(&self, task_id: &str) {
        let mut transfers = self
            .transfers
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        transfers.remove(task_id);
    }

    fn cancel(&self, task_id: &str) {
        let transfers = self
            .transfers
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if let Some(token) = transfers.get(task_id) {
            token.cancel();
        }
    }
}

struct TaskRegistration<'a> {
    state: &'a AppState,
    task_id: String,
}

impl Drop for TaskRegistration<'_> {
    fn drop(&mut self) {
        self.state.finish(&self.task_id);
    }
}

#[tauri::command]
pub fn load_workspace() -> WorkspaceState {
    WorkspaceStore::new().load()
}

#[tauri::command]
pub fn save_workspace(state: WorkspaceState) -> Result<(), String> {
    WorkspaceStore::new().save(&state)
}

#[tauri::command]
pub fn load_credential(bucket_id: String) -> Result<Option<StoredCredential>, String> {
    read_credential(&bucket_id)
}

#[tauri::command]
pub fn save_credential(bucket_id: String, credential: CredentialInput) -> Result<(), String> {
    write_credential(&bucket_id, &credential)
}

#[tauri::command]
pub fn delete_credential(bucket_id: String) -> Result<(), String> {
    remove_credential(&bucket_id)
}

fn client_for(bucket: BucketProfile, settings: OssSettings) -> Result<OssClient, String> {
    let credential = require_credential(&bucket.id)?;
    OssClient::new(bucket, credential, settings)
}

fn emit_progress(app: &AppHandle, task_id: &str, current: usize, total: usize, name: String) {
    emit_transfer_progress(app, task_id, current, total, name, None);
}

fn emit_transfer_progress(
    app: &AppHandle,
    task_id: &str,
    current: usize,
    total: usize,
    name: String,
    upload_bytes: Option<UploadBytes>,
) {
    let _ = app.emit(
        "transfer-progress",
        TransferProgress {
            task_id: task_id.to_string(),
            current,
            total,
            name,
            upload_bytes,
        },
    );
}

#[tauri::command]
pub async fn list_objects(
    bucket: BucketProfile,
    prefix: String,
    settings: OssSettings,
) -> Result<ObjectListResult, String> {
    let client = client_for(bucket, settings)?;
    client
        .list_objects(&prefix, &CancellationToken::new())
        .await
}

#[tauri::command]
pub async fn list_child_folders(
    bucket: BucketProfile,
    prefix: String,
    settings: OssSettings,
) -> Result<Vec<String>, String> {
    let client = client_for(bucket, settings)?;
    client
        .list_child_folders(&prefix, &CancellationToken::new())
        .await
}

#[tauri::command]
pub async fn create_folder(
    bucket: BucketProfile,
    key: String,
    settings: OssSettings,
) -> Result<(), String> {
    let client = client_for(bucket, settings)?;
    client.create_folder(&key, &CancellationToken::new()).await
}

#[tauri::command]
pub async fn upload_paths(
    app: AppHandle,
    state: State<'_, AppState>,
    task_id: String,
    bucket: BucketProfile,
    prefix: String,
    paths: Vec<String>,
    settings: OssSettings,
) -> Result<UploadResult, String> {
    let _registration = TaskRegistration {
        state: &state,
        task_id: task_id.clone(),
    };
    let token = state.begin(&task_id);
    let client = client_for(bucket, settings)?;
    let paths: Vec<PathBuf> = paths.into_iter().map(PathBuf::from).collect();
    client
        .upload_paths(paths, &prefix, token, move |current, total, name, bytes| {
            emit_transfer_progress(&app, &task_id, current, total, name, Some(bytes));
        })
        .await
        .map_err(canceled_error)
}

#[tauri::command]
pub async fn download_entries(
    app: AppHandle,
    state: State<'_, AppState>,
    task_id: String,
    bucket: BucketProfile,
    entries: Vec<ObjectEntry>,
    destination: String,
    settings: OssSettings,
) -> Result<DownloadResult, String> {
    let _registration = TaskRegistration {
        state: &state,
        task_id: task_id.clone(),
    };
    let token = state.begin(&task_id);
    let client = client_for(bucket, settings)?;
    let event_app = app.clone();
    let event_task = task_id.clone();
    client
        .download_entries(
            entries,
            &PathBuf::from(destination),
            token,
            move |current, total, name| {
                emit_progress(&event_app, &event_task, current, total, name);
            },
        )
        .await
        .map_err(canceled_error)
}

#[tauri::command]
pub async fn delete_entries(
    app: AppHandle,
    state: State<'_, AppState>,
    task_id: String,
    bucket: BucketProfile,
    entries: Vec<ObjectEntry>,
    settings: OssSettings,
) -> Result<DeleteResult, String> {
    let _registration = TaskRegistration {
        state: &state,
        task_id: task_id.clone(),
    };
    let token = state.begin(&task_id);
    let client = client_for(bucket, settings)?;
    let event_app = app.clone();
    let event_task = task_id.clone();
    client
        .delete_entries(entries, token, move |current, total, name| {
            emit_progress(&event_app, &event_task, current, total, name);
        })
        .await
        .map_err(canceled_error)
}

#[tauri::command]
pub async fn copy_entries(
    app: AppHandle,
    state: State<'_, AppState>,
    task_id: String,
    bucket: BucketProfile,
    entries: Vec<ObjectEntry>,
    target_prefix: String,
    settings: OssSettings,
) -> Result<CopyResult, String> {
    let _registration = TaskRegistration {
        state: &state,
        task_id: task_id.clone(),
    };
    let token = state.begin(&task_id);
    let client = client_for(bucket, settings)?;
    let event_app = app.clone();
    let event_task = task_id.clone();
    client
        .copy_entries(
            entries,
            &target_prefix,
            token,
            move |current, total, name| {
                emit_progress(&event_app, &event_task, current, total, name);
            },
        )
        .await
        .map_err(canceled_error)
}

#[tauri::command]
pub async fn move_entries(
    app: AppHandle,
    state: State<'_, AppState>,
    task_id: String,
    bucket: BucketProfile,
    entries: Vec<ObjectEntry>,
    target_prefix: String,
    settings: OssSettings,
) -> Result<CopyResult, String> {
    let _registration = TaskRegistration {
        state: &state,
        task_id: task_id.clone(),
    };
    let token = state.begin(&task_id);
    let client = client_for(bucket, settings)?;
    let event_app = app.clone();
    let event_task = task_id.clone();
    let copy_app = event_app.clone();
    let copy_task = event_task.clone();
    let result = client
        .copy_entries(
            entries.clone(),
            &target_prefix,
            token.clone(),
            move |current, total, name| {
                emit_progress(&copy_app, &copy_task, current, total, name);
            },
        )
        .await
        .map_err(canceled_error)?;
    client
        .delete_entries(entries, token, move |current, total, name| {
            emit_progress(&event_app, &event_task, current, total, name);
        })
        .await
        .map_err(canceled_error)?;
    Ok(result)
}

#[tauri::command]
pub async fn get_text_object(
    bucket: BucketProfile,
    key: String,
    maximum_bytes: usize,
    settings: OssSettings,
) -> Result<String, String> {
    let client = client_for(bucket, settings)?;
    client
        .get_text(&key, maximum_bytes, &CancellationToken::new())
        .await
}

#[tauri::command]
pub async fn get_object_bytes(
    bucket: BucketProfile,
    key: String,
    maximum_bytes: usize,
    settings: OssSettings,
) -> Result<String, String> {
    let client = client_for(bucket, settings)?;
    let bytes = client
        .get_bytes(&key, maximum_bytes, &CancellationToken::new())
        .await?;
    Ok(BASE64.encode(bytes))
}

#[tauri::command]
pub async fn put_text_object(
    bucket: BucketProfile,
    key: String,
    content: String,
    settings: OssSettings,
) -> Result<(), String> {
    let client = client_for(bucket, settings)?;
    client
        .put_bytes(
            &key,
            content.into_bytes(),
            text_content_type(&key),
            &CancellationToken::new(),
        )
        .await
}

#[tauri::command]
pub fn object_address(bucket: BucketProfile, key: String) -> Result<String, String> {
    let credential = StoredCredential {
        access_key_id: String::new(),
        access_key_secret: String::new(),
    };
    OssClient::new(bucket, credential, OssSettings::default())?.address(&key)
}

#[tauri::command]
pub fn cancel_task(state: State<'_, AppState>, task_id: String) {
    state.cancel(&task_id);
}

fn canceled_error(error: String) -> String {
    if error == crate::oss::CANCELED {
        "CANCELED".into()
    } else {
        error
    }
}
