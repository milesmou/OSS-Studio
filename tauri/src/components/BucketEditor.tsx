import { useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import type { BucketProfile, CredentialInput, StoredCredential } from "../types";
import { REGIONS, regionEndpoint, regionName } from "../data";
import { Modal } from "./Modal";

function normalizeEndpoint(value: string): string {
  return value
    .trim()
    .replace(/^https?:\/\//i, "")
    .replace(/\/+$/, "");
}

function resolveEndpointMode(bucket: BucketProfile | null): string {
  if (!bucket) return "Default";
  if (bucket.EndpointMode === "Custom" || bucket.EndpointMode === "Cname") {
    return bucket.EndpointMode;
  }
  return bucket.Endpoint === regionEndpoint(bucket.RegionCode) ? "Default" : "Custom";
}

function parseOssPath(value: string): { bucket: string; prefix: string } {
  let path = value.trim();
  if (path.toLowerCase().startsWith("oss://")) path = path.slice(6);
  const parts = path.split("/");
  const bucket = parts.shift() ?? "";
  const rest = parts.join("/");
  let prefix = rest.replace(/^\/+|\/+$/g, "");
  if (prefix) prefix += "/";
  return { bucket: bucket.trim(), prefix };
}

export function BucketEditor({
  bucket,
  credential,
  nameExists,
  onCancel,
  onSubmit,
}: {
  bucket: BucketProfile | null;
  credential: StoredCredential | null;
  nameExists: (name: string) => boolean;
  onCancel: () => void;
  onSubmit: (bucket: BucketProfile, credential: CredentialInput) => Promise<void>;
}) {
  const initialMode = resolveEndpointMode(bucket);
  const [endpointMode, setEndpointMode] = useState(initialMode);
  const [regionCode, setRegionCode] = useState(
    bucket?.RegionCode || REGIONS[0].code,
  );
  const [customEndpoint, setCustomEndpoint] = useState(
    initialMode === "Default" ? "" : bucket?.Endpoint ?? "",
  );
  const [useHttps, setUseHttps] = useState(bucket?.UseHttps ?? true);
  const [ossPath, setOssPath] = useState(
    bucket ? `${bucket.Name}/${bucket.Prefix}` : "",
  );
  const [accessKeyId, setAccessKeyId] = useState(credential?.accessKeyId ?? "");
  const [accessKeySecret, setAccessKeySecret] = useState(
    credential?.accessKeySecret ?? "",
  );
  const [showSecret, setShowSecret] = useState(false);
  const [requestPayer, setRequestPayer] = useState(bucket?.RequestPayer ?? false);
  const [note, setNote] = useState(bucket?.Note ?? "");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    const parsed = parseOssPath(ossPath);
    const endpoint =
      endpointMode === "Default"
        ? regionEndpoint(regionCode)
        : normalizeEndpoint(customEndpoint);
    const keyId = accessKeyId.trim();
    const secret = accessKeySecret.trim();

    if (!parsed.bucket) {
      setError("请输入有效的预设 OSS 路径，例如 bucket-name/path/。");
      return;
    }
    if (!endpoint) {
      setError(
        endpointMode === "Cname"
          ? "使用 cname 时，请填写已绑定到 Bucket 的自定义域名。"
          : "使用自定义模式时，请填写 OSS Endpoint。",
      );
      return;
    }
    if (!keyId || !secret) {
      setError("请完整填写 AccessKeyId 和 AccessKeySecret。");
      return;
    }
    if (nameExists(parsed.bucket)) {
      setError("已存在同名资源桶，请填写其它 OSS 路径。");
      return;
    }
    if (note.trim().length > 30) {
      setError("备注最多可以填写 30 个字符。");
      return;
    }

    setBusy(true);
    try {
      await onSubmit(
        {
          Id: bucket?.Id ?? `bucket-${crypto.randomUUID().replace(/-/g, "")}`,
          Name: parsed.bucket,
          RegionCode: regionCode,
          RegionName: regionName(regionCode),
          Endpoint: endpoint,
          UseHttps: useHttps,
          Prefix: parsed.prefix,
          RequestPayer: requestPayer,
          Note: note.trim(),
          KeepLogin: true,
          RememberSecret: true,
          EndpointMode: endpointMode,
          DisplayName: bucket?.DisplayName ?? "",
        },
        { accessKeyId: keyId, accessKeySecret: secret },
      );
    } catch (reason) {
      setError(String(reason));
      setBusy(false);
    }
  };

  const endpointPlaceholder =
    endpointMode === "Cname"
      ? "请输入已绑定的自定义域名"
      : "请输入 OSS Endpoint";

  return (
    <Modal
      title={bucket ? "编辑资源桶" : "添加资源桶"}
      subtitle="配置 Bucket 访问地址与 AK 凭据"
      width={680}
      onClose={onCancel}
      footer={
        <>
          <button className="button" onClick={onCancel} disabled={busy}>
            取消
          </button>
          <button className="button accent" onClick={() => void submit()} disabled={busy}>
            {busy ? "保存中…" : bucket ? "保存" : "添加"}
          </button>
        </>
      }
    >
      <div className="bucket-form">
        <label className="form-row">
          <span className="required">* Endpoint：</span>
          <div className="endpoint-grid">
            <select
              className="select"
              value={endpointMode}
              onChange={(event) => setEndpointMode(event.target.value)}
            >
              <option value="Default">默认（公共云）</option>
              <option value="Custom">自定义</option>
              <option value="Cname">cname</option>
            </select>
            {endpointMode === "Default" ? (
              <select
                className="select grow"
                value={regionCode}
                onChange={(event) => setRegionCode(event.target.value)}
              >
                {REGIONS.map((region) => (
                  <option key={region.code} value={region.code}>
                    {region.name}
                  </option>
                ))}
              </select>
            ) : (
              <input
                className="input grow"
                value={customEndpoint}
                placeholder={endpointPlaceholder}
                onChange={(event) => setCustomEndpoint(event.target.value)}
              />
            )}
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={useHttps}
                onChange={(event) => setUseHttps(event.target.checked)}
              />
              HTTPS 加密
            </label>
          </div>
        </label>

        <label className="form-row">
          <span className="required">* AccessKeyId：</span>
          <input
            className="input"
            value={accessKeyId}
            placeholder="请输入 AccessKey ID"
            spellCheck={false}
            onChange={(event) => setAccessKeyId(event.target.value)}
          />
        </label>

        <label className="form-row">
          <span className="required">* AccessKeySecret：</span>
          <div className="secret-input">
            <input
              className="input"
              type={showSecret ? "text" : "password"}
              value={accessKeySecret}
              placeholder="请输入 AccessKey Secret"
              spellCheck={false}
              onChange={(event) => setAccessKeySecret(event.target.value)}
            />
            <button
              type="button"
              className="icon-button inline"
              title={showSecret ? "隐藏密钥" : "显示密钥"}
              onClick={() => setShowSecret((value) => !value)}
            >
              {showSecret ? <EyeOff size={16} /> : <Eye size={16} />}
            </button>
          </div>
        </label>

        <label className="form-row">
          <span>预设 OSS 路径：</span>
          <div className="oss-path-input">
            <span>oss://</span>
            <input
              className="input"
              value={ossPath}
              placeholder="bucket-name/path/"
              onChange={(event) => setOssPath(event.target.value)}
            />
          </div>
        </label>

        <div className="form-row">
          <span />
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={requestPayer}
              onChange={(event) => setRequestPayer(event.target.checked)}
            />
            请求者付费模式
          </label>
        </div>

        <label className="form-row">
          <span>区域：</span>
          <select
            className="select"
            value={regionCode}
            onChange={(event) => setRegionCode(event.target.value)}
          >
            {REGIONS.map((region) => (
              <option key={region.code} value={region.code}>
                {region.name}
              </option>
            ))}
          </select>
        </label>

        <label className="form-row">
          <span>备注：</span>
          <input
            className="input"
            value={note}
            maxLength={30}
            placeholder="可以为空，最多 30 个字符"
            onChange={(event) => setNote(event.target.value)}
          />
        </label>

        {error && (
          <div className="form-row">
            <span />
            <p className="form-error">{error}</p>
          </div>
        )}

        <p className="security-note">
          AccessKey 只会保存到 Windows 凭据管理器，不会写入工作区 JSON。
        </p>
      </div>
    </Modal>
  );
}
