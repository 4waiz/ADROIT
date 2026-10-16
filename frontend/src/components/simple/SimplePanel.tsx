// SPDX-License-Identifier: Apache-2.0
// Simple mode's control panel: the scene in a sentence, the robot moved with arrows and its hand
// opened and closed, programs run with one button or taught by showing the robot where to go,
// and what is happening in plain words. Every button sends the same validated commands the
// expert panels send; nothing here is a shortcut around the simulation.

import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Hand,
  Home,
  LayoutGrid,
  MapPin,
  Play,
  Plus,
  Repeat,
  RotateCcw,
  Square,
  Target,
  Timer,
  Trash2,
  Wand2,
} from "lucide-react";
import { type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { bridge } from "../../api/socket";
import type { ArmInfo, Manifest } from "../../api/types";
import { useT } from "../../i18n";
import { useStore } from "../../state/store";
import { pickAndPlace, type StepDraft, nid, toWire } from "../TaskPanel";
import { getEngine } from "../Viewport";

const ROBOTS: Record<string, string> = {
  panda: "Franka Panda",
  yam_bimanual: "YAM (two arms)",
  yam_i2rt: "YAM (two arms)",
  yam: "YAM",
};

const pretty = (name: string) => name.replace(/_/g, " ");

function Card({ title, icon, children, tour, aside }: { title: string; icon: ReactNode; children: ReactNode; tour?: string; aside?: ReactNode }) {
  return (
    <section className="s-card" data-tour={tour}>
      <header className="s-card-head">
        <span className="s-card-icon">{icon}</span>
        <h3>{title}</h3>
        <span className="grow" />
        {aside}
      </header>
      {children}
    </section>
  );
}

// -------------------------------------------------------------------- scene
function SceneCard() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const status = useStore((s) => s.status);
  const scenario = manifest?.scenario;
  const robot = scenario ? (ROBOTS[scenario.robot] ?? pretty(scenario.robot)) : "";
  return (
    <Card title={t("simple.scene")} icon={<LayoutGrid size={15} />} tour="scene">
      {/* Scene text comes from the simulation, in English: each line takes its own direction. */}
      <div className="s-scene-title" dir="auto">{scenario?.title ?? t("simple.loading")}</div>
      {scenario?.summary && <p className="s-text" dir="auto">{scenario.summary}</p>}
      {robot && (
        <div className="s-facts">
          <span>{t("simple.robot")}</span>
          <b dir="auto">{robot}</b>
        </div>
      )}
      <div className="s-row">
        <button type="button" className="btn grow" onClick={() => useStore.getState().openDialog("gallery")} data-tour="change-scene">
          <LayoutGrid size={14} /> {t("simple.changeScene")}
        </button>
        <button type="button" className="btn grow" disabled={status?.phase !== "ready"} onClick={() => void bridge.command("sim.reset")}>
          <RotateCcw size={14} /> {t("simple.startOver")}
        </button>
      </div>
    </Card>
  );
}

// -------------------------------------------------------------------- robot
const STEPS = [
  { value: 0.01, key: "simple.stepSmall" },
  { value: 0.05, key: "simple.stepMedium" },
  { value: 0.1, key: "simple.stepLarge" },
] as const;

/** The camera's ground-plane forward and right, so "left" means left on screen. */
function screenAxes(): { forward: [number, number]; right: [number, number] } {
  const view = getEngine()?.cameraState();
  let fx = 1;
  let fy = 0;
  if (view) {
    fx = view.lookAt[0] - view.eye[0];
    fy = view.lookAt[1] - view.eye[1];
    const n = Math.hypot(fx, fy) || 1;
    fx /= n;
    fy /= n;
  }
  // Right = forward x up (z), in the ground plane.
  return { forward: [fx, fy], right: [fy, -fx] };
}

