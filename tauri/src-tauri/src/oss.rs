use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use base64::engine::general_purpose::STANDARD as BASE64;
use base64::Engine;
use chrono::{DateTime, Utc};
use futures_util::stream::{self, StreamExt};
use hmac::{Hmac, Mac};
use percent_encoding::{utf8_percent_encode, AsciiSet, NON_ALPHANUMERIC};
use quick_xml::de::from_str;
use reqwest::header::{HeaderMap, HeaderValue, CONTENT_LENGTH, CONTENT_TYPE, DATE, HOST};
use reqwest::{Method, Response, StatusCode};
use serde::Deserialize;
use sha1::Sha1;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::sync::Semaphore;
use tokio_util::io::ReaderStream;
use tokio_util::sync::CancellationToken;
use walkdir::WalkDir;

use crate::models::{
    normalize_prefix, BucketProfile, CopyResult, DeleteResult, DownloadResult, ObjectEntry,
    ObjectListResult, OssSettings, StoredCredential, UploadBytes, UploadResult,
};

const MAX_KEYS: usize = 999;
pub(crate) const CANCELED: &str = "CANCELED";
const URI_SET: &AsciiSet = &NON_ALPHANUMERIC
    .remove(b'-')
    .remove(b'_')
    .remove(b'.')
    .remove(b'~');
const SIGNED_SUBRESOURCES: &[&str] = &[
    "acl",
    "append",
    "bucketInfo",
    "callback",
    "callback-var",
    "cname",
    "comp",
    "continuation-token",
    "cors",
    "delete",
    "endTime",
    "img",
    "lifecycle",
    "live",
    "location",
    "logging",
    "objectMeta",
    "partNumber",
    "position",
    "qos",
    "referer",
    "region",
    "replication",
    "replicationLocation",
    "replicationProgress",
    "restore",
    "security-token",
    "startTime",
    "status",
    "style",
    "styleName",
    "symlink",
    "tagging",
    "transferAcceleration",
    "uploadId",
    "uploads",
    "versionId",
    "vod",
    "website",
    "withHashContext",
    "worm",
    "wormExtend",
    "wormId",
    "x-oss-ac-forward-allow",
    "x-oss-ac-source-ip",
    "x-oss-ac-subnet-mask",
    "x-oss-ac-vpc-id",
    "x-oss-enable-md5",
    "x-oss-enable-sha1",
    "x-oss-enable-sha256",
    "x-oss-hash-ctx",
    "x-oss-md5-ctx",
    "x-oss-process",
];

#[derive(Clone)]
pub struct OssClient {
    bucket: BucketProfile,
    credential: StoredCredential,
    settings: OssSettings,
    http: reqwest::Client,
}

#[derive(Clone)]
enum RequestBody {
    Empty,
    Bytes(Vec<u8>),
    File {
        path: PathBuf,
        size: u64,
        on_bytes: Arc<dyn Fn(u64) + Send + Sync>,
    },
}

struct UploadProgress {
    bytes: UploadBytes,
    file_bytes: Vec<u64>,
    completed: usize,
    last_update: Instant,
}

impl RequestBody {
    fn len(&self) -> Option<u64> {
        match self {
            RequestBody::Empty => Some(0),
            RequestBody::Bytes(bytes) => Some(bytes.len() as u64),
            RequestBody::File { size, .. } => Some(*size),
        }
    }
}

#[derive(Clone, Debug)]
pub struct OssFailure {
    pub status: u16,
    pub code: String,
    pub message: String,
    pub endpoint: Option<String>,
}

impl std::fmt::Display for OssFailure {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        if self.status > 0 && !self.code.is_empty() {
            write!(formatter, "{} {}: {}", self.status, self.code, self.message)
        } else if self.code.is_empty() {
            write!(formatter, "{}", self.message)
        } else {
            write!(formatter, "{}: {}", self.code, self.message)
        }
    }
}

impl OssClient {
    pub fn new(
        bucket: BucketProfile,
        credential: StoredCredential,
        settings: OssSettings,
    ) -> Result<Self, String> {
        if bucket.name.trim().is_empty() {
            return Err("Bucket 名称不能为空".into());
        }
        if bucket.endpoint.trim().is_empty() {
            return Err("资源桶 Endpoint 不能为空".into());
        }
        let settings = settings.normalized();
        let timeout = Duration::from_secs(settings.request_timeout_seconds);
        let http = reqwest::Client::builder()
            .connect_timeout(timeout)
            .timeout(timeout)
            .user_agent("OSS-Studio-Tauri/1.0")
            .build()
            .map_err(|error| error.to_string())?;
        Ok(Self {
            bucket,
            credential,
            settings,
            http,
        })
    }

    pub fn address(&self, key: &str) -> Result<String, String> {
        let endpoint = normalize_endpoint(&self.bucket.endpoint);
        if endpoint.is_empty() {
            return Err("资源桶 Endpoint 不能为空".into());
        }
        let host = self.host_for(&endpoint);
        let scheme = if self.bucket.use_https {
            "https"
        } else {
            "http"
        };
        let encoded = uri_encode(key.trim_start_matches('/'), false);
        Ok(format!("{scheme}://{host}/{encoded}"))
    }

    fn host_for(&self, endpoint: &str) -> String {
        let endpoint = normalize_endpoint(endpoint);
        if self.bucket.endpoint_mode.eq_ignore_ascii_case("Cname") {
            endpoint
        } else {
            format!("{}.{}", self.bucket.name, endpoint)
        }
    }

    pub async fn list_objects(
        &self,
        prefix: &str,
        token: &CancellationToken,
    ) -> Result<ObjectListResult, String> {
        let requested_prefix = normalize_prefix(prefix);
        match self
            .list_objects_with_endpoint(&self.bucket.endpoint, &requested_prefix, token)
            .await
        {
            Ok(mut result) => {
                result.endpoint = self.bucket.endpoint.clone();
                result.region_code = self.bucket.region_code.clone();
                Ok(result)
            }
            Err((failure, objects)) => {
                let Some(suggested) = failure
                    .endpoint
                    .as_deref()
                    .and_then(|endpoint| redirect_region(endpoint, &self.bucket.endpoint))
                else {
                    return Err(failure.to_string());
                };
                let (endpoint, region_code) = suggested;
                let mut corrected = self.clone();
                corrected.bucket.endpoint = endpoint.clone();
                corrected.bucket.region_code = region_code.clone();
                corrected
                    .list_objects_with_endpoint(&endpoint, &requested_prefix, token)
                    .await
                    .map(|mut result| {
                        result.endpoint = endpoint;
                        result.region_code = region_code;
                        result.endpoint_corrected = true;
                        result
                    })
                    .map_err(|(error, _)| {
                        if objects.is_empty() {
                            error.to_string()
                        } else {
                            format!("{}（已尝试自动修正 Endpoint）", error)
                        }
                    })
            }
        }
    }

