// SPDX-License-Identifier: LicenseRef-BRIDGE
// About (with the open-source licences this product is built on), settings, and the keyboard map.

import { ExternalLink } from "lucide-react";
import { useEffect, useState } from "react";
import { getJson } from "../api/rest";
import { useT } from "../i18n";
import { applyLang, applyTheme, useStore } from "../state/store";
import { Logo } from "./Logo";
import { Dialog, Segmented, Toggle } from "./ui/primitives";

export const AUTHOR_URL = "https://kanbanstudios.ae/team-kanban";

export function Attribution() {
  const t = useT();
  return (
    <span className="attribution">
      <a href={AUTHOR_URL} target="_blank" rel="noopener noreferrer" aria-label={t("about.builtByLink")}>
        {t("about.builtBy")} <ExternalLink size={10} style={{ verticalAlign: "-1px" }} />
      </a>
    </span>
  );
}

interface Licences {
  bridgeLicense: string;
  license: string;
  notice: string;
  citation: string;
  thirdParty: { path: string; text: string }[];
  client: { name: string; license: string }[];
}

export function AboutDialog() {
  const t = useT();
  const info = useStore((s) => s.info);
  const [tab, setTab] = useState<"about" | "licences">("about");
  const [licences, setLicences] = useState<Licences | null>(null);
  useEffect(() => {
    if (tab === "licences" && !licences) void getJson<Licences>("/api/licenses").then(setLicences).catch(() => setLicences(null));
  }, [tab, licences]);
  return (
    <Dialog title={t("about.title")} onClose={() => useStore.getState().openDialog(null)}>
      <div className="row" style={{ marginBottom: 14 }}>
        <Segmented
          label={t("about.title")}
          value={tab}
          onChange={setTab}
          options={[
            { value: "about", label: t("about.tabAbout") },
            { value: "licences", label: t("about.tabLicences") },
          ]}
        />
      </div>
      {tab === "about" ? (
        <div className="prose">
          <div className="row" style={{ gap: 14, marginBottom: 12 }}>
            <Logo height={30} />
            <div>
              <div style={{ fontWeight: 700, letterSpacing: "0.14em", textTransform: "uppercase", fontSize: 13 }}>{t("app.title")}</div>
              <div className="muted" style={{ fontSize: 12 }}>{t("app.subtitle")} · v{info?.version ?? "0.1.0"}</div>
            </div>
          </div>
          <p>{t("about.intro")}</p>
          <h3>{t("about.creditTitle")}</h3>
          <p>
            <a href={AUTHOR_URL} target="_blank" rel="noopener noreferrer" style={{ fontWeight: 700 }}>
              {t("about.builtBy")} <ExternalLink size={12} style={{ verticalAlign: "-1px" }} />
            </a>
            <br />
            <span className="muted">{t("about.builtByNote")}</span>
          </p>
          <h3>{t("about.foundationTitle")}</h3>
          <p>{t("about.foundation")}</p>
          <p className="muted" style={{ fontSize: 12 }}>{t("about.endorsement")}</p>
          <h3>{t("about.machineTitle")}</h3>
          <p className="mono" style={{ fontSize: 12 }}>
            {info ? `${info.machine.os} · Python ${info.machine.python} · ${info.machine.gpu.map((g) => g.name).join(", ") || t("diag.noGpu")} · Warp ${info.machine.warp}` : "—"}
          </p>
        </div>
      ) : (
        <div className="prose">
          <p>{t("about.licencesIntro")}</p>
          {!licences ? (
            <p className="muted">{t("common.loading")}</p>
          ) : (
            <>
              <h3>{t("about.bridgeLicence")}</h3>
              <pre className="license">{licences.bridgeLicense}</pre>
              <h3>{t("about.robosimLicence")}</h3>
              <pre className="license">{licences.license}</pre>
              <h3>NOTICE</h3>
              <pre className="license">{licences.notice}</pre>
              <h3>{t("about.thirdParty")}</h3>
              {licences.thirdParty.map((l) => (
                <details key={l.path} style={{ marginBottom: 6 }}>
                  <summary className="mono" style={{ cursor: "pointer", fontSize: 12 }}>{l.path}</summary>
                  <pre className="license">{l.text}</pre>
                </details>
              ))}
              <h3>{t("about.clientLibraries")}</h3>
              <ul style={{ fontSize: 12 }}>
                {licences.client.map((c) => (
                  <li key={c.name}>{c.name} — {c.license}</li>
                ))}
              </ul>
              <h3>{t("about.citation")}</h3>
              <pre className="license">{licences.citation}</pre>
            </>
          )}
        </div>
      )}
    </Dialog>
  );
}

