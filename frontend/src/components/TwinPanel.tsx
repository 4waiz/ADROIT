// SPDX-License-Identifier: LicenseRef-BRIDGE
// Where commands go, and why that is the simulation. The hardware connector is an interface
// with no device behind it in this build; its interlocks are listed as what they are: not met.

import { CircleSlash, Lock, ShieldAlert, ShieldCheck } from "lucide-react";
import { useT } from "../i18n";
import { useStore } from "../state/store";

export function TwinPanel() {
  const t = useT();
  const info = useStore((s) => s.info);
  const setPref = useStore((s) => s.setPref);
  const interlocks = [
    "twin.lock.mode",
    "twin.lock.operator",
    "twin.lock.device",
    "twin.lock.limits",
    "twin.lock.watchdog",
    "twin.lock.estop",
    "twin.lock.stale",
  ] as const;
  return (
    <div>
      <div className="section">
        <div className="section-title">{t("twin.target")}</div>
        <div className="notice ok">
          <ShieldCheck size={14} />
          <span>{t("twin.targetSim")}</span>
        </div>
        <dl className="kv" style={{ marginTop: 10 }}>
          <dt>{t("twin.stream")}</dt>
          <dd><span className="badge info">{t("twin.simulated")}</span></dd>
          <dt>{t("twin.hardware")}</dt>
          <dd><span className="badge">{t("twin.disconnected")}</span></dd>
        </dl>
        <p className="muted" style={{ fontSize: 11, margin: "8px 0 0" }}>{info?.hardware.detail ?? t("twin.noConnector")}</p>
      </div>
      <div className="section">
        <div className="section-title">
          <span className="grow">{t("twin.hardwareMode")}</span>
          <span className="badge err"><Lock size={10} /> {t("twin.locked")}</span>
        </div>
        <p style={{ margin: "0 0 10px", fontSize: 12, color: "var(--text-1)" }}>{t("twin.hardwareIntro")}</p>
        <ul style={{ listStyle: "none", padding: 0, margin: 0, display: "grid", gap: 6 }}>
          {interlocks.map((key) => (
            <li key={key} className="row" style={{ alignItems: "flex-start", fontSize: 12 }}>
              <CircleSlash size={14} style={{ color: "var(--err)", flexShrink: 0, marginTop: 1 }} />
              <span>{t(key)}</span>
            </li>
          ))}
        </ul>
        <div className="notice warn" style={{ marginTop: 12 }}>
          <ShieldAlert size={14} />
          <span>{t("twin.estopNote")}</span>
        </div>
      </div>
      <div className="section">
        <div className="section-title">{t("twin.teleop")}</div>
        <p style={{ margin: 0, fontSize: 12, color: "var(--text-1)" }}>{t("twin.teleopNote")}</p>
      </div>
      <div className="section">
        <div className="section-title">{t("twin.real2sim")}</div>
        <p style={{ margin: "0 0 8px", fontSize: 12, color: "var(--text-1)" }}>{t("twin.real2simNote")}</p>
        <button type="button" className="btn sm" onClick={() => { setPref("dockTab", "reference"); setPref("dockCollapsed", false); }}>
          {t("twin.openReference")}
        </button>
      </div>
    </div>
  );
}
