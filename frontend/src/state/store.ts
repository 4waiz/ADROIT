// SPDX-License-Identifier: LicenseRef-BRIDGE
// The page's reactive state. Frame-rate data stays in `live` and `telemetry`; this holds what
// panels render: connection, status, the manifest, selection, logs, preferences, dialogs.

import { create } from "zustand";
import { type Presence, servedLocally } from "../api/presence";
import type {
  Ack,
  CommandRecord,
  EventRecord,
  FrameHeader,
  HostStatus,
  Manifest,
  ServiceInfo,
  WorkcellCatalog,
  WorkcellDoc,
} from "../api/types";

export type Theme = "dark" | "light";
export type Lang = "en" | "ar";
export type Quality = "performance" | "balanced" | "ultra";
/** Where the cell is drawn: a lit industrial hall, or the neutral studio stage. */
export type EnvironmentName = "factory" | "studio";
/** Simple shows what an operator needs in plain words; Expert adds every engineering panel. */
export type UiMode = "simple" | "expert";
export type Connection = "connecting" | "open" | "reconnecting" | "closed";
export type LeftTab = "scene" | "library" | "scenarios";
export type RightTab = "inspector" | "physics" | "twin";
export type DockTab = "telemetry" | "timeline" | "events" | "commands" | "tasks" | "reference" | "diagnostics";
export type DialogName = "gallery" | "about" | "settings" | "help" | "save" | "load" | null;

export interface Overlays {
  collision: boolean;
  frames: boolean;
  contacts: boolean;
  forces: boolean;
  com: boolean;
  target: boolean;
  regions: boolean;
  velocity: boolean;
  workspace: boolean;
  grid: boolean;
}

export interface Toast {
  id: number;
  level: "info" | "ok" | "warn" | "err";
  title: string;
  body?: string;
}

interface Prefs {
  theme: Theme;
  lang: Lang;
  quality: Quality;
  leftWidth: number;
  rightWidth: number;
  dockHeight: number;
  leftCollapsed: boolean;
  rightCollapsed: boolean;
  dockCollapsed: boolean;
  leftTab: LeftTab;
  rightTab: RightTab;
  dockTab: DockTab;
  overlays: Overlays;
  projection: "perspective" | "orthographic";
  gizmoMode: "translate" | "rotate";
  gizmoSpace: "world" | "local";
  jogStep: number; // m
  jogAngle: number; // rad
  jointStep: number; // rad
  angleUnit: "deg" | "rad";
  environment: EnvironmentName;
  uiMode: UiMode;
  tutorialSeen: boolean;
}

const DEFAULT_PREFS: Prefs = {
  theme: "dark",
  lang: "en",
  quality: "balanced",
  leftWidth: 272,
  rightWidth: 336,
  dockHeight: 236,
  leftCollapsed: false,
  rightCollapsed: false,
  dockCollapsed: false,
  leftTab: "scene",
  rightTab: "inspector",
  dockTab: "telemetry",
  overlays: {
    collision: false,
    frames: false,
    contacts: false,
    forces: false,
    com: false,
    target: true,
    regions: true,
    velocity: false,
    workspace: true,
    grid: true,
  },
  projection: "perspective",
  gizmoMode: "translate",
  gizmoSpace: "world",
  jogStep: 0.01,
  jogAngle: 0.0872665,
  jointStep: 0.0872665,
  angleUnit: "deg",
  environment: "factory",
  uiMode: "simple",
  tutorialSeen: false,
};

function loadPrefs(): Prefs {
  const prefs: Prefs = structuredClone(DEFAULT_PREFS);
  try {
    const raw = localStorage.getItem("bridge.prefs");
    if (raw) {
      const saved = JSON.parse(raw) as Partial<Prefs>;
      Object.assign(prefs, saved, { overlays: { ...DEFAULT_PREFS.overlays, ...(saved.overlays ?? {}) } });
    }
    const theme = localStorage.getItem("bridge.theme");
    if (theme === "light" || theme === "dark") prefs.theme = theme;
    const lang = localStorage.getItem("bridge.lang");
    if (lang === "en" || lang === "ar") prefs.lang = lang;
  } catch {
    /* private mode or blocked storage: the defaults stand */
  }
  return prefs;
}

function savePrefs(prefs: Prefs): void {
  try {
    localStorage.setItem("bridge.prefs", JSON.stringify(prefs));
    localStorage.setItem("bridge.theme", prefs.theme);
    localStorage.setItem("bridge.lang", prefs.lang);
  } catch {
    /* ignore */
  }
}