    async fn list_objects_with_endpoint(
        &self,
        endpoint: &str,
        prefix: &str,
        token: &CancellationToken,
    ) -> Result<ObjectListResult, (OssFailure, Vec<ObjectEntry>)> {
        let host = self.host_for(endpoint);
        let mut entries = Vec::new();
        let mut continuation_token: Option<String> = None;
        loop {
            let mut query = vec![
                ("delimiter".to_string(), "/".to_string()),
                ("list-type".to_string(), "2".to_string()),
                ("max-keys".to_string(), MAX_KEYS.to_string()),
                ("prefix".to_string(), prefix.to_string()),
            ];
            if let Some(value) = &continuation_token {
                query.push(("continuation-token".to_string(), value.clone()));
            }
            let response = self
                .send(
                    Method::GET,
                    endpoint,
                    "",
                    &query,
                    RequestBody::Empty,
                    token,
                    Some(""),
                )
                .await?;
            let bytes = response
                .bytes()
                .await
                .map_err(|error| (network_failure(error, &host), Vec::new()))?;
            let page: ListBucketResult = from_str(&String::from_utf8_lossy(&bytes))
                .map_err(|error| (parse_failure_detail(error), Vec::new()))?;

            for common in page.common_prefixes {
                let key = common.prefix;
                let name = relative_name(&key, prefix)
                    .trim_end_matches('/')
                    .to_string();
                if !name.is_empty() {
                    entries.push(ObjectEntry {
                        name,
                        is_folder: true,
                        size: "—".into(),
                        storage_class: "—".into(),
                        last_modified: None,
                        key,
                        size_bytes: 0,
                    });
                }
            }
            for item in page.contents {
                if item.key.is_empty() || item.key == prefix {
                    continue;
                }
                let name = relative_name(&item.key, prefix);
                if name.is_empty() {
                    continue;
                }
                entries.push(ObjectEntry {
                    name,
                    is_folder: false,
                    size: format_size(item.size),
                    storage_class: if item.storage_class.is_empty() {
                        "Standard".into()
                    } else {
                        item.storage_class
                    },
                    last_modified: item
                        .last_modified
                        .and_then(|value| DateTime::parse_from_rfc3339(&value).ok())
                        .map(|value| value.with_timezone(&Utc).to_rfc3339()),
                    key: item.key,
                    size_bytes: item.size,
                });
            }

            if page.is_truncated {
                continuation_token = page.next_continuation_token;
                if continuation_token.is_none() {
                    break;
                }
            } else {
                break;
            }
        }

        let mut seen = HashSet::new();
        entries.retain(|entry| seen.insert(entry.key.clone()));
        entries.sort_by(|left, right| {
            right
                .is_folder
                .cmp(&left.is_folder)
                .then_with(|| left.name.to_lowercase().cmp(&right.name.to_lowercase()))
        });
        Ok(ObjectListResult {
            objects: entries,
            endpoint: String::new(),
            region_code: String::new(),
            endpoint_corrected: false,
        })
    }

    pub async fn list_child_folders(
        &self,
        prefix: &str,
        token: &CancellationToken,
    ) -> Result<Vec<String>, String> {
        let prefix = normalize_prefix(prefix);
        let mut continuation_token: Option<String> = None;
        let mut folders = HashSet::new();
        loop {
            let mut query = vec![
                ("delimiter".to_string(), "/".to_string()),
                ("list-type".to_string(), "2".to_string()),
                ("max-keys".to_string(), MAX_KEYS.to_string()),
                ("prefix".to_string(), prefix.clone()),
            ];
            if let Some(value) = &continuation_token {
                query.push(("continuation-token".to_string(), value.clone()));
            }
            let response = self
                .send(
                    Method::GET,
                    &self.bucket.endpoint,
                    "",
                    &query,
                    RequestBody::Empty,
                    token,
                    Some(""),
                )
                .await
                .map_err(|(error, _)| error.to_string())?;
            let bytes = response.bytes().await.map_err(|error| error.to_string())?;
            let page: ListBucketResult =
                from_str(&String::from_utf8_lossy(&bytes)).map_err(|error| error.to_string())?;
            for common in page.common_prefixes {
                folders.insert(common.prefix);
            }
            if !page.is_truncated {
                break;
            }
            continuation_token = page.next_continuation_token;
            if continuation_token.is_none() {
                break;
            }
        }
        let mut result: Vec<String> = folders.into_iter().collect();
        result.sort_by_key(|value| value.to_lowercase());
        Ok(result)
    }

    pub async fn list_all_keys(
        &self,
        prefix: &str,
        token: &CancellationToken,
    ) -> Result<Vec<ObjectEntry>, String> {
        let prefix = if prefix.is_empty() || prefix.ends_with('/') {
            prefix.to_string()
        } else {
            format!("{prefix}/")
        };
        let mut continuation_token: Option<String> = None;
        let mut entries = Vec::new();
        loop {
            let mut query = vec![
                ("list-type".to_string(), "2".to_string()),
                ("max-keys".to_string(), MAX_KEYS.to_string()),
                ("prefix".to_string(), prefix.clone()),
            ];
            if let Some(value) = &continuation_token {
                query.push(("continuation-token".to_string(), value.clone()));
            }
            let response = self
                .send(
                    Method::GET,
                    &self.bucket.endpoint,
                    "",
                    &query,
                    RequestBody::Empty,
                    token,
                    Some(""),
                )
                .await
                .map_err(|(error, _)| error.to_string())?;
            let bytes = response.bytes().await.map_err(|error| error.to_string())?;
            let page: ListBucketResult =
                from_str(&String::from_utf8_lossy(&bytes)).map_err(|error| error.to_string())?;
            for item in page.contents {
                if item.key.is_empty() || item.key.ends_with('/') {
                    continue;
                }
                entries.push(ObjectEntry {
                    name: relative_name(&item.key, &prefix),
                    is_folder: false,
                    size: format_size(item.size),
                    storage_class: item.storage_class,
                    last_modified: None,
                    key: item.key,
                    size_bytes: item.size,
                });
            }
            if !page.is_truncated {
                break;
            }
            continuation_token = page.next_continuation_token;
            if continuation_token.is_none() {
                break;
            }
        }
        Ok(entries)
    }

