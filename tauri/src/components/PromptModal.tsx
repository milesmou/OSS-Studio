import { useState } from "react";
import { Modal } from "./Modal";

export function PromptModal({
  title,
  message,
  placeholder,
  initialValue = "",
  confirmText = "确定",
  validate,
  onCancel,
  onSubmit,
}: {
  title: string;
  message?: string;
  placeholder?: string;
  initialValue?: string;
  confirmText?: string;
  validate?: (value: string) => string | null;
  onCancel: () => void;
  onSubmit: (value: string) => void | Promise<void>;
}) {
  const [value, setValue] = useState(initialValue);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    const trimmed = value.trim();
    const message = validate?.(trimmed);
    if (message) {
      setError(message);
      return;
    }
    setBusy(true);
    try {
      await onSubmit(trimmed);
    } catch (reason) {
      setError(String(reason));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={title}
      width={520}
      onClose={onCancel}
      footer={
        <>
          <button className="button" onClick={onCancel} disabled={busy}>
            取消
          </button>
          <button className="button accent" onClick={submit} disabled={busy}>
            {busy ? "处理中…" : confirmText}
          </button>
        </>
      }
    >
      <div className="form-stack">
        {message && <p className="muted">{message}</p>}
        <input
          className="input"
          autoFocus
          value={value}
          placeholder={placeholder}
          onChange={(event) => {
            setValue(event.target.value);
            setError("");
          }}
          onKeyDown={(event) => {
            if (event.key === "Enter") void submit();
            if (event.key === "Escape") onCancel();
          }}
        />
        {error && <p className="form-error">{error}</p>}
      </div>
    </Modal>
  );
}
