use std::fs;
use std::path::PathBuf;

use crate::models::{CredentialInput, StoredCredential, WorkspaceState};

pub struct WorkspaceStore {
    path: PathBuf,
}

impl WorkspaceStore {
    pub fn new() -> Self {
        let app_data = std::env::var_os("LOCALAPPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from("."));
        Self {
            path: app_data.join("OSS-Studio").join("workspace.json"),
        }
    }

    pub fn load(&self) -> WorkspaceState {
        let mut state = fs::read_to_string(&self.path)
            .ok()
            .and_then(|text| serde_json::from_str::<WorkspaceState>(&text).ok())
            .unwrap_or_default();
        state.normalize();
        state
    }

    pub fn save(&self, state: &WorkspaceState) -> Result<(), String> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent).map_err(|error| error.to_string())?;
        }
        let text = serde_json::to_string_pretty(state).map_err(|error| error.to_string())?;
        fs::write(&self.path, text).map_err(|error| error.to_string())
    }
}

#[cfg(windows)]
mod wincred {
    use super::*;
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use std::ptr::null_mut;

    const GENERIC_CREDENTIAL: u32 = 1;
    const PERSIST_LOCAL_MACHINE: u32 = 2;
    const ERROR_NOT_FOUND: u32 = 1168;

    #[repr(C)]
    struct FileTime {
        low: u32,
        high: u32,
    }

    #[repr(C)]
    struct CredentialAttribute {
        keyword: *mut u16,
        flags: u32,
        value_size: u32,
        value: *mut u8,
    }

    #[repr(C)]
    struct NativeCredential {
        flags: u32,
        credential_type: u32,
        target_name: *mut u16,
        comment: *mut u16,
        last_written: FileTime,
        credential_blob_size: u32,
        credential_blob: *mut u8,
        persist: u32,
        attribute_count: u32,
        attributes: *mut CredentialAttribute,
        target_alias: *mut u16,
        user_name: *mut u16,
    }

    #[link(name = "advapi32")]
    extern "system" {
        fn CredReadW(
            target: *const u16,
            credential_type: u32,
            flags: u32,
            credential: *mut *mut NativeCredential,
        ) -> i32;
        fn CredWriteW(credential: *const NativeCredential, flags: u32) -> i32;
        fn CredDeleteW(target: *const u16, credential_type: u32, flags: u32) -> i32;
        fn CredFree(buffer: *mut std::ffi::c_void);
    }

    fn wide(value: &str) -> Vec<u16> {
        OsStr::new(value).encode_wide().chain(Some(0)).collect()
    }

    fn target_name(bucket_id: &str) -> Vec<u16> {
        wide(&format!("OSS-Studio/{bucket_id}"))
    }

    pub fn load(bucket_id: &str) -> Result<Option<StoredCredential>, String> {
        let target = target_name(bucket_id);
        let mut pointer: *mut NativeCredential = null_mut();
        let ok = unsafe { CredReadW(target.as_ptr(), GENERIC_CREDENTIAL, 0, &mut pointer) };
        if ok == 0 {
            return Ok(None);
        }
        let result = unsafe {
            let credential = &*pointer;
            let user = if credential.user_name.is_null() {
                String::new()
            } else {
                wide_ptr_to_string(credential.user_name)
            };
            let secret =
                if credential.credential_blob.is_null() || credential.credential_blob_size == 0 {
                    String::new()
                } else {
                    let count = credential.credential_blob_size as usize / 2;
                    let slice =
                        std::slice::from_raw_parts(credential.credential_blob as *const u16, count);
                    String::from_utf16_lossy(slice)
                };
            CredFree(pointer as *mut std::ffi::c_void);
            StoredCredential {
                access_key_id: user,
                access_key_secret: secret,
            }
        };
        Ok(Some(result))
    }

    pub fn save(bucket_id: &str, credential: &CredentialInput) -> Result<(), String> {
        let mut target = target_name(bucket_id);
        let mut user = wide(&credential.access_key_id);
        let mut secret: Vec<u16> = credential.access_key_secret.encode_utf16().collect();
        let native = NativeCredential {
            flags: 0,
            credential_type: GENERIC_CREDENTIAL,
            target_name: target.as_mut_ptr(),
            comment: null_mut(),
            last_written: FileTime { low: 0, high: 0 },
            credential_blob_size: (secret.len() * std::mem::size_of::<u16>()) as u32,
            credential_blob: secret.as_mut_ptr() as *mut u8,
            persist: PERSIST_LOCAL_MACHINE,
            attribute_count: 0,
            attributes: null_mut(),
            target_alias: null_mut(),
            user_name: user.as_mut_ptr(),
        };
        let ok = unsafe { CredWriteW(&native, 0) };
        if ok == 0 {
            return Err(std::io::Error::last_os_error().to_string());
        }
        Ok(())
    }

    pub fn delete(bucket_id: &str) -> Result<(), String> {
        let target = target_name(bucket_id);
        let ok = unsafe { CredDeleteW(target.as_ptr(), GENERIC_CREDENTIAL, 0) };
        if ok == 0 {
            let error = std::io::Error::last_os_error();
            if error.raw_os_error().map(|value| value as u32) == Some(ERROR_NOT_FOUND) {
                return Ok(());
            }
            return Err(error.to_string());
        }
        Ok(())
    }

    unsafe fn wide_ptr_to_string(pointer: *const u16) -> String {
        let mut length = 0;
        while *pointer.add(length) != 0 {
            length += 1;
        }
        String::from_utf16_lossy(std::slice::from_raw_parts(pointer, length))
    }
}

#[cfg(not(windows))]
mod wincred {
    use super::*;

    pub fn load(_bucket_id: &str) -> Result<Option<StoredCredential>, String> {
        Ok(None)
    }

    pub fn save(_bucket_id: &str, _credential: &CredentialInput) -> Result<(), String> {
        Err("Windows 凭据管理器仅支持 Windows".into())
    }

    pub fn delete(_bucket_id: &str) -> Result<(), String> {
        Ok(())
    }
}

pub fn load_credential(bucket_id: &str) -> Result<Option<StoredCredential>, String> {
    wincred::load(bucket_id)
}

pub fn save_credential(bucket_id: &str, credential: &CredentialInput) -> Result<(), String> {
    wincred::save(bucket_id, credential)
}

pub fn delete_credential(bucket_id: &str) -> Result<(), String> {
    wincred::delete(bucket_id)
}

pub fn require_credential(bucket_id: &str) -> Result<StoredCredential, String> {
    load_credential(bucket_id)?.ok_or_else(|| "未找到 AccessKey，请重新编辑资源桶凭据".to_string())
}