function RobotCard({ arm, arms }: { arm: ArmInfo | null; arms: ArmInfo[] }) {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const tele = useStore((s) => (arm ? s.telemetry?.arms[arm.name] : undefined));
  const status = useStore((s) => s.status);
  const [step, setStep] = useState<number>(0.05);
  const generation = manifest?.generation ?? 0;
  const owner = arm ? (status?.arms[arm.name]?.owner ?? "operator") : "operator";
  const locked = !arm || owner !== "operator" || status?.phase !== "ready";

  const move = (dx: number, dy: number, dz: number) => {
    if (!arm || !tele) return;
    const base = tele.mode === "cartesian" ? tele.cursor : tele.ee;
    const position = [base[0] + dx, base[1] + dy, base[2] + dz];
    void bridge.command("arm.cursor", { arm: arm.name, position, quat: base.slice(3, 7), generation });
  };
  const planar = (along: "forward" | "right", sign: number) => {
    const axes = screenAxes();
    const [x, y] = axes[along];
    move(x * step * sign, y * step * sign, 0);
  };
  const grip = tele?.grip;
  const closed = (grip?.command ?? 0) > 0.5;
  const opening = grip ? Math.round(grip.width * 1000) : null;
  const span = arm?.gripper ? Math.round(arm.gripper.span * 1000) : null;

  return (
    <Card title={t("simple.robotTitle")} icon={<Hand size={15} />} tour="robot">
      {arms.length > 1 && (
        <div className="s-segmented" role="group" aria-label={t("simple.whichArm")}>
          {arms.map((a) => (
            <button key={a.name} type="button" aria-pressed={a.name === arm?.name} onClick={() => useStore.getState().select(`arm:${a.name}`)}>
              {t("simple.armNamed", { name: pretty(a.name) })}
            </button>
          ))}
        </div>
      )}
      {owner !== "operator" && <p className="s-note">{t("simple.busy")}</p>}

      <div className="s-label">{t("simple.moveHand")}</div>
      <div className="s-pad" data-tour="pad">
        {/* Screen directions are physical: the pad keeps left on the left in either language. */}
        <div className="s-dpad" role="group" aria-label={t("simple.moveHand")} dir="ltr">
          <span />
          <button type="button" className="btn" disabled={locked} onClick={() => planar("forward", 1)} aria-label={t("simple.away")} title={t("simple.away")}>
            <ArrowUp size={16} />
          </button>
          <span />
          <button type="button" className="btn" disabled={locked} onClick={() => planar("right", -1)} aria-label={t("simple.left")} title={t("simple.left")}>
            <ArrowLeft size={16} />
          </button>
          <span className="s-dpad-centre" aria-hidden>
            <Hand size={14} />
          </span>
          <button type="button" className="btn" disabled={locked} onClick={() => planar("right", 1)} aria-label={t("simple.right")} title={t("simple.right")}>
            <ArrowRight size={16} />
          </button>
          <span />
          <button type="button" className="btn" disabled={locked} onClick={() => planar("forward", -1)} aria-label={t("simple.toward")} title={t("simple.toward")}>
            <ArrowDown size={16} />
          </button>
          <span />
        </div>
        <div className="s-updown" role="group" aria-label={t("simple.height")}>
          <button type="button" className="btn" disabled={locked} onClick={() => move(0, 0, step)}>
            <ChevronUp size={16} /> {t("simple.up")}
          </button>
          <button type="button" className="btn" disabled={locked} onClick={() => move(0, 0, -step)}>
            <ChevronDown size={16} /> {t("simple.down")}
          </button>
        </div>
      </div>
      <div className="s-segmented small" role="group" aria-label={t("simple.stepSize")}>
        {STEPS.map((s) => (
          <button key={s.value} type="button" aria-pressed={step === s.value} onClick={() => setStep(s.value)}>
            {t(s.key)}
          </button>
        ))}
      </div>
      <p className="s-hint">{t("simple.dragHint")}</p>

      {arm?.gripper && (
        <>
          <div className="s-label">{t("simple.hand")}</div>
          <div className="s-hand" data-tour="hand">
            <button
              type="button"
              className={`btn s-big${closed ? "" : " primary"}`}
              disabled={locked}
              onClick={() => void bridge.command("arm.grip", { arm: arm.name, value: closed ? 0 : 1, generation })}
            >
              <Hand size={16} /> {closed ? t("simple.openHand") : t("simple.closeHand")}
            </button>
            <div className="s-meter" aria-label={t("simple.handOpening")}>
              <div className="s-meter-track">
                <div className="s-meter-fill" style={{ width: span && opening !== null ? `${Math.min(100, (opening / span) * 100)}%` : "0%" }} />
              </div>
              <span>{opening !== null ? t("simple.openBy", { mm: opening }) : "—"}</span>
            </div>
          </div>
        </>
      )}
      <button type="button" className="btn s-wide" disabled={locked} onClick={() => arm && void bridge.command("arm.home", { arm: arm.name, generation })}>
        <Home size={14} /> {t("simple.home")}
      </button>
    </Card>
  );
}