    pub async fn create_folder(&self, key: &str, token: &CancellationToken) -> Result<(), String> {
        let folder_key = if key.ends_with('/') {
            key.to_string()
        } else {
            format!("{key}/")
        };
        let response = self
            .send(
                Method::PUT,
                &self.bucket.endpoint,
                &folder_key,
                &[],
                RequestBody::Bytes(Vec::new()),
                token,
                Some("application/octet-stream"),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        drop(response);
        Ok(())
    }

    pub async fn put_bytes(
        &self,
        key: &str,
        bytes: Vec<u8>,
        content_type: &str,
        token: &CancellationToken,
    ) -> Result<(), String> {
        let response = self
            .send(
                Method::PUT,
                &self.bucket.endpoint,
                key,
                &[],
                RequestBody::Bytes(bytes),
                token,
                Some(content_type),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        drop(response);
        Ok(())
    }

    async fn put_file(
        &self,
        key: &str,
        path: PathBuf,
        size: u64,
        token: &CancellationToken,
        on_bytes: impl Fn(u64) + Send + Sync + 'static,
    ) -> Result<(), String> {
        let content_type = content_type_for_path(&path);
        let response = self
            .send(
                Method::PUT,
                &self.bucket.endpoint,
                key,
                &[],
                RequestBody::File {
                    path,
                    size,
                    on_bytes: Arc::new(on_bytes),
                },
                token,
                Some(content_type),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        drop(response);
        Ok(())
    }

    pub async fn get_bytes(
        &self,
        key: &str,
        maximum_bytes: usize,
        token: &CancellationToken,
    ) -> Result<Vec<u8>, String> {
        let response = self
            .send(
                Method::GET,
                &self.bucket.endpoint,
                key,
                &[],
                RequestBody::Empty,
                token,
                Some(""),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        if response
            .content_length()
            .is_some_and(|length| length > maximum_bytes as u64)
        {
            return Err(format!("文件不能超过 {} MB", maximum_bytes / 1024 / 1024));
        }
        let mut stream = response.bytes_stream();
        let mut bytes = Vec::new();
        while let Some(chunk) = tokio::select! {
            _ = token.cancelled() => return Err(CANCELED.into()),
            chunk = stream.next() => chunk,
        } {
            let chunk = chunk.map_err(|error| error.to_string())?;
            if bytes.len() + chunk.len() > maximum_bytes {
                return Err(format!("文件不能超过 {} MB", maximum_bytes / 1024 / 1024));
            }
            bytes.extend_from_slice(&chunk);
        }
        Ok(bytes)
    }

    pub async fn get_text(
        &self,
        key: &str,
        maximum_bytes: usize,
        token: &CancellationToken,
    ) -> Result<String, String> {
        let bytes = self.get_bytes(key, maximum_bytes, token).await?;
        if bytes.contains(&0) {
            return Err("文件内容包含二进制数据，无法使用文本编辑器打开".into());
        }
        let text = std::str::from_utf8(&bytes)
            .map_err(|_| "文件不是有效的 UTF-8 文本，暂不支持在线编辑".to_string())?;
        Ok(text.trim_start_matches('\u{feff}').to_string())
    }

    pub async fn download_key(
        &self,
        key: &str,
        path: &Path,
        token: &CancellationToken,
    ) -> Result<(), String> {
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent)
                .await
                .map_err(|error| error.to_string())?;
        }
        let temporary = path.with_extension(format!(
            "{}.oss-studio-part",
            path.extension()
                .and_then(|value| value.to_str())
                .unwrap_or_default()
        ));
        let response = self
            .send(
                Method::GET,
                &self.bucket.endpoint,
                key,
                &[],
                RequestBody::Empty,
                token,
                Some(""),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;

        let mut file = tokio::fs::File::create(&temporary)
            .await
            .map_err(|error| error.to_string())?;
        let mut stream = response.bytes_stream();
        let write_result: Result<(), String> = async {
            while let Some(chunk) = tokio::select! {
                _ = token.cancelled() => return Err(CANCELED.into()),
                chunk = stream.next() => chunk,
            } {
                let chunk = chunk.map_err(|error| error.to_string())?;
                file.write_all(&chunk)
                    .await
                    .map_err(|error| error.to_string())?;
            }
            file.flush().await.map_err(|error| error.to_string())?;
            Ok(())
        }
        .await;
        drop(file);
        if let Err(error) = write_result {
            let _ = tokio::fs::remove_file(&temporary).await;
            return Err(error);
        }
        if tokio::fs::try_exists(path).await.unwrap_or(false) {
            tokio::fs::remove_file(path)
                .await
                .map_err(|error| error.to_string())?;
        }
        tokio::fs::rename(&temporary, path).await.map_err(|error| {
            let _ = std::fs::remove_file(&temporary);
            error.to_string()
        })
    }

    pub async fn delete_key(&self, key: &str, token: &CancellationToken) -> Result<(), String> {
        let response = self
            .send(
                Method::DELETE,
                &self.bucket.endpoint,
                key,
                &[],
                RequestBody::Empty,
                token,
                Some(""),
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        drop(response);
        Ok(())
    }

    pub async fn copy_key(
        &self,
        source_key: &str,
        target_key: &str,
        token: &CancellationToken,
    ) -> Result<(), String> {
        let source = format!("/{}/{}", self.bucket.name, uri_encode(source_key, true));
        let mut headers = HeaderMap::new();
        headers.insert(
            "x-oss-copy-source",
            HeaderValue::from_str(&source).map_err(|error| error.to_string())?,
        );
        headers.insert("x-oss-forbid-overwrite", HeaderValue::from_static("true"));
        let response = self
            .send_with_headers(
                Method::PUT,
                &self.bucket.endpoint,
                target_key,
                &[],
                RequestBody::Empty,
                token,
                Some(""),
                headers,
            )
            .await
            .map_err(|(error, _)| error.to_string())?;
        drop(response);
        Ok(())
    }

    pub async fn upload_paths(
        &self,
        paths: Vec<PathBuf>,
        prefix: &str,
        token: CancellationToken,
        on_progress: impl Fn(usize, usize, String, UploadBytes) + Send + Sync + 'static,
    ) -> Result<UploadResult, String> {
        let prefix = prefix.to_owned();
        let scan = tokio::task::spawn_blocking(move || {
            collect_uploads(&prefix, &paths)
                .into_iter()
                .map(|(path, key)| {
                    let size = std::fs::metadata(&path)
                        .map(|metadata| metadata.len())
                        .map_err(|error| format!("读取文件失败 {}：{error}", path.display()));
                    (path, key, size)
                })
                .collect::<Vec<_>>()
        });
        let uploads = tokio::select! {
            _ = token.cancelled() => return Err(CANCELED.into()),
            result = scan => result.map_err(|error| format!("读取上传列表失败：{error}"))?,
        };
        let total = uploads.len();
        let bytes = UploadBytes {
            sent: 0,
            total: uploads
                .iter()
                .filter_map(|(_, _, size)| size.as_ref().ok())
                .sum(),
        };
        on_progress(0, total, "正在上传…".into(), bytes);
        let progress = Mutex::new(UploadProgress {
            bytes,
            file_bytes: vec![0; total],
            completed: 0,
            last_update: Instant::now(),
        });
        let report = Arc::new(move |index: usize, key: &str, sent: u64, finished: bool| {
            let mut progress = progress.lock().unwrap();
            let previous = std::mem::replace(&mut progress.file_bytes[index], sent);
            progress.bytes.sent = progress.bytes.sent - previous + sent;
            if finished {
                progress.completed += 1;
            }
            if finished
                || sent < previous
                || (sent > previous && progress.bytes.sent == progress.bytes.total)
                || progress.last_update.elapsed() >= Duration::from_millis(150)
            {
                progress.last_update = Instant::now();
                on_progress(progress.completed, total, key.to_owned(), progress.bytes);
            }
        });
        let limit = self.settings.upload_concurrency;
        let mut results = stream::iter(uploads)
            .enumerate()
            .map(|(index, (path, key, size))| {
                let token = token.clone();
                let report = Arc::clone(&report);
                async move {
                    if token.is_cancelled() {
                        return Err(CANCELED.to_string());
                    }
                    let size = size?;
                    let report_bytes = Arc::clone(&report);
                    let upload_key = key.clone();
                    let result = self
                        .put_file(&key, path, size, &token, move |sent| {
                            report_bytes(index, &upload_key, sent, false);
                        })
                        .await;
                    if result.is_ok() {
                        report(index, &key, size, true);
                    }
                    result
                }
            })
            .buffer_unordered(limit)
            .collect::<Vec<_>>()
            .await;
        if let Some(Err(error)) = results.drain(..).find(|result| result.is_err()) {
            return Err(error);
        }
        Ok(UploadResult { file_count: total })
    }

    pub async fn download_entries(
        &self,
        entries: Vec<ObjectEntry>,
        destination: &Path,
        token: CancellationToken,
        on_progress: impl Fn(usize, usize, String) + Send + Sync,
    ) -> Result<DownloadResult, String> {
        let single_file = entries.len() == 1 && !entries[0].is_folder;
        if single_file {
            if let Some(parent) = destination.parent() {
                tokio::fs::create_dir_all(parent)
                    .await
                    .map_err(|error| error.to_string())?;
            }
        } else {
            tokio::fs::create_dir_all(destination)
                .await
                .map_err(|error| error.to_string())?;
        }
        let reserved = std::sync::Arc::new(tokio::sync::Mutex::new(HashSet::<String>::new()));
        let limit = self.settings.download_concurrency;
        let total = entries.len();
        let completed = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let file_count = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let renamed = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));

        let tasks = entries.into_iter().map(|entry| {
            let client = self.clone();
            let token = token.clone();
            let destination = destination.to_path_buf();
            let reserved = reserved.clone();
            let completed = completed.clone();
            let file_count = file_count.clone();
            let renamed = renamed.clone();
            let on_progress = &on_progress;
            async move {
                if token.is_cancelled() {
                    return Err(CANCELED.to_string());
                }
                if entry.is_folder {
                    let result = client
                        .download_folder(&entry.key, &destination, token.clone())
                        .await?;
                    file_count.fetch_add(result.file_count, std::sync::atomic::Ordering::SeqCst);
                    renamed.fetch_add(result.renamed_count, std::sync::atomic::Ordering::SeqCst);
                } else {
                    let requested = if single_file {
                        destination.to_path_buf()
                    } else {
                        destination.join(sanitize_segment(&entry.name))
                    };
                    let path = {
                        let mut reserved = reserved.lock().await;
                        unique_download_path(&requested, &mut reserved)
                    };
                    if path != requested {
                        renamed.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                    }
                    client.download_key(&entry.key, &path, &token).await?;
                    file_count.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                }
                let current = completed.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
                on_progress(current, total, entry.name);
                Ok::<(), String>(())
            }
        });
        let mut results = stream::iter(tasks)
            .buffer_unordered(limit)
            .collect::<Vec<_>>()
            .await;
        if let Some(Err(error)) = results.drain(..).find(|result| result.is_err()) {
            return Err(error);
        }
        Ok(DownloadResult {
            file_count: file_count.load(std::sync::atomic::Ordering::SeqCst),
            renamed_count: renamed.load(std::sync::atomic::Ordering::SeqCst),
        })
    }

    async fn download_folder(
        &self,
        prefix: &str,
        destination: &Path,
        token: CancellationToken,
    ) -> Result<DownloadResult, String> {
        let objects = self.list_all_keys(prefix, &token).await?;
        let folder_name = prefix
            .trim_end_matches('/')
            .rsplit('/')
            .next()
            .filter(|value| !value.is_empty())
            .unwrap_or(&self.bucket.name);
        let target_root = destination.join(sanitize_segment(folder_name));
        tokio::fs::create_dir_all(&target_root)
            .await
            .map_err(|error| error.to_string())?;
        let mut reserved = HashSet::new();
        let mut downloads = Vec::new();
        let mut renamed = 0usize;
        for entry in objects {
            let relative = entry
                .key
                .strip_prefix(prefix)
                .unwrap_or(&entry.key)
                .trim_start_matches('/');
            let segments: Vec<String> = relative
                .split('/')
                .filter(|segment| !segment.is_empty())
                .map(sanitize_segment)
                .collect();
            if segments.is_empty() {
                continue;
            }
            let requested = target_root.join(segments.join(std::path::MAIN_SEPARATOR_STR));
            let path = unique_download_path(&requested, &mut reserved);
            if path != requested {
                renamed += 1;
            }
            downloads.push((entry.key, path));
        }

        let semaphore = std::sync::Arc::new(Semaphore::new(self.settings.download_concurrency));
        let client = self.clone();
        let mut tasks = Vec::new();
        for (key, path) in downloads {
            let permit = semaphore
                .clone()
                .acquire_owned()
                .await
                .map_err(|error| error.to_string())?;
            let client = client.clone();
            let token = token.clone();
            tasks.push(tokio::spawn(async move {
                let _permit = permit;
                client.download_key(&key, &path, &token).await
            }));
        }
        let mut file_count = 0;
        for task in tasks {
            task.await
                .map_err(|error| error.to_string())?
                .map_err(|error| error)?;
            file_count += 1;
        }
        Ok(DownloadResult {
            file_count,
            renamed_count: renamed,
        })
    }

    pub async fn copy_entries(
        &self,
        entries: Vec<ObjectEntry>,
        target_prefix: &str,
        token: CancellationToken,
        on_progress: impl Fn(usize, usize, String) + Send + Sync,
    ) -> Result<CopyResult, String> {
        let destination = normalize_prefix(target_prefix);
        let mut copies = Vec::new();
        for entry in entries {
            if token.is_cancelled() {
                return Err(CANCELED.into());
            }
            if entry.is_folder {
                for item in self.list_all_keys(&entry.key, &token).await? {
                    let source_key = item.key;
                    let relative = source_key
                        .strip_prefix(&entry.key)
                        .unwrap_or(&source_key)
                        .trim_start_matches('/')
                        .to_string();
                    copies.push((
                        source_key,
                        format!("{}{}/{}", destination, entry.name, relative),
                    ));
                }
            } else {
                let target = format!("{}{}", destination, entry.name);
                copies.push((entry.key, target));
            }
        }
        if copies.iter().any(|(source, target)| source == target) {
            return Err("目标目录与源目录相同，请选择其它 OSS 目录".into());
        }
        let total = copies.len();
        let completed = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let mut results = stream::iter(copies)
            .map(|(source, target)| {
                let client = self.clone();
                let token = token.clone();
                let completed = completed.clone();
                let on_progress = &on_progress;
                async move {
                    let result = client.copy_key(&source, &target, &token).await;
                    if result.is_ok() {
                        let current =
                            completed.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
                        on_progress(current, total, target);
                    }
                    result
                }
            })
            .buffer_unordered(3)
            .collect::<Vec<_>>()
            .await;
        if let Some(Err(error)) = results.drain(..).find(|result| result.is_err()) {
            return Err(error);
        }
        Ok(CopyResult {
            object_count: total,
        })
    }

    pub async fn delete_entries(
        &self,
        entries: Vec<ObjectEntry>,
        token: CancellationToken,
        on_progress: impl Fn(usize, usize, String) + Send + Sync,
    ) -> Result<DeleteResult, String> {
        let total = entries.len();
        let completed = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let deleted = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let mut tasks = Vec::new();
        for entry in entries {
            let client = self.clone();
            let token = token.clone();
            let completed = completed.clone();
            let deleted = deleted.clone();
            let on_progress = &on_progress;
            tasks.push(async move {
                if entry.is_folder {
                    let objects = client.list_all_keys(&entry.key, &token).await?;
                    for item in objects {
                        if token.is_cancelled() {
                            return Err(CANCELED.to_string());
                        }
                        client.delete_key(&item.key, &token).await?;
                        deleted.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                    }
                } else {
                    client.delete_key(&entry.key, &token).await?;
                    deleted.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                }
                let current = completed.fetch_add(1, std::sync::atomic::Ordering::SeqCst) + 1;
                on_progress(current, total, entry.name);
                Ok::<(), String>(())
            });
        }
        let mut results = stream::iter(tasks)
            .buffer_unordered(2)
            .collect::<Vec<_>>()
            .await;
        if let Some(Err(error)) = results.drain(..).find(|result| result.is_err()) {
            return Err(error);
        }
        Ok(DeleteResult {
            object_count: deleted.load(std::sync::atomic::Ordering::SeqCst),
        })
    }

    async fn send(
        &self,
        method: Method,
        endpoint: &str,
        key: &str,
        query: &[(String, String)],
        body: RequestBody,
        token: &CancellationToken,
        content_type: Option<&str>,
    ) -> Result<Response, (OssFailure, Vec<ObjectEntry>)> {
        self.send_with_headers(
            method,
            endpoint,
            key,
            query,
            body,
            token,
            content_type,
            HeaderMap::new(),
        )
        .await
    }

    async fn send_with_headers(
        &self,
        method: Method,
        endpoint: &str,
        key: &str,
        query: &[(String, String)],
        body: RequestBody,
        token: &CancellationToken,
        content_type: Option<&str>,
        extra_headers: HeaderMap,
    ) -> Result<Response, (OssFailure, Vec<ObjectEntry>)> {
        let attempts = self.settings.retry_count as usize + 1;
        let mut last_error = None;
        for attempt in 0..attempts {
            if token.is_cancelled() {
                return Err((cancelled_failure(), Vec::new()));
            }
            match self
                .send_once(
                    method.clone(),
                    endpoint,
                    key,
                    query,
                    &body,
                    token,
                    content_type,
                    &extra_headers,
                )
                .await
            {
                Ok(response) if response.status().is_success() => return Ok(response),
                Ok(response) => {
                    let status = response.status();
                    let text = response.text().await.unwrap_or_default();
                    let failure = parse_failure(status, &text);
                    if attempt + 1 < attempts
                        && (status.is_server_error() || status.as_u16() == 429)
                    {
                        last_error = Some(failure);
                        tokio::time::sleep(Duration::from_millis(250 * (attempt as u64 + 1))).await;
                        continue;
                    }
                    return Err((failure, Vec::new()));
                }
                Err(failure) => {
                    if token.is_cancelled() {
                        return Err((cancelled_failure(), Vec::new()));
                    }
                    if attempt + 1 < attempts {
                        last_error = Some(failure);
                        tokio::time::sleep(Duration::from_millis(250 * (attempt as u64 + 1))).await;
                        continue;
                    }
                    return Err((failure, Vec::new()));
                }
            }
        }
        Err((
            last_error.unwrap_or_else(|| transport_failure("请求失败")),
            Vec::new(),
        ))
    }

    #[allow(clippy::too_many_arguments)]
    async fn send_once(
        &self,
        method: Method,
        endpoint: &str,
        key: &str,
        query: &[(String, String)],
        body: &RequestBody,
        token: &CancellationToken,
        content_type: Option<&str>,
        extra_headers: &HeaderMap,
    ) -> Result<Response, OssFailure> {
        let (url, host, canonical_query) = self
            .build_url(endpoint, key, query)
            .map_err(transport_failure)?;
        let date = Utc::now().format("%a, %d %b %Y %H:%M:%S GMT").to_string();
        let content_type = content_type.unwrap_or("");
        let mut headers = extra_headers.clone();
        if self.bucket.request_payer {
            headers.insert("x-oss-request-payer", HeaderValue::from_static("requester"));
        }
        if !content_type.is_empty() {
            headers.insert(
                CONTENT_TYPE,
                HeaderValue::from_str(content_type).unwrap_or(HeaderValue::from_static("")),
            );
        }
        headers.insert(DATE, HeaderValue::from_str(&date).unwrap());
        headers.insert(HOST, HeaderValue::from_str(&host).unwrap());

        let canonical_headers = self.canonical_headers(&headers);
        let resource = self.canonical_resource(key, &canonical_query);
        let string_to_sign = format!(
            "{}\n{}\n{}\n{}\n{}{}",
            method.as_str(),
            "",
            content_type,
            date,
            canonical_headers,
            resource
        );
        let mut mac = Hmac::<Sha1>::new_from_slice(self.credential.access_key_secret.as_bytes())
            .expect("HMAC accepts any key length");
        mac.update(string_to_sign.as_bytes());
        let signature = BASE64.encode(mac.finalize().into_bytes());
        let authorization = format!("OSS {}:{}", self.credential.access_key_id, signature);
        headers.insert(
            "authorization",
            HeaderValue::from_str(&authorization).unwrap(),
        );

        let mut request = self.http.request(method, url).headers(headers);
        if let Some(length) = body.len() {
            request = request.header(CONTENT_LENGTH, length);
        }
        request = match body {
            RequestBody::Empty => request,
            RequestBody::Bytes(bytes) => request.body(bytes.clone()),
            RequestBody::File {
                path,
                size,
                on_bytes,
            } => {
                on_bytes(0);
                let file = tokio::fs::File::open(path)
                    .await
                    .map_err(transport_failure)?;
                let on_bytes = Arc::clone(on_bytes);
                let mut sent = 0;
                let stream =
                    ReaderStream::with_capacity(file.take(*size), 64 * 1024).map(move |chunk| {
                        if let Ok(bytes) = &chunk {
                            sent += bytes.len() as u64;
                            on_bytes(sent);
                        }
                        chunk
                    });
                request.body(reqwest::Body::wrap_stream(stream))
            }
        };
        tokio::select! {
            _ = token.cancelled() => Err(cancelled_failure()),
            response = request.send() => response.map_err(|error| network_failure(error, &host)),
        }
    }

    fn build_url(
        &self,
        endpoint: &str,
        key: &str,
        query: &[(String, String)],
    ) -> Result<(String, String, String), String> {
        let endpoint = normalize_endpoint(endpoint);
        let host = self.host_for(&endpoint);
        let scheme = if self.bucket.use_https {
            "https"
        } else {
            "http"
        };
        let path = if key.is_empty() {
            "/".to_string()
        } else {
            format!("/{}", uri_encode(key.trim_start_matches('/'), false))
        };
        let mut canonical_pairs: Vec<(String, String)> = query
            .iter()
            .filter(|(name, _)| is_signed_subresource(name))
            .cloned()
            .collect();
        canonical_pairs.sort();
        let canonical_query = canonical_pairs
            .iter()
            .map(|(name, value)| {
                if value.is_empty() {
                    name.clone()
                } else {
                    format!("{name}={value}")
                }
            })
            .collect::<Vec<_>>()
            .join("&");
        let mut url_pairs: Vec<(String, String)> = query
            .iter()
            .map(|(name, value)| (uri_encode(name, true), uri_encode(value, true)))
            .collect();
        url_pairs.sort();
        let url_query = url_pairs
            .iter()
            .map(|(name, value)| {
                if value.is_empty() {
                    name.clone()
                } else {
                    format!("{name}={value}")
                }
            })
            .collect::<Vec<_>>()
            .join("&");
        let url = if url_query.is_empty() {
            format!("{scheme}://{host}{path}")
        } else {
            format!("{scheme}://{host}{path}?{url_query}")
        };
        let url = reqwest::Url::parse(&url).map_err(|error| error.to_string())?;
        Ok((url.to_string(), host, canonical_query))
    }

    fn canonical_resource(&self, key: &str, canonical_query: &str) -> String {
        let resource = if key.is_empty() {
            format!("/{}/", self.bucket.name)
        } else {
            format!("/{}/{}", self.bucket.name, key.trim_start_matches('/'))
        };
        if canonical_query.is_empty() {
            resource
        } else {
            format!("{resource}?{canonical_query}")
        }
    }

    fn canonical_headers(&self, headers: &HeaderMap) -> String {
        let mut values: Vec<(String, String)> = headers
            .iter()
            .filter_map(|(name, value)| {
                let name = name.as_str().to_ascii_lowercase();
                if name.starts_with("x-oss-") {
                    Some((name, value.to_str().unwrap_or_default().trim().to_string()))
                } else {
                    None
                }
            })
            .collect();
        values.sort_by(|left, right| left.0.cmp(&right.0));
        values
            .into_iter()
            .map(|(name, value)| format!("{name}:{value}\n"))
            .collect()
    }
}

fn normalize_endpoint(value: &str) -> String {
    value
        .trim()
        .trim_start_matches("https://")
        .trim_start_matches("http://")
        .trim_end_matches('/')
        .to_string()
}

fn is_signed_subresource(name: &str) -> bool {
    SIGNED_SUBRESOURCES.contains(&name)
}

fn uri_encode(value: &str, encode_slash: bool) -> String {
    if encode_slash {
        return utf8_percent_encode(value, URI_SET).to_string();
    }
    value
        .split('/')
        .map(|segment| utf8_percent_encode(segment, URI_SET).to_string())
        .collect::<Vec<_>>()
        .join("/")
}

fn relative_name(key: &str, prefix: &str) -> String {
    key.strip_prefix(prefix).unwrap_or(key).to_string()
}

fn format_size(size: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut value = size as f64;
    let mut unit = 0usize;
    while value >= 1024.0 && unit < UNITS.len() - 1 {
        value /= 1024.0;
        unit += 1;
    }
    if unit == 0 {
        format!("{value:.0} {}", UNITS[unit])
    } else {
        let amount = format!("{value:.2}");
        let amount = amount.trim_end_matches('0').trim_end_matches('.');
        format!("{amount} {}", UNITS[unit])
    }
}

fn content_type_for_path(path: &Path) -> &'static str {
    match path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase()
        .as_str()
    {
        "json" => "application/json",
        "xml" => "application/xml",
        "html" | "htm" => "text/html; charset=utf-8",
        "css" => "text/css; charset=utf-8",
        "js" | "mjs" | "cjs" => "text/javascript; charset=utf-8",
        "csv" => "text/csv; charset=utf-8",
        "svg" => "image/svg+xml; charset=utf-8",
        "txt" | "md" | "log" | "yaml" | "yml" | "toml" | "ini" => "text/plain; charset=utf-8",
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "bmp" => "image/bmp",
        "ico" => "image/x-icon",
        "pdf" => "application/pdf",
        _ => "application/octet-stream",
    }
}

fn sanitize_segment(segment: &str) -> String {
    if segment == "." || segment == ".." {
        return format!("_{segment}");
    }
    let mut value = String::new();
    for character in segment.chars() {
        if matches!(
            character,
            '<' | '>' | ':' | '"' | '/' | '\\' | '|' | '?' | '*'
        ) || character.is_control()
        {
            value.push('_');
        } else {
            value.push(character);
        }
    }
    let trimmed = value.trim().trim_end_matches(['.', ' ']);
    if trimmed.is_empty() {
        "_".into()
    } else {
        trimmed.to_string()
    }
}

fn unique_download_path(requested: &Path, reserved: &mut HashSet<String>) -> PathBuf {
    let key = requested.to_string_lossy().to_lowercase();
    if !requested.exists() && reserved.insert(key) {
        return requested.to_path_buf();
    }
    let parent = requested.parent().unwrap_or_else(|| Path::new("."));
    let stem = requested
        .file_stem()
        .and_then(|value| value.to_str())
        .unwrap_or("download");
    let extension = requested
        .extension()
        .and_then(|value| value.to_str())
        .map(|value| format!(".{value}"))
        .unwrap_or_default();
    for suffix in 2..10_000 {
        let candidate = parent.join(format!("{stem} ({suffix}){extension}"));
        let key = candidate.to_string_lossy().to_lowercase();
        if !candidate.exists() && reserved.insert(key) {
            return candidate;
        }
    }
    requested.to_path_buf()
}

fn collect_uploads(prefix: &str, paths: &[PathBuf]) -> Vec<(PathBuf, String)> {
    let normalized = normalize_prefix(prefix);
    let mut seen = HashSet::new();
    let mut uploads = Vec::new();
    for path in paths {
        if !seen.insert(path.to_string_lossy().to_lowercase()) {
            continue;
        }
        if path.is_file() {
            if let Some(name) = path.file_name().and_then(|value| value.to_str()) {
                uploads.push((path.clone(), format!("{normalized}{name}")));
            }
            continue;
        }
        if !path.is_dir() {
            continue;
        }
        let directory_name = path
            .file_name()
            .and_then(|value| value.to_str())
            .unwrap_or("upload");
        for entry in WalkDir::new(path).follow_links(false).into_iter().flatten() {
            if !entry.file_type().is_file() {
                continue;
            }
            let relative = entry
                .path()
                .strip_prefix(path)
                .unwrap_or(entry.path())
                .to_string_lossy()
                .replace('\\', "/");
            uploads.push((
                entry.path().to_path_buf(),
                format!("{normalized}{directory_name}/{relative}"),
            ));
        }
    }
    uploads
}

fn parse_failure(status: StatusCode, text: &str) -> OssFailure {
    #[derive(Deserialize)]
    struct ErrorBody {
        #[serde(rename = "Code", default)]
        code: String,
        #[serde(rename = "Message", default)]
        message: String,
        #[serde(rename = "Endpoint", default)]
        endpoint: String,
    }
    if let Ok(body) = from_str::<ErrorBody>(text) {
        return OssFailure {
            status: status.as_u16(),
            code: body.code,
            message: body.message,
            endpoint: if body.endpoint.is_empty() {
                None
            } else {
                Some(body.endpoint)
            },
        };
    }
    OssFailure {
        status: status.as_u16(),
        code: status.canonical_reason().unwrap_or("HTTP_ERROR").into(),
        message: text.chars().take(500).collect(),
        endpoint: None,
    }
}

fn transport_failure(error: impl std::fmt::Display) -> OssFailure {
    OssFailure {
        status: 0,
        code: "NETWORK_ERROR".into(),
        message: error.to_string(),
        endpoint: None,
    }
}

fn network_failure(error: reqwest::Error, host: &str) -> OssFailure {
    let reason = if error.is_timeout() {
        "连接超时"
    } else if error.is_connect() {
        "无法建立连接"
    } else if error.is_body() {
        "传输中断"
    } else if error.is_decode() {
        "响应解析失败"
    } else {
        "网络请求失败"
    };
    let detail = deepest_error_message(&error);
    let host = host.trim();
    let message = match (host.is_empty(), detail) {
        (false, Some(detail)) => {
            format!("无法访问 {host}（{reason}：{detail}）。请检查网络、代理或防火墙后重试。")
        }
        (false, None) => format!("无法访问 {host}（{reason}）。请检查网络、代理或防火墙后重试。"),
        (true, Some(detail)) => format!("{reason}：{detail}。请检查网络、代理或防火墙后重试。"),
        (true, None) => format!("{reason}。请检查网络、代理或防火墙后重试。"),
    };
    OssFailure {
        status: 0,
        code: "NETWORK_ERROR".into(),
        message,
        endpoint: None,
    }
}

fn deepest_error_message(error: &reqwest::Error) -> Option<String> {
    let mut source = std::error::Error::source(error);
    let mut deepest = None;
    while let Some(cause) = source {
        let text = cause.to_string();
        if !text.trim().is_empty() {
            deepest = Some(text);
        }
        source = cause.source();
    }
    deepest.map(|text| {
        let flattened = text.replace(['\r', '\n'], " ");
        let mut clipped: String = flattened.chars().take(160).collect();
        if flattened.chars().count() > 160 {
            clipped.push('…');
        }
        clipped
    })
}

fn parse_failure_detail(error: impl std::fmt::Display) -> OssFailure {
    OssFailure {
        status: 0,
        code: "RESPONSE_ERROR".into(),
        message: error.to_string(),
        endpoint: None,
    }
}

fn cancelled_failure() -> OssFailure {
    OssFailure {
        status: 0,
        code: CANCELED.into(),
        message: "操作已取消".into(),
        endpoint: None,
    }
}

fn redirect_region(endpoint: &str, current_endpoint: &str) -> Option<(String, String)> {
    let endpoint = normalize_endpoint(endpoint);
    let current = normalize_endpoint(current_endpoint);
    if endpoint.eq_ignore_ascii_case(&current)
        || !endpoint.starts_with("oss-")
        || !endpoint.ends_with(".aliyuncs.com")
    {
        return None;
    }
    let region = endpoint
        .strip_prefix("oss-")?
        .strip_suffix(".aliyuncs.com")?;
    if region.is_empty() {
        return None;
    }
    Some((endpoint.clone(), region.to_string()))
}

pub fn text_content_type(key: &str) -> &'static str {
    match Path::new(key)
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or_default()
        .to_ascii_lowercase()
        .as_str()
    {
        "json" => "application/json; charset=utf-8",
        "xml" => "application/xml; charset=utf-8",
        "html" | "htm" => "text/html; charset=utf-8",
        "css" => "text/css; charset=utf-8",
        "js" | "mjs" | "cjs" => "text/javascript; charset=utf-8",
        "csv" => "text/csv; charset=utf-8",
        "svg" => "image/svg+xml; charset=utf-8",
        _ => "text/plain; charset=utf-8",
    }
}

