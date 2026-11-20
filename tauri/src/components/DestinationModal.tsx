import { useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronRight,
  Folder,
  FolderPlus,
  LoaderCircle,
  Pencil,
  RefreshCw,
  Trash2,
} from "lucide-react";
import { api } from "../api";
import type {
  BucketProfile,
  ObjectEntry,
  OssSettings,
} from "../types";
import { normalizePrefix, validateFolderName } from "../data";
import { ConfirmModal } from "./ConfirmModal";
import { Modal } from "./Modal";
import { PromptModal } from "./PromptModal";

function folderLabel(prefix: string, rootPrefix: string): string {
  const relative = prefix.startsWith(rootPrefix)
    ? prefix.slice(rootPrefix.length)
    : prefix;
  const segments = relative.replace(/\/+$/, "").split("/").filter(Boolean);
  return segments.at(-1) ?? "根目录";
}

function parentPrefix(prefix: string, rootPrefix: string): string {
  const normalized = normalizePrefix(prefix);
  if (normalized === rootPrefix) return rootPrefix;
  const segments = normalized.replace(/\/+$/, "").split("/");
  segments.pop();
  const parent = segments.length ? `${segments.join("/")}/` : "";
  return parent.length < rootPrefix.length ? rootPrefix : parent;
}

function ancestorPrefixes(root: string, target: string): string[] {
  if (!target.startsWith(root) || target === root) return [];
  const rest = target.slice(root.length).replace(/\/+$/, "");
  const segments = rest.split("/").filter(Boolean);
  const result = [root];
  let current = root;
  for (let index = 0; index < segments.length - 1; index += 1) {
    current += `${segments[index]}/`;
    result.push(current);
  }
  return result;
}

