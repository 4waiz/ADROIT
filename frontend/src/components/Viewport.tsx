// SPDX-License-Identifier: LicenseRef-BRIDGE
// The centre of the page: the 3D workcell, its camera and overlay tools, the transport, and the
// readouts that say how fast the browser draws and how fast the simulator steps (both measured).

import {
  Aperture,
  Box,
  Camera,
  CheckCircle2,
  Crosshair,
  Expand,
  Eye,
  Factory,
  Gauge,
  House,
  Layers,
  Move3d,
  Pause,
  Play,
  RotateCcw,
  Rotate3d,
  ScanEye,
  SkipForward,
  Sparkles,
  Target,
  Video,
  X,
} from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { bridge } from "../api/socket";
import { fmt, useT } from "../i18n";
import { live } from "../state/live";
import { type Overlays, type Quality, useStore } from "../state/store";
import { telemetry } from "../state/telemetry";
import { type FollowMode, type PresetName, ViewportEngine } from "../viewport/engine";
import { Logo } from "./Logo";
import { IconButton, MenuItem, Popover } from "./ui/primitives";

let engineRef: ViewportEngine | null = null;
export function getEngine(): ViewportEngine | null {
  return engineRef;
}

// The keyboard-flight hint shows until the camera first flies, or is dismissed: once per browser.
const FLY_HINT = "bridge.flyHint";
function flyHintWanted(): boolean {
  try {
    return localStorage.getItem(FLY_HINT) !== "done";
  } catch {
    return true;
  }
}
function flyHintDone(): void {
  try {
    localStorage.setItem(FLY_HINT, "done");
  } catch {
    // A private window may refuse storage; the hint then comes back next time.
  }
}

function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 2000);
}

