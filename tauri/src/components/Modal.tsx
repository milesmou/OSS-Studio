import { useEffect, type ReactNode } from "react";
import { X } from "lucide-react";

export function Modal({
  title,
  subtitle,
  width = 520,
  height,
  onClose,
  footer,
  children,
  className = "",
}: {
  title: string;
  subtitle?: string;
  width?: number;
  height?: number;
  onClose: () => void;
  footer?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <section
        className={`modal-panel ${className}`}
        style={{ width, height }}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header className="modal-header">
          <div>
            <h2>{title}</h2>
            {subtitle && <p>{subtitle}</p>}
          </div>
          <button className="icon-button" onClick={onClose} title="关闭">
            <X size={18} />
          </button>
        </header>
        <div className="modal-body">{children}</div>
        {footer && <footer className="modal-footer">{footer}</footer>}
      </section>
    </div>
  );
}