export function SettingsDialog() {
  const t = useT();
  const s = useStore();
  return (
    <Dialog title={t("settings.title")} onClose={() => s.openDialog(null)} size="narrow">
      <div className="field">
        <span className="field-label">{t("settings.theme")}</span>
        <Segmented
          label={t("settings.theme")}
          value={s.theme}
          onChange={(v) => {
            s.setPref("theme", v);
            applyTheme(v);
          }}
          options={[
            { value: "dark", label: t("settings.dark") },
            { value: "light", label: t("settings.light") },
          ]}
        />
      </div>
      <div className="field">
        <span className="field-label">{t("settings.language")}</span>
        <Segmented
          label={t("settings.language")}
          value={s.lang}
          onChange={(v) => {
            s.setPref("lang", v);
            applyLang(v);
          }}
          options={[
            { value: "en", label: "English" },
            { value: "ar", label: "العربية" },
          ]}
        />
      </div>
      <div className="field">
        <span className="field-label">{t("settings.quality")}</span>
        <Segmented
          label={t("settings.quality")}
          value={s.quality}
          onChange={(v) => s.setPref("quality", v)}
          options={[
            { value: "performance", label: t("quality.performance") },
            { value: "balanced", label: t("quality.balanced") },
            { value: "ultra", label: t("quality.ultra") },
          ]}
        />
        <p className="muted" style={{ fontSize: 11, margin: "6px 0 0" }}>{t(`quality.${s.quality}Hint` as "quality.balancedHint")}</p>
      </div>
      <div className="field">
        <span className="field-label">{t("settings.angles")}</span>
        <Segmented
          label={t("settings.angles")}
          value={s.angleUnit}
          onChange={(v) => s.setPref("angleUnit", v)}
          options={[
            { value: "deg", label: t("settings.degrees") },
            { value: "rad", label: t("settings.radians") },
          ]}
        />
      </div>
      <div className="field">
        <span className="field-label">{t("settings.projection")}</span>
        <Segmented
          label={t("settings.projection")}
          value={s.projection}
          onChange={(v) => s.setPref("projection", v)}
          options={[
            { value: "perspective", label: t("settings.perspective") },
            { value: "orthographic", label: t("settings.orthographic") },
          ]}
        />
      </div>
      <Toggle label={t("overlay.grid")} checked={s.overlays.grid} onChange={(v) => s.setOverlay("grid", v)} />
      <Toggle label={t("overlay.workspace")} checked={s.overlays.workspace} onChange={(v) => s.setOverlay("workspace", v)} />
      <button
        type="button"
        className="btn sm"
        style={{ marginTop: 12 }}
        onClick={() => {
          try {
            localStorage.removeItem("bridge.prefs");
          } catch {
            /* ignore */
          }
          window.location.reload();
        }}
      >
        {t("settings.resetLayout")}
      </button>
    </Dialog>
  );
}

export const SHORTCUTS: { keys: string; action: string }[] = [
  { keys: "Space", action: "help.playPause" },
  { keys: ".", action: "help.step" },
  { keys: "Shift + R", action: "help.reset" },
  { keys: "1 – 6", action: "help.presets" },
  { keys: "O", action: "help.projection" },
  { keys: "F", action: "help.focus" },
  { keys: "W A S D · Q / E", action: "help.fly" },
  { keys: "Home", action: "help.resetView" },
  { keys: "T / R / L", action: "help.gizmo" },
  { keys: "← → ↑ ↓ / PgUp PgDn", action: "help.jog" },
  { keys: "Alt + arrows", action: "help.jogRotate" },
  { keys: "[ / ]", action: "help.grip" },
  { keys: "H", action: "help.home" },
  { keys: "Esc", action: "help.deselect" },
  { keys: "G", action: "help.gallery" },
  { keys: "P", action: "help.screenshot" },
  { keys: "Ctrl + S / Ctrl + O", action: "help.saveLoad" },
  { keys: "Ctrl + Z / Ctrl + Y", action: "help.undoRedo" },
  { keys: "?", action: "help.help" },
];

export function HelpDialog() {
  const t = useT();
  return (
    <Dialog title={t("help.title")} onClose={() => useStore.getState().openDialog(null)}>
      <div className="split" style={{ background: "transparent", gap: 24 }}>
        <div style={{ background: "transparent" }}>
          <div className="section-title">{t("help.keyboard")}</div>
          <table className="data">
            <tbody>
              {SHORTCUTS.map((s) => (
                <tr key={s.keys}>
                  <td style={{ width: 150 }}><kbd style={{ marginInlineStart: 0 }}>{s.keys}</kbd></td>
                  <td>{t(s.action as "help.playPause")}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="muted" style={{ fontSize: 11 }}>{t("help.keysNote")}</p>
        </div>
        <div className="prose" style={{ background: "transparent", fontSize: 12.5 }}>
          <div className="section-title">{t("help.mouse")}</div>
          <p>{t("help.mouseText")}</p>
          <div className="section-title">{t("help.control")}</div>
          <p>{t("help.controlText")}</p>
          <div className="section-title">{t("help.gamepad")}</div>
          <p>{t("help.gamepadText")}</p>
          <div className="section-title">{t("help.api")}</div>
          <p>
            {t("help.apiText")} <a href="/docs" target="_blank" rel="noopener noreferrer">/docs</a>
          </p>
        </div>
      </div>
    </Dialog>
  );
}
