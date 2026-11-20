import { useEffect, useRef, useState } from "react";
import type { MenuItem } from "../types";

export function ContextMenu({
  x,
  y,
  items,
  onClose,
}: {
  x: number;
  y: number;
  items: MenuItem[];
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ x, y });

  useEffect(() => {
    const menu = ref.current;
    if (!menu) return;
    const rect = menu.getBoundingClientRect();
    setPosition({
      x: Math.min(x, window.innerWidth - rect.width - 8),
      y: Math.min(y, window.innerHeight - rect.height - 8),
    });
  }, [x, y]);

  useEffect(() => {
    const close = () => onClose();
    window.addEventListener("mousedown", close);
    window.addEventListener("blur", close);
    window.addEventListener("resize", close);
    return () => {
      window.removeEventListener("mousedown", close);
      window.removeEventListener("blur", close);
      window.removeEventListener("resize", close);
    };
  }, [onClose]);

  return (
    <div
      ref={ref}
      className="context-menu"
      style={{ left: position.x, top: position.y }}
      onMouseDown={(event) => event.stopPropagation()}
    >
      {items.map((item, index) => (
        <div key={`${item.label}-${index}`}>
          {item.separator && <div className="menu-separator" />}
          <button
            className={`menu-item ${item.tone === "danger" ? "danger" : ""}`}
            disabled={item.disabled}
            onClick={() => {
              onClose();
              item.action();
            }}
          >
            {item.label}
          </button>
        </div>
      ))}
    </div>
  );
}
