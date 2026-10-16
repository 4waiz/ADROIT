// SPDX-License-Identifier: LicenseRef-BRIDGE
// How the scene is simulated, read from the compiled program, and the one parameter the running
// solvers accept live (gravity). Everything else that shapes the physics is fixed at build time
// and is marked so: changing it means rebuilding the workcell, which the editor does.

import { AlertTriangle, RefreshCw, Zap } from "lucide-react";
import { useState } from "react";
import { bridge } from "../api/socket";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { NumberField } from "./ui/primitives";

const mm = (v: number | null, fallback: string) => (v === null ? fallback : `${(v * 1000).toFixed(1)} mm`);

export function PhysicsPanel() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const [gravity, setGravity] = useState<number | null>(null);
  if (!manifest) return <div className="empty">{t("inspect.noScene")}</div>;
  const p = manifest.physics;
  const g = gravity ?? -Math.abs(p.gravity);
  const apply = async (value: number) => {
    const ack = await bridge.command("physics.gravity", { value });
    if (ack.status === "completed") setGravity(value);
  };
  return (
    <div>
      <div className="section">
        <div className="section-title">{t("physics.arrangement")}</div>
        <dl className="kv">
          <dt>{t("physics.control")}</dt>
          <dd className="mono">{p.controlHz.toFixed(0)} Hz · {(1000 / p.controlHz).toFixed(1)} ms</dd>
          <dt>{t("physics.substeps")}</dt>
          <dd className="mono">{p.substeps} · dt {(p.simDt * 1000).toFixed(2)} ms</dd>
          <dt>{t("physics.device")}</dt>
          <dd className="mono">{p.device}</dd>
          <dt>{t("physics.graph")}</dt>
          <dd>{p.cudaGraph ? <span className="badge ok">{t("physics.captured")}</span> : <span className="badge">{t("physics.eager")}</span>}</dd>
          <dt>{t("physics.coupler")}</dt>
          <dd className="mono">{p.coupler || "—"}</dd>
        </dl>
      </div>

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("physics.gravity")}</span>
          <span className="badge ok"><Zap size={10} /> {t("physics.live")}</span>
        </div>
        <div className="row">
          <input
            className="grow"
            type="range"
            min={-20}
            max={0}
            step={0.01}
            value={g}
            aria-label={t("physics.gravity")}
            onChange={(e) => setGravity(Number(e.target.value))}
            onPointerUp={(e) => void apply(Number((e.target as HTMLInputElement).value))}
            onKeyUp={(e) => void apply(Number((e.target as HTMLInputElement).value))}
          />
          <div style={{ width: 84 }}>
            <NumberField label={t("physics.gravity")} value={g} digits={2} min={-30} max={0} onCommit={(v) => void apply(v)} />
          </div>
        </div>
        <div className="row" style={{ marginTop: 6 }}>
          {[
            { label: t("physics.earth"), v: -9.81 },
            { label: t("physics.moon"), v: -1.62 },
            { label: t("physics.mars"), v: -3.71 },
          ].map((preset) => (
            <button key={preset.label} type="button" className="btn sm" onClick={() => void apply(preset.v)}>
              {preset.label}
            </button>
          ))}
        </div>
        <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("physics.gravityNote")}</p>
      </div>

      <div className="section">
        <div className="section-title">{t("physics.islands")}</div>
        {p.islands.map((island) => (
          <details key={island.name} style={{ marginBottom: 8 }} open={p.islands.length === 1}>
            <summary style={{ cursor: "pointer", fontSize: 12.5, fontWeight: 600 }}>
              {island.name} · <span className="mono">{island.solver}</span>
              {island.hostEngine && <span className="badge info" style={{ marginInlineStart: 6 }}>{t("physics.cosim")}</span>}
            </summary>
            <dl className="kv" style={{ marginTop: 6 }}>
              <dt>{t("physics.solverClass")}</dt>
              <dd className="mono" style={{ fontSize: 11 }}>{island.solverClass}</dd>
              <dt>{t("physics.domains")}</dt>
              <dd>{island.domains.join(", ")}</dd>
              <dt>{t("physics.entities")}</dt>
              <dd>{island.entities.join(", ")}</dd>
              <dt>{t("physics.size")}</dt>
              <dd className="mono">{island.bodies} bodies · {island.particles} particles · {island.substeps} substep(s)</dd>
              {Object.entries(island.params).map(([k, v]) => (
                <div key={k} style={{ display: "contents" }}>
                  <dt className="mono" style={{ fontSize: 10.5 }}>{k}</dt>
                  <dd className="mono" style={{ fontSize: 10.5 }}>{String(v)} <span className="badge" style={{ height: 15 }}>{t("physics.rebuild")}</span></dd>
                </div>
              ))}
            </dl>
          </details>
        ))}
      </div>

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("physics.contact")}</span>
          <span className="badge"><RefreshCw size={10} /> {t("physics.rebuild")}</span>
        </div>
        <dl className="kv">
          <dt>{t("physics.margin")}</dt>
          <dd className="mono">{mm(p.collision.softContactMargin, t("physics.solverDefault"))}</dd>
          <dt>{t("physics.gap")}</dt>
          <dd className="mono">{mm(p.collision.rigidGap, t("physics.solverDefault"))}</dd>
          <dt>{t("physics.stiffness")}</dt>
          <dd className="mono">
            ke {p.collision.contactKe === null ? t("physics.solverDefault") : p.collision.contactKe.toExponential(1)} · kd{" "}
            {p.collision.contactKd === null ? t("physics.solverDefault") : p.collision.contactKd.toExponential(1)}
          </dd>
          <dt>{t("physics.fullSurface")}</dt>
          <dd>{p.collision.fullSurfaceContact ? t("common.yes") : t("common.no")}</dd>
        </dl>
        <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("physics.rebuildNote")}</p>
      </div>

      {(p.problems.length > 0 || p.warnings.length > 0) && (
        <div className="section">
          {[...p.problems, ...p.warnings].map((w) => (
            <div key={w} className="notice warn" style={{ marginBottom: 6 }}>
              <AlertTriangle size={14} />
              <span>{w}</span>
            </div>
          ))}
        </div>
      )}

      <div className="section">
        <div className="section-title">{t("physics.summary")}</div>
        <pre className="license" style={{ maxHeight: 200 }}>{p.summary}</pre>
      </div>
    </div>
  );
}