// ----------------------------------------------------------------- programs
function stepLabel(t: ReturnType<typeof useT>, s: StepDraft, points: Map<string, number>): string {
  switch (s.op) {
    case "home":
      return t("simple.stepHome");
    case "wait":
      return t("simple.stepWait", { s: s.seconds ?? 1 });
    case "grip":
      return (s.value ?? 1) > 0.5 ? t("simple.stepClose") : t("simple.stepOpen");
    case "move": {
      const target = s.target;
      if (!target) return t("simple.stepMove");
      if (target.kind === "pose") return t("simple.stepPoint", { n: points.get(s.id) ?? 1 });
      if (target.kind === "object") return (target.offset?.[2] ?? 0) > 0.02 ? t("simple.stepAbove", { name: pretty(target.name ?? "") }) : t("simple.stepTo", { name: pretty(target.name ?? "") });
      if (target.kind === "region") return (target.height ?? 0) > 0.1 ? t("simple.stepAbove", { name: pretty(target.name ?? "") }) : t("simple.stepInto", { name: pretty(target.name ?? "") });
      const dz = target.offset?.[2] ?? 0;
      return dz >= 0 ? t("simple.stepLift", { cm: Math.round(dz * 100) }) : t("simple.stepLower", { cm: Math.round(-dz * 100) });
    }
  }
}

function stepIcon(s: StepDraft) {
  if (s.op === "grip") return <Hand size={13} />;
  if (s.op === "wait") return <Timer size={13} />;
  if (s.op === "home") return <Home size={13} />;
  return s.target?.kind === "pose" ? <MapPin size={13} /> : <Target size={13} />;
}

function storageKey(manifest: Manifest | null) {
  return `bridge.myprogram.${manifest?.scenario.id ?? "scene"}`;
}

