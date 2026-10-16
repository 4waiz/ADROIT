// SPDX-License-Identifier: Apache-2.0
// Driving one arm: its real joints (names, limits, measured and commanded values), the gripper
// (command against measured closure), the tool centre (measured pose, a typed target with an
// off-line reachability verdict) and jogging. Every control sends a validated command to the
// host and shows the host's answer; nothing here moves the drawn robot by itself.

import { AlertTriangle, CheckCircle2, ChevronsDown, ChevronsUp, Hand, Home, LocateFixed, Minus, Plus, Undo2 } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { bridge } from "../api/socket";
import type { ArmInfo, ArmTelemetry, JointInfo } from "../api/types";
import { fmt, useT } from "../i18n";
import { useStore } from "../state/store";
import { NumberField, Segmented } from "./ui/primitives";

const RAD = 180 / Math.PI;

function quatToRpy(q: number[]): [number, number, number] {
  const [x, y, z, w] = q;
  const roll = Math.atan2(2 * (w * x + y * z), 1 - 2 * (x * x + y * y));
  const sinp = Math.max(-1, Math.min(1, 2 * (w * y - z * x)));
  const pitch = Math.asin(sinp);
  const yaw = Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
  return [roll, pitch, yaw];
}

function rpyToQuat(r: number, p: number, y: number): [number, number, number, number] {
  const cr = Math.cos(r / 2), sr = Math.sin(r / 2), cp = Math.cos(p / 2), sp = Math.sin(p / 2), cy = Math.cos(y / 2), sy = Math.sin(y / 2);
  return [sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy];
}