#[derive(Debug, Deserialize)]
struct ListBucketResult {
    #[serde(rename = "IsTruncated", default)]
    is_truncated: bool,
    #[serde(rename = "NextContinuationToken", default)]
    next_continuation_token: Option<String>,
    #[serde(rename = "Contents", default)]
    contents: Vec<ListedObject>,
    #[serde(rename = "CommonPrefixes", default)]
    common_prefixes: Vec<CommonPrefix>,
}

#[derive(Debug, Deserialize)]
struct ListedObject {
    #[serde(rename = "Key", default)]
    key: String,
    #[serde(rename = "LastModified", default)]
    last_modified: Option<String>,
    #[serde(rename = "Size", default)]
    size: u64,
    #[serde(rename = "StorageClass", default)]
    storage_class: String,
}

#[derive(Debug, Deserialize)]
struct CommonPrefix {
    #[serde(rename = "Prefix", default)]
    prefix: String,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn upload_reports_bytes_during_request_and_resets_them_on_retry() {
        use std::io::{BufRead, BufReader, Read, Write};
        use std::net::TcpListener;

        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let endpoint = listener.local_addr().unwrap().to_string();
        let size = 12 * 1024 * 1024;
        let server = std::thread::spawn(move || {
            for status in ["500 Internal Server Error", "200 OK"] {
                let (mut socket, _) = listener.accept().unwrap();
                socket
                    .set_read_timeout(Some(Duration::from_secs(5)))
                    .unwrap();
                let mut request = BufReader::new(&mut socket);
                let mut content_length = None;
                loop {
                    let mut header = String::new();
                    assert_ne!(request.read_line(&mut header).unwrap(), 0);
                    if header == "\r\n" {
                        break;
                    }
                    if let Some(length) =
                        header.to_ascii_lowercase().strip_prefix("content-length:")
                    {
                        content_length = Some(length.trim().parse::<u64>().unwrap());
                    }
                }
                assert_eq!(content_length, Some(size));
                // Stall the socket so the streaming progress timer can elapse.
                std::thread::sleep(Duration::from_millis(300));
                assert_eq!(
                    std::io::copy(&mut request.take(size), &mut std::io::sink()).unwrap(),
                    size
                );
                write!(
                    socket,
                    "HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                )
                .unwrap();
            }
        });
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("upload.bin");
        std::fs::File::create(&path).unwrap().set_len(size).unwrap();
        let client = OssClient {
            bucket: BucketProfile {
                name: "demo".into(),
                endpoint,
                endpoint_mode: "Cname".into(),
                use_https: false,
                ..Default::default()
            },
            credential: StoredCredential {
                access_key_id: "test-id".into(),
                access_key_secret: "test-secret".into(),
            },
            settings: OssSettings {
                retry_count: 1,
                ..Default::default()
            },
            http: reqwest::Client::builder()
                .no_proxy()
                .timeout(Duration::from_secs(5))
                .build()
                .unwrap(),
        };
        let updates = Arc::new(Mutex::new(Vec::new()));
        let captured = Arc::clone(&updates);
        let result = client
            .upload_paths(
                vec![path],
                "",
                CancellationToken::new(),
                move |completed, _, _, bytes| {
                    captured
                        .lock()
                        .unwrap()
                        .push((completed, bytes.sent, bytes.total));
                },
            )
            .await
            .unwrap();
        server.join().unwrap();

        let updates = updates.lock().unwrap();
        assert_eq!(result.file_count, 1);
        assert_eq!(updates.first(), Some(&(0, 0, size)));
        assert!(updates
            .iter()
            .any(|&(completed, sent, _)| completed == 0 && sent > 0 && sent < size));
        assert!(updates
            .windows(2)
            .any(|pair| pair == [(0, size, size), (0, 0, size)]));
        assert_eq!(updates.last(), Some(&(1, size, size)));
    }

    #[test]
    fn encodes_object_keys_without_double_encoding() {
        assert_eq!(
            uri_encode("目录/a b+1.txt", false),
            "%E7%9B%AE%E5%BD%95/a%20b%2B1.txt"
        );
    }

    #[test]
    fn normalizes_endpoint_and_prefix() {
        assert_eq!(
            normalize_endpoint(" https://oss-cn-hangzhou.aliyuncs.com/ "),
            "oss-cn-hangzhou.aliyuncs.com"
        );
        assert_eq!(normalize_prefix(" /foo/bar "), "foo/bar/");
        assert_eq!(normalize_prefix("/"), "");
    }

    #[test]
    fn sanitizes_traversal_segments() {
        assert_eq!(sanitize_segment(".."), "_..");
        assert_eq!(sanitize_segment("a:b?.txt"), "a_b_.txt");
        assert_eq!(sanitize_segment(" "), "_");
    }

    #[test]
    fn parses_redirect_region() {
        assert_eq!(
            redirect_region(
                "oss-cn-shanghai.aliyuncs.com",
                "oss-cn-hangzhou.aliyuncs.com"
            ),
            Some(("oss-cn-shanghai.aliyuncs.com".into(), "cn-shanghai".into()))
        );
        assert_eq!(
            redirect_region(
                "oss-cn-shanghai.aliyuncs.com",
                "oss-cn-shanghai.aliyuncs.com"
            ),
            None
        );
    }

    #[test]
    fn formats_sizes() {
        assert_eq!(format_size(0), "0 B");
        assert_eq!(format_size(1024), "1 KB");
        assert_eq!(format_size(1536), "1.5 KB");
        assert_eq!(format_size(10 * 1024), "10 KB");
        assert_eq!(format_size(100 * 1024), "100 KB");
    }

    #[test]
    fn parses_namespaced_list_objects_v2_response() {
        let xml = r#"<?xml version="1.0" encoding="UTF-8"?>
<ListBucketResult xmlns="http://doc.oss-cn-hangzhou.aliyuncs.com">
  <Name>demo</Name>
  <Prefix>photos/</Prefix>
  <IsTruncated>true</IsTruncated>
  <NextContinuationToken>next/token+1=</NextContinuationToken>
  <Contents>
    <Key>photos/a b.jpg</Key>
    <LastModified>2026-11-20T02:03:04.000Z</LastModified>
    <Size>2048</Size>
    <StorageClass>Standard</StorageClass>
  </Contents>
  <CommonPrefixes><Prefix>photos/2026/</Prefix></CommonPrefixes>
</ListBucketResult>"#;
        let parsed: ListBucketResult = from_str(xml).expect("valid OSS XML");
        assert!(parsed.is_truncated);
        assert_eq!(
            parsed.next_continuation_token.as_deref(),
            Some("next/token+1=")
        );
        assert_eq!(parsed.contents[0].key, "photos/a b.jpg");
        assert_eq!(parsed.contents[0].size, 2048);
        assert_eq!(parsed.common_prefixes[0].prefix, "photos/2026/");
    }

    #[test]
    fn copy_source_encodes_slashes_and_unicode() {
        assert_eq!(
            uri_encode("目录/a b.txt", true),
            "%E7%9B%AE%E5%BD%95%2Fa%20b.txt"
        );
    }

    #[test]
    fn signs_only_subresources_but_sends_all_query_values() {
        let client = OssClient::new(
            BucketProfile {
                id: "bucket-test".into(),
                name: "demo".into(),
                region_code: "cn-hangzhou".into(),
                endpoint: "oss-cn-hangzhou.aliyuncs.com".into(),
                ..Default::default()
            },
            StoredCredential {
                access_key_id: "test-id".into(),
                access_key_secret: "test-secret".into(),
            },
            OssSettings::default(),
        )
        .expect("client");
        let query = vec![
            ("prefix".into(), "photos/目录/".into()),
            ("delimiter".into(), "/".into()),
            ("list-type".into(), "2".into()),
            ("max-keys".into(), "999".into()),
        ];
        let (url, host, canonical) = client
            .build_url("oss-cn-hangzhou.aliyuncs.com", "", &query)
            .expect("url");
        assert_eq!(host, "demo.oss-cn-hangzhou.aliyuncs.com");
        assert_eq!(canonical, "");
        assert!(url.contains("prefix=photos%2F%E7%9B%AE%E5%BD%95%2F"));
        assert!(url.contains("list-type=2"));
        assert_eq!(
            client.canonical_resource("photos/a b.jpg", &canonical),
            "/demo/photos/a b.jpg"
        );

        let query = vec![("continuation-token".into(), "next_page".into())];
        let (_, _, canonical) = client
            .build_url("oss-cn-hangzhou.aliyuncs.com", "", &query)
            .expect("url");
        assert_eq!(canonical, "continuation-token=next_page");
        assert_eq!(
            client.canonical_resource("", &canonical),
            "/demo/?continuation-token=next_page"
        );
    }

    #[tokio::test]
    async fn network_failures_report_host_without_request_url() {
        let http = reqwest::Client::builder()
            .proxy(reqwest::Proxy::all("http://127.0.0.1:1").expect("proxy"))
            .build()
            .expect("client");
        let error = http
            .get("https://demo.oss-cn-hangzhou.aliyuncs.com/?prefix=secret&continuation-token=hidden")
            .send()
            .await
            .expect_err("loopback proxy should refuse the connection");
        let message = network_failure(error, "demo.oss-cn-hangzhou.aliyuncs.com").to_string();
        assert!(message.starts_with("NETWORK_ERROR:"));
        assert!(message.contains("demo.oss-cn-hangzhou.aliyuncs.com"));
        assert!(!message.contains("prefix="));
        assert!(!message.contains("continuation-token"));
        assert!(!message.contains("http://"));
        assert!(!message.contains("https://"));
    }
}