function ProgramsCard({ arm }: { arm: ArmInfo | null }) {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const status = useStore((s) => s.status);
  const tele = useStore((s) => (arm ? s.telemetry?.arms[arm.name] : undefined));
  const [steps, setSteps] = useState<StepDraft[]>([]);
  const [repeat, setRepeat] = useState(false);
  const [object, setObject] = useState("");
  const [region, setRegion] = useState("");
  const runner = status?.runner ?? null;
  const ready = status?.phase === "ready";
  const routines = status?.routines ?? [];
  const objects = useMemo(() => manifest?.subjects.filter((s) => s.kind === "object" && !s.static).map((s) => s.name) ?? [], [manifest]);
  const regions = useMemo(() => manifest?.regions.map((r) => r.name) ?? [], [manifest]);
  const lastRun = useRef<string | null>(null);

  // One program per scene, kept in this browser.
  useEffect(() => {
    try {
      const raw = localStorage.getItem(storageKey(manifest));
      setSteps(raw ? (JSON.parse(raw) as StepDraft[]) : []);
    } catch {
      setSteps([]);
    }
    setObject(objects[0] ?? "");
    setRegion(regions[0] ?? "");
  }, [manifest, objects, regions]);
  const save = (next: StepDraft[]) => {
    setSteps(next);
    try {
      localStorage.setItem(storageKey(manifest), JSON.stringify(next));
    } catch {
      /* storage unavailable: the program lives until the page closes */
    }
  };

  const add = (step: Omit<StepDraft, "id">) => save([...steps, { id: nid(), ...step }]);
  const savePoint = () => {
    if (!arm || !tele) return;
    const ee = tele.ee;
    add({ op: "move", arm: arm.name, target: { kind: "pose", position: ee.slice(0, 3), quat: ee.slice(3, 7) }, orientation: "keep", speed: 0.15 });
  };
  const run = () => {
    if (!steps.length) return;
    lastRun.current = "mine";
    void bridge.command("task.run", { name: t("simple.myProgram"), steps: steps.map(toWire) });
  };
  const stop = () => void bridge.command(runner?.kind === "task" ? "task.stop" : "routine.stop");

  // Repeat: when my program finishes cleanly, run it again.
  const last = status?.lastRunner;
  useEffect(() => {
    if (!repeat || runner || !last || last.kind !== "task" || last.status !== "completed" || lastRun.current !== "mine") return;
    const id = window.setTimeout(run, 600);
    return () => window.clearTimeout(id);
  }, [last, repeat, runner]);

  const points = new Map<string, number>();
  let n = 0;
  for (const s of steps) if (s.op === "move" && s.target?.kind === "pose") points.set(s.id, ++n);
  const moveStep = (k: number, d: number) => {
    const next = [...steps];
    const [s] = next.splice(k, 1);
    next.splice(Math.max(0, Math.min(next.length, k + d)), 0, s);
    save(next);
  };

  return (
    <Card title={t("simple.programs")} icon={<Play size={15} />} tour="programs">
      {runner ? (
        <div className="s-running">
          <div className="s-running-head">
            <span className="s-pulse" />
            <b dir="auto">{runner.name}</b>
            <span className="grow" />
            <span className="mono">{runner.seconds.toFixed(1)} s</span>
          </div>
          {runner.plan.length > 0 && (
            <div className="s-progress" aria-label={t("simple.progress")}>
              <div style={{ width: `${(runner.plan.filter((p) => p.status === "done").length / runner.plan.length) * 100}%` }} />
            </div>
          )}
          {runner.phase && <p className="s-text">{runner.phase}</p>}
          <button type="button" className="btn s-wide danger" onClick={stop}>
            <Square size={13} /> {t("simple.stopProgram")}
          </button>
        </div>
      ) : (
        routines.length > 0 && (
          <>
            <div className="s-label">{t("simple.readyMade")}</div>
            <div className="s-list" data-tour="ready-programs">
              {routines.map((name) => (
                <button key={name} type="button" className="s-program" disabled={!ready} onClick={() => { lastRun.current = name; void bridge.command("routine.start", { name }); }}>
                  <span className="s-play"><Play size={13} /></span>
                  <span className="grow" dir="auto">{name}</span>
                  <span className="muted">{t("simple.run")}</span>
                </button>
              ))}
            </div>
          </>
        )
      )}
      {last && !runner && (
        <p className={`s-note ${last.status === "completed" ? "ok" : last.status === "failed" ? "err" : ""}`}>
          {last.status === "completed" ? t("simple.lastDone", { name: last.name }) : last.status === "failed" ? t("simple.lastFailed", { name: last.name }) : t("simple.lastStopped", { name: last.name })}
        </p>
      )}

      <div className="s-teach" data-tour="teach">
        <div className="s-label">{t("simple.myProgram")}</div>
        <p className="s-hint">{t("simple.teachHint")}</p>
        <div className="s-add">
          <button type="button" className="btn sm" disabled={!arm || !tele} onClick={savePoint}>
            <MapPin size={13} /> {t("simple.addPoint")}
          </button>
          {arm?.gripper && (
            <>
              <button type="button" className="btn sm" disabled={!arm} onClick={() => arm && add({ op: "grip", arm: arm.name, value: 1, seconds: 0.8 })}>
                <Hand size={13} /> {t("simple.addClose")}
              </button>
              <button type="button" className="btn sm" disabled={!arm} onClick={() => arm && add({ op: "grip", arm: arm.name, value: 0, seconds: 0.6 })}>
                <Hand size={13} /> {t("simple.addOpen")}
              </button>
            </>
          )}
          <button type="button" className="btn sm" onClick={() => add({ op: "wait", seconds: 1 })}>
            <Timer size={13} /> {t("simple.addWait")}
          </button>
          <button type="button" className="btn sm" disabled={!arm} onClick={() => arm && add({ op: "home", arm: arm.name, speed: 0.2 })}>
            <Home size={13} /> {t("simple.addHome")}
          </button>
        </div>
        {objects.length > 0 && regions.length > 0 && arm && (
          <div className="s-wizard">
            <Wand2 size={13} />
            <span>{t("simple.quickPick")}</span>
            <select value={object} onChange={(e) => setObject(e.target.value)} aria-label={t("simple.whatToPick")}>
              {objects.map((o) => (
                <option key={o} value={o}>{pretty(o)}</option>
              ))}
            </select>
            <span>{t("simple.into")}</span>
            <select value={region} onChange={(e) => setRegion(e.target.value)} aria-label={t("simple.whereToPlace")}>
              {regions.map((r) => (
                <option key={r} value={r}>{pretty(r)}</option>
              ))}
            </select>
            <button type="button" className="btn sm" onClick={() => manifest && save(pickAndPlace(manifest, arm.name, object, region))}>
              <Plus size={13} /> {t("simple.make")}
            </button>
          </div>
        )}
        {steps.length === 0 ? (
          <p className="s-empty">{t("simple.noSteps")}</p>
        ) : (
          <ol className="s-steps">
            {steps.map((s, k) => {
              const live = runner?.kind === "task" && lastRun.current === "mine" ? runner.plan[k]?.status : undefined;
              return (
                <li key={s.id} className={live ? `is-${live}` : ""}>
                  <span className="s-step-n">{k + 1}</span>
                  <span className="s-step-icon">{stepIcon(s)}</span>
                  <span className="grow">{stepLabel(t, s, points)}</span>
                  <button type="button" className="icon-btn sm" aria-label={t("simple.earlier")} disabled={k === 0 || Boolean(runner)} onClick={() => moveStep(k, -1)}>
                    <ChevronUp size={13} />
                  </button>
                  <button type="button" className="icon-btn sm" aria-label={t("simple.later")} disabled={k === steps.length - 1 || Boolean(runner)} onClick={() => moveStep(k, 1)}>
                    <ChevronDown size={13} />
                  </button>
                  <button type="button" className="icon-btn sm" aria-label={t("simple.remove")} disabled={Boolean(runner)} onClick={() => save(steps.filter((x) => x.id !== s.id))}>
                    <Trash2 size={13} />
                  </button>
                </li>
              );
            })}
          </ol>
        )}
        <div className="s-row">
          <button type="button" className="btn primary grow" disabled={!steps.length || Boolean(runner) || !ready} onClick={run} data-tour="run-mine">
            <Play size={14} /> {t("simple.runMine")}
          </button>
          <button type="button" className={`btn${repeat ? " active" : ""}`} aria-pressed={repeat} onClick={() => setRepeat(!repeat)} title={t("simple.repeatHint")}>
            <Repeat size={14} /> {t("simple.repeat")}
          </button>
          <button type="button" className="btn" disabled={!steps.length || Boolean(runner)} onClick={() => save([])} title={t("simple.clear")} aria-label={t("simple.clear")}>
            <Trash2 size={14} />
          </button>
        </div>
      </div>
    </Card>
  );
}

