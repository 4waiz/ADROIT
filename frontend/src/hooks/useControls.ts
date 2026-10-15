// SPDX-License-Identifier: Apache-2.0
// Keyboard and gamepad. Keys never act while you are typing in a field. The gamepad moves the
// selected arm's target through the same cursor channel the gizmo uses, so the host's IK and
// rate limits apply exactly as they do to a mouse drag.

import { useEffect } from "react";
import { bridge } from "../api/socket";
import { getEngine } from "../components/Viewport";
import { live } from "../state/live";
import { useStore } from "../state/store";
import type { PresetName } from "../viewport/engine";

const PRESETS: PresetName[] = ["iso", "operator", "front", "side", "top", "tool"];

function typing(target: EventTarget | null): boolean {
  const el = target as HTMLElement | null;
  if (!el) return false;
  const tag = el.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
}

function selectedArm(): string | null {
  const s = useStore.getState().selected;
  return s?.startsWith("arm:") ? s.slice(4) : null;
}

export function useControls(): void {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (typing(e.target)) return;
      const store = useStore.getState();
      const mod = e.ctrlKey || e.metaKey;
      if (mod && e.key.toLowerCase() === "s") {
        e.preventDefault();
        store.openDialog("save");
        return;
      }
      if (mod && e.key.toLowerCase() === "o") {
        e.preventDefault();
        store.openDialog("load");
        return;
      }
      if (mod && e.key.toLowerCase() === "z") {
        e.preventDefault();
        store.undo();
        return;
      }
      if (mod && e.key.toLowerCase() === "y") {
        e.preventDefault();
        store.redo();
        return;
      }
      if (mod || store.dialog) return;
      const arm = selectedArm();
      const generation = store.manifest?.generation;
      const jog = (axis: string, sign: number) => {
        if (!arm) return;
        e.preventDefault();
        const rotational = axis.startsWith("r");
        void bridge.command("arm.jog", {
          arm,
          space: "cartesian",
          axis,
          frame: "world",
          delta: sign * (rotational ? store.jogAngle : store.jogStep),
          generation,
        });
      };
      switch (e.key) {
        case " ":
          e.preventDefault();
          void bridge.command(store.status?.playing ? "sim.pause" : "sim.play");
          return;
        case ".":
          void bridge.command("sim.step", { count: 1 });
          return;
        case "R":
          if (e.shiftKey) void bridge.command("sim.reset");
          return;
        case "o":
        case "O":
          store.setPref("projection", store.projection === "perspective" ? "orthographic" : "perspective");
          return;
        case "f":
        case "F":
          getEngine()?.focusSelected();
          return;
        case "Home":
          getEngine()?.applyPreset("iso");
          return;
        case "w":
        case "W":
          store.setPref("gizmoMode", "translate");
          return;
        case "e":
        case "E":
          store.setPref("gizmoMode", "rotate");
          return;
        case "q":
        case "Q":
          store.setPref("gizmoSpace", store.gizmoSpace === "world" ? "local" : "world");
          return;
        case "ArrowUp":
          jog(e.altKey ? "ry" : "x", 1);
          return;
        case "ArrowDown":
          jog(e.altKey ? "ry" : "x", -1);
          return;
        case "ArrowLeft":
          jog(e.altKey ? "rz" : "y", 1);
          return;
        case "ArrowRight":
          jog(e.altKey ? "rz" : "y", -1);
          return;
        case "PageUp":
          jog(e.altKey ? "rx" : "z", 1);
          return;
        case "PageDown":
          jog(e.altKey ? "rx" : "z", -1);
          return;
        case "[":
          if (arm) void bridge.command("arm.grip", { arm, value: 0, generation });
          return;
        case "]":
          if (arm) void bridge.command("arm.grip", { arm, value: 1, generation });
          return;
        case "h":
        case "H":
          if (arm) void bridge.command("arm.home", { arm, generation });
          return;
        case "Escape":
          store.select(null);
          return;
        case "g":
        case "G":
          store.openDialog("gallery");
          return;
        case "p":
        case "P":
          void getEngine()
            ?.screenshot()
            .then((blob) => {
              if (!blob) return;
              const a = document.createElement("a");
              a.href = URL.createObjectURL(blob);
              a.download = `bridge-${Date.now()}.png`;
              a.click();
            });
          return;
        case "?":
          store.openDialog("help");
          return;
      }
      if (/^[1-6]$/.test(e.key)) getEngine()?.applyPreset(PRESETS[Number(e.key) - 1]);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // Gamepad: left stick x/y, right stick z, triggers grip. Polled only while a pad is connected.
  useEffect(() => {
    let raf = 0;
    let active = false;
    let cursor: number[] | null = null;
    let quat: number[] | null = null;
    let last = performance.now();
    let lastSent = 0;
    let gripState: number | null = null;
    const dead = (v: number) => (Math.abs(v) < 0.18 ? 0 : v);
    const poll = () => {
      raf = requestAnimationFrame(poll);
      const pads = navigator.getGamepads?.() ?? [];
      const pad = Array.from(pads).find((p) => p && p.connected);
      const now = performance.now();
      const dt = Math.min(0.05, (now - last) / 1000);
      last = now;
      const arm = selectedArm();
      if (!pad || !arm) return;
      const tele = live.frame?.header.arms[arm];
      if (!tele || tele.error) return;
      const vx = -dead(pad.axes[1] ?? 0);
      const vy = -dead(pad.axes[0] ?? 0);
      const vz = -dead(pad.axes[3] ?? 0);
      const moving = vx !== 0 || vy !== 0 || vz !== 0;
      const speed = 0.12; // m/s at full deflection
      if (moving) {
        if (!active) {
          cursor = tele.cursor.slice(0, 3);
          quat = tele.cursor.slice(3);
          active = true;
          bridge.cursor(arm, cursor, quat, "start");
        }
        cursor![0] += vx * speed * dt;
        cursor![1] += vy * speed * dt;
        cursor![2] += vz * speed * dt;
        if (now - lastSent > 33) {
          lastSent = now;
          bridge.cursor(arm, cursor!, quat!, "update");
        }
      } else if (active) {
        active = false;
        bridge.cursor(arm, cursor!, quat!, "end");
      }
      const close = (pad.buttons[7]?.value ?? 0) > 0.5;
      const open = (pad.buttons[6]?.value ?? 0) > 0.5;
      const want = close ? 1 : open ? 0 : null;
      if (want !== null && want !== gripState) {
        gripState = want;
        void bridge.command("arm.grip", { arm, value: want, generation: useStore.getState().manifest?.generation });
      }
      if (want === null) gripState = null;
    };
    const start = () => {
      if (!raf) raf = requestAnimationFrame(poll);
    };
    window.addEventListener("gamepadconnected", start);
    if (Array.from(navigator.getGamepads?.() ?? []).some(Boolean)) start();
    return () => {
      window.removeEventListener("gamepadconnected", start);
      cancelAnimationFrame(raf);
    };
  }, []);
}
