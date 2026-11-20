import { useState } from "react";
import type { WorkspaceState } from "../types";
import { Modal } from "./Modal";

export interface SettingsResult {
  transferConcurrency: number;
  requestTimeoutSeconds: number;
  retryCount: number;
  themeMode: WorkspaceState["ThemeMode"];
}

export function SettingsModal({
  workspace,
  onCancel,
  onSave,
}: {
  workspace: WorkspaceState;
  onCancel: () => void;
  onSave: (result: SettingsResult) => void;
}) {
  const [concurrency, setConcurrency] = useState(workspace.UploadConcurrency);
  const [timeout, setTimeoutValue] = useState(workspace.RequestTimeoutSeconds);
  const [retry, setRetry] = useState(workspace.RetryCount);
  const [theme, setTheme] = useState(workspace.ThemeMode);

  return (
    <Modal
      title="设置"
      subtitle="调整传输性能与应用外观"
      width={620}
      onClose={onCancel}
      footer={
        <>
          <button className="button" onClick={onCancel}>
            取消
          </button>
          <button
            className="button accent"
            onClick={() =>
              onSave({
                transferConcurrency: concurrency,
                requestTimeoutSeconds: timeout,
                retryCount: retry,
                themeMode: theme,
              })
            }
          >
            保存设置
          </button>
        </>
      }
    >
      <div className="settings-list">
        <h3 className="section-label">传输</h3>
        <div className="setting-card">
          <div>
            <strong>上传下载并发数</strong>
            <p>同时上传或下载的文件数量。网络或设备负载较高时可适当降低。</p>
          </div>
          <select
            className="select"
            value={concurrency}
            onChange={(event) => setConcurrency(Number(event.target.value))}
          >
            {Array.from({ length: 8 }, (_, index) => index + 1).map((value) => (
              <option key={value} value={value}>
                {value} 个任务
              </option>
            ))}
          </select>
        </div>

        <div className="divider" />
        <h3 className="section-label">网络请求</h3>
        <div className="setting-card">
          <div>
            <strong>超时时间</strong>
            <p>连接、读取或写入持续超过该时间后，本次请求将判定为超时。</p>
          </div>
          <select
            className="select"
            value={timeout}
            onChange={(event) => setTimeoutValue(Number(event.target.value))}
          >
            {[10, 20, 30, 60, 120, 300].map((value) => (
              <option key={value} value={value}>
                {value} 秒
              </option>
            ))}
          </select>
        </div>
        <div className="setting-card">
          <div>
            <strong>重试次数</strong>
            <p>遇到可恢复的网络错误时自动重试；不包含首次请求。</p>
          </div>
          <select
            className="select"
            value={retry}
            onChange={(event) => setRetry(Number(event.target.value))}
          >
            {Array.from({ length: 6 }, (_, value) => value).map((value) => (
              <option key={value} value={value}>
                {value === 0 ? "不重试" : `${value} 次`}
              </option>
            ))}
          </select>
        </div>

        <div className="divider" />
        <h3 className="section-label">外观</h3>
        <div className="setting-card">
          <div>
            <strong>主题模式</strong>
            <p>可跟随 Windows 外观设置，或固定使用浅色、深色主题。</p>
          </div>
          <select
            className="select"
            value={theme}
            onChange={(event) =>
              setTheme(event.target.value as WorkspaceState["ThemeMode"])
            }
          >
            <option value="System">跟随系统</option>
            <option value="Light">浅色</option>
            <option value="Dark">深色</option>
          </select>
        </div>
        <p className="hint">
          建议并发数为 3、超时 60 秒、重试 5 次；设置会用于之后发起的请求，主题立即生效。
        </p>
      </div>
    </Modal>
  );
}
