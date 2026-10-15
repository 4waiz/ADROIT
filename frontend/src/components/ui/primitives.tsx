// SPDX-License-Identifier: Apache-2.0
// Small controls shared by every panel: icon buttons with tooltips, popover menus, dialogs,
// segmented switches, toggles and the numeric field that commits on Enter.

import { X } from "lucide-react";
import { type ReactNode, useEffect, useId, useRef, useState } from "react";

export function IconButton({
  label,
  onClick,
  children,
  active,
  disabled,
  size,
  shortcut,
}: {
  label: string;
  onClick?: () => void;
  children: ReactNode;
  active?: boolean;
  disabled?: boolean;
  size?: "sm";
  shortcut?: string;
}) {
  return (
    <button
      type="button"
      className={`icon-btn${active ? " active" : ""}${size ? ` ${size}` : ""}`}
      aria-label={label}
      aria-pressed={active === undefined ? undefined : active}
      title={shortcut ? `${label} (${shortcut})` : label}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}

export function Popover({
  anchor,
  open,
  onClose,
  children,
  align = "start",
  width,
}: {
  anchor: ReactNode;
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  align?: "start" | "end";
  width?: number;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const onDown = (e: PointerEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) onClose();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("pointerdown", onDown, true);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("pointerdown", onDown, true);
      window.removeEventListener("keydown", onKey);
    };
  }, [open, onClose]);
  return (
    <div ref={ref} style={{ position: "relative", display: "inline-flex" }}>
      {anchor}
      {open && (
        <div
          className="menu"
          role="menu"
          style={{ top: "calc(100% + 6px)", [align === "start" ? "insetInlineStart" : "insetInlineEnd"]: 0, width }}
        >
          {children}
        </div>
      )}
    </div>
  );
}

export function MenuItem({
  children,
  onClick,
  checked,
  kbd,
  disabled,
}: {
  children: ReactNode;
  onClick: () => void;
  checked?: boolean;
  kbd?: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      className="menu-item"
      role={checked === undefined ? "menuitem" : "menuitemcheckbox"}
      aria-checked={checked}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
      {kbd && <kbd>{kbd}</kbd>}
    </button>
  );
}

export function Dialog({
  title,
  onClose,
  children,
  footer,
  size,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  size?: "wide" | "narrow";
}) {
  const titleId = useId();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>("button, [href], input, select, textarea")?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      previous?.focus?.();
    };
  }, [onClose]);
  return (
    <div className="scrim" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={ref} className={`dialog${size ? ` ${size}` : ""}`} role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <div className="dialog-head">
          <h2 id={titleId}>{title}</h2>
          <span className="grow" />
          <IconButton label="Close" onClick={onClose}>
            <X size={16} />
          </IconButton>
        </div>
        <div className="dialog-body">{children}</div>
        {footer && <div className="dialog-foot">{footer}</div>}
      </div>
    </div>
  );
}

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T;
  options: { value: T; label: string }[];
  onChange: (v: T) => void;
  label: string;
}) {
  return (
    <div className="segmented" role="group" aria-label={label}>
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={o.value === value} onClick={() => onChange(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  );
}

export function Toggle({ label, checked, onChange, hint }: { label: string; checked: boolean; onChange: (v: boolean) => void; hint?: string }) {
  return (
    <label className="toggle" title={hint}>
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  );
}

/** A number that is only committed on Enter or blur, so typing "-0." is not sent as -0. */
export function NumberField({
  value,
  onCommit,
  step = 0.001,
  min,
  max,
  label,
  digits = 3,
  disabled,
}: {
  value: number | null | undefined;
  onCommit: (v: number) => void;
  step?: number;
  min?: number;
  max?: number;
  label: string;
  digits?: number;
  disabled?: boolean;
}) {
  const [text, setText] = useState<string | null>(null);
  const shown = text ?? (value === null || value === undefined || !Number.isFinite(value) ? "" : value.toFixed(digits));
  const commit = () => {
    if (text === null) return;
    const v = Number(text);
    setText(null);
    if (!Number.isFinite(v)) return;
    let clipped = v;
    if (min !== undefined) clipped = Math.max(min, clipped);
    if (max !== undefined) clipped = Math.min(max, clipped);
    onCommit(clipped);
  };
  return (
    <input
      className="input num"
      inputMode="decimal"
      aria-label={label}
      value={shown}
      step={step}
      disabled={disabled}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          commit();
          (e.target as HTMLInputElement).blur();
        } else if (e.key === "Escape") {
          setText(null);
          (e.target as HTMLInputElement).blur();
        }
      }}
    />
  );
}