export interface PendingCommand {
  id: string;
  name: string;
  args: Record<string, unknown>;
  sentAt: number;
}

interface State extends Prefs {
  /** Whether a simulation answers this page; always "live" where the service serves it locally. */
  presence: Presence;
  connection: Connection;
  latencyMs: number | null;
  clientId: string;
  info: ServiceInfo | null;
  status: HostStatus | null;
  manifest: Manifest | null;
  sceneLoading: string | null;
  sceneError: string | null;
  telemetry: FrameHeader | null;
  selected: string | null;
  focusJoint: number | null;
  focusRequest: number;
  events: EventRecord[];
  commands: CommandRecord[];
  acks: Record<string, Ack>;
  pending: Record<string, PendingCommand>;
  toasts: Toast[];
  dialog: DialogName;
  draft: WorkcellDoc | null;
  catalog: WorkcellCatalog | null;
  past: WorkcellDoc[];
  future: WorkcellDoc[];
  draftDirty: boolean;
  tutorialOpen: boolean;
  renderStats: { fps: number; ms: number; calls: number; triangles: number; quality: Quality; shadows: boolean; ao: boolean };

  setPref<K extends keyof Prefs>(key: K, value: Prefs[K]): void;
  setOverlay(key: keyof Overlays, value: boolean): void;
  select(key: string | null, joint?: number | null): void;
  requestFocus(): void;
  openDialog(name: DialogName): void;
  toast(level: Toast["level"], title: string, body?: string): void;
  dismissToast(id: number): void;
  setDraft(doc: WorkcellDoc | null, opts?: { record?: boolean; dirty?: boolean }): void;
  undo(): void;
  redo(): void;
}

let toastSeq = 0;

export const useStore = create<State>((set, get) => ({
  ...loadPrefs(),
  presence: servedLocally() ? "live" : "checking",
  connection: "connecting",
  latencyMs: null,
  clientId: "",
  info: null,
  status: null,
  manifest: null,
  sceneLoading: null,
  sceneError: null,
  telemetry: null,
  selected: null,
  focusJoint: null,
  focusRequest: 0,
  events: [],
  commands: [],
  acks: {},
  pending: {},
  toasts: [],
  dialog: null,
  draft: null,
  catalog: null,
  past: [],
  future: [],
  draftDirty: false,
  tutorialOpen: false,
  renderStats: { fps: 0, ms: 0, calls: 0, triangles: 0, quality: "balanced", shadows: false, ao: false },

  setPref(key, value) {
    set({ [key]: value } as Partial<State>);
    const s = get();
    const prefs = Object.fromEntries(Object.keys(DEFAULT_PREFS).map((k) => [k, s[k as keyof Prefs]])) as unknown as Prefs;
    savePrefs(prefs);
  },
  setOverlay(key, value) {
    get().setPref("overlays", { ...get().overlays, [key]: value });
  },
  select(key, joint = null) {
    set({ selected: key, focusJoint: joint });
  },
  requestFocus() {
    set({ focusRequest: get().focusRequest + 1 });
  },
  openDialog(name) {
    set({ dialog: name });
  },
  toast(level, title, body) {
    const id = ++toastSeq;
    set({ toasts: [...get().toasts.slice(-4), { id, level, title, body }] });
    window.setTimeout(() => get().dismissToast(id), level === "err" ? 9000 : 5000);
  },
  dismissToast(id) {
    set({ toasts: get().toasts.filter((t) => t.id !== id) });
  },
  setDraft(doc, opts) {
    const record = opts?.record ?? true;
    const prev = get().draft;
    if (record && prev && doc) {
      set({ past: [...get().past.slice(-79), prev], future: [] });
    }
    set({ draft: doc, draftDirty: opts?.dirty ?? (record ? true : get().draftDirty) });
  },
  undo() {
    const { past, draft, future } = get();
    if (!past.length || !draft) return;
    set({ draft: past[past.length - 1], past: past.slice(0, -1), future: [draft, ...future], draftDirty: true });
  },
  redo() {
    const { past, draft, future } = get();
    if (!future.length || !draft) return;
    set({ draft: future[0], future: future.slice(1), past: [...past, draft], draftDirty: true });
  },
}));

export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute("data-theme", theme);
}

export function applyLang(lang: Lang): void {
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
}