export function DestinationModal({
  title,
  bucket,
  rootPrefix,
  initialPrefix,
  initialName,
  allowRename,
  confirmText,
  sourceKey,
  sourceIsFolder,
  settings,
  onCancel,
  onConfirm,
}: {
  title: string;
  bucket: BucketProfile;
  rootPrefix: string;
  initialPrefix: string;
  initialName: string;
  allowRename: boolean;
  confirmText: string;
  sourceKey?: string;
  sourceIsFolder?: boolean;
  settings: OssSettings;
  onCancel: () => void;
  onConfirm: (prefix: string, name: string) => void;
}) {
  const root = normalizePrefix(rootPrefix);
  const [children, setChildren] = useState<Record<string, string[]>>({});
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set([root]));
  const [loading, setLoading] = useState<Set<string>>(new Set());
  const [selected, setSelected] = useState(
    normalizePrefix(initialPrefix).startsWith(root)
      ? normalizePrefix(initialPrefix)
      : root,
  );
  const [name, setName] = useState(initialName);
  const [error, setError] = useState("");
  const [treeStatus, setTreeStatus] = useState("展开目录时按需加载下一层");
  const [busy, setBusy] = useState(false);
  const [folderPrompt, setFolderPrompt] = useState<{
    mode: "create" | "rename";
    title: string;
    initial: string;
  } | null>(null);
  const [folderDelete, setFolderDelete] = useState(false);

  const loadNode = async (prefix: string) => {
    if (loading.has(prefix)) return;
    setLoading((current) => new Set(current).add(prefix));
    try {
      const result = await api.listChildFolders(bucket, prefix, settings);
      setChildren((current) => ({ ...current, [prefix]: result }));
      setExpanded((current) => new Set(current).add(prefix));
      setTreeStatus("目录已加载");
    } catch (reason) {
      setTreeStatus(`加载目录失败：${String(reason)}`);
    } finally {
      setLoading((current) => {
        const next = new Set(current);
        next.delete(prefix);
        return next;
      });
    }
  };

  useEffect(() => {
    let disposed = false;
    const locate = async () => {
      setTreeStatus("正在定位当前目录…");
      try {
        const ancestors =
          selected === root ? [root] : ancestorPrefixes(root, selected);
        for (const prefix of ancestors) {
          if (disposed) return;
          const result = await api.listChildFolders(bucket, prefix, settings);
          if (disposed) return;
          setChildren((current) => ({ ...current, [prefix]: result }));
          setExpanded((current) => new Set(current).add(prefix));
        }
        setTreeStatus("已定位当前目录");
      } catch (reason) {
        if (!disposed) setTreeStatus(`定位当前目录失败：${String(reason)}`);
      }
    };
    void locate();
    return () => {
      disposed = true;
    };
    // The modal is intentionally tied to the initial location.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const toggle = (prefix: string) => {
    if (expanded.has(prefix)) {
      setExpanded((current) => {
        const next = new Set(current);
        next.delete(prefix);
        return next;
      });
      return;
    }
    if (children[prefix]) {
      setExpanded((current) => new Set(current).add(prefix));
    } else {
      void loadNode(prefix);
    }
  };

  const selectedLabel = useMemo(
    () => `oss://${bucket.Name}/${selected}`,
    [bucket.Name, selected],
  );

  const submit = () => {
    const targetName = name.trim();
    if (allowRename) {
      const message = validateFolderName(targetName);
      if (message) {
        setError(message.replace("目录", "对象"));
        return;
      }
    }
    const targetKey = `${selected}${allowRename ? targetName : ""}`;
    if (sourceKey && targetKey === sourceKey) {
      setError("目标路径与源对象相同，请修改名称或目标目录");
      return;
    }
    if (sourceKey && sourceIsFolder && targetKey.startsWith(sourceKey)) {
      setError("不能将目录移动到自身内部");
      return;
    }
    onConfirm(selected, targetName);
  };

  const createFolder = async (folderName: string) => {
    const message = validateFolderName(folderName);
    if (message) throw new Error(message);
    const key = `${selected}${folderName}/`;
    await api.createFolder(bucket, key, settings);
    const parent = selected;
    await loadNode(parent);
    setExpanded((current) => new Set(current).add(parent));
    setSelected(key);
  };

  const renameFolder = async (folderName: string) => {
    const message = validateFolderName(folderName);
    if (message) throw new Error(message);
    if (selected === root) throw new Error("不能重命名资源桶根目录");
    const parent = parentPrefix(selected, root);
    const entry: ObjectEntry = {
      Name: folderLabel(selected, root),
      IsFolder: true,
      Size: "—",
      StorageClass: "—",
      LastModified: null,
      Key: selected,
      SizeBytes: 0,
    };
    const renamed = { ...entry, Name: folderName };
    await api.moveEntries(
      crypto.randomUUID(),
      bucket,
      [renamed],
      parent,
      settings,
    );
    await loadNode(parent);
    setSelected(`${parent}${folderName}/`);
  };

  const deleteFolder = async () => {
    if (selected === root) return;
    const parent = parentPrefix(selected, root);
    const entry: ObjectEntry = {
      Name: folderLabel(selected, root),
      IsFolder: true,
      Size: "—",
      StorageClass: "—",
      LastModified: null,
      Key: selected,
      SizeBytes: 0,
    };
    await api.deleteEntries(crypto.randomUUID(), bucket, [entry], settings);
    setSelected(parent);
    await loadNode(parent);
  };

  const renderNode = (prefix: string, depth: number) => {
    const isExpanded = expanded.has(prefix);
    const isRoot = prefix === root;
    const kids = children[prefix] ?? [];
    return (
      <div key={prefix}>
        <div
          className={`tree-row ${selected === prefix ? "selected" : ""}`}
          style={{ paddingLeft: 8 + depth * 18 }}
          onClick={() => {
            setSelected(prefix);
            setError("");
          }}
        >
          <button
            className="tree-toggle"
            title={isExpanded ? "收起目录" : "展开目录"}
            onClick={(event) => {
              event.stopPropagation();
              toggle(prefix);
            }}
          >
            {loading.has(prefix) ? (
              <LoaderCircle size={14} className="spin" />
            ) : isExpanded ? (
              <ChevronDown size={14} />
            ) : (
              <ChevronRight size={14} />
            )}
          </button>
          <Folder size={15} />
          <span className="tree-label">
            {isRoot ? `oss://${bucket.Name}/${root}` : folderLabel(prefix, root)}
          </span>
        </div>
        {isExpanded &&
          kids.map((child) => renderNode(child, depth + 1))}
        {isExpanded && kids.length === 0 && !loading.has(prefix) && (
          <div
            className="tree-empty"
            style={{ paddingLeft: 48 + depth * 18 }}
          >
            无子目录
          </div>
        )}
      </div>
    );
  };

  return (
    <>
      <Modal
        title={title}
        subtitle="从目录树选择目标目录；修改名称即表示同时重命名。"
        width={820}
        height={620}
        onClose={onCancel}
        footer={
          <>
            <button className="button" onClick={onCancel}>
              取消
            </button>
            <button
              className="button accent"
              disabled={busy}
              onClick={submit}
            >
              {confirmText}
            </button>
          </>
        }
      >
        <div className="destination-layout">
          <div className="destination-tree-panel">
            <div className="destination-tools">
              <button
                className="button small"
                onClick={() =>
                  setFolderPrompt({
                    mode: "create",
                    title: "新建目录",
                    initial: "",
                  })
                }
                title="在所选目录下新建目录"
              >
                <FolderPlus size={14} />
                新建
              </button>
              <button
                className="button small"
                onClick={() =>
                  setFolderPrompt({
                    mode: "rename",
                    title: "重命名目录",
                    initial: folderLabel(selected, root),
                  })
                }
                disabled={selected === root}
                title="重命名所选目录"
              >
                <Pencil size={14} />
                重命名
              </button>
              <button
                className="button small danger"
                onClick={() => setFolderDelete(true)}
                disabled={selected === root}
                title="递归删除所选目录"
              >
                <Trash2 size={14} />
                删除
              </button>
              <button
                className="icon-button"
                title="刷新所选目录"
                onClick={() => void loadNode(selected)}
              >
                <RefreshCw size={15} />
              </button>
            </div>
            <div className="directory-tree">{renderNode(root, 0)}</div>
            <div className="tree-status">{treeStatus}</div>
          </div>

          <div className="destination-side">
            <strong>目标目录</strong>
            <div className="selected-directory">{selectedLabel}</div>
            <strong>{allowRename ? "对象名称（修改即重命名）" : "对象名称"}</strong>
            <input
              className="input"
              value={name}
              disabled={!allowRename}
              onChange={(event) => {
                setName(event.target.value);
                setError("");
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter") submit();
              }}
            />
            <p className="hint">
              目标路径：
              <br />
              <code>
                oss://{bucket.Name}/{selected}
                {allowRename ? name.trim() : ""}
              </code>
            </p>
            {error && <p className="form-error">{error}</p>}
          </div>
        </div>
      </Modal>

      {folderPrompt && (
        <PromptModal
          title={folderPrompt.title}
          message={
            folderPrompt.mode === "create"
              ? `将在 ${selectedLabel} 下创建目录。`
              : "目录内的对象会随目录一起移动。"
          }
          placeholder="目录名称"
          initialValue={folderPrompt.initial}
          validate={validateFolderName}
          onCancel={() => setFolderPrompt(null)}
          onSubmit={async (value) => {
            setBusy(true);
            try {
              if (folderPrompt.mode === "create") await createFolder(value);
              else await renameFolder(value);
              setFolderPrompt(null);
            } finally {
              setBusy(false);
            }
          }}
        />
      )}

      {folderDelete && (
        <ConfirmModal
          title="删除目录"
          message={`确定删除目录“${folderLabel(selected, root)}”吗？`}
          detail="该目录前缀下的所有 OSS 对象都会被递归删除，此操作不可撤销。"
          onCancel={() => setFolderDelete(false)}
          onConfirm={() => {
            setFolderDelete(false);
            setBusy(true);
            void deleteFolder()
              .catch((reason) => setError(String(reason)))
              .finally(() => setBusy(false));
          }}
        />
      )}
    </>
  );
}
