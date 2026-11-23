use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct BucketProfile {
    pub id: String,
    pub name: String,
    pub region_code: String,
    pub region_name: String,
    pub endpoint: String,
    pub use_https: bool,
    pub prefix: String,
    pub request_payer: bool,
    pub note: String,
    pub keep_login: bool,
    pub remember_secret: bool,
    pub endpoint_mode: String,
    pub display_name: String,
}

impl Default for BucketProfile {
    fn default() -> Self {
        Self {
            id: String::new(),
            name: String::new(),
            region_code: String::new(),
            region_name: String::new(),
            endpoint: String::new(),
            use_https: true,
            prefix: String::new(),
            request_payer: false,
            note: String::new(),
            keep_login: true,
            remember_secret: true,
            endpoint_mode: "Default".into(),
            display_name: String::new(),
        }
    }
}

pub fn normalize_prefix(value: &str) -> String {
    let trimmed = value.trim().trim_matches('/');
    if trimmed.is_empty() {
        String::new()
    } else {
        format!("{trimmed}/")
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct DirectoryBookmark {
    pub id: String,
    pub bucket_id: String,
    pub prefix: String,
    pub display_name: String,
}

impl Default for DirectoryBookmark {
    fn default() -> Self {
        Self {
            id: String::new(),
            bucket_id: String::new(),
            prefix: String::new(),
            display_name: String::new(),
        }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct WorkspaceState {
    pub buckets: Vec<BucketProfile>,
    pub open_bucket_ids: Vec<String>,
    pub active_bucket_id: String,
    pub bookmarks: Vec<DirectoryBookmark>,
    pub upload_concurrency: u32,
    pub download_concurrency: u32,
    pub request_timeout_seconds: u32,
    pub retry_count: u32,
    pub theme_mode: String,
}

impl Default for WorkspaceState {
    fn default() -> Self {
        Self {
            buckets: Vec::new(),
            open_bucket_ids: Vec::new(),
            active_bucket_id: String::new(),
            bookmarks: Vec::new(),
            upload_concurrency: 3,
            download_concurrency: 3,
            request_timeout_seconds: 60,
            retry_count: 5,
            theme_mode: "Light".into(),
        }
    }
}

impl WorkspaceState {
    pub fn normalize(&mut self) {
        const SAMPLE_IDS: [&str; 6] = [
            "bucket-prod-images",
            "bucket-web-static",
            "bucket-app-backups",
            "bucket-audit-archive",
            "bucket-test-assets",
            "bucket-test-logs",
        ];
        self.buckets
            .retain(|bucket| !SAMPLE_IDS.contains(&bucket.id.as_str()));
        for bucket in &mut self.buckets {
            bucket.prefix = normalize_prefix(&bucket.prefix);
            bucket.endpoint_mode = match bucket.endpoint_mode.as_str() {
                "Default" | "Custom" | "Cname" => bucket.endpoint_mode.clone(),
                _ => "Default".into(),
            };
            if bucket.display_name.is_empty() {
                bucket.display_name = String::new();
            }
        }

        self.upload_concurrency = self.upload_concurrency.clamp(1, 8);
        self.download_concurrency = self.download_concurrency.clamp(1, 8);
        self.request_timeout_seconds = self.request_timeout_seconds.clamp(10, 300);
        self.retry_count = self.retry_count.clamp(0, 5);
        if !matches!(self.theme_mode.as_str(), "System" | "Light" | "Dark") {
            self.theme_mode = "Light".into();
        }

        let bucket_ids: Vec<String> = self
            .buckets
            .iter()
            .map(|bucket| bucket.id.clone())
            .collect();
        self.open_bucket_ids.retain(|id| bucket_ids.contains(id));
        self.bookmarks
            .retain(|bookmark| bucket_ids.contains(&bookmark.bucket_id));
        for bookmark in &mut self.bookmarks {
            bookmark.prefix = normalize_prefix(&bookmark.prefix);
        }

        let mut seen = std::collections::HashSet::new();
        self.bookmarks
            .retain(|bookmark| seen.insert((bookmark.bucket_id.clone(), bookmark.prefix.clone())));

        if self.open_bucket_ids.is_empty() {
            if let Some(first) = self.buckets.first() {
                self.open_bucket_ids.push(first.id.clone());
            }
        }
        if !self.open_bucket_ids.contains(&self.active_bucket_id) {
            self.active_bucket_id = self.open_bucket_ids.first().cloned().unwrap_or_default();
        }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct ObjectEntry {
    pub name: String,
    pub is_folder: bool,
    pub size: String,
    pub storage_class: String,
    pub last_modified: Option<String>,
    pub key: String,
    pub size_bytes: u64,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct OssSettings {
    pub upload_concurrency: usize,
    pub download_concurrency: usize,
    pub request_timeout_seconds: u64,
    pub retry_count: u32,
}

impl Default for OssSettings {
    fn default() -> Self {
        Self {
            upload_concurrency: 3,
            download_concurrency: 3,
            request_timeout_seconds: 60,
            retry_count: 5,
        }
    }
}

impl OssSettings {
    pub fn normalized(&self) -> Self {
        Self {
            upload_concurrency: self.upload_concurrency.clamp(1, 8),
            download_concurrency: self.download_concurrency.clamp(1, 8),
            request_timeout_seconds: self.request_timeout_seconds.clamp(10, 300),
            retry_count: self.retry_count.min(5),
        }
    }
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ObjectListResult {
    pub objects: Vec<ObjectEntry>,
    pub endpoint: String,
    pub region_code: String,
    pub endpoint_corrected: bool,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TransferProgress {
    pub task_id: String,
    pub current: usize,
    pub total: usize,
    pub name: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub upload_bytes: Option<UploadBytes>,
}

#[derive(Clone, Copy, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UploadBytes {
    pub sent: u64,
    pub total: u64,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DownloadResult {
    pub file_count: usize,
    pub renamed_count: usize,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UploadResult {
    pub file_count: usize,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CopyResult {
    pub object_count: usize,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeleteResult {
    pub object_count: usize,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CredentialInput {
    pub access_key_id: String,
    pub access_key_secret: String,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct StoredCredential {
    pub access_key_id: String,
    pub access_key_secret: String,
}
