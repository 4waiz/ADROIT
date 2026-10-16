// SPDX-License-Identifier: Apache-2.0
// Mission control: the scenario's own routines, and sequences you build step by step. A sequence
// runs on the host as a task -- each step a real motion of the arm's target, a real grip, a real
// wait -- and each step reports done, or failed with the reason (a hand that stopped short).

import { ArrowDown, ArrowUp, ListPlus, Play, Plus, Square, Trash2, Wand2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { bridge } from "../api/socket";
import type { Manifest, RunnerInfo } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";

type Op = "home" | "move" | "grip" | "wait";
type TargetKind = "object" | "region" | "relative" | "pose";

export interface StepDraft {
  id: string;
  op: Op;
  arm?: string;
  value?: number;
  seconds?: number;
  target?: { kind: TargetKind; name?: string; offset?: [number, number, number]; height?: number; position?: number[]; quat?: number[] };
  orientation?: "keep" | "down" | "grasp";
  speed?: number;
  tolerance?: number;
}

let seq = 0;
export const nid = () => `s${Date.now().toString(36)}${(seq++).toString(36)}`;

function objectHalfHeight(manifest: Manifest, name: string): number {
  const shape = manifest.shapes.find((s) => s.owner === `object:${name}` && s.type === "box");
  if (shape) return shape.size[2];
  const sphere = manifest.shapes.find((s) => s.owner === `object:${name}` && (s.type === "sphere" || s.type === "cylinder" || s.type === "capsule"));
  return sphere ? (sphere.type === "sphere" ? sphere.size[0] : sphere.size[1]) : 0.025;
}

export function pickAndPlace(manifest: Manifest, arm: string, object: string, region: string): StepDraft[] {
  const half = objectHalfHeight(manifest, object);
  return [
    { id: nid(), op: "home", arm, speed: 0.2 },
    { id: nid(), op: "grip", arm, value: 0, seconds: 0.4 },
    { id: nid(), op: "move", arm, target: { kind: "object", name: object, offset: [0, 0, 0.12] }, orientation: "grasp", speed: 0.2 },
    { id: nid(), op: "move", arm, target: { kind: "object", name: object, offset: [0, 0, 0] }, orientation: "grasp", speed: 0.08 },
    { id: nid(), op: "grip", arm, value: 1, seconds: 0.8 },
    { id: nid(), op: "move", arm, target: { kind: "relative", offset: [0, 0, 0.14] }, orientation: "keep", speed: 0.12, tolerance: 0.012 },
    { id: nid(), op: "move", arm, target: { kind: "region", name: region, height: 0.16 }, orientation: "keep", speed: 0.15, tolerance: 0.012 },
    { id: nid(), op: "move", arm, target: { kind: "region", name: region, height: half + 0.004 }, orientation: "keep", speed: 0.06, tolerance: 0.012 },
    { id: nid(), op: "grip", arm, value: 0, seconds: 0.6 },
    { id: nid(), op: "move", arm, target: { kind: "relative", offset: [0, 0, 0.12] }, orientation: "keep", speed: 0.15 },
    { id: nid(), op: "home", arm, speed: 0.2 },
  ];
}

export function toWire(step: StepDraft): Record<string, unknown> {
  const out: Record<string, unknown> = { op: step.op };
  if (step.op !== "wait") out.arm = step.arm;
  if (step.op === "grip") Object.assign(out, { value: step.value ?? 1, seconds: step.seconds ?? 0.6 });
  if (step.op === "wait") out.seconds = step.seconds ?? 1;
  if (step.op === "move" || step.op === "home") {
    if (step.speed !== undefined) out.speed = step.speed;
    if (step.tolerance !== undefined) out.tolerance = step.tolerance;
  }
  if (step.op === "move") {
    out.target = step.target;
    out.orientation = step.orientation ?? "keep";
  }
  return out;
}

function loadSteps(): StepDraft[] {
  try {
    const raw = localStorage.getItem("bridge.mission");
    return raw ? (JSON.parse(raw) as StepDraft[]) : [];
  } catch {
    return [];
  }
}

function RunnerView({ runner, live }: { runner: RunnerInfo; live: boolean }) {
  const t = useT();
  return (
    <div>
      <div className="row" style={{ marginBottom: 8 }}>
        <span className={`badge ${live ? "accent" : runner.status === "completed" ? "ok" : runner.status === "failed" ? "err" : "warn"}`}>
          {live ? t("tasks.running") : t(`ack.${runner.status ?? "completed"}` as "ack.completed")}
        </span>
        <b style={{ fontSize: 13 }}>{runner.name}</b>
        <span className="grow" />
        <span className="mono muted" style={{ fontSize: 11 }}>{runner.seconds.toFixed(1)} s · {runner.steps} {t("tasks.controlSteps")}</span>
      </div>
      {runner.message && !live && <p style={{ margin: "0 0 8px", fontSize: 12, color: runner.status === "failed" ? "var(--err)" : "var(--text-1)" }}>{runner.message}</p>}
      {live && runner.phase && <p style={{ margin: "0 0 8px", fontSize: 12 }}>{runner.phase}</p>}
      {runner.plan.length > 0 && (
        <ol className="steps">
          {runner.plan.map((step) => (
            <li key={step.index} className={`step ${step.status}`}>
              <span className="n">{step.index + 1}</span>
              <span>
                {step.label}
                {step.warning && <div className="err">{t("tasks.warning")}: {step.warning}</div>}
                {step.error && <div className="err">{step.error}</div>}
              </span>
              <span className="mono muted" style={{ fontSize: 10.5 }}>{step.seconds !== undefined ? `${step.seconds.toFixed(2)} s` : t(`step.${step.status}` as "step.pending")}</span>
            </li>
          ))}
        </ol>
      )}
      {runner.log.length > 0 && (
        <pre className="license" style={{ maxHeight: 120, marginTop: 8 }}>{runner.log.join("\n")}</pre>
      )}
    </div>
  );
}

export function TaskPanel() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const status = useStore((s) => s.status);
  const [steps, setSteps] = useState<StepDraft[]>(loadSteps);
  const arms = manifest?.arms.map((a) => a.name) ?? [];
  const objects = manifest?.subjects.filter((s) => s.kind === "object" && !s.static).map((s) => s.name) ?? [];
  const regions = manifest?.regions.map((r) => r.name) ?? [];
  const [tpl, setTpl] = useState({ arm: "", object: "", region: "" });
  const runner = status?.runner ?? null;
  const last = status?.lastRunner ?? null;

  useEffect(() => {
    try {
      localStorage.setItem("bridge.mission", JSON.stringify(steps));
    } catch {
      /* ignore */
    }
  }, [steps]);

  useEffect(() => {
    setTpl({ arm: arms[0] ?? "", object: objects[0] ?? "", region: regions[0] ?? "" });
    // Steps that name things this scene does not have are kept; the host refuses them by name.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [manifest?.generation]);

  const invalid = useMemo(() => {
    const out: string[] = [];
    steps.forEach((s, k) => {
      if (s.op !== "wait" && !arms.includes(s.arm ?? "")) out.push(t("tasks.badArm", { n: k + 1, arm: s.arm ?? "?" }));
      if (s.op === "move" && s.target?.kind === "object" && !objects.includes(s.target.name ?? "")) out.push(t("tasks.badObject", { n: k + 1, name: s.target.name ?? "?" }));
      if (s.op === "move" && s.target?.kind === "region" && !regions.includes(s.target.name ?? "")) out.push(t("tasks.badRegion", { n: k + 1, name: s.target.name ?? "?" }));
    });
    return out;
  }, [steps, arms, objects, regions, t]);

  if (!manifest) return <div className="empty">{t("inspect.noScene")}</div>;

  const update = (id: string, patch: Partial<StepDraft>) => setSteps(steps.map((s) => (s.id === id ? { ...s, ...patch } : s)));
  const move = (k: number, d: number) => {
    const next = [...steps];
    const [s] = next.splice(k, 1);
    next.splice(Math.max(0, Math.min(next.length, k + d)), 0, s);
    setSteps(next);
  };
  const add = (op: Op) => {
    const arm = arms[0];
    const base: StepDraft = { id: nid(), op, arm };
    if (op === "grip") Object.assign(base, { value: 1, seconds: 0.6 });
    if (op === "wait") Object.assign(base, { seconds: 1 });
    if (op === "move") Object.assign(base, { target: { kind: "relative", offset: [0, 0, 0.05] }, orientation: "keep", speed: 0.15 });
    setSteps([...steps, base]);
  };

  return (
    <div className="split">
      <div>
        <div className="section">
          <div className="section-title">{t("tasks.routines")}</div>
          {status?.routines.length ? (
            <div className="row wrap">
              {status.routines.map((name) => (
                <button key={name} type="button" className="btn sm primary" disabled={Boolean(runner)} onClick={() => void bridge.command("routine.start", { name })}>
                  <Play size={12} /> {name}
                </button>
              ))}
            </div>
          ) : (
            <p className="muted" style={{ margin: 0, fontSize: 12 }}>{t("tasks.noRoutines")}</p>
          )}
          <p className="muted" style={{ margin: "8px 0 0", fontSize: 11 }}>{t("tasks.routinesNote")}</p>
        </div>
        <div className="section">
          <div className="section-title">{t("tasks.template")}</div>
          <div className="grid-3">
            <select className="select" aria-label={t("tasks.arm")} value={tpl.arm} onChange={(e) => setTpl({ ...tpl, arm: e.target.value })}>
              {arms.map((a) => <option key={a}>{a}</option>)}
            </select>
            <select className="select" aria-label={t("tasks.object")} value={tpl.object} onChange={(e) => setTpl({ ...tpl, object: e.target.value })}>
              {objects.length ? objects.map((o) => <option key={o}>{o}</option>) : <option value="">{t("tasks.noObjects")}</option>}
            </select>
            <select className="select" aria-label={t("tasks.zone")} value={tpl.region} onChange={(e) => setTpl({ ...tpl, region: e.target.value })}>
              {regions.length ? regions.map((r) => <option key={r}>{r}</option>) : <option value="">{t("tasks.noRegions")}</option>}
            </select>
          </div>
          <button
            type="button"
            className="btn sm"
            style={{ marginTop: 8 }}
            disabled={!tpl.arm || !tpl.object || !tpl.region}
            onClick={() => setSteps(pickAndPlace(manifest, tpl.arm, tpl.object, tpl.region))}
          >
            <Wand2 size={12} /> {t("tasks.buildPickPlace")}
          </button>
        </div>
        <div className="section">
          <div className="section-title">
            <span className="grow">{t("tasks.sequence", { n: steps.length })}</span>
            <button type="button" className="btn sm ghost" onClick={() => setSteps([])} disabled={!steps.length}>
              <Trash2 size={12} /> {t("tasks.clear")}
            </button>
          </div>
          <ol className="steps">
            {steps.map((s, k) => (
              <li key={s.id} className="step" style={{ gridTemplateColumns: "26px minmax(0,1fr) auto" }}>
                <span className="n">{k + 1}</span>
                <div className="row wrap" style={{ gap: 4 }}>
                  <select className="select" style={{ width: 82 }} aria-label={t("tasks.op")} value={s.op} onChange={(e) => update(s.id, { op: e.target.value as Op })}>
                    {(["home", "move", "grip", "wait"] as Op[]).map((op) => <option key={op} value={op}>{t(`op.${op}` as "op.home")}</option>)}
                  </select>
                  {s.op !== "wait" && (
                    <select className="select" style={{ width: 80 }} aria-label={t("tasks.arm")} value={s.arm} onChange={(e) => update(s.id, { arm: e.target.value })}>
                      {arms.map((a) => <option key={a}>{a}</option>)}
                    </select>
                  )}
                  {s.op === "grip" && (
                    <select className="select" style={{ width: 90 }} aria-label={t("tasks.grip")} value={s.value} onChange={(e) => update(s.id, { value: Number(e.target.value) })}>
                      <option value={0}>{t("arm.open")}</option>
                      <option value={0.5}>50%</option>
                      <option value={1}>{t("arm.close")}</option>
                    </select>
                  )}
                  {(s.op === "wait" || s.op === "grip") && (
                    <input className="input num" style={{ width: 64 }} aria-label={t("tasks.seconds")} type="number" step={0.1} min={0.1} value={s.seconds ?? 1} onChange={(e) => update(s.id, { seconds: Number(e.target.value) })} />
                  )}
                  {s.op === "move" && s.target && (
                    <>
                      <select
                        className="select"
                        style={{ width: 92 }}
                        aria-label={t("tasks.targetKind")}
                        value={s.target.kind}
                        onChange={(e) => {
                          const kind = e.target.value as TargetKind;
                          update(s.id, {
                            target: kind === "object" ? { kind, name: objects[0], offset: [0, 0, 0.1] } : kind === "region" ? { kind, name: regions[0], height: 0.12 } : { kind, offset: [0, 0, 0.05] },
                            orientation: kind === "object" ? "grasp" : "keep",
                          });
                        }}
                      >
                        <option value="object">{t("tasks.toObject")}</option>
                        <option value="region">{t("tasks.toRegion")}</option>
                        <option value="relative">{t("tasks.relative")}</option>
                      </select>
                      {s.target.kind === "object" && (
                        <select className="select" style={{ width: 90 }} aria-label={t("tasks.object")} value={s.target.name} onChange={(e) => update(s.id, { target: { ...s.target!, name: e.target.value } })}>
                          {objects.map((o) => <option key={o}>{o}</option>)}
                        </select>
                      )}
                      {s.target.kind === "region" && (
                        <select className="select" style={{ width: 96 }} aria-label={t("tasks.zone")} value={s.target.name} onChange={(e) => update(s.id, { target: { ...s.target!, name: e.target.value } })}>
                          {regions.map((r) => <option key={r}>{r}</option>)}
                        </select>
                      )}
                      {s.target.kind === "region" ? (
                        <input className="input num" style={{ width: 70 }} aria-label={t("tasks.heightMm")} type="number" step={5} value={Math.round((s.target.height ?? 0.1) * 1000)} onChange={(e) => update(s.id, { target: { ...s.target!, height: Number(e.target.value) / 1000 } })} title={t("tasks.heightMm")} />
                      ) : (
                        <input className="input num" style={{ width: 70 }} aria-label={t("tasks.dzMm")} type="number" step={5} value={Math.round((s.target.offset?.[2] ?? 0) * 1000)} onChange={(e) => update(s.id, { target: { ...s.target!, offset: [s.target!.offset?.[0] ?? 0, s.target!.offset?.[1] ?? 0, Number(e.target.value) / 1000] } })} title={t("tasks.dzMm")} />
                      )}
                    </>
                  )}
                </div>
                <div className="row" style={{ gap: 0 }}>
                  <button type="button" className="icon-btn sm" aria-label={t("tasks.up")} onClick={() => move(k, -1)} disabled={k === 0}><ArrowUp size={12} /></button>
                  <button type="button" className="icon-btn sm" aria-label={t("tasks.down")} onClick={() => move(k, 1)} disabled={k === steps.length - 1}><ArrowDown size={12} /></button>
                  <button type="button" className="icon-btn sm" aria-label={t("tasks.remove")} onClick={() => setSteps(steps.filter((x) => x.id !== s.id))}><Trash2 size={12} /></button>
                </div>
              </li>
            ))}
          </ol>
          <div className="row wrap" style={{ marginTop: 6 }}>
            <ListPlus size={14} className="muted" />
            {(["move", "grip", "wait", "home"] as Op[]).map((op) => (
              <button key={op} type="button" className="btn sm" onClick={() => add(op)}>
                <Plus size={11} /> {t(`op.${op}` as "op.home")}
              </button>
            ))}
          </div>
          {invalid.length > 0 && (
            <div className="notice warn" style={{ marginTop: 8 }}>
              <span>{invalid.slice(0, 3).join(" · ")}</span>
            </div>
          )}
          <div className="row" style={{ marginTop: 10 }}>
            <button
              type="button"
              className="btn primary"
              disabled={!steps.length || Boolean(runner) || invalid.length > 0}
              onClick={() => void bridge.command("task.run", { name: t("tasks.missionName"), steps: steps.map(toWire) })}
            >
              <Play size={13} /> {t("tasks.run")}
            </button>
            <button type="button" className="btn danger" disabled={!runner} onClick={() => void bridge.command("task.stop")}>
              <Square size={12} /> {t("tasks.stop")}
            </button>
          </div>
        </div>
      </div>
      <div>
        <div className="section">
          <div className="section-title">{t("tasks.execution")}</div>
          {runner ? (
            <RunnerView runner={runner} live />
          ) : last ? (
            <RunnerView runner={last} live={false} />
          ) : (
            <p className="muted" style={{ margin: 0, fontSize: 12 }}>{t("tasks.idle")}</p>
          )}
        </div>
        {status?.success && (
          <div className="section">
            <div className="section-title">{t("tasks.goal")}</div>
            <div className={`notice ${status.success.done ? "ok" : ""}`}>
              <span>{status.success.detail}</span>
            </div>
            {status.goalMet && !status.success.done && (
              <div className="notice ok" style={{ marginTop: 6 }}>
                <span>{t("tasks.metAt", { t: status.goalMet.simTime.toFixed(2), detail: status.goalMet.detail })}</span>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
