// SPDX-License-Identifier: LicenseRef-BRIDGE
// The machine and the service, checked: Python, GPU, solvers, renderer, client build, the
// browser's WebGL device, the socket, and what the viewport is spending per frame.

import { AlertTriangle, CheckCircle2, RefreshCw, XCircle } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { getJson } from "../api/rest";
import type { DoctorCheck } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";

function webglDevice(): string {
  try {
    const canvas = document.createElement("canvas");
    const gl = canvas.getContext("webgl2");
    if (!gl) return "WebGL2 unavailable";
    const ext = gl.getExtension("WEBGL_debug_renderer_info");
    return ext ? String(gl.getParameter(ext.UNMASKED_RENDERER_WEBGL)) : String(gl.getParameter(gl.RENDERER));
  } catch {
    return "unknown";
  }
}

export function DiagnosticsPanel() {
  const t = useT();
  const info = useStore((s) => s.info);
  const connection = useStore((s) => s.connection);
  const latency = useStore((s) => s.latencyMs);
  const clientId = useStore((s) => s.clientId);
  const stats = useStore((s) => s.renderStats);
  const tele = useStore((s) => s.telemetry);
  const manifest = useStore((s) => s.manifest);
  const [checks, setChecks] = useState<DoctorCheck[] | null>(null);
  const [error, setError] = useState("");
  const [device] = useState(webglDevice);

  const load = useCallback(async () => {
    setError("");
    try {
      setChecks((await getJson<{ checks: DoctorCheck[] }>("/api/doctor")).checks);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="split">
      <div>
        <div className="section">
          <div className="section-title">
            <span className="grow">{t("diag.environment")}</span>
            <button type="button" className="btn sm ghost" onClick={() => void load()}>
              <RefreshCw size={12} /> {t("diag.recheck")}
            </button>
          </div>
          {error && <div className="notice err">{error}</div>}
          {!checks && !error && <p className="muted">{t("diag.checking")}</p>}
          <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "grid", gap: 6 }}>
            {checks?.map((c) => (
              <li key={c.name} className="row" style={{ alignItems: "flex-start", fontSize: 12 }}>
                {c.status === "ok" ? (
                  <CheckCircle2 size={14} style={{ color: "var(--ok)", flexShrink: 0, marginTop: 1 }} />
                ) : c.status === "warn" ? (
                  <AlertTriangle size={14} style={{ color: "var(--warn)", flexShrink: 0, marginTop: 1 }} />
                ) : (
                  <XCircle size={14} style={{ color: "var(--err)", flexShrink: 0, marginTop: 1 }} />
                )}
                <span>
                  <b>{c.name}</b> — {c.detail}
                  {c.fix && <div className="muted" style={{ fontSize: 11 }}>{c.fix}</div>}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </div>
      <div>
        <div className="section">
          <div className="section-title">{t("diag.service")}</div>
          <dl className="kv">
            <dt>{t("diag.version")}</dt>
            <dd className="mono">{info?.version ?? "—"}</dd>
            <dt>{t("diag.os")}</dt>
            <dd>{info?.machine.os ?? "—"} · Python {info?.machine.python}</dd>
            <dt>{t("diag.gpu")}</dt>
            <dd>{info?.machine.gpu.map((g) => `${g.name} (${g.arch}, ${g.memoryGB} GB)`).join(", ") || t("diag.noGpu")}</dd>
            <dt>Warp</dt>
            <dd className="mono">{info?.machine.warp ?? "—"}</dd>
            <dt>{t("diag.snapshotRenderer")}</dt>
            <dd>{info ? `${info.machine.renderer.backend}: ${info.machine.renderer.why}` : "—"}</dd>
            <dt>{t("diag.target")}</dt>
            <dd>{info?.commandTarget ?? "—"}</dd>
          </dl>
        </div>
        <div className="section">
          <div className="section-title">{t("diag.link")}</div>
          <dl className="kv">
            <dt>{t("diag.socket")}</dt>
            <dd>{connection} {latency !== null ? `· ${latency} ms ${t("diag.roundTrip")}` : ""}</dd>
            <dt>{t("diag.client")}</dt>
            <dd className="mono">{clientId || "—"}</dd>
            <dt>{t("diag.frames")}</dt>
            <dd className="mono">{tele ? `seq ${tele.seq} · gen ${tele.gen}` : "—"}</dd>
          </dl>
        </div>
        <div className="section">
          <div className="section-title">{t("diag.viewport")}</div>
          <dl className="kv">
            <dt>WebGL</dt>
            <dd style={{ fontSize: 11 }}>{device}</dd>
            <dt>{t("diag.profile")}</dt>
            <dd>{t(`quality.${stats.quality}` as "quality.balanced")} · {t("diag.shadows")} {stats.shadows ? t("common.on") : t("common.off")} · AO {stats.ao ? t("common.on") : t("common.off")}</dd>
            <dt>{t("diag.frame")}</dt>
            <dd className="mono">{stats.fps.toFixed(0)} fps · {stats.ms.toFixed(2)} ms · {stats.calls} draws · {(stats.triangles / 1000).toFixed(0)}k tris</dd>
            <dt>{t("diag.scene")}</dt>
            <dd className="mono">{manifest ? `${manifest.counts.shapes} shapes · ${manifest.meshes.length} meshes · ${(manifest.geometryBytes / 1e6).toFixed(1)} MB` : "—"}</dd>
          </dl>
        </div>
      </div>
    </div>
  );
}
