// SPDX-License-Identifier: Apache-2.0
// Live plots of what the simulator reported: joint positions, velocities and actuator efforts,
// the tool centre, the gripper, contacts and the step rate, plus the browser's own frame rate.
// A plot that has no source says so; it never draws a stand-in.

import { Pause, Play } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { type Series, telemetry } from "../state/telemetry";
import { Segmented } from "./ui/primitives";

const PALETTE = ["#ff6a3d", "#4ea8ff", "#37c088", "#f0b429", "#c78bff", "#ff5c8a", "#3fd0d0", "#a3b0bd"];

function cssVar(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || "#888";
}

interface PlotSpec {
  id: string;
  title: string;
  unit: string;
  labels: string[];
  series: () => Series | undefined;
  scale?: (v: number) => number;
  note?: string;
}

function Plot({ spec, seconds, frozen, theme }: { spec: PlotSpec; seconds: number; frozen: boolean; theme: string }) {
  const host = useRef<HTMLDivElement>(null);
  const plot = useRef<uPlot | null>(null);
  const [empty, setEmpty] = useState(true);

  useEffect(() => {
    const el = host.current;
    if (!el) return;
    const axis = cssVar("--text-3");
    const grid = cssVar("--plot-grid");
    const opts: uPlot.Options = {
      width: el.clientWidth || 300,
      height: Math.max(60, el.clientHeight || 100),
      legend: { show: false },
      cursor: { drag: { x: true, y: false }, points: { size: 5 } },
      scales: { x: { time: false } },
      axes: [
        { stroke: axis, grid: { stroke: grid, width: 1 }, ticks: { stroke: grid }, size: 22, font: "10px IBM Plex Mono", values: (_u, vals) => vals.map((v) => `${v.toFixed(0)}s`) },
        { stroke: axis, grid: { stroke: grid, width: 1 }, ticks: { stroke: grid }, size: 46, font: "10px IBM Plex Mono" },
      ],
      series: [{}, ...spec.labels.map((label, k) => ({ label, stroke: PALETTE[k % PALETTE.length], width: 1.5, spanGaps: false }))],
    };
    plot.current = new uPlot(opts, [[], ...spec.labels.map(() => [])] as uPlot.AlignedData, el);
    const ro = new ResizeObserver(() => {
      plot.current?.setSize({ width: el.clientWidth, height: Math.max(60, el.clientHeight) });
    });
    ro.observe(el);
    return () => {
      ro.disconnect();
      plot.current?.destroy();
      plot.current = null;
    };
  }, [spec.id, spec.labels.length, theme]);

  useEffect(() => {
    if (frozen) return;
    const tick = () => {
      const series = spec.series();
      if (!series || !plot.current) {
        setEmpty(true);
        return;
      }
      const data = series.window(seconds);
      if (spec.scale) for (let c = 1; c < data.length; c++) data[c] = data[c].map(spec.scale);
      const hasValue = data.slice(1).some((col) => col.some((v) => Number.isFinite(v)));
      setEmpty(!hasValue);
      plot.current.setData(data as uPlot.AlignedData);
    };
    tick();
    const id = window.setInterval(tick, 125);
    return () => window.clearInterval(id);
  }, [spec, seconds, frozen]);

  return (
    <div className="plot">
      <div className="plot-head">
        <b>{spec.title}</b>
        <span className="mono">{spec.unit}</span>
        {spec.note && <span className="badge" style={{ marginInlineStart: "auto" }}>{spec.note}</span>}
      </div>
      <div className="plot-legend">
        {spec.labels.map((l, k) => (
          <span key={l}>
            <i style={{ background: PALETTE[k % PALETTE.length] }} />
            {l}
          </span>
        ))}
      </div>
      <div className="plot-body" ref={host} dir="ltr">
        {empty && <div className="empty" style={{ position: "absolute", inset: 0 }}>{spec.note ?? "—"}</div>}
      </div>
    </div>
  );
}