function JointRow({
  arm,
  joint,
  index,
  tele,
  focused,
  generation,
}: {
  arm: ArmInfo;
  joint: JointInfo;
  index: number;
  tele: ArmTelemetry | undefined;
  focused: boolean;
  generation: number;
}) {
  const t = useT();
  const angleUnit = useStore((s) => s.angleUnit);
  const jointStep = useStore((s) => s.jointStep);
  const [drag, setDrag] = useState<number | null>(null);
  const lastSent = useRef(0);
  const row = useRef<HTMLDivElement>(null);
  const lo = joint.lower ?? -Math.PI;
  const hi = joint.upper ?? Math.PI;
  const measured = tele?.q[index];
  const goal = tele?.mode === "joint" ? tele.goal?.[index] : undefined;
  const value = drag ?? goal ?? measured ?? joint.home ?? 0;
  const angular = joint.type === "revolute";
  const scale = angular && angleUnit === "deg" ? RAD : angular ? 1 : 1000;
  const unit = angular ? (angleUnit === "deg" ? "°" : "rad") : "mm";
  const effort = tele?.effort?.[index];

  useEffect(() => {
    if (focused) row.current?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [focused]);

  const send = (v: number, final: boolean) => {
    const now = performance.now();
    if (!final && now - lastSent.current < 90) return;
    lastSent.current = now;
    void bridge.command("arm.joint", { arm: arm.name, joint: index, value: v, generation });
  };
  const pct = (v: number) => `${((Math.min(hi, Math.max(lo, v)) - lo) / (hi - lo)) * 100}%`;

  return (
    <div className="joint" ref={row} style={focused ? { background: "var(--accent-soft)" } : undefined}>
      <span className="jn" title={joint.name}>{index + 1}</span>
      <div style={{ minWidth: 0 }}>
        <div className="row" style={{ justifyContent: "space-between" }}>
          <span className="jname" title={joint.name}>{joint.name}</span>
          <span className="mono muted" style={{ fontSize: 10 }}>
            {effort !== null && effort !== undefined ? `${fmt(effort, 2)} ${angular ? "N·m" : "N"}` : ""}
          </span>
        </div>
        <div className="range">
          <div className="track">
            <div className="fill" style={{ insetInlineStart: 0, width: pct(value) }} />
          </div>
          {measured !== undefined && <span className="measured" style={{ insetInlineStart: pct(measured) }} title={t("arm.measured")} />}
          <input
            type="range"
            min={lo}
            max={hi}
            step={(hi - lo) / 1000}
            value={value}
            aria-label={t("arm.jointTarget", { n: index + 1, name: joint.name })}
            aria-valuetext={`${fmt(value * scale, angular ? 1 : 1)} ${unit}`}
            onChange={(e) => {
              const v = Number(e.target.value);
              setDrag(v);
              send(v, false);
            }}
            onPointerUp={() => {
              if (drag !== null) send(drag, true);
              window.setTimeout(() => setDrag(null), 250);
            }}
            onKeyUp={() => {
              if (drag !== null) send(drag, true);
              window.setTimeout(() => setDrag(null), 250);
            }}
          />
        </div>
        <div className="jlim">
          <span className="num">{fmt(lo * scale, 0)}</span>
          <span>
            {t("arm.measuredShort")} <span className="num">{measured !== undefined ? fmt(measured * scale, 1) : "—"} {unit}</span>
          </span>
          <span className="num">{fmt(hi * scale, 0)}</span>
        </div>
      </div>
      <div className="jval">
        <NumberField
          label={t("arm.jointValue", { n: index + 1 })}
          value={value * scale}
          digits={1}
          onCommit={(v) => send(Math.min(hi, Math.max(lo, v / scale)), true)}
        />
        <div className="row" style={{ gap: 2, marginTop: 3 }}>
          <button
            type="button"
            className="btn sm grow"
            aria-label={t("arm.jogMinus", { n: index + 1 })}
            onClick={() => void bridge.command("arm.jog", { arm: arm.name, space: "joint", joint: index, delta: -jointStep * (angular ? 1 : 0.1), generation })}
          >
            <Minus size={11} />
          </button>
          <button
            type="button"
            className="btn sm grow"
            aria-label={t("arm.jogPlus", { n: index + 1 })}
            onClick={() => void bridge.command("arm.jog", { arm: arm.name, space: "joint", joint: index, delta: jointStep * (angular ? 1 : 0.1), generation })}
          >
            <Plus size={11} />
          </button>
        </div>
      </div>
    </div>
  );
}

export function ArmInspector({ arm }: { arm: ArmInfo }) {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const tele = useStore((s) => s.telemetry?.arms[arm.name]);
  const status = useStore((s) => s.status);
  const focusJoint = useStore((s) => s.focusJoint);
  const jogStep = useStore((s) => s.jogStep);
  const jogAngle = useStore((s) => s.jogAngle);
  const jointStep = useStore((s) => s.jointStep);
  const angleUnit = useStore((s) => s.angleUnit);
  const setPref = useStore((s) => s.setPref);
  const [frame, setFrame] = useState<"world" | "tool">("world");
  const generation = manifest?.generation ?? 0;
  const owner = status?.arms[arm.name]?.owner ?? "operator";
  const locked = owner !== "operator";

  const ee = tele?.ee;
  const rpy = useMemo(() => (ee ? quatToRpy(ee.slice(3)) : [0, 0, 0]), [ee]);
  const [target, setTarget] = useState<number[] | null>(null);
  const shownTarget = target ?? (ee ? [ee[0], ee[1], ee[2], rpy[0], rpy[1], rpy[2]] : [0, 0, 0, 0, 0, 0]);

  const grip = tele?.grip;
  const stalled = grip && grip.closure !== null && grip.command > 0.6 && grip.closure < grip.command - 0.12;
  const reach = tele?.reach;

  const jog = (axis: string, sign: number) => {
    const rotational = axis.startsWith("r");
    void bridge.command("arm.jog", {
      arm: arm.name,
      space: "cartesian",
      axis,
      frame,
      delta: sign * (rotational ? jogAngle : jogStep),
      generation,
    });
  };

  const moveTo = (values: number[]) => {
    const quat = rpyToQuat(values[3], values[4], values[5]);
    void bridge.command("arm.cursor", { arm: arm.name, position: values.slice(0, 3), quat, generation }).then((ack) => {
      if (ack.status === "completed") setTarget(null);
    });
  };

  return (
    <div>
      <div className="section">
        <div className="section-title">
          <span className="grow">{t("arm.title", { name: arm.name })}</span>
          <span className={`badge ${tele?.mode === "joint" ? "info" : "accent"}`}>
            {tele?.mode === "joint" ? t("arm.modeJoint") : t("arm.modeCartesian")}
          </span>
        </div>
        <dl className="kv">
          <dt>{t("arm.robot")}</dt>
          <dd>{manifest?.scenario.robot || "—"}{arm.role ? ` · ${arm.role}` : ""}</dd>
          <dt>{t("arm.dof")}</dt>
          <dd>{arm.joints.length}</dd>
          <dt>{t("arm.controlledBy")}</dt>
          <dd>{locked ? <span className="badge warn">{owner}</span> : <span className="badge ok">{t("arm.operator")}</span>}</dd>
          <dt>{t("arm.status")}</dt>
          <dd className="mono">{tele?.status ?? "—"}</dd>
        </dl>
        {locked && (
          <div className="notice warn" style={{ marginTop: 10 }}>
            <AlertTriangle size={14} />
            <span>{t("arm.lockedNote", { owner })}</span>
          </div>
        )}
        <div className="row wrap" style={{ marginTop: 10 }}>
          <button type="button" className="btn sm" disabled={locked} onClick={() => void bridge.command("arm.home", { arm: arm.name, generation })}>
            <Home size={13} /> {t("arm.home")}
          </button>
          <button type="button" className="btn sm" disabled={locked} onClick={() => void bridge.command("arm.take", { arm: arm.name, generation })} title={t("arm.holdHint")}>
            <LocateFixed size={13} /> {t("arm.hold")}
          </button>
          <button type="button" className="btn sm" onClick={() => useStore.getState().requestFocus()}>
            <Undo2 size={13} /> {t("arm.focus")}
          </button>
        </div>
      </div>

      {arm.gripper && (
        <div className="section">
          <div className="section-title">
            <span className="grow">{t("arm.gripper")}</span>
            <span className="aside">{arm.gripper.kind} · {(arm.gripper.span * 1000).toFixed(0)} mm</span>
          </div>
          <div className="row" style={{ marginBottom: 10 }}>
            <button type="button" className="btn grow" disabled={locked} onClick={() => void bridge.command("arm.grip", { arm: arm.name, value: 0, generation })}>
              <ChevronsUp size={14} /> {t("arm.open")}
            </button>
            <button type="button" className="btn primary grow" disabled={locked} onClick={() => void bridge.command("arm.grip", { arm: arm.name, value: 1, generation })}>
              <Hand size={14} /> {t("arm.close")}
            </button>
          </div>
          <div className="field">
            <span className="field-label">{t("arm.gripCommand")}</span>
            <div className="range">
              <div className="track"><div className="fill" style={{ insetInlineStart: 0, width: `${(grip?.command ?? 0) * 100}%` }} /></div>
              {grip?.closure !== null && grip?.closure !== undefined && (
                <span className="measured" style={{ insetInlineStart: `${grip.closure * 100}%` }} title={t("arm.measuredClosure")} />
              )}
              <input
                type="range"
                min={0}
                max={1}
                step={0.01}
                disabled={locked}
                value={grip?.command ?? 0}
                aria-label={t("arm.gripCommand")}
                onChange={(e) => void bridge.command("arm.grip", { arm: arm.name, value: Number(e.target.value), generation })}
              />
            </div>
          </div>
          <dl className="kv">
            <dt>{t("arm.opening")}</dt>
            <dd className="mono">{grip ? `${(grip.width * 1000).toFixed(1)} mm` : "—"}</dd>
            <dt>{t("arm.closure")}</dt>
            <dd className="mono">
              {grip?.closure !== null && grip?.closure !== undefined ? `${(grip.closure * 100).toFixed(0)}% ${t("arm.measuredSuffix")}` : "—"} ·{" "}
              {grip ? `${(grip.command * 100).toFixed(0)}% ${t("arm.commandedSuffix")}` : ""}
            </dd>
          </dl>
          {stalled && (
            <div className="notice ok" style={{ marginTop: 8 }}>
              <CheckCircle2 size={14} />
              <span>{t("arm.stalled", { closure: ((grip?.closure ?? 0) * 100).toFixed(0), command: ((grip?.command ?? 0) * 100).toFixed(0) })}</span>
            </div>
          )}
        </div>
      )}

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("arm.endEffector")}</span>
          <span className="aside">{t("arm.worldFrame")}</span>
        </div>
        <div className="grid-3" style={{ marginBottom: 6 }}>
          {["X", "Y", "Z"].map((axis, k) => (
            <div key={axis} className="field" style={{ marginBottom: 0 }}>
              <span className="field-label">{axis} (mm)</span>
              <NumberField
                label={`${t("arm.target")} ${axis}`}
                value={shownTarget[k] * 1000}
                digits={1}
                disabled={locked}
                onCommit={(v) => {
                  const next = [...shownTarget];
                  next[k] = v / 1000;
                  setTarget(next);
                }}
              />
            </div>
          ))}
          {["Roll", "Pitch", "Yaw"].map((axis, k) => (
            <div key={axis} className="field" style={{ marginBottom: 0 }}>
              <span className="field-label">{t(`arm.${axis.toLowerCase()}` as "arm.roll")} (°)</span>
              <NumberField
                label={`${t("arm.target")} ${axis}`}
                value={shownTarget[3 + k] * RAD}
                digits={1}
                disabled={locked}
                onCommit={(v) => {
                  const next = [...shownTarget];
                  next[3 + k] = v / RAD;
                  setTarget(next);
                }}
              />
            </div>
          ))}
        </div>
        <div className="row" style={{ marginBottom: 8 }}>
          <button type="button" className="btn sm primary grow" disabled={locked || !target} onClick={() => target && moveTo(target)}>
            {t("arm.moveTo")}
          </button>
          <button
            type="button"
            className="btn sm grow"
            onClick={() =>
              void bridge.command("arm.reach", {
                arm: arm.name,
                position: shownTarget.slice(0, 3),
                quat: rpyToQuat(shownTarget[3], shownTarget[4], shownTarget[5]),
              })
            }
          >
            {t("arm.checkReach")}
          </button>
          <button type="button" className="btn sm" disabled={!target} onClick={() => setTarget(null)}>
            {t("arm.clear")}
          </button>
        </div>
        {target && <p className="muted" style={{ margin: "0 0 8px", fontSize: 11 }}>{t("arm.targetPending")}</p>}
        {reach && (
          <div className={`notice ${reach.reachable ? "ok" : "err"}`} role="status">
            {reach.reachable ? <CheckCircle2 size={14} /> : <AlertTriangle size={14} />}
            <span>
              {reach.reachable ? t("arm.reachable") : `${t("arm.unreachable")}: ${reach.why ?? ""}`}
              <br />
              <span className="mono muted">
                {t("arm.residual")} {((reach.positionError ?? 0) * 1000).toFixed(1)} mm · {(((reach.angleError ?? 0) * RAD)).toFixed(1)}° · {reach.solver}
              </span>
            </span>
          </div>
        )}
        <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("arm.collisionNote")}</p>
      </div>

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("arm.jog")}</span>
          <Segmented
            label={t("arm.jogFrame")}
            value={frame}
            onChange={setFrame}
            options={[
              { value: "world", label: t("arm.world") },
              { value: "tool", label: t("arm.tool") },
            ]}
          />
        </div>
        <div className="jog" style={{ marginBottom: 8 }}>
          {(["x", "y", "z"] as const).map((axis) => (
            <div key={axis} className="row" style={{ gap: 2 }}>
              <button type="button" className="btn sm grow" disabled={locked} onClick={() => jog(axis, -1)} aria-label={`-${axis.toUpperCase()}`}>
                −{axis.toUpperCase()}
              </button>
              <button type="button" className="btn sm grow" disabled={locked} onClick={() => jog(axis, 1)} aria-label={`+${axis.toUpperCase()}`}>
                +{axis.toUpperCase()}
              </button>
            </div>
          ))}
          {(["rx", "ry", "rz"] as const).map((axis) => (
            <div key={axis} className="row" style={{ gap: 2 }}>
              <button type="button" className="btn sm grow" disabled={locked} onClick={() => jog(axis, -1)} aria-label={`-${axis.toUpperCase()}`}>
                −{axis.toUpperCase()}
              </button>
              <button type="button" className="btn sm grow" disabled={locked} onClick={() => jog(axis, 1)} aria-label={`+${axis.toUpperCase()}`}>
                +{axis.toUpperCase()}
              </button>
            </div>
          ))}
        </div>
        <div className="grid-3">
          <label className="field" style={{ marginBottom: 0 }}>
            <span className="field-label">{t("arm.step")}</span>
            <select className="select" aria-label={t("arm.jogStepLabel")} value={jogStep} onChange={(e) => setPref("jogStep", Number(e.target.value))}>
              {[0.001, 0.005, 0.01, 0.025, 0.05].map((v) => (
                <option key={v} value={v}>{v * 1000} mm</option>
              ))}
            </select>
          </label>
          <label className="field" style={{ marginBottom: 0 }}>
            <span className="field-label">{t("arm.turn")}</span>
            <select className="select" aria-label={t("arm.jogTurnLabel")} value={jogAngle} onChange={(e) => setPref("jogAngle", Number(e.target.value))}>
              {[1, 5, 15, 30].map((v) => (
                <option key={v} value={v / RAD}>{v}°</option>
              ))}
            </select>
          </label>
          <label className="field" style={{ marginBottom: 0 }}>
            <span className="field-label">{t("arm.jointStep")}</span>
            <select className="select" aria-label={t("arm.jointStepLabel")} value={jointStep} onChange={(e) => setPref("jointStep", Number(e.target.value))}>
              {[1, 5, 15].map((v) => (
                <option key={v} value={v / RAD}>{v}°</option>
              ))}
            </select>
          </label>
        </div>
        <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("arm.keyboardHint")}</p>
      </div>

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("arm.joints")}</span>
          <Segmented
            label={t("arm.units")}
            value={angleUnit}
            onChange={(v) => setPref("angleUnit", v)}
            options={[
              { value: "deg", label: "deg" },
              { value: "rad", label: "rad" },
            ]}
          />
        </div>
        {arm.joints.map((joint, k) => (
          <JointRow key={joint.name + k} arm={arm} joint={joint} index={k} tele={tele} focused={focusJoint === k} generation={generation} />
        ))}
      </div>

      <div className="section">
        <div className="section-title">{t("arm.speed")}</div>
        <div className="field">
          <span className="field-label">{t("arm.jointSpeed", { v: (tele?.speed.joint ?? 1).toFixed(2) })}</span>
          <input
            type="range"
            min={0.05}
            max={3}
            step={0.05}
            defaultValue={tele?.speed.joint ?? 1}
            aria-label={t("arm.jointSpeedLabel")}
            onPointerUp={(e) => void bridge.command("arm.speed", { arm: arm.name, joint: Number((e.target as HTMLInputElement).value) })}
            onKeyUp={(e) => void bridge.command("arm.speed", { arm: arm.name, joint: Number((e.target as HTMLInputElement).value) })}
          />
        </div>
        <div className="field" style={{ marginBottom: 0 }}>
          <span className="field-label">{t("arm.handSpeed", { v: ((tele?.speed.cartesian ?? 0.31) * 1000).toFixed(0) })}</span>
          <input
            type="range"
            min={0.05}
            max={1}
            step={0.05}
            defaultValue={1}
            aria-label={t("arm.handSpeedLabel")}
            onPointerUp={(e) => void bridge.command("arm.speed", { arm: arm.name, cartesian: Number((e.target as HTMLInputElement).value) })}
            onKeyUp={(e) => void bridge.command("arm.speed", { arm: arm.name, cartesian: Number((e.target as HTMLInputElement).value) })}
          />
        </div>
      </div>
      <div style={{ height: 8 }} />
      <span className="sr-only"><ChevronsDown size={1} /></span>
    </div>
  );
}