export function Viewport() {
  const t = useT();
  const host = useRef<HTMLDivElement>(null);
  const wrap = useRef<HTMLDivElement>(null);
  const manifest = useStore((s) => s.manifest);
  const theme = useStore((s) => s.theme);
  const quality = useStore((s) => s.quality);
  const overlays = useStore((s) => s.overlays);
  const selected = useStore((s) => s.selected);
  const projection = useStore((s) => s.projection);
  const gizmoMode = useStore((s) => s.gizmoMode);
  const gizmoSpace = useStore((s) => s.gizmoSpace);
  const focusRequest = useStore((s) => s.focusRequest);
  const draft = useStore((s) => s.draft);
  const catalog = useStore((s) => s.catalog);
  const leftTab = useStore((s) => s.leftTab);
  const leftCollapsed = useStore((s) => s.leftCollapsed);
  const status = useStore((s) => s.status);
  const sceneLoading = useStore((s) => s.sceneLoading);
  const sceneError = useStore((s) => s.sceneError);
  const connection = useStore((s) => s.connection);
  const stats = useStore((s) => s.renderStats);
  const tele = useStore((s) => s.telemetry);
  const setPref = useStore((s) => s.setPref);
  const setOverlay = useStore((s) => s.setOverlay);
  const environment = useStore((s) => s.environment);
  const simple = useStore((s) => s.uiMode === "simple");
  const [envLoading, setEnvLoading] = useState<number | null>(null);
  const [menu, setMenu] = useState<"camera" | "overlays" | "quality" | "follow" | "speed" | null>(null);
  const [follow, setFollow] = useState<FollowMode>("none");
  const [snapshotBusy, setSnapshotBusy] = useState(false);
  const [flyHint, setFlyHint] = useState(flyHintWanted);
  const dismissFlyHint = useCallback(() => {
    flyHintDone();
    setFlyHint(false);
  }, []);

  useEffect(() => {
    if (!host.current) return;
    const engine = new ViewportEngine(host.current, {
      // The keys have the camera now: a follow mode lets go, and the hint has done its job.
      onFly: () => {
        setFollow("none");
        dismissFlyHint();
      },
      onSelect: (key) => useStore.getState().select(key),
      onCursor: (arm, position, quat, phase) => bridge.cursor(arm, position, quat, phase),
      onDraftMove: (id, at, rpy) => {
        const s = useStore.getState();
        if (!s.draft) return;
        s.setDraft({ ...s.draft, objects: s.draft.objects.map((o) => (o.id === id ? { ...o, at, rpy } : o)) });
      },
      onStats: (s) => {
        useStore.setState({ renderStats: s });
        telemetry.ingestRender(performance.now() / 1000, s.fps, s.ms);
      },
    });
    engineRef = engine;
    const off = live.onFrame((frame) => engine.setFrame(frame));
    return () => {
      off();
      engine.dispose();
      engineRef = null;
    };
  }, []);

  useEffect(() => {
    if (manifest && live.geometry && engineRef) {
      engineRef.setManifest(manifest, live.geometry);
      engineRef.setSelection(useStore.getState().selected);
    }
  }, [manifest]);
  useEffect(() => engineRef?.setTheme(theme), [theme]);
  useEffect(() => engineRef?.setEnvironment(environment, (fraction, done) => setEnvLoading(done ? null : fraction)), [environment]);
  useEffect(() => engineRef?.setQuality(quality), [quality]);
  useEffect(() => engineRef?.setOverlays(overlays), [overlays]);
  useEffect(() => engineRef?.setSelection(selected), [selected, manifest]);
  useEffect(() => engineRef?.setProjection(projection), [projection]);
  // Simple mode moves the hand with arrows in the world's axes: no rotate ring, no local frame.
  useEffect(() => engineRef?.setGizmoMode(simple ? "translate" : gizmoMode, simple ? "world" : gizmoSpace), [gizmoMode, gizmoSpace, simple]);
  useEffect(() => {
    if (focusRequest) engineRef?.focusSelected();
  }, [focusRequest]);
  useEffect(() => {
    const origin = draft && catalog ? (catalog.cellFrames[draft.cell]?.origin ?? null) : null;
    engineRef?.setDraft(draft, origin, leftTab === "library" && !leftCollapsed, selected);
  }, [draft, catalog, leftTab, leftCollapsed, selected]);
  useEffect(() => {
    const arm = selected?.startsWith("arm:") ? selected.slice(4) : null;
    engineRef?.setFollow(follow, arm);
  }, [follow, selected]);

  const close = useCallback(() => setMenu(null), []);
  const playing = status?.playing ?? false;
  const ready = status?.phase === "ready" && connection === "open";
  const speed = status?.speed ?? 1;
  const runnerName = status?.runner?.name ?? null;
  const finite = tele?.finite ?? true;

  // The factory's stack light and operator panel say what the simulation is doing.
  useEffect(() => {
    const title = t("factory.cell");
    if (connection !== "open" || !status || status.phase === "starting" || status.phase === "idle") {
      engineRef?.setMachineState("offline", { title, status: t("factory.offline"), tone: "idle", detail: t("factory.offlineDetail") });
    } else if (status.phase === "error" || !finite) {
      engineRef?.setMachineState("fault", { title, status: t("factory.fault"), tone: "err", detail: status.buildError || t("factory.faultDetail") });
    } else if (status.phase === "building") {
      engineRef?.setMachineState("paused", { title, status: t("factory.loading"), tone: "warn", detail: status.phaseDetail });
    } else if (playing && runnerName) {
      engineRef?.setMachineState("program", { title, status: t("factory.program"), tone: "busy", detail: runnerName });
    } else if (playing) {
      engineRef?.setMachineState("running", { title, status: t("factory.running"), tone: "ok", detail: manifest?.scenario.title ?? "" });
    } else {
      engineRef?.setMachineState("paused", { title, status: t("factory.paused"), tone: "warn", detail: manifest?.scenario.title ?? "" });
    }
  }, [connection, status?.phase, status?.buildError, status?.phaseDetail, playing, runnerName, finite, manifest?.scenario.title, t, status]);

  const screenshot = async () => {
    const blob = await engineRef?.screenshot();
    if (blob) download(blob, `bridge-${manifest?.scenario.id ?? "scene"}-${Date.now()}.png`);
  };

  const snapshot = async () => {
    const view = engineRef?.cameraState();
    if (!view) return;
    setSnapshotBusy(true);
    try {
      const response = await fetch("/api/render", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ eye: view.eye, lookAt: view.lookAt, fovY: view.fovY, width: 1600, height: 900 }),
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.detail ?? response.statusText);
      }
      const renderer = response.headers.get("X-Bridge-Renderer") ?? "?";
      const seconds = response.headers.get("X-Bridge-Seconds") ?? "?";
      download(await response.blob(), `bridge-snapshot-${renderer}-${Date.now()}.png`);
      useStore.getState().toast("ok", t("viewport.snapshotDone"), t("viewport.snapshotDetail", { renderer, seconds }));
    } catch (error) {
      useStore.getState().toast("err", t("viewport.snapshotFailed"), error instanceof Error ? error.message : String(error));
    } finally {
      setSnapshotBusy(false);
    }
  };

  const preset = (name: PresetName) => {
    engineRef?.applyPreset(name);
    close();
  };

  const overlayItems: { key: keyof Overlays; label: string }[] = [
    { key: "target", label: t("overlay.target") },
    { key: "frames", label: t("overlay.frames") },
    { key: "collision", label: t("overlay.collision") },
    { key: "contacts", label: t("overlay.contacts") },
    { key: "forces", label: t("overlay.forces") },
    { key: "com", label: t("overlay.com") },
    { key: "regions", label: t("overlay.regions") },
    { key: "workspace", label: t("overlay.workspace") },
    { key: "grid", label: environment === "factory" ? t("overlay.gridStudio") : t("overlay.grid") },
  ];
  const qualities: { key: Quality; label: string; hint: string }[] = [
    { key: "performance", label: t("quality.performance"), hint: t("quality.performanceHint") },
    { key: "balanced", label: t("quality.balanced"), hint: t("quality.balancedHint") },
    { key: "ultra", label: t("quality.ultra"), hint: t("quality.ultraHint") },
  ];
  const armSelected = selected?.startsWith("arm:") ?? false;
  const success = status?.success;
  const goalMet = status?.goalMet ?? null;
  const runner = status?.runner;

  return (
    <div className="viewport" ref={wrap}>
      <div ref={host} style={{ position: "absolute", inset: 0 }} />

      <div className="hud hud-tools" role="toolbar" aria-label={t("viewport.tools")} data-tour="view-tools">
        <Popover
          open={menu === "camera"}
          onClose={close}
          anchor={
            <IconButton label={t("viewport.cameraPresets")} onClick={() => setMenu(menu === "camera" ? null : "camera")} active={menu === "camera"}>
              <Video size={16} />
            </IconButton>
          }
        >
          <div className="menu-label">{t("viewport.cameraPresets")}</div>
          <MenuItem onClick={() => preset("iso")} kbd="1">{t("camera.iso")}</MenuItem>
          <MenuItem onClick={() => preset("operator")} kbd="2">{t("camera.operator")}</MenuItem>
          <MenuItem onClick={() => preset("front")} kbd="3">{t("camera.front")}</MenuItem>
          <MenuItem onClick={() => preset("side")} kbd="4">{t("camera.side")}</MenuItem>
          <MenuItem onClick={() => preset("top")} kbd="5">{t("camera.top")}</MenuItem>
          <MenuItem onClick={() => preset("tool")} kbd="6">{t("camera.tool")}</MenuItem>
          <div className="menu-sep" />
          <MenuItem onClick={() => setPref("projection", projection === "perspective" ? "orthographic" : "perspective")} kbd="O">
            {projection === "perspective" ? t("camera.toOrtho") : t("camera.toPerspective")}
          </MenuItem>
        </Popover>
        {!simple && (
        <Popover
          open={menu === "follow"}
          onClose={close}
          anchor={
            <IconButton label={t("viewport.follow")} onClick={() => setMenu(menu === "follow" ? null : "follow")} active={follow !== "none"}>
              <ScanEye size={16} />
            </IconButton>
          }
        >
          <div className="menu-label">{t("viewport.follow")}</div>
          {(["none", "robot", "tool"] as FollowMode[]).map((m) => (
            <MenuItem key={m} checked={follow === m} onClick={() => { setFollow(m); close(); }}>
              {t(`follow.${m}` as "follow.none")}
            </MenuItem>
          ))}
        </Popover>
        )}
        {!simple && (
          <IconButton label={t("viewport.focus")} onClick={() => engineRef?.focusSelected()} disabled={!selected} shortcut="F">
            <Crosshair size={16} />
          </IconButton>
        )}
        <IconButton label={t("viewport.reset")} onClick={() => engineRef?.applyPreset("iso")} shortcut="Home">
          <House size={16} />
        </IconButton>
        <span className="sep" />
        {!simple && (
        <Popover
          open={menu === "overlays"}
          onClose={close}
          width={250}
          anchor={
            <IconButton label={t("viewport.overlays")} onClick={() => setMenu(menu === "overlays" ? null : "overlays")} active={menu === "overlays"}>
              <Layers size={16} />
            </IconButton>
          }
        >
          <div className="menu-label">{t("viewport.overlays")}</div>
          {overlayItems.map((o) => (
            <MenuItem key={o.key} checked={overlays[o.key]} onClick={() => setOverlay(o.key, !overlays[o.key])}>
              <Eye size={14} style={{ opacity: overlays[o.key] ? 1 : 0.3 }} />
              {o.label}
            </MenuItem>
          ))}
        </Popover>
        )}
        {!simple && (
        <Popover
          open={menu === "quality"}
          onClose={close}
          width={280}
          anchor={
            <IconButton label={t("viewport.quality")} onClick={() => setMenu(menu === "quality" ? null : "quality")} active={menu === "quality"}>
              <Sparkles size={16} />
            </IconButton>
          }
        >
          <div className="menu-label">{t("viewport.quality")}</div>
          {qualities.map((q) => (
            <MenuItem key={q.key} checked={quality === q.key} onClick={() => { setPref("quality", q.key); close(); }}>
              <span style={{ display: "grid" }}>
                <span>{q.label}</span>
                <small className="muted">{q.hint}</small>
              </span>
            </MenuItem>
          ))}
        </Popover>
        )}
        <IconButton
          label={environment === "factory" ? t("env.toStudio") : t("env.toFactory")}
          active={environment === "factory"}
          onClick={() => setPref("environment", environment === "factory" ? "studio" : "factory")}
          shortcut="B"
        >
          <Factory size={16} />
        </IconButton>
        <IconButton label={t("viewport.screenshot")} onClick={screenshot} shortcut="P">
          <Camera size={16} />
        </IconButton>
        {!simple && (
          <IconButton label={t("viewport.snapshot")} onClick={snapshot} disabled={!ready || snapshotBusy}>
            <Aperture size={16} />
          </IconButton>
        )}
        <IconButton
          label={t("viewport.fullscreen")}
          onClick={() => {
            if (document.fullscreenElement) void document.exitFullscreen();
            else void wrap.current?.requestFullscreen();
          }}
        >
          <Expand size={16} />
        </IconButton>
      </div>

      {flyHint && manifest && connection === "open" && (
        <div className="hud hud-keys" role="note" aria-label={t("fly.label")}>
          <span>
            <kbd>W</kbd>
            <kbd>A</kbd>
            <kbd>S</kbd>
            <kbd>D</kbd> {t("fly.move")}
          </span>
          <span>
            <kbd>Q</kbd>
            <kbd>E</kbd> {t("fly.upDown")}
          </span>
          <span>
            <kbd>Shift</kbd> {t("fly.faster")}
          </span>
          <span>
            <House size={13} aria-hidden /> <kbd>Home</kbd> {t("fly.reset")}
          </span>
          <button type="button" className="close" onClick={dismissFlyHint} aria-label={t("fly.dismiss")} title={t("fly.dismiss")}>
            <X size={13} />
          </button>
        </div>
      )}

      {armSelected && !simple && (
        <div className="hud hud-gizmo" role="toolbar" aria-label={t("viewport.gizmo")}>
          <IconButton label={t("gizmo.translate")} active={gizmoMode === "translate"} onClick={() => setPref("gizmoMode", "translate")} shortcut="T">
            <Move3d size={16} />
          </IconButton>
          <IconButton label={t("gizmo.rotate")} active={gizmoMode === "rotate"} onClick={() => setPref("gizmoMode", "rotate")} shortcut="R">
            <Rotate3d size={16} />
          </IconButton>
          <IconButton
            label={gizmoSpace === "world" ? t("gizmo.world") : t("gizmo.local")}
            active={gizmoSpace === "local"}
            onClick={() => setPref("gizmoSpace", gizmoSpace === "world" ? "local" : "world")}
            shortcut="L"
          >
            <Box size={16} />
          </IconButton>
        </div>
      )}

      {(success || runner) && (
        <div className="hud hud-goal" role="status" aria-live="polite" data-tour="goal">
          {simple && !runner && manifest?.scenario.successLabel ? (
            <>
              {success?.done || goalMet ? (
                <span className="badge ok"><CheckCircle2 size={11} /> {t("simple.goalReached")}</span>
              ) : (
                <span className="badge"><Target size={11} /> {t("goal.goal")}</span>
              )}
              <span className="detail" dir="auto">{manifest.scenario.successLabel}</span>
            </>
          ) : runner ? (
            <>
              <span className="badge accent">{runner.kind === "task" ? t("goal.task") : t("goal.routine")}</span>
              <span className="detail">
                <b>{runner.name}</b> · {runner.phase || t("goal.running")} · <span className="mono">{runner.seconds.toFixed(1)} s</span>
              </span>
            </>
          ) : success ? (
            <>
              {success.done ? (
                <span className="badge ok"><CheckCircle2 size={11} /> {t("goal.met")}</span>
              ) : goalMet ? (
                <span className="badge ok" title={goalMet.detail}>
                  <CheckCircle2 size={11} /> {t("goal.metAt", { t: goalMet.simTime.toFixed(1) })}
                </span>
              ) : (
                <span className="badge"><Target size={11} /> {t("goal.goal")}</span>
              )}
              <span className="detail" title={success.detail}>{success.detail}</span>
            </>
          ) : null}
        </div>
      )}

      <div className="hud transport" role="toolbar" aria-label={t("transport.label")} data-tour="transport">
        <IconButton
          label={playing ? t("transport.pause") : t("transport.play")}
          onClick={() => void bridge.command(playing ? "sim.pause" : "sim.play")}
          disabled={!ready}
          shortcut="Space"
        >
          {playing ? <Pause size={18} /> : <Play size={18} />}
        </IconButton>
        {!simple && (
          <IconButton label={t("transport.step")} onClick={() => void bridge.command("sim.step", { count: 1 })} disabled={!ready} shortcut=".">
            <SkipForward size={16} />
          </IconButton>
        )}
        <IconButton label={t("transport.reset")} onClick={() => void bridge.command("sim.reset")} disabled={!ready} shortcut="Shift+R">
          <RotateCcw size={16} />
        </IconButton>
        <span className="sep" />
        <Popover
          open={menu === "speed"}
          onClose={close}
          anchor={
            <button type="button" className="btn ghost sm" onClick={() => setMenu(menu === "speed" ? null : "speed")} disabled={!ready} title={t("transport.speed")}>
              <Gauge size={14} />
              <span className="mono">{speed === 0 ? t("transport.max") : `${speed}×`}</span>
            </button>
          }
        >
          <div className="menu-label">{t("transport.speed")}</div>
          {(status?.speeds ?? [0.1, 0.25, 0.5, 1, 2, 4, 0]).map((v) => (
            <MenuItem key={v} checked={speed === v} onClick={() => { void bridge.command("sim.speed", { value: v }); close(); }}>
              {v === 0 ? t("transport.maxLong") : `${v}×`}
            </MenuItem>
          ))}
        </Popover>
        <span className="sep" />
        <div className="time" aria-label={t("transport.time")}>
          <b>{fmt(tele?.t ?? status?.simTime ?? 0, simple ? 1 : 3)} s</b>
          {simple ? (
            <span>{t("simple.simTime")}</span>
          ) : (
            <span>
              {t("transport.stepN", { n: tele?.step ?? status?.step ?? 0 })} · {fmt(tele?.rate.realtime ?? status?.realtimeFactor ?? 0, 2)}× RT
            </span>
          )}
        </div>
      </div>

      {!simple && (
      <div className="hud hud-perf" aria-label={t("perf.label")}>
        <div>
          {t("perf.render")} <b>{stats.fps.toFixed(0)}</b> fps · {stats.ms.toFixed(1)} ms · {t(`quality.${stats.quality}` as "quality.balanced")}
        </div>
        <div>
          {t("perf.sim")} <b>{fmt(tele?.rate.steps ?? 0, 0)}</b> {t("perf.stepsPerS")} · {t("perf.physics")} {fmt(tele?.rate.stepMs, 2)} ms · {t("perf.control")} {fmt(tele?.rate.controlMs, 2)} ms
        </div>
        {tele && !tele.finite && <div style={{ color: "var(--err)" }}>{t("perf.nonFinite")}</div>}
      </div>
      )}

      {envLoading !== null && (
        <div className="hud hud-env" role="status" aria-live="polite">
          <Factory size={13} /> {t("env.loading", { pct: Math.round(envLoading * 100) })}
        </div>
      )}

      {(status?.phase === "building" || sceneLoading || !manifest || connection !== "open") && (
        <div className="viewport-overlay">
          <div className="loader" role="status" aria-live="polite">
            <Logo height={19} />
            {connection !== "open" ? (
              <>
                <div className="bar" />
                <p>{connection === "connecting" ? t("loader.connecting") : t("loader.reconnecting")}</p>
              </>
            ) : status?.phase === "building" ? (
              <>
                <div className="bar" />
                <p><b>{status.phaseDetail}</b></p>
                <p>{t("loader.buildHint")}</p>
              </>
            ) : status?.phase === "error" ? (
              <p style={{ color: "var(--err)" }}>{status.buildError}</p>
            ) : sceneError ? (
              <p style={{ color: "var(--err)" }}>{sceneError}</p>
            ) : (
              <>
                <div className="bar" />
                <p>{sceneLoading ?? t("loader.waiting")}</p>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
