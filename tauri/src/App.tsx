import {
  useEffect,
  useMemo,
  useRef,
  useState,
  type MouseEvent as ReactMouseEvent,
} from "react";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { open, save } from "@tauri-apps/plugin-dialog";
import { writeText } from "@tauri-apps/plugin-clipboard-manager";
import { useVirtualizer } from "@tanstack/react-virtual";
import {
  ArrowLeft,
  ArrowRight,
  ArrowDownUp,
  ArrowUp,
  Bookmark,
  Check,
  Cloud,
  Copy,
  Database,
  Download,
  File,
  FileArchive,
  FileText,
  Folder,
  FolderPlus,
  FolderUp,
  Home,
  Image as ImageIcon,
  LoaderCircle,
  MoreHorizontal,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Star,
  Trash2,
  Upload,
  X,
} from "lucide-react";
import { api, settingsFromWorkspace } from "./api";
import {
  bucketLabel,
  displayPath,
  formatModified,
  isTextFile,
  normalizePrefix,
  parentPrefix,
  regionName,
  selectedEntries,
  uploadPercentage,
  validateFolderName,
  validateObjectName,
} from "./data";
import type {
  BucketProfile,
  BucketTab,
  DirectoryBookmark,
  MenuItem,
  ObjectEntry,
  RetryDescriptor,
  StoredCredential,
  ToastMessage,
  TransferProgress,
  TransferTask,
  WorkspaceState,
} from "./types";
import { BucketEditor } from "./components/BucketEditor";
import { ConfirmModal } from "./components/ConfirmModal";
import { ContextMenu } from "./components/ContextMenu";
import { DestinationModal } from "./components/DestinationModal";
import { PreviewModal } from "./components/PreviewModal";
import { PromptModal } from "./components/PromptModal";
import { SettingsModal, type SettingsResult } from "./components/SettingsModal";
import { TransfersModal } from "./components/TransfersModal";

function createTab(bucket: BucketProfile): BucketTab {
  const prefix = normalizePrefix(bucket.Prefix);
  return {
    bucketId: bucket.Id,
    prefix,
    history: [prefix],
    historyIndex: 0,
    items: [],
    loading: false,
    search: "",
    selectedKey: null,
    checkedKeys: new Set(),
    refreshVersion: 0,
  };
}

function errorText(reason: unknown): string {
  if (reason instanceof Error) return reason.message;
  return String(reason);
}

function isCanceled(reason: unknown): boolean {
  return errorText(reason).includes("CANCELED");
}

function bookmarkName(bookmark: DirectoryBookmark, bucket?: BucketProfile): string {
  if (bookmark.DisplayName.trim()) return bookmark.DisplayName;
  const folder = bookmark.Prefix.replace(/\/+$/, "").split("/").filter(Boolean).at(-1);
  const suffix = bucket ? bucket.Name : "";
  return `${folder || "/"}${suffix ? ` · ${suffix}` : ""}`;
}

function fileParentKey(entry: ObjectEntry): string {
  if (entry.IsFolder) {
    return entry.Key.slice(0, Math.max(0, entry.Key.length - entry.Name.length - 1));
  }
  return entry.Key.slice(0, Math.max(0, entry.Key.length - entry.Name.length));
}

function objectIconKind(
  entry: ObjectEntry,
): "folder" | "image" | "text" | "archive" | "file" {
  if (entry.IsFolder) return "folder";
  if (/\.(png|jpe?g|bmp|ico|gif|webp|svg)$/i.test(entry.Name)) return "image";
  if (isTextFile(entry.Name)) return "text";
  if (/\.(zip|7z|rar|tar|gz|tgz|bz2|xz)$/i.test(entry.Name)) return "archive";
  return "file";
}

function ObjectGlyph({ entry }: { entry: ObjectEntry }) {
  const kind = objectIconKind(entry);
  return (
    <span className={`file-icon ${kind}`}>
      {kind === "folder" ? (
        <Folder size={16} />
      ) : kind === "image" ? (
        <ImageIcon size={16} />
      ) : kind === "text" ? (
        <FileText size={16} />
      ) : kind === "archive" ? (
        <FileArchive size={16} />
      ) : (
        <File size={16} />
      )}
    </span>
  );
}

function transferTitle(descriptor: RetryDescriptor): string {
  const count = descriptor.entries?.length ?? descriptor.paths?.length ?? 1;
  switch (descriptor.kind) {
    case "upload":
      return count === 1 ? "上传本地项目" : `上传 ${count} 个本地项目`;
    case "download":
      return count === 1 ? `下载“${descriptor.entries?.[0]?.Name ?? "对象"}”` : `下载 ${count} 个对象`;
    case "delete":
      return count === 1 ? `删除“${descriptor.entries?.[0]?.Name ?? "对象"}”` : `删除 ${count} 个对象`;
    case "copy":
      return count === 1 ? `复制“${descriptor.entries?.[0]?.Name ?? "对象"}”` : `复制 ${count} 个对象`;
    case "move":
      return count === 1 ? `移动“${descriptor.entries?.[0]?.Name ?? "对象"}”` : `移动 ${count} 个对象`;
  }
}

function transferOperation(kind: RetryDescriptor["kind"]): string {
  return { upload: "上传", download: "下载", delete: "删除", copy: "复制", move: "移动" }[kind];
}

function SelectionCheckbox({
  checked,
  indeterminate = false,
  onChange,
  title,
}: {
  checked: boolean;
  indeterminate?: boolean;
  onChange: () => void;
  title: string;
}) {
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = indeterminate;
  }, [indeterminate]);
  return (
    <input
      ref={ref}
      type="checkbox"
      checked={checked}
      title={title}
      onClick={(event) => event.stopPropagation()}
      onChange={onChange}
    />
  );
}

type PromptState = {
  title: string;
  message?: string;
  placeholder?: string;
  initialValue?: string;
  confirmText?: string;
  validate?: (value: string) => string | null;
  onSubmit: (value: string) => void | Promise<void>;
};

type ConfirmState = {
  title: string;
  message: string;
  detail?: string;
  confirmText?: string;
  onConfirm: () => void | Promise<void>;
};

type DestinationState = {
  mode: "copy" | "move";
  bucket: BucketProfile;
  entries: ObjectEntry[];
  initialPrefix: string;
  initialName: string;
  allowRename: boolean;
};

