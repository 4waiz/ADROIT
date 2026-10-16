// SPDX-License-Identifier: LicenseRef-BRIDGE
// Non-blocking notices: command outcomes, build results, goal reached.

import { AlertTriangle, CheckCircle2, Info, X, XCircle } from "lucide-react";
import { useStore } from "../state/store";

export function Toasts() {
  const toasts = useStore((s) => s.toasts);
  const dismiss = useStore((s) => s.dismissToast);
  return (
    <div className="toasts" role="region" aria-live="polite" aria-label="Notifications">
      {toasts.map((toast) => (
        <div key={toast.id} className={`toast ${toast.level}`} role={toast.level === "err" ? "alert" : "status"}>
          {toast.level === "ok" ? (
            <CheckCircle2 size={16} style={{ color: "var(--ok)", flexShrink: 0 }} />
          ) : toast.level === "err" ? (
            <XCircle size={16} style={{ color: "var(--err)", flexShrink: 0 }} />
          ) : toast.level === "warn" ? (
            <AlertTriangle size={16} style={{ color: "var(--warn)", flexShrink: 0 }} />
          ) : (
            <Info size={16} style={{ color: "var(--info)", flexShrink: 0 }} />
          )}
          <div className="grow">
            <b>{toast.title}</b>
            {toast.body}
          </div>
          <button type="button" className="icon-btn sm" aria-label="Dismiss" onClick={() => dismiss(toast.id)}>
            <X size={13} />
          </button>
        </div>
      ))}
    </div>
  );
}
