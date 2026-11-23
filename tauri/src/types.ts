export interface BucketProfile {
  Id: string;
  Name: string;
  RegionCode: string;
  RegionName: string;
  Endpoint: string;
  UseHttps: boolean;
  Prefix: string;
  RequestPayer: boolean;
  Note: string;
  KeepLogin: boolean;
  RememberSecret: boolean;
  EndpointMode: string;
  DisplayName: string;
}

export interface DirectoryBookmark {
  Id: string;
  BucketId: string;
  Prefix: string;
  DisplayName: string;
}

export interface WorkspaceState {
  Buckets: BucketProfile[];
  OpenBucketIds: string[];
  ActiveBucketId: string;
  Bookmarks: DirectoryBookmark[];
  UploadConcurrency: number;
  DownloadConcurrency: number;
  RequestTimeoutSeconds: number;
  RetryCount: number;
  ThemeMode: "System" | "Light" | "Dark";
}

export interface ObjectEntry {
  Name: string;
  IsFolder: boolean;
  Size: string;
  StorageClass: string;
  LastModified: string | null;
  Key: string;
  SizeBytes: number;
}

export interface OssSettings {
  uploadConcurrency: number;
  downloadConcurrency: number;
  requestTimeoutSeconds: number;
  retryCount: number;
}

export interface ObjectListResult {
  objects: ObjectEntry[];
  endpoint: string;
  regionCode: string;
  endpointCorrected: boolean;
}

export interface StoredCredential {
  accessKeyId: string;
  accessKeySecret: string;
}

export interface CredentialInput {
  accessKeyId: string;
  accessKeySecret: string;
}

export interface TransferProgress {
  taskId: string;
  current: number;
  total: number;
  name: string;
  uploadBytes?: UploadBytes;
}

export interface UploadBytes {
  sent: number;
  total: number;
}

export interface DownloadResult {
  fileCount: number;
  renamedCount: number;
}

export interface UploadResult {
  fileCount: number;
}

export interface CopyResult {
  objectCount: number;
}

export interface DeleteResult {
  objectCount: number;
}

export interface BucketTab {
  bucketId: string;
  prefix: string;
  history: string[];
  historyIndex: number;
  items: ObjectEntry[];
  loading: boolean;
  search: string;
  selectedKey: string | null;
  checkedKeys: Set<string>;
  refreshVersion: number;
}

export type TransferKind = "upload" | "download" | "delete" | "copy" | "move";

export interface RetryDescriptor {
  kind: TransferKind;
  bucket: BucketProfile;
  prefix?: string;
  paths?: string[];
  entries?: ObjectEntry[];
  destination?: string;
  targetPrefix?: string;
}

export interface TransferTask {
  Id: string;
  BucketId: string;
  BucketName: string;
  Operation: string;
  Title: string;
  Current: number;
  Total: number;
  UploadBytes?: UploadBytes;
  Detail: string;
  State: "进行中" | "取消中" | "已完成" | "已取消" | "失败";
  IsRunning: boolean;
  Error?: string;
  Descriptor: RetryDescriptor;
}

export interface ToastMessage {
  Id: number;
  Tone: "info" | "success" | "error";
  Text: string;
}

export interface MenuItem {
  label: string;
  action: () => void;
  tone?: "normal" | "danger";
  separator?: boolean;
  disabled?: boolean;
}