export default function App() {
  const [workspace, setWorkspace] = useState<WorkspaceState | null>(null);
  const [tabs, setTabs] = useState<BucketTab[]>([]);
  const [activeBucketId, setActiveBucketId] = useState("");
  const [tasks, setTasks] = useState<TransferTask[]>([]);
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const [ready, setReady] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const [transfersOpen, setTransfersOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [bucketEditor, setBucketEditor] = useState<{
    bucket: BucketProfile | null;
    credential: StoredCredential | null;
  } | null>(null);
  const [preview, setPreview] = useState<{
    bucket: BucketProfile;
    entry: ObjectEntry;
  } | null>(null);
  const [destination, setDestination] = useState<DestinationState | null>(null);
  const [prompt, setPrompt] = useState<PromptState | null>(null);
  const [confirm, setConfirm] = useState<ConfirmState | null>(null);
  const [menu, setMenu] = useState<{
    x: number;
    y: number;
    items: MenuItem[];
  } | null>(null);
  const [systemDark, setSystemDark] = useState(false);

  const workspaceRef = useRef<WorkspaceState | null>(null);
  const tabsRef = useRef<BucketTab[]>([]);
  const activeIdRef = useRef("");
  const tasksRef = useRef<TransferTask[]>([]);
  const refreshVersions = useRef(new Map<string, number>());
  const uploadPathsRef = useRef<(paths: string[]) => void>(() => undefined);
  const scrollRef = useRef<HTMLDivElement>(null);

  const commitWorkspace = (next: WorkspaceState) => {
    workspaceRef.current = next;
    setWorkspace(next);
  };

  const commitTabs = (next: BucketTab[]) => {
    tabsRef.current = next;
    setTabs(next);
  };

  const patchTab = (bucketId: string, patcher: (tab: BucketTab) => BucketTab) => {
    commitTabs(
      tabsRef.current.map((tab) => (tab.bucketId === bucketId ? patcher(tab) : tab)),
    );
  };

  const commitTasks = (next: TransferTask[]) => {
    tasksRef.current = next;
    setTasks(next);
  };

  const findBucket = (bucketId: string) =>
    workspaceRef.current?.Buckets.find((bucket) => bucket.Id === bucketId);

  const showToast = (text: string, tone: ToastMessage["Tone"] = "info") => {
    const id = Date.now() + Math.floor(Math.random() * 1000);
    setToasts((current) => {
      const withoutDuplicate = current.filter(
        (toast) => toast.Text !== text || toast.Tone !== tone,
      );
      return [...withoutDuplicate, { Id: id, Tone: tone, Text: text }].slice(-4);
    });
    window.setTimeout(
      () => setToasts((current) => current.filter((toast) => toast.Id !== id)),
      4200,
    );
  };

  const setActive = (bucketId: string) => {
    activeIdRef.current = bucketId;
    setActiveBucketId(bucketId);
    const current = workspaceRef.current;
    if (current && current.ActiveBucketId !== bucketId) {
      commitWorkspace({ ...current, ActiveBucketId: bucketId });
    }
  };

  async function refreshTab(bucketId: string, prefix?: string) {
    const tab = tabsRef.current.find((item) => item.bucketId === bucketId);
    const bucket = findBucket(bucketId);
    if (!tab || !bucket || !workspaceRef.current) return;
    const targetPrefix = normalizePrefix(prefix ?? tab.prefix);
    const version = (refreshVersions.current.get(bucketId) ?? 0) + 1;
    refreshVersions.current.set(bucketId, version);
    patchTab(bucketId, (current) => ({
      ...current,
      prefix: targetPrefix,
      loading: true,
      items: [],
      selectedKey: null,
      checkedKeys: new Set(),
      refreshVersion: version,
    }));
    try {
      const result = await api.listObjects(
        bucket,
        targetPrefix,
        settingsFromWorkspace(workspaceRef.current),
      );
      if (refreshVersions.current.get(bucketId) !== version) return;
      patchTab(bucketId, (current) => ({
        ...current,
        loading: false,
        items: result.objects,
      }));
      if (result.endpointCorrected && workspaceRef.current) {
        commitWorkspace({
          ...workspaceRef.current,
          Buckets: workspaceRef.current.Buckets.map((item) =>
            item.Id === bucketId
              ? {
                  ...item,
                  Endpoint: result.endpoint,
                  RegionCode: result.regionCode,
                  RegionName: regionName(result.regionCode),
                }
              : item,
          ),
        });
        showToast(`已自动修正区域为 ${result.regionCode}`, "success");
      }
    } catch (reason) {
      if (refreshVersions.current.get(bucketId) !== version) return;
      const message = `加载失败：${errorText(reason)}`;
      patchTab(bucketId, (current) => ({
        ...current,
        loading: false,
        items: [],
      }));
      showToast(message, "error");
    }
  }

  const openBucket = (bucket: BucketProfile) => {
    const existing = tabsRef.current.find((tab) => tab.bucketId === bucket.Id);
    if (existing) {
      setActive(bucket.Id);
      return;
    }
    const tab = createTab(bucket);
    commitTabs([...tabsRef.current, tab]);
    const current = workspaceRef.current;
    if (current) {
      const openIds = current.OpenBucketIds.includes(bucket.Id)
        ? current.OpenBucketIds
        : [...current.OpenBucketIds, bucket.Id];
      commitWorkspace({ ...current, OpenBucketIds: openIds, ActiveBucketId: bucket.Id });
    }
    setActive(bucket.Id);
    void refreshTab(bucket.Id);
  };

  const closeBucket = (bucketId: string) => {
    const index = tabsRef.current.findIndex((tab) => tab.bucketId === bucketId);
    if (index < 0) return;
    const next = tabsRef.current.filter((tab) => tab.bucketId !== bucketId);
    commitTabs(next);
    const nextActive =
      activeIdRef.current === bucketId
        ? next[Math.min(index, next.length - 1)]?.bucketId ?? ""
        : activeIdRef.current;
    const current = workspaceRef.current;
    if (current) {
      commitWorkspace({
        ...current,
        OpenBucketIds: next.map((tab) => tab.bucketId),
        ActiveBucketId: nextActive,
      });
    }
    setActive(nextActive);
  };

  const navigate = (bucketId: string, prefix: string, recordHistory = true) => {
    const tab = tabsRef.current.find((item) => item.bucketId === bucketId);
    const bucket = findBucket(bucketId);
    if (!tab || !bucket) return;
    const target = normalizePrefix(prefix);
    patchTab(bucketId, (current) => {
      const history = recordHistory
        ? current.history.slice(0, current.historyIndex + 1)
        : current.history;
      const shouldRecord = recordHistory && history.at(-1) !== target;
      if (shouldRecord) history.push(target);
      return {
        ...current,
        prefix: target,
        history,
        historyIndex: shouldRecord ? history.length - 1 : current.historyIndex,
        loading: true,
        items: [],
        selectedKey: null,
        checkedKeys: new Set(),
      };
    });
    void refreshTab(bucketId, target);
  };

  const navigateHistory = (bucketId: string, offset: number) => {
    const tab = tabsRef.current.find((item) => item.bucketId === bucketId);
    if (!tab) return;
    const nextIndex = tab.historyIndex + offset;
    if (nextIndex < 0 || nextIndex >= tab.history.length) return;
    const target = tab.history[nextIndex];
    patchTab(bucketId, (current) => ({
      ...current,
      historyIndex: nextIndex,
      loading: true,
    }));
    void refreshTab(bucketId, target);
  };

  const executeTransfer = async (descriptor: RetryDescriptor) => {
    const currentWorkspace = workspaceRef.current;
    if (!currentWorkspace) return;
    const bucket =
      findBucket(descriptor.bucket.Id) ??
      descriptor.bucket;
    const settings = settingsFromWorkspace(currentWorkspace);
    const taskId = crypto.randomUUID();
    const task: TransferTask = {
      Id: taskId,
      BucketId: bucket.Id,
      BucketName: bucketLabel(bucket),
      Operation: transferOperation(descriptor.kind),
      Title: transferTitle(descriptor),
      Current: 0,
      Total: 0,
      Detail: "正在准备…",
      State: "进行中",
      IsRunning: true,
      Descriptor: { ...descriptor, bucket },
    };
    commitTasks([task, ...tasksRef.current]);
    try {
      let message = "";
      if (descriptor.kind === "upload") {
        const result = await api.uploadPaths(
          taskId,
          bucket,
          normalizePrefix(descriptor.prefix ?? ""),
          descriptor.paths ?? [],
          settings,
        );
        message = `上传完成，共 ${result.fileCount} 个文件`;
      } else if (descriptor.kind === "download") {
        const result = await api.downloadEntries(
          taskId,
          bucket,
          descriptor.entries ?? [],
          descriptor.destination ?? "",
          settings,
        );
        message =
          result.renamedCount > 0
            ? `下载完成，共 ${result.fileCount} 个文件，${result.renamedCount} 个自动重命名`
            : `下载完成，共 ${result.fileCount} 个文件`;
      } else if (descriptor.kind === "delete") {
        const result = await api.deleteEntries(
          taskId,
          bucket,
          descriptor.entries ?? [],
          settings,
        );
        message = `删除完成，共移除 ${result.objectCount} 个对象`;
        if (workspaceRef.current) {
          const prefixes = (descriptor.entries ?? [])
            .filter((entry) => entry.IsFolder)
            .map((entry) => entry.Key);
          commitWorkspace({
            ...workspaceRef.current,
            Bookmarks: workspaceRef.current.Bookmarks.filter(
              (bookmark) =>
                bookmark.BucketId !== bucket.Id ||
                !prefixes.some((prefix) => bookmark.Prefix.startsWith(prefix)),
            ),
          });
        }
      } else {
        const operation =
          descriptor.kind === "move" ? api.moveEntries : api.copyEntries;
        const result = await operation(
          taskId,
          bucket,
          descriptor.entries ?? [],
          normalizePrefix(descriptor.targetPrefix ?? ""),
          settings,
        );
        message = `${descriptor.kind === "move" ? "移动" : "复制"}完成，共 ${result.objectCount} 个对象`;
        if (
          descriptor.kind === "move" &&
          workspaceRef.current
        ) {
          let bookmarks = [...workspaceRef.current.Bookmarks];
          for (const entry of descriptor.entries ?? []) {
            if (!entry.IsFolder) continue;
            const targetRoot = `${normalizePrefix(descriptor.targetPrefix ?? "")}${entry.Name}/`;
            bookmarks = bookmarks.map((bookmark) =>
              bookmark.BucketId === bucket.Id &&
              bookmark.Prefix.startsWith(entry.Key)
                ? {
                    ...bookmark,
                    Prefix: `${targetRoot}${bookmark.Prefix.slice(entry.Key.length)}`,
                  }
                : bookmark,
            );
          }
          commitWorkspace({ ...workspaceRef.current, Bookmarks: bookmarks });
        }
      }

      commitTasks(
        tasksRef.current.map((item) =>
          item.Id === taskId
            ? {
                ...item,
                IsRunning: false,
                State: "已完成",
                Current: item.Total || item.Current || 1,
                Total: item.Total || item.Current || 1,
                Detail: message,
              }
            : item,
        ),
      );
      if (descriptor.kind === "upload" || descriptor.kind === "delete") {
        patchTab(bucket.Id, (tab) => ({
          ...tab,
          checkedKeys: new Set(),
          selectedKey: null,
        }));
      }
      if (descriptor.kind !== "download") void refreshTab(bucket.Id);
      showToast(message, "success");
    } catch (reason) {
      const canceled = isCanceled(reason);
      const message = canceled ? "任务已取消" : errorText(reason);
      commitTasks(
        tasksRef.current.map((item) =>
          item.Id === taskId
            ? {
                ...item,
                IsRunning: false,
                State: canceled ? "已取消" : "失败",
                Detail: message,
                Error: canceled ? undefined : message,
              }
            : item,
        ),
      );
      showToast(
        `${transferOperation(descriptor.kind)}${canceled ? "已取消" : `失败：${message}`}`,
        canceled ? "info" : "error",
      );
    }
  };

  const startUpload = (paths: string[]) => {
    const bucket = findBucket(activeIdRef.current);
    const tab = tabsRef.current.find((item) => item.bucketId === activeIdRef.current);
    if (!bucket || !tab) {
      showToast("请先打开一个资源桶", "error");
      return;
    }
    if (paths.length === 0) return;
    void executeTransfer({
      kind: "upload",
      bucket,
      prefix: tab.prefix,
      paths,
    });
  };
  uploadPathsRef.current = startUpload;

  const chooseUploadFiles = async () => {
    const result = await open({ multiple: true, directory: false, title: "选择要上传的文件" });
    if (!result) return;
    startUpload(Array.isArray(result) ? result : [result]);
  };

  const chooseUploadFolder = async () => {
    const result = await open({ multiple: true, directory: true, title: "选择要上传的目录" });
    if (!result) return;
    startUpload(Array.isArray(result) ? result : [result]);
  };

  const downloadEntries = async (bucket: BucketProfile, entries: ObjectEntry[]) => {
    if (!entries.length) {
      showToast("请先选择要下载的文件或文件夹", "error");
      return;
    }
    if (entries.length === 1 && !entries[0].IsFolder) {
      const destinationPath = await save({
        title: `下载 ${entries[0].Name}`,
        defaultPath: entries[0].Name,
      });
      if (!destinationPath) return;
      void executeTransfer({
        kind: "download",
        bucket,
        entries,
        destination: destinationPath,
      });
      return;
    }
    const directory = await open({
      directory: true,
      multiple: false,
      title: `选择${entries.length === 1 ? `“${entries[0].Name}”` : "对象"}的下载位置`,
    });
    if (!directory || Array.isArray(directory)) return;
    void executeTransfer({
      kind: "download",
      bucket,
      entries,
      destination: directory,
    });
  };

  const cancelTask = (taskId: string) => {
    commitTasks(
      tasksRef.current.map((task) =>
        task.Id === taskId ? { ...task, State: "取消中", Detail: "正在取消…" } : task,
      ),
    );
    void api.cancelTask(taskId).catch((reason) =>
      showToast(`取消任务失败：${errorText(reason)}`, "error"),
    );
  };

  const retryTask = (task: TransferTask) => {
    void executeTransfer(task.Descriptor);
  };

  const copyText = async (text: string, message = "已复制到剪贴板") => {
    try {
      await writeText(text);
      showToast(message, "success");
    } catch {
      try {
        await navigator.clipboard.writeText(text);
        showToast(message, "success");
      } catch (reason) {
        showToast(`复制失败：${errorText(reason)}`, "error");
      }
    }
  };

  const copyAddress = async (bucket: BucketProfile, entry: ObjectEntry) => {
    try {
      const address = await api.objectAddress(bucket, entry.Key);
      await copyText(address, "已复制对象原始地址");
    } catch (reason) {
      showToast(`获取地址失败：${errorText(reason)}`, "error");
    }
  };

  const toggleBookmark = (bucket: BucketProfile, prefix: string) => {
    const current = workspaceRef.current;
    if (!current) return;
    const normalized = normalizePrefix(prefix);
    const existing = current.Bookmarks.find(
      (bookmark) => bookmark.BucketId === bucket.Id && bookmark.Prefix === normalized,
    );
    if (existing) {
      commitWorkspace({
        ...current,
        Bookmarks: current.Bookmarks.filter((bookmark) => bookmark.Id !== existing.Id),
      });
      showToast("已取消收藏");
    } else {
      commitWorkspace({
        ...current,
        Bookmarks: [
          ...current.Bookmarks,
          {
            Id: crypto.randomUUID().replace(/-/g, ""),
            BucketId: bucket.Id,
            Prefix: normalized,
            DisplayName: "",
          },
        ],
      });
      showToast("已添加书签", "success");
    }
  };

  const openBookmark = (bookmark: DirectoryBookmark) => {
    const bucket = findBucket(bookmark.BucketId);
    if (!bucket) {
      const current = workspaceRef.current;
      if (current) {
        commitWorkspace({
          ...current,
          Bookmarks: current.Bookmarks.filter((item) => item.Id !== bookmark.Id),
        });
      }
      return;
    }
    openBucket(bucket);
    navigate(bucket.Id, bookmark.Prefix);
  };

  const openObjectMenu = (
    event: ReactMouseEvent,
    bucket: BucketProfile,
    entry: ObjectEntry,
  ) => {
    event.preventDefault();
    event.stopPropagation();
    const isBookmarked = workspaceRef.current?.Bookmarks.some(
      (bookmark) => bookmark.BucketId === bucket.Id && bookmark.Prefix === entry.Key,
    );
    const items: MenuItem[] = [
      { label: "下载", action: () => void downloadEntries(bucket, [entry]) },
      {
        label: "复制到…",
        action: () =>
          setDestination({
            mode: "copy",
            bucket,
            entries: [entry],
            initialPrefix: tabsRef.current.find((tab) => tab.bucketId === bucket.Id)?.prefix ?? bucket.Prefix,
            initialName: entry.Name,
            allowRename: true,
          }),
      },
      {
        label: "移动到…",
        action: () =>
          setDestination({
            mode: "move",
            bucket,
            entries: [entry],
            initialPrefix: tabsRef.current.find((tab) => tab.bucketId === bucket.Id)?.prefix ?? bucket.Prefix,
            initialName: entry.Name,
            allowRename: true,
          }),
      },
      {
        label: "重命名",
        action: () =>
          setPrompt({
            title: entry.IsFolder ? "重命名文件夹" : "重命名文件",
            message: "名称修改后会先复制到新路径，再删除原对象。",
            placeholder: "对象名称",
            initialValue: entry.Name,
            confirmText: "重命名",
            validate: validateObjectName,
            onSubmit: async (value) => {
              if (value === entry.Name) return;
              setPrompt(null);
              const target = { ...entry, Name: value };
              void executeTransfer({
                kind: "move",
                bucket,
                entries: [target],
                targetPrefix: fileParentKey(entry),
              });
            },
          }),
      },
      { separator: true, label: "获取原始地址", action: () => void copyAddress(bucket, entry) },
    ];
    if (entry.IsFolder) {
      items.push({
        label: isBookmarked ? "取消收藏此目录" : "收藏此目录",
        action: () => toggleBookmark(bucket, entry.Key),
      });
    }
    items.push(
      { separator: true, label: "删除", tone: "danger", action: () => confirmDelete(bucket, [entry]) },
    );
    setMenu({ x: event.clientX, y: event.clientY, items });
  };

  const openBucketMenu = (
    event: ReactMouseEvent,
    bucket: BucketProfile,
  ) => {
    event.preventDefault();
    event.stopPropagation();
    const current = workspaceRef.current;
    if (!current) return;
    const index = current.Buckets.findIndex((item) => item.Id === bucket.Id);
    const items: MenuItem[] = [
      {
        label: "编辑资源桶",
        action: () => void editBucket(bucket),
      },
      {
        label: "自定义显示名称",
        action: () =>
          setPrompt({
            title: "自定义资源桶显示名称",
            message: "输入本地显示名称；留空会恢复为真实 Bucket 名称。",
            placeholder: "资源桶显示名称",
            initialValue: bucket.DisplayName,
            confirmText: "保存",
            validate: () => null,
            onSubmit: (value) => {
              const latest = workspaceRef.current;
              if (!latest) return;
              commitWorkspace({
                ...latest,
                Buckets: latest.Buckets.map((item) =>
                  item.Id === bucket.Id ? { ...item, DisplayName: value } : item,
                ),
              });
              setPrompt(null);
              showToast("已更新资源桶显示名称", "success");
            },
          }),
      },
    ];
    if (index > 0) {
      items.push({
        separator: true,
        label: "上移",
        action: () => moveBucket(index, -1),
      });
    }
    if (index < current.Buckets.length - 1) {
      items.push({ label: "下移", action: () => moveBucket(index, 1) });
    }
    items.push({
      separator: true,
      label: "删除资源桶",
      tone: "danger",
      action: () => confirmDeleteBucket(bucket),
    });
    setMenu({ x: event.clientX, y: event.clientY, items });
  };

  const openBookmarkMenu = (
    event: ReactMouseEvent,
    bookmark: DirectoryBookmark,
  ) => {
    event.preventDefault();
    event.stopPropagation();
    const current = workspaceRef.current;
    if (!current) return;
    const index = current.Bookmarks.findIndex((item) => item.Id === bookmark.Id);
    const items: MenuItem[] = [
      { label: "打开书签", action: () => openBookmark(bookmark) },
      {
        label: "自定义显示名称",
        action: () =>
          setPrompt({
            title: "自定义书签显示名称",
            message: "输入书签名称；留空会恢复为目录名称。",
            placeholder: "书签显示名称",
            initialValue: bookmark.DisplayName,
            confirmText: "保存",
            validate: () => null,
            onSubmit: (value) => {
              const latest = workspaceRef.current;
              if (!latest) return;
              commitWorkspace({
                ...latest,
                Bookmarks: latest.Bookmarks.map((item) =>
                  item.Id === bookmark.Id ? { ...item, DisplayName: value } : item,
                ),
              });
              setPrompt(null);
              showToast("已更新书签显示名称", "success");
            },
          }),
      },
    ];
    if (index > 0) {
      items.push({
        separator: true,
        label: "上移",
        action: () => moveBookmark(index, -1),
      });
    }
    if (index < current.Bookmarks.length - 1) {
      items.push({ label: "下移", action: () => moveBookmark(index, 1) });
    }
    items.push({
      separator: true,
      label: "删除书签",
      tone: "danger",
      action: () => {
        const latest = workspaceRef.current;
        if (!latest) return;
        commitWorkspace({
          ...latest,
          Bookmarks: latest.Bookmarks.filter((item) => item.Id !== bookmark.Id),
        });
        showToast("已删除书签");
      },
    });
    setMenu({ x: event.clientX, y: event.clientY, items });
  };

  const moveBucket = (index: number, offset: number) => {
    const current = workspaceRef.current;
    if (!current) return;
    const target = index + offset;
    if (target < 0 || target >= current.Buckets.length) return;
    const buckets = [...current.Buckets];
    [buckets[index], buckets[target]] = [buckets[target], buckets[index]];
    commitWorkspace({ ...current, Buckets: buckets });
  };

  const moveBookmark = (index: number, offset: number) => {
    const current = workspaceRef.current;
    if (!current) return;
    const target = index + offset;
    if (target < 0 || target >= current.Bookmarks.length) return;
    const bookmarks = [...current.Bookmarks];
    [bookmarks[index], bookmarks[target]] = [bookmarks[target], bookmarks[index]];
    commitWorkspace({ ...current, Bookmarks: bookmarks });
  };

  const editBucket = async (bucket: BucketProfile) => {
    try {
      const credential = await api.loadCredential(bucket.Id);
      setBucketEditor({ bucket, credential });
    } catch (reason) {
      setBucketEditor({ bucket, credential: null });
      showToast(`读取凭据失败：${errorText(reason)}`, "error");
    }
  };

  const saveBucket = async (
    profile: BucketProfile,
    credential: { accessKeyId: string; accessKeySecret: string },
  ) => {
    await api.saveCredential(profile.Id, credential);
    const current = workspaceRef.current;
    if (!current) return;
    const exists = current.Buckets.some((bucket) => bucket.Id === profile.Id);
    const buckets = exists
      ? current.Buckets.map((bucket) => (bucket.Id === profile.Id ? profile : bucket))
      : [...current.Buckets, profile];
    commitWorkspace({ ...current, Buckets: buckets });
    setBucketEditor(null);
    if (exists) {
      patchTab(profile.Id, (tab) => ({ ...tab }));
      void refreshTab(profile.Id);
      showToast("资源桶配置已保存", "success");
    } else {
      openBucket(profile);
      showToast("资源桶已添加", "success");
    }
  };

  const confirmDeleteBucket = (bucket: BucketProfile) => {
    setConfirm({
      title: "删除资源桶",
      message: `确定删除资源桶“${bucketLabel(bucket)}”吗？`,
      detail: "只会删除本地配置和 Windows 凭据，不会删除 OSS 上的真实 Bucket。",
      confirmText: "删除",
      onConfirm: async () => {
        try {
          await api.deleteCredential(bucket.Id);
          const current = workspaceRef.current;
          if (current) {
            const bookmarks = current.Bookmarks.filter(
              (bookmark) => bookmark.BucketId !== bucket.Id,
            );
            commitWorkspace({
              ...current,
              Buckets: current.Buckets.filter((item) => item.Id !== bucket.Id),
              Bookmarks: bookmarks,
            });
          }
          closeBucket(bucket.Id);
          setConfirm(null);
          showToast("资源桶配置已删除");
        } catch (reason) {
          setConfirm(null);
          showToast(`删除资源桶失败：${errorText(reason)}`, "error");
        }
      },
    });
  };

  const confirmDelete = (bucket: BucketProfile, entries: ObjectEntry[]) => {
    const single = entries.length === 1 ? entries[0] : null;
    setConfirm({
      title: single?.IsFolder ? "删除文件夹" : "删除对象",
      message: single
        ? `确定删除${single.IsFolder ? "文件夹" : "文件"}“${single.Name}”吗？`
        : `确定删除选中的 ${entries.length} 个对象吗？`,
      detail: single?.IsFolder
        ? "该文件夹前缀下的所有 OSS 对象都会被递归删除，此操作不可撤销。"
        : "OSS 对象删除后不可撤销。",
      confirmText: "删除",
      onConfirm: () => {
        setConfirm(null);
        void executeTransfer({ kind: "delete", bucket, entries });
      },
    });
  };

  const toggleChecked = (bucketId: string, key: string) => {
    patchTab(bucketId, (tab) => {
      const checked = new Set(tab.checkedKeys);
      if (checked.has(key)) checked.delete(key);
      else checked.add(key);
      return { ...tab, checkedKeys: checked, selectedKey: key };
    });
  };

  const toggleAll = (bucket: BucketProfile, items: ObjectEntry[]) => {
    const tab = tabsRef.current.find((item) => item.bucketId === bucket.Id);
    if (!tab) return;
    const allChecked = items.length > 0 && items.every((item) => tab.checkedKeys.has(item.Key));
    patchTab(bucket.Id, (current) => ({
      ...current,
      checkedKeys: allChecked
        ? new Set()
        : new Set(items.map((item) => item.Key)),
    }));
  };

  const openEntry = (bucket: BucketProfile, entry: ObjectEntry) => {
    if (entry.IsFolder) {
      navigate(bucket.Id, entry.Key);
    } else {
      setPreview({ bucket, entry });
    }
  };

  useEffect(() => {
    let disposed = false;
    api
      .loadWorkspace()
      .then((loaded) => {
        if (disposed) return;
        const openIds = loaded.OpenBucketIds.length
          ? loaded.OpenBucketIds.filter((id) =>
              loaded.Buckets.some((bucket) => bucket.Id === id),
            )
          : loaded.Buckets.slice(0, 1).map((bucket) => bucket.Id);
        const tabs = openIds
          .map((id) => loaded.Buckets.find((bucket) => bucket.Id === id))
          .filter((bucket): bucket is BucketProfile => !!bucket)
          .map(createTab);
        const active =
          tabs.find((tab) => tab.bucketId === loaded.ActiveBucketId)?.bucketId ??
          tabs[0]?.bucketId ??
          "";
        workspaceRef.current = { ...loaded, OpenBucketIds: openIds, ActiveBucketId: active };
        tabsRef.current = tabs;
        activeIdRef.current = active;
        setWorkspace(workspaceRef.current);
        setTabs(tabs);
        setActiveBucketId(active);
        setReady(true);
        if (active) {
          window.setTimeout(() => {
            for (const tab of tabs) void refreshTab(tab.bucketId);
          }, 0);
        }
      })
      .catch((reason) => {
        if (!disposed) {
          setReady(true);
          showToast(`加载工作区失败：${errorText(reason)}`, "error");
        }
      });
    return () => {
      disposed = true;
    };
    // Initial workspace hydration only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!ready || !workspace) return;
    const timer = window.setTimeout(() => {
      void api.saveWorkspace(workspace).catch((reason) => {
        showToast(`保存工作区失败：${errorText(reason)}`, "error");
      });
    }, 280);
    return () => window.clearTimeout(timer);
  }, [workspace, ready]);

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const update = () => setSystemDark(media.matches);
    update();
    media.addEventListener("change", update);
    return () => media.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    const mode = workspace?.ThemeMode ?? "Light";
    const theme = mode === "System" ? (systemDark ? "dark" : "light") : mode.toLowerCase();
    document.documentElement.dataset.theme = theme;
    void getCurrentWindow()
      .setTheme(theme as "light" | "dark")
      .catch(() => undefined);
  }, [workspace?.ThemeMode, systemDark]);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    void listen<TransferProgress>("transfer-progress", (event) => {
      const payload = event.payload;
      commitTasks(
        tasksRef.current.map((task) =>
          task.Id === payload.taskId && task.IsRunning
            ? {
                ...task,
                Current: payload.current,
                Total: payload.total,
                Detail: payload.name,
                UploadBytes: payload.uploadBytes,
              }
            : task,
        ),
      );
    }).then((stop) => {
      unlisten = stop;
    });
    return () => unlisten?.();
  }, []);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    let disposed = false;
    void getCurrentWebview()
      .onDragDropEvent((event) => {
        if (event.payload.type === "enter" || event.payload.type === "over") {
          setDragOver(true);
        } else if (event.payload.type === "drop") {
          setDragOver(false);
          const paths = event.payload.paths ?? [];
          if (paths.length) uploadPathsRef.current(paths);
        } else {
          setDragOver(false);
        }
      })
      .then((stop) => {
        if (disposed) stop();
        else unlisten = stop;
      });
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, []);

  const activeTab = tabs.find((tab) => tab.bucketId === activeBucketId) ?? null;
  const activeBucket = activeTab ? findBucket(activeTab.bucketId) : undefined;
  const filteredItems = useMemo(() => {
    if (!activeTab) return [];
    const needle = activeTab.search.trim().toLowerCase();
    if (!needle) return activeTab.items;
    return activeTab.items.filter(
      (item) =>
        item.Name.toLowerCase().includes(needle) ||
        item.Key.toLowerCase().includes(needle),
    );
  }, [activeTab]);
  const objectSettings = useMemo(
    () =>
      workspace
        ? settingsFromWorkspace(workspace)
        : {
            uploadConcurrency: 3,
            downloadConcurrency: 3,
            requestTimeoutSeconds: 60,
            retryCount: 5,
          },
    [workspace],
  );

  const virtualizer = useVirtualizer({
    count: filteredItems.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => 38,
    overscan: 12,
  });

  if (!ready) {
    return (
      <div className="app-loading">
        <img src="/brand.svg" alt="" />
        <strong>OSS Studio</strong>
        <span>正在加载工作区…</span>
      </div>
    );
  }

  const allChecked =
    filteredItems.length > 0 &&
    filteredItems.every((item) => activeTab?.checkedKeys.has(item.Key));
  const someChecked =
    filteredItems.some((item) => activeTab?.checkedKeys.has(item.Key)) && !allChecked;
  const activeUploads = tasks.filter(
    (task) => task.IsRunning && task.Descriptor.kind === "upload",
  );
  const currentUpload = activeUploads[0];
  const cancelingUpload = activeUploads.length === 1 && currentUpload.State === "取消中";
  let uploadPercent: number | undefined;
  if (currentUpload && activeUploads.every((task) => task.UploadBytes)) {
    let sentBytes = 0;
    let totalBytes = 0;
    let completedFiles = 0;
    let totalFiles = 0;
    for (const task of activeUploads) {
      sentBytes += task.UploadBytes?.sent ?? 0;
      totalBytes += task.UploadBytes?.total ?? 0;
      completedFiles += task.Current;
      totalFiles += task.Total;
    }
    uploadPercent =
      totalBytes > 0
        ? uploadPercentage(sentBytes, totalBytes)
        : uploadPercentage(completedFiles, totalFiles);
  }
  let uploadStatus = "正在上传…";
  const uploadLabel = uploadPercent === undefined ? "正在上传" : `上传 ${uploadPercent}%`;
  if (activeUploads.length > 1) {
    uploadStatus = `${uploadLabel} · ${activeUploads.length} 个任务`;
  } else if (cancelingUpload) {
    uploadStatus = "正在取消上传…";
  } else if (currentUpload && currentUpload.Total > 0) {
    uploadStatus = `${uploadLabel} · ${currentUpload.Current}/${currentUpload.Total} 个文件`;
  }
  const isBookmarked =
    !!activeTab &&
    !!workspace?.Bookmarks.some(
      (bookmark) =>
        bookmark.BucketId === activeTab.bucketId &&
        bookmark.Prefix === activeTab.prefix,
    );

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand">
          <img src="/brand.svg" alt="" />
          <span>OSS Studio</span>
        </div>
        <div className="topbar-actions">
          <button
            className="button ghost"
            title="查看上传、下载和删除进度"
            onClick={() => setTransfersOpen(true)}
          >
            <ArrowDownUp size={15} />
            传输任务
            {tasks.some((task) => task.IsRunning) && <span className="busy-dot" />}
          </button>
          <button
            className="button ghost"
            title="调整传输性能和主题"
            onClick={() => setSettingsOpen(true)}
          >
            <Settings size={15} />
            设置
          </button>
        </div>
      </header>

      <div className="app-body">
        <aside className="sidebar">
          <section className="sidebar-section">
            <div className="sidebar-heading">
              <span>资源桶（{workspace?.Buckets.length ?? 0}）</span>
              <div>
                <button
                  className="icon-button"
                  title="刷新当前资源桶"
                  onClick={() => activeTab && void refreshTab(activeTab.bucketId)}
                >
                  <RefreshCw size={14} />
                </button>
                <button
                  className="icon-button"
                  title="添加资源桶"
                  onClick={() => setBucketEditor({ bucket: null, credential: null })}
                >
                  <Plus size={15} />
                </button>
              </div>
            </div>
            <div className="sidebar-scroll">
              {workspace?.Buckets.map((bucket) => {
                const opened = tabs.some((tab) => tab.bucketId === bucket.Id);
                const isActive = bucket.Id === activeBucketId;
                return (
                  <button
                    key={bucket.Id}
                    className={`bucket-row ${isActive ? "active" : ""}`}
                    onClick={() => openBucket(bucket)}
                    onContextMenu={(event) => openBucketMenu(event, bucket)}
                    title={`${bucket.Name} · ${bucket.RegionName || bucket.RegionCode}`}
                  >
                    <span className="bucket-icon">
                      <Database size={16} />
                    </span>
                    <span className="bucket-copy">
                      <strong>{bucketLabel(bucket)}</strong>
                      <small>
                        {bucket.Name}
                        {bucket.DisplayName ? ` · ${bucket.RegionName}` : ""}
                      </small>
                    </span>
                    {opened && <span className="open-mark" />}
                  </button>
                );
              })}
              {!workspace?.Buckets.length && (
                <div className="sidebar-empty">
                  <Cloud size={24} />
                  <span>尚未添加资源桶</span>
                  <button
                    className="button accent small"
                    onClick={() => setBucketEditor({ bucket: null, credential: null })}
                  >
                    添加资源桶
                  </button>
                </div>
              )}
            </div>
          </section>

          <section className="sidebar-section bookmark-section">
            <div className="sidebar-heading">
              <span>书签（{workspace?.Bookmarks.length ?? 0}）</span>
              {activeTab && activeBucket && (
                <button
                  className={`icon-button ${isBookmarked ? "starred" : ""}`}
                  title={isBookmarked ? "取消收藏当前目录" : "收藏当前目录"}
                  onClick={() => toggleBookmark(activeBucket, activeTab.prefix)}
                >
                  <Star size={15} fill={isBookmarked ? "currentColor" : "none"} />
                </button>
              )}
            </div>
            <div className="bookmark-frame">
              <div className="sidebar-scroll">
                {workspace?.Bookmarks.map((bookmark) => {
                  const bucket = findBucket(bookmark.BucketId);
                  return (
                    <button
                      key={bookmark.Id}
                      className="bookmark-row"
                      onClick={() => openBookmark(bookmark)}
                      onContextMenu={(event) => openBookmarkMenu(event, bookmark)}
                      title={`oss://${bucket?.Name ?? "?"}/${bookmark.Prefix}`}
                    >
                      <Bookmark size={14} />
                      <span>{bookmarkName(bookmark, bucket)}</span>
                    </button>
                  );
                })}
                {!workspace?.Bookmarks.length && (
                  <div className="bookmark-empty">点击工具栏星标收藏当前目录</div>
                )}
              </div>
            </div>
          </section>
        </aside>

        <main className="workspace">
          {tabs.length > 0 && (
            <div className="tabbar">
              {tabs.map((tab) => {
                const bucket = findBucket(tab.bucketId);
                if (!bucket) return null;
                return (
                  <button
                    key={tab.bucketId}
                    className={`tab ${tab.bucketId === activeBucketId ? "active" : ""}`}
                    onClick={() => setActive(tab.bucketId)}
                    title={`oss://${bucket.Name}/${tab.prefix}`}
                  >
                    <Database size={14} />
                    <span>{bucketLabel(bucket)}</span>
                    <span
                      className="tab-close"
                      role="button"
                      tabIndex={0}
                      title="关闭标签页"
                      onClick={(event) => {
                        event.stopPropagation();
                        closeBucket(tab.bucketId);
                      }}
                      onKeyDown={(event) => {
                        if (event.key === "Enter") closeBucket(tab.bucketId);
                      }}
                    >
                      <X size={13} />
                    </span>
                  </button>
                );
              })}
            </div>
          )}

          {!activeTab || !activeBucket ? (
            <div className="welcome">
              <div className="welcome-logo">
                <img src="/brand.svg" alt="" />
              </div>
              <h1>连接你的阿里云 OSS</h1>
              <p>
                资源桶配置与 AccessKey 分别保存在本地工作区和 Windows
                凭据管理器。添加后即可在一个窗口内用多标签页浏览。
              </p>
              <button
                className="button accent large"
                onClick={() => setBucketEditor({ bucket: null, credential: null })}
              >
                <Plus size={17} />
                添加资源桶
              </button>
            </div>
          ) : (
            <div className="bucket-page">
              <div className="navigation-bar">
                <div className="nav-buttons">
                  <button
                    className="tool-button"
                    title="返回上一个访问目录"
                    disabled={activeTab.historyIndex <= 0}
                    onClick={() => navigateHistory(activeTab.bucketId, -1)}
                  >
                    <ArrowLeft size={16} />
                  </button>
                  <button
                    className="tool-button"
                    title="前进到下一个访问目录"
                    disabled={activeTab.historyIndex >= activeTab.history.length - 1}
                    onClick={() => navigateHistory(activeTab.bucketId, 1)}
                  >
                    <ArrowRight size={16} />
                  </button>
                  <button
                    className="tool-button"
                    title="返回上一级目录"
                    disabled={activeTab.prefix === normalizePrefix(activeBucket.Prefix)}
                    onClick={() =>
                      navigate(
                        activeBucket.Id,
                        parentPrefix(activeTab.prefix, normalizePrefix(activeBucket.Prefix)),
                      )
                    }
                  >
                    <ArrowUp size={16} />
                  </button>
                  <button
                    className="tool-button"
                    title="刷新当前目录"
                    onClick={() => void refreshTab(activeBucket.Id)}
                  >
                    <RefreshCw size={15} />
                  </button>
                  <button
                    className="tool-button"
                    title="返回资源桶预设目录"
                    onClick={() => navigate(activeBucket.Id, activeBucket.Prefix)}
                  >
                    <Home size={15} />
                  </button>
                  <button
                    className={`tool-button ${isBookmarked ? "starred" : ""}`}
                    title={isBookmarked ? "取消收藏当前目录" : "收藏当前目录"}
                    onClick={() => toggleBookmark(activeBucket, activeTab.prefix)}
                  >
                    <Star size={15} fill={isBookmarked ? "currentColor" : "none"} />
                    <span>{isBookmarked ? "已收藏" : "未收藏"}</span>
                  </button>
                </div>
                <div className="path-pill" title={displayPath(activeBucket, activeTab.prefix)}>
                  <Cloud size={13} />
                  {displayPath(activeBucket, activeTab.prefix)}
                </div>
              </div>

              <div className="command-bar">
                <div className="command-buttons">
                  <button
                    className="tool-button"
                    title="上传一个或多个文件到当前目录"
                    onClick={() => void chooseUploadFiles()}
                  >
                    <Upload size={15} />
                    文件
                  </button>
                  <button
                    className="tool-button"
                    title="上传本地目录到当前目录"
                    onClick={() => void chooseUploadFolder()}
                  >
                    <FolderUp size={15} />
                    目录
                  </button>
                  <button
                    className="tool-button"
                    title="在当前路径创建新目录"
                    onClick={() =>
                      setPrompt({
                        title: "创建目录",
                        message: `将在 ${displayPath(activeBucket, activeTab.prefix)} 下创建目录。`,
                        placeholder: "目录名称",
                        validate: validateFolderName,
                        confirmText: "创建",
                        onSubmit: async (value) => {
                          await api.createFolder(
                            activeBucket,
                            `${activeTab.prefix}${value}/`,
                            settingsFromWorkspace(workspace!),
                          );
                          setPrompt(null);
                          showToast(`已创建目录：${value}`, "success");
                          void refreshTab(activeBucket.Id);
                        },
                      })
                    }
                  >
                    <FolderPlus size={15} />
                    创建目录
                  </button>
                  <span className="command-separator" />
                  <button
                    className="tool-button"
                    title="选择或取消选择当前目录中的全部对象"
                    onClick={() => toggleAll(activeBucket, filteredItems)}
                  >
                    <Check size={15} />
                    全选
                  </button>
                  <button
                    className="tool-button"
                    title="下载当前选中的文件或目录"
                    onClick={() =>
                      void downloadEntries(
                        activeBucket,
                        selectedEntries(activeTab.items, activeTab.checkedKeys),
                      )
                    }
                  >
                    <Download size={15} />
                    下载
                  </button>
                  <button
                    className="tool-button"
                    title="复制当前选中的对象到其它 OSS 目录"
                    onClick={() => {
                      const entries = selectedEntries(activeTab.items, activeTab.checkedKeys);
                      if (!entries.length) {
                        showToast("请先选择要复制的对象", "error");
                        return;
                      }
                      setDestination({
                        mode: "copy",
                        bucket: activeBucket,
                        entries,
                        initialPrefix: activeTab.prefix,
                        initialName: entries.length === 1 ? entries[0].Name : "",
                        allowRename: entries.length === 1,
                      });
                    }}
                  >
                    <Copy size={15} />
                    复制
                  </button>
                  <button
                    className="tool-button danger"
                    title="删除当前勾选的所有文件或目录"
                    onClick={() => {
                      const entries = selectedEntries(activeTab.items, activeTab.checkedKeys);
                      if (!entries.length) {
                        showToast("请先选择要删除的文件或文件夹", "error");
                        return;
                      }
                      confirmDelete(activeBucket, entries);
                    }}
                  >
                    <Trash2 size={15} />
                    删除
                  </button>
                </div>
                <label className="search-box">
                  <Search size={14} />
                  <input
                    value={activeTab.search}
                    placeholder="搜索当前目录"
                    onChange={(event) =>
                      patchTab(activeBucket.Id, (tab) => ({
                        ...tab,
                        search: event.target.value,
                      }))
                    }
                  />
                  {activeTab.search && (
                    <button
                      className="icon-button"
                      title="清空搜索"
                      onClick={() =>
                        patchTab(activeBucket.Id, (tab) => ({ ...tab, search: "" }))
                      }
                    >
                      <X size={13} />
                    </button>
                  )}
                </label>
              </div>

              <div className="object-table">
                <div className="table-header">
                  <div className="selection-cell">
                    <SelectionCheckbox
                      checked={allChecked}
                      indeterminate={someChecked}
                      title="全选当前目录"
                      onChange={() => toggleAll(activeBucket, filteredItems)}
                    />
                  </div>
                  <div>名称</div>
                  <div>大小</div>
                  <div>最后修改时间</div>
                  <div className="align-right">操作</div>
                </div>

                <div
                  className={`table-scroll ${dragOver ? "drag-over" : ""}`}
                  ref={scrollRef}
                >
                  {activeTab.loading && (
                    <div className="loading-rail">
                      <span />
                    </div>
                  )}
                  {!activeTab.loading && filteredItems.length === 0 && (
                    <div className="empty-state table-empty">
                      <Folder size={30} />
                      <strong>
                        {activeTab.search ? "没有匹配的对象" : "当前目录为空"}
                      </strong>
                      <p>
                        {activeTab.search
                          ? "尝试更换关键词，搜索范围仅限当前目录。"
                          : "可以上传文件、创建目录，或从资源管理器拖入文件。"}
                      </p>
                    </div>
                  )}
                  <div
                    className="virtual-canvas"
                    style={{ height: virtualizer.getTotalSize() }}
                  >
                    {virtualizer.getVirtualItems().map((virtualRow) => {
                      const entry = filteredItems[virtualRow.index];
                      const checked = activeTab.checkedKeys.has(entry.Key);
                      return (
                        <div
                          key={entry.Key}
                          className={`object-row ${checked ? "checked" : ""}`}
                          style={{
                            height: virtualRow.size,
                            transform: `translateY(${virtualRow.start}px)`,
                          }}
                          onClick={() => toggleChecked(activeBucket.Id, entry.Key)}
                          onDoubleClick={() => openEntry(activeBucket, entry)}
                          onContextMenu={(event) =>
                            openObjectMenu(event, activeBucket, entry)
                          }
                          title={entry.Key}
                        >
                          <div className="selection-cell">
                            <SelectionCheckbox
                              checked={checked}
                              title="选择此对象"
                              onChange={() => toggleChecked(activeBucket.Id, entry.Key)}
                            />
                          </div>
                          <div className="object-name">
                            <ObjectGlyph entry={entry} />
                            <span>{entry.Name}</span>
                          </div>
                          <div className="muted">{entry.Size}</div>
                          <div className="muted">{formatModified(entry.LastModified)}</div>
                          <div className="row-actions">
                            <button
                              className="mini-button blue"
                              title="下载此文件或目录"
                              onClick={(event) => {
                                event.stopPropagation();
                                void downloadEntries(activeBucket, [entry]);
                              }}
                            >
                              下载
                            </button>
                            <button
                              className="mini-button coral"
                              title="删除此文件或目录"
                              onClick={(event) => {
                                event.stopPropagation();
                                confirmDelete(activeBucket, [entry]);
                              }}
                            >
                              删除
                            </button>
                            <button
                              className="icon-button row-more"
                              title="更多操作"
                              onClick={(event) => openObjectMenu(event, activeBucket, entry)}
                            >
                              <MoreHorizontal size={15} />
                            </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                  {dragOver && (
                    <div className="drop-overlay">
                      <Upload size={30} />
                      <strong>松开以上传到 {displayPath(activeBucket, activeTab.prefix)}</strong>
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}
          {((activeTab && activeBucket) || currentUpload) && (
            <div className="statusbar">
              {activeTab && activeBucket && (
                <span className="statusbar-objects">
                  {filteredItems.length} / {activeTab.items.length} 个对象 ·{" "}
                  {activeTab.checkedKeys.size} 个已选
                </span>
              )}
              {currentUpload && (
                <span className="statusbar-upload" role="status">
                  {uploadPercent !== undefined && !cancelingUpload ? (
                    <span className="progress-track statusbar-progress" aria-hidden="true">
                      <span className="progress-value" style={{ width: `${uploadPercent}%` }} />
                    </span>
                  ) : (
                    <LoaderCircle size={13} className="spin" aria-hidden="true" />
                  )}
                  {uploadStatus}
                </span>
              )}
              {activeTab && activeBucket && (
                <span className="statusbar-endpoint">
                  {activeBucket.RegionName || activeBucket.RegionCode} ·{" "}
                  {activeBucket.UseHttps ? "HTTPS" : "HTTP"}
                </span>
              )}
            </div>
          )}
        </main>
      </div>

      <div className="toast-stack">
        {toasts.map((toast) => (
          <div key={toast.Id} className={`toast ${toast.Tone}`}>
            {toast.Text}
          </div>
        ))}
      </div>

      {menu && (
        <ContextMenu
          x={menu.x}
          y={menu.y}
          items={menu.items}
          onClose={() => setMenu(null)}
        />
      )}

      {bucketEditor && (
        <BucketEditor
          bucket={bucketEditor.bucket}
          credential={bucketEditor.credential}
          nameExists={(name) =>
            workspace?.Buckets.some(
              (bucket) =>
                bucket.Name.toLowerCase() === name.toLowerCase() &&
                bucket.Id !== bucketEditor.bucket?.Id,
            ) ?? false
          }
          onCancel={() => setBucketEditor(null)}
          onSubmit={saveBucket}
        />
      )}

      {settingsOpen && workspace && (
        <SettingsModal
          workspace={workspace}
          onCancel={() => setSettingsOpen(false)}
          onSave={(result: SettingsResult) => {
            commitWorkspace({
              ...workspace,
              UploadConcurrency: result.transferConcurrency,
              DownloadConcurrency: result.transferConcurrency,
              RequestTimeoutSeconds: result.requestTimeoutSeconds,
              RetryCount: result.retryCount,
              ThemeMode: result.themeMode,
            });
            setSettingsOpen(false);
            showToast("设置已保存并立即生效", "success");
          }}
        />
      )}

      {preview && workspace && (
        <PreviewModal
          bucket={preview.bucket}
          entry={preview.entry}
          settings={objectSettings}
          onDownload={(entry) => void downloadEntries(preview.bucket, [entry])}
          onAddress={(entry) => void copyAddress(preview.bucket, entry)}
          onClose={() => setPreview(null)}
        />
      )}

      {destination && workspace && (
        <DestinationModal
          title={destination.mode === "copy" ? "复制对象" : "移动对象"}
          bucket={destination.bucket}
          rootPrefix={destination.bucket.Prefix}
          initialPrefix={destination.initialPrefix}
          initialName={destination.initialName}
          allowRename={destination.allowRename}
          confirmText={destination.mode === "copy" ? "复制" : "移动"}
          sourceKey={
            destination.entries.length === 1 ? destination.entries[0].Key : undefined
          }
          sourceIsFolder={
            destination.entries.length === 1
              ? destination.entries[0].IsFolder
              : undefined
          }
          settings={objectSettings}
          onCancel={() => setDestination(null)}
          onConfirm={(prefix, name) => {
            const entries = destination.entries.map((entry, index) =>
              destination.allowRename && index === 0 ? { ...entry, Name: name } : entry,
            );
            const targetPrefix = prefix;
            setDestination(null);
            void executeTransfer({
              kind: destination.mode,
              bucket: destination.bucket,
              entries,
              targetPrefix,
            });
          }}
        />
      )}

      {prompt && (
        <PromptModal
          title={prompt.title}
          message={prompt.message}
          placeholder={prompt.placeholder}
          initialValue={prompt.initialValue}
          confirmText={prompt.confirmText}
          validate={prompt.validate}
          onCancel={() => setPrompt(null)}
          onSubmit={prompt.onSubmit}
        />
      )}

      {confirm && (
        <ConfirmModal
          title={confirm.title}
          message={confirm.message}
          detail={confirm.detail}
          confirmText={confirm.confirmText}
          onCancel={() => setConfirm(null)}
          onConfirm={() => void confirm.onConfirm()}
        />
      )}

      {transfersOpen && (
        <TransfersModal
          tasks={tasks}
          onCancel={cancelTask}
          onRetry={retryTask}
          onClear={() =>
            commitTasks(tasksRef.current.filter((task) => task.IsRunning))
          }
          onClose={() => setTransfersOpen(false)}
        />
      )}
    </div>
  );
}
