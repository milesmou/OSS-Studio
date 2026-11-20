import { TriangleAlert } from "lucide-react";
import { Modal } from "./Modal";

export function ConfirmModal({
  title,
  message,
  detail,
  confirmText = "删除",
  cancelText = "取消",
  danger = true,
  onCancel,
  onConfirm,
}: {
  title: string;
  message: string;
  detail?: string;
  confirmText?: string;
  cancelText?: string;
  danger?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  return (
    <Modal
      title={title}
      width={500}
      onClose={onCancel}
      footer={
        <>
          <button className="button" onClick={onCancel}>
            {cancelText}
          </button>
          <button
            className={`button ${danger ? "danger-solid" : "accent"}`}
            onClick={onConfirm}
          >
            {confirmText}
          </button>
        </>
      }
    >
      <div className="confirm-content">
        <div className="warning-badge">
          <TriangleAlert size={20} />
        </div>
        <div>
          <strong>{message}</strong>
          {detail && <p className="muted">{detail}</p>}
        </div>
      </div>
    </Modal>
  );
}