export function TelemetryPanel() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const theme = useStore((s) => s.theme);
  const selected = useStore((s) => s.selected);
  const angleUnit = useStore((s) => s.angleUnit);
  const effortSource = useStore((s) => s.telemetry?.effortSource ?? null);
  const contactsAvailable = useStore((s) => s.telemetry?.contacts.available ?? false);
  const [frozen, setFrozen] = useState(false);
  const [seconds, setSeconds] = useState("15");
  const arms = manifest?.arms ?? [];
  const selectedArm = selected?.startsWith("arm:") ? selected.slice(4) : null;
  const [armChoice, setArmChoice] = useState<string | null>(null);
  const armName = armChoice ?? selectedArm ?? arms[0]?.name ?? null;
  const arm = arms.find((a) => a.name === armName);

  const specs = useMemo<PlotSpec[]>(() => {
    if (!arm) return [];
    const labels = arm.joints.map((_, k) => `J${k + 1}`);
    const deg = angleUnit === "deg";
    const toDeg = (v: number) => (deg ? (v * 180) / Math.PI : v);
    return [
      { id: `${arm.name}:q`, title: t("telemetry.jointPositions"), unit: deg ? "deg" : "rad", labels, series: () => telemetry.arms.get(arm.name)?.q, scale: toDeg },
      { id: `${arm.name}:qd`, title: t("telemetry.jointVelocities"), unit: deg ? "deg/s" : "rad/s", labels, series: () => telemetry.arms.get(arm.name)?.qd, scale: toDeg },
      {
        id: `${arm.name}:effort`,
        title: t("telemetry.jointEfforts"),
        unit: "N·m",
        labels,
        series: () => telemetry.arms.get(arm.name)?.effort,
        note: effortSource ? t("telemetry.effortSource", { source: effortSource }) : t("telemetry.unavailable"),
      },
      { id: `${arm.name}:ee`, title: t("telemetry.toolCentre"), unit: "mm", labels: ["x", "y", "z"], series: () => telemetry.arms.get(arm.name)?.ee, scale: (v) => v * 1000 },
      ...(arm.gripper
        ? [{ id: `${arm.name}:grip`, title: t("telemetry.gripper"), unit: "0-1 / mm", labels: [t("telemetry.command"), t("telemetry.widthMm"), t("telemetry.closure")], series: () => telemetry.arms.get(arm.name)?.grip }]
        : []),
      {
        id: "contacts",
        title: t("telemetry.contacts"),
        unit: "n / N",
        labels: [t("telemetry.count"), t("telemetry.totalForce")],
        series: () => telemetry.contacts,
        note: contactsAvailable ? undefined : t("telemetry.unavailable"),
      },
      { id: "rate", title: t("telemetry.simRate"), unit: "steps/s · ×RT", labels: [t("telemetry.stepsPerS"), t("telemetry.realtime")], series: () => telemetry.rate },
      { id: "render", title: t("telemetry.renderRate"), unit: "fps · ms", labels: ["fps", "ms"], series: () => telemetry.render },
    ];
  }, [arm, angleUnit, effortSource, contactsAvailable, t]);

  if (!manifest) return <div className="empty">{t("inspect.noScene")}</div>;
  if (!arm) return <div className="empty">{t("telemetry.noArm")}</div>;

  return (
    <div style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
      <div className="row" style={{ padding: "6px 10px", borderBottom: "1px solid var(--line)", gap: 10 }}>
        <span className="badge info">{t("twin.simulated")}</span>
        {arms.length > 1 && (
          <Segmented label={t("telemetry.arm")} value={arm.name} onChange={(v) => setArmChoice(v)} options={arms.map((a) => ({ value: a.name, label: a.name }))} />
        )}
        <Segmented
          label={t("telemetry.window")}
          value={seconds}
          onChange={setSeconds}
          options={[
            { value: "5", label: "5 s" },
            { value: "15", label: "15 s" },
            { value: "30", label: "30 s" },
          ]}
        />
        <span className="grow" />
        <span className="muted" style={{ fontSize: 11 }}>{t("telemetry.hint")}</span>
        <button type="button" className={`btn sm${frozen ? " active" : ""}`} onClick={() => setFrozen(!frozen)}>
          {frozen ? <Play size={12} /> : <Pause size={12} />} {frozen ? t("telemetry.resume") : t("telemetry.freeze")}
        </button>
      </div>
      <div className="plots" style={{ flex: 1 }}>
        {specs.map((spec) => (
          <Plot key={spec.id + theme} spec={spec} seconds={Number(seconds)} frozen={frozen} theme={theme} />
        ))}
      </div>
    </div>
  );
}
