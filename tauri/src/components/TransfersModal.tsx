import { CircleCheck, CircleX, LoaderCircle, RotateCcw, X } from "lucide-react";
import { uploadPercentage } from "../data";
import type { TransferTask } from "../types";
import { Modal } from "./Modal";

function progress(task: TransferTask): number {
  if (task.State === "已完成") return 100;
  if (task.UploadBytes) {
    const { sent, total } = task.UploadBytes;
    return total > 0
      ? uploadPercentage(sent, total)
      : uploadPercentage(task.Current, task.Total);
  }
  if (!task.Total) return 0;
  return Math.max(0, Math.min(100, (task.Current * 100) / task.Total));
}

function StateIcon({ task }: { task: TransferTask }) {
  if (task.State === "已完成") return <CircleCheck size={18} className="teal" />;
  if (task.State === "失败") return <CircleX size={18} className="coral" />;
  if (task.State === "已取消") return <CircleX size={18} className="muted" />;
  return <LoaderCircle size={18} className="spin teal" />;
}

export function TransfersModal({
  tasks,
  onCancel,
  onRetry,
  onClear,
  onClose,
}: {
  tasks: TransferTask[];
  onCancel: (taskId: string) => void;
  onRetry: (task: TransferTask) => void;
  onClear: () => void;
  onClose: () => void;
}) {
  return (
    <Modal
      title="传输任务"
      subtitle="上传、下载、复制、移动与删除进度"
      width={760}
      height={560}
      onClose={onClose}
      footer={
        <>
          <span className="muted footer-summary">
            {tasks.filter((task) => task.IsRunning).length} 个进行中 ·{" "}
            {tasks.filter((task) => !task.IsRunning).length} 个已结束
          </span>
          <button
            className="button"
            onClick={onClear}
            disabled={!tasks.some((task) => !task.IsRunning)}
          >
            清除已完成
          </button>
          <button className="button accent" onClick={onClose}>
            关闭
          </button>
        </>
      }
    >
      <div className="transfer-list">
        {tasks.length === 0 && (
          <div className="empty-state">
            <LoaderCircle size={28} />
            <strong>暂无传输任务</strong>
            <p>上传、下载或删除对象后，任务进度会显示在这里。</p>
          </div>
        )}
        {tasks.map((task) => (
          <article className="transfer-item" key={task.Id}>
            <div className="transfer-top">
              <StateIcon task={task} />
              <div className="transfer-title">
                <strong>{task.Title}</strong>
                <span>
                  {task.Operation} · {task.BucketName}
                </span>
              </div>
              <span className={`state-pill ${task.State === "失败" ? "error" : ""}`}>
                {task.State}
              </span>
              {task.IsRunning ? (
                <button
                  className="button small"
                  onClick={() => onCancel(task.Id)}
                  disabled={task.State === "取消中"}
                  title="取消任务"
                >
                  <X size={14} />
                  取消
                </button>
              ) : (
                <button
                  className="button small"
                  onClick={() => onRetry(task)}
                  title="重新执行任务"
                >
                  <RotateCcw size={14} />
                  重试
                </button>
              )}
            </div>
            <div className="progress-track">
              <div
                className={`progress-value ${task.State === "失败" ? "error" : ""}`}
                style={{ width: `${progress(task)}%` }}
              />
            </div>
            <div className="transfer-meta">
              <span>
                {task.Total > 0
                  ? `${task.Current}/${task.Total} · ${task.Detail}`
                  : task.Detail}
              </span>
              <span>{Math.round(progress(task))}%</span>
            </div>
          </article>
        ))}
      </div>
    </Modal>
  );
}
