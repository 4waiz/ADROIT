// SPDX-License-Identifier: Apache-2.0
// The command bar: brand, where you are, what is true right now, and the global actions.
// Every chip reads real state: the sim chip the host's phase, the backend chip the compiled
// islands, the link chip the socket and its measured round trip.

import {
  ChevronRight,
  FolderOpen,
  HelpCircle,
  Info,
  Languages,
  LayoutGrid,
  Maximize,
  Minimize,
  Moon,
  Save,
  Settings,
  Sun,
} from "lucide-react";
import { useEffect, useState } from "react";
import { useT } from "../i18n";
import { applyLang, applyTheme, useStore } from "../state/store";
import { Logo } from "./Logo";
import { IconButton } from "./ui/primitives";

export function backendLabel(islands: { solver: string; hostEngine: boolean }[] | undefined): string {
  if (!islands?.length) return "—";
  const names: Record<string, string> = { mujoco: "MuJoCo Warp", vbd: "Newton VBD", superdex: "SuperDex FEM", xpbd: "Newton XPBD", style3d: "Style3D", genesis: "Genesis IPC" };
  return [...new Set(islands.map((i) => names[i.solver] ?? i.solver))].join(" + ");
}

export function TopBar() {
  const t = useT();
  const status = useStore((s) => s.status);
  const manifest = useStore((s) => s.manifest);
  const connection = useStore((s) => s.connection);
  const latency = useStore((s) => s.latencyMs);
  const theme = useStore((s) => s.theme);
  const lang = useStore((s) => s.lang);
  const setPref = useStore((s) => s.setPref);
  const openDialog = useStore((s) => s.openDialog);
  const [fullscreen, setFullscreen] = useState(Boolean(document.fullscreenElement));

  useEffect(() => {
    const onChange = () => setFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener("fullscreenchange", onChange);
    return () => document.removeEventListener("fullscreenchange", onChange);
  }, []);

  const phase = status?.phase ?? "starting";
  const simChip =
    phase === "building"
      ? { cls: "warn", text: t("chip.building") }
      : phase === "error"
        ? { cls: "err", text: t("chip.error") }
        : phase !== "ready"
          ? { cls: "", text: t("chip.starting") }
          : status?.playing
            ? { cls: "ok", text: t("chip.running") }
            : { cls: "info", text: t("chip.paused") };
  const linkChip =
    connection === "open"
      ? { cls: "ok", text: t("chip.connected") }
      : connection === "closed"
        ? { cls: "err", text: t("chip.disconnected") }
        : { cls: "warn", text: connection === "connecting" ? t("chip.connecting") : t("chip.reconnecting") };
  const workcell =
    (status?.scenario && "title" in status.scenario ? status.scenario.title : "") || manifest?.scenario?.title || t("crumb.none");
  const backend = backendLabel(manifest?.physics.islands);

  return (
    <header className="topbar" role="banner">
      <div className="brand">
        <Logo />
        <span className="brand-divider" aria-hidden />
        <div className="brand-title">
          <strong>{t("app.title")}</strong>
          <span>{t("app.subtitle")}</span>
        </div>
      </div>
      <nav className="crumbs" aria-label={t("crumb.label")}>
        <span>{t("crumb.project")}</span>
        <ChevronRight size={12} className="flip-rtl" />
        <b title={workcell}>{workcell}</b>
        {manifest && <span className="mono muted">#{manifest.generation}</span>}
      </nav>
      <span className="topbar-spacer" />
      <div className="chips" role="status" aria-live="polite">
        <span className={`chip ${simChip.cls}`} title={t("chip.simHint")}>
          <span className="dot" />
          {simChip.text}
          {phase === "ready" && status && <small>{status.simTime.toFixed(2)} s</small>}
        </span>
        <span className="chip" title={manifest ? manifest.physics.summary : t("chip.backendHint")}>
          <span className={`dot${manifest ? "" : ""}`} style={{ background: manifest ? "var(--accent)" : undefined }} />
          {backend}
          {manifest && <small>{manifest.physics.device}</small>}
        </span>
        <span className={`chip ${linkChip.cls}`} title={t("chip.linkHint")}>
          <span className="dot" />
          {linkChip.text}
          {connection === "open" && latency !== null && <small>{latency} ms</small>}
        </span>
        <span className="chip accent" title={t("chip.targetHint")}>
          {t("chip.target")}
        </span>
      </div>
      <div className="toolbar">
        <IconButton label={t("action.scenarios")} onClick={() => openDialog("gallery")} shortcut="G">
          <LayoutGrid size={17} />
        </IconButton>
        <IconButton label={t("action.save")} onClick={() => openDialog("save")} shortcut="Ctrl+S">
          <Save size={17} />
        </IconButton>
        <IconButton label={t("action.load")} onClick={() => openDialog("load")} shortcut="Ctrl+O">
          <FolderOpen size={17} />
        </IconButton>
        <IconButton
          label={theme === "dark" ? t("action.lightMode") : t("action.darkMode")}
          onClick={() => {
            const next = theme === "dark" ? "light" : "dark";
            setPref("theme", next);
            applyTheme(next);
          }}
        >
          {theme === "dark" ? <Sun size={17} /> : <Moon size={17} />}
        </IconButton>
        <IconButton
          label={lang === "en" ? "العربية" : "English"}
          onClick={() => {
            const next = lang === "en" ? "ar" : "en";
            setPref("lang", next);
            applyLang(next);
          }}
        >
          <Languages size={17} />
        </IconButton>
        <IconButton label={t("action.settings")} onClick={() => openDialog("settings")}>
          <Settings size={17} />
        </IconButton>
        <IconButton
          label={fullscreen ? t("action.exitFullscreen") : t("action.fullscreen")}
          onClick={() => {
            if (document.fullscreenElement) void document.exitFullscreen();
            else void document.documentElement.requestFullscreen();
          }}
        >
          {fullscreen ? <Minimize size={17} /> : <Maximize size={17} />}
        </IconButton>
        <IconButton label={t("action.help")} onClick={() => openDialog("help")} shortcut="?">
          <HelpCircle size={17} />
        </IconButton>
        <IconButton label={t("action.about")} onClick={() => openDialog("about")}>
          <Info size={17} />
        </IconButton>
      </div>
    </header>
  );
}
