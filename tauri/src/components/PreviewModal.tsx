import { useEffect, useState } from "react";
import { Download, Link2, Minus, Plus, RotateCcw } from "lucide-react";
import { api } from "../api";
import type { BucketProfile, ObjectEntry, OssSettings } from "../types";
import { imageMime, isImageFile, isTextFile } from "../data";
import { Modal } from "./Modal";

const TEXT_LIMIT = 2 * 1024 * 1024;
const IMAGE_LIMIT = 20 * 1024 * 1024;

type PreviewKind = "text" | "image" | "unsupported";

export function PreviewModal({
  bucket,
  entry,
  settings,
  onDownload,
  onAddress,
  onClose,
}: {
  bucket: BucketProfile;
  entry: ObjectEntry;
  settings: OssSettings;
  onDownload: (entry: ObjectEntry) => void;
  onAddress: (entry: ObjectEntry) => void;
  onClose: () => void;
}) {
  const [kind, setKind] = useState<PreviewKind>(() =>
    isImageFile(entry.Name) ? "image" : isTextFile(entry.Name) ? "text" : "unsupported",
  );
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [text, setText] = useState("");
  const [originalText, setOriginalText] = useState("");
  const [imageUrl, setImageUrl] = useState("");
  const [imageSize, setImageSize] = useState("");
  const [zoom, setZoom] = useState(1);
  const [status, setStatus] = useState("");

  useEffect(() => {
    let disposed = false;
    const load = async () => {
      setLoading(true);
      setError("");
      try {
        if (kind === "text") {
          const value = await api.getTextObject(bucket, entry.Key, TEXT_LIMIT, settings);
          if (disposed) return;
          setText(value);
          setOriginalText(value);
          setStatus("保存后将以 UTF-8 编码覆盖 OSS 上的原文件");
        } else if (kind === "image") {
          const base64 = await api.getObjectBytes(bucket, entry.Key, IMAGE_LIMIT, settings);
          if (disposed) return;
          const url = `data:${imageMime(entry.Name)};base64,${base64}`;
          setImageUrl(url);
          const image = new Image();
          image.onload = () => {
            if (!disposed) setImageSize(`${image.naturalWidth} × ${image.naturalHeight}`);
          };
          image.src = url;
          setStatus("滚轮缩放图片");
        }
      } catch (reason) {
        if (!disposed) {
          setError(String(reason));
          if (kind === "text") setStatus("无法作为文本文件打开");
        }
      } finally {
        if (!disposed) setLoading(false);
      }
    };
    if (kind !== "unsupported") void load();
    else {
      setLoading(false);
      setStatus("该文件类型无法预览");
    }
    return () => {
      disposed = true;
    };
  }, [bucket, entry.Key, entry.Name, kind, settings]);

  const save = async () => {
    if (text === originalText) {
      setStatus("文件内容未修改");
      return;
    }
    setStatus("正在保存…");
    try {
      await api.putTextObject(bucket, entry.Key, text, settings);
      setOriginalText(text);
      setStatus("已保存到 OSS");
    } catch (reason) {
      setStatus(`保存失败：${String(reason)}`);
    }
  };

  return (
    <Modal
      title={`预览：${entry.Name}`}
      subtitle={`oss://${bucket.Name}/${entry.Key}`}
      width={920}
      height={680}
      className="preview-modal"
      onClose={onClose}
      footer={
        <>
          <div className="footer-actions">
            <button className="button blue" onClick={() => onDownload(entry)}>
              <Download size={15} />
              下载
            </button>
            <button className="button teal" onClick={() => onAddress(entry)}>
              <Link2 size={15} />
              获取地址
            </button>
          </div>
          <span className="preview-status">{status}</span>
          <div className="footer-actions">
            {kind === "text" && (
              <button
                className="button accent"
                onClick={() => void save()}
                disabled={loading || !!error}
              >
                保存
              </button>
            )}
            <button className="button" onClick={onClose}>
              关闭
            </button>
          </div>
        </>
      }
    >
      <div className="preview-body">
        {loading && (
          <div className="empty-state compact">
            <strong>正在加载预览…</strong>
          </div>
        )}

        {!loading && error && (
          <div className="empty-state compact">
            <strong>{error}</strong>
            {kind === "unsupported" && (
              <button className="button accent" onClick={() => setKind("text")}>
                尝试作为文本文件打开
              </button>
            )}
          </div>
        )}

        {!loading && !error && kind === "text" && (
          <textarea
            className="text-editor"
            value={text}
            spellCheck={false}
            onChange={(event) => setText(event.target.value)}
          />
        )}

        {!loading && !error && kind === "image" && (
          <div
            className="image-viewport"
            onWheel={(event) => {
              event.preventDefault();
              setZoom((value) =>
                Math.max(0.05, Math.min(4, value * (event.deltaY > 0 ? 0.9 : 1.1))),
              );
            }}
          >
            <div className="image-tools">
              <button
                className="icon-button"
                title="缩小"
                onClick={() => setZoom((value) => Math.max(0.05, value * 0.9))}
              >
                <Minus size={16} />
              </button>
              <span>{Math.round(zoom * 100)}%</span>
              <button
                className="icon-button"
                title="放大"
                onClick={() => setZoom((value) => Math.min(4, value * 1.1))}
              >
                <Plus size={16} />
              </button>
              <button
                className="icon-button"
                title="重置缩放"
                onClick={() => setZoom(1)}
              >
                <RotateCcw size={15} />
              </button>
            </div>
            {imageUrl && (
              <img
                src={imageUrl}
                alt={entry.Name}
                style={{ transform: `scale(${zoom})` }}
              />
            )}
            <span className="image-size">{imageSize}</span>
          </div>
        )}

        {!loading && !error && kind === "unsupported" && (
          <div className="empty-state">
            <strong>该文件类型无法预览。</strong>
            <p>可以将文件下载到本地，或尝试按 UTF-8 文本读取。</p>
            <button className="button accent" onClick={() => setKind("text")}>
              尝试作为文本文件打开
            </button>
          </div>
        )}
      </div>
    </Modal>
  );
}