// ------------------------------------------------------------------- status
function StatusCard() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const status = useStore((s) => s.status);
  const events = useStore((s) => s.events);
  const success = status?.success;
  const met = Boolean(success?.done || status?.goalMet);
  const goal = manifest?.scenario.successLabel;
  const recent = events.slice(-4).reverse();
  return (
    <Card title={t("simple.happening")} icon={<Target size={15} />} tour="status">
      {goal && (
        <div className={`s-goal${met ? " met" : ""}`}>
          {met ? <CheckCircle2 size={16} /> : <Target size={16} />}
          <div>
            <b>{met ? t("simple.goalReached") : t("simple.goal")}</b>
            <span dir="auto">{goal}</span>
          </div>
        </div>
      )}
      <ul className="s-events">
        {recent.length === 0 && <li className="muted">{t("simple.nothingYet")}</li>}
        {recent.map((e) => (
          <li key={e.seq} className={`lvl-${e.level}`}>
            <span className="dot" />
            <span dir="auto">{e.message}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

export function SimplePanel() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const selected = useStore((s) => s.selected);
  const arms = manifest?.arms ?? [];
  const arm = arms.find((a) => `arm:${a.name}` === selected) ?? arms[0] ?? null;

  // Simple mode always has an arm in hand: its gizmo is what the user drags.
  useEffect(() => {
    if (arms.length && !arms.some((a) => `arm:${a.name}` === selected)) useStore.getState().select(`arm:${arms[0].name}`);
  }, [arms, selected]);

  return (
    <aside className="panel right simple-panel" aria-label={t("simple.panel")}>
      <div className="s-scroll">
        <SceneCard />
        <RobotCard arm={arm} arms={arms} />
        <ProgramsCard arm={arm} />
        <StatusCard />
        <button type="button" className="s-expert" onClick={() => useStore.getState().setPref("uiMode", "expert")} data-tour="expert-link">
          {t("simple.toExpert")}
        </button>
      </div>
    </aside>
  );
}
