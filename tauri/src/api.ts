import { invoke } from "@tauri-apps/api/core";
import type {
  BucketProfile,
  CopyResult,
  CredentialInput,
  DeleteResult,
  DownloadResult,
  ObjectEntry,
  ObjectListResult,
  OssSettings,
  StoredCredential,
  UploadResult,
  WorkspaceState,
} from "./types";

export const api = {
  loadWorkspace: () => invoke<WorkspaceState>("load_workspace"),
  saveWorkspace: (state: WorkspaceState) => invoke<void>("save_workspace", { state }),
  loadCredential: (bucketId: string) =>
    invoke<StoredCredential | null>("load_credential", { bucketId }),
  saveCredential: (bucketId: string, credential: CredentialInput) =>
    invoke<void>("save_credential", { bucketId, credential }),
  deleteCredential: (bucketId: string) =>
    invoke<void>("delete_credential", { bucketId }),
  listObjects: (bucket: BucketProfile, prefix: string, settings: OssSettings) =>
    invoke<ObjectListResult>("list_objects", { bucket, prefix, settings }),
  listChildFolders: (
    bucket: BucketProfile,
    prefix: string,
    settings: OssSettings,
  ) => invoke<string[]>("list_child_folders", { bucket, prefix, settings }),
  createFolder: (bucket: BucketProfile, key: string, settings: OssSettings) =>
    invoke<void>("create_folder", { bucket, key, settings }),
  uploadPaths: (
    taskId: string,
    bucket: BucketProfile,
    prefix: string,
    paths: string[],
    settings: OssSettings,
  ) =>
    invoke<UploadResult>("upload_paths", {
      taskId,
      bucket,
      prefix,
      paths,
      settings,
    }),
  downloadEntries: (
    taskId: string,
    bucket: BucketProfile,
    entries: ObjectEntry[],
    destination: string,
    settings: OssSettings,
  ) =>
    invoke<DownloadResult>("download_entries", {
      taskId,
      bucket,
      entries,
      destination,
      settings,
    }),
  deleteEntries: (
    taskId: string,
    bucket: BucketProfile,
    entries: ObjectEntry[],
    settings: OssSettings,
  ) =>
    invoke<DeleteResult>("delete_entries", {
      taskId,
      bucket,
      entries,
      settings,
    }),
  copyEntries: (
    taskId: string,
    bucket: BucketProfile,
    entries: ObjectEntry[],
    targetPrefix: string,
    settings: OssSettings,
  ) =>
    invoke<CopyResult>("copy_entries", {
      taskId,
      bucket,
      entries,
      targetPrefix,
      settings,
    }),
  moveEntries: (
    taskId: string,
    bucket: BucketProfile,
    entries: ObjectEntry[],
    targetPrefix: string,
    settings: OssSettings,
  ) =>
    invoke<CopyResult>("move_entries", {
      taskId,
      bucket,
      entries,
      targetPrefix,
      settings,
    }),
  getTextObject: (
    bucket: BucketProfile,
    key: string,
    maximumBytes: number,
    settings: OssSettings,
  ) =>
    invoke<string>("get_text_object", {
      bucket,
      key,
      maximumBytes,
      settings,
    }),
  getObjectBytes: (
    bucket: BucketProfile,
    key: string,
    maximumBytes: number,
    settings: OssSettings,
  ) =>
    invoke<string>("get_object_bytes", {
      bucket,
      key,
      maximumBytes,
      settings,
    }),
  putTextObject: (
    bucket: BucketProfile,
    key: string,
    content: string,
    settings: OssSettings,
  ) => invoke<void>("put_text_object", { bucket, key, content, settings }),
  objectAddress: (bucket: BucketProfile, key: string) =>
    invoke<string>("object_address", { bucket, key }),
  cancelTask: (taskId: string) => invoke<void>("cancel_task", { taskId }),
};

export function settingsFromWorkspace(
  workspace: WorkspaceState,
): OssSettings {
  return {
    uploadConcurrency: workspace.UploadConcurrency,
    downloadConcurrency: workspace.DownloadConcurrency,
    requestTimeoutSeconds: workspace.RequestTimeoutSeconds,
    retryCount: workspace.RetryCount,
  };
}
