// SPDX-License-Identifier: LicenseRef-BRIDGE
// What the page shows, served away from the simulation's own machine (adroit.bridgeae.com), while
// no simulation answers it: that the live simulation is offline, what BRIDGE is, and how it runs.
// The page keeps asking quietly in the background (api/socket.ts); the studio opens if one answers.
// The picture is a screenshot of the studio, captioned as one: nothing here is live.

import { Check, Copy, Languages, Moon, Sun } from "lucide-react";
import { useEffect, useState } from "react";
import factoryHall from "../../../docs/bridge/media/factory-hall.webp";
import { useT } from "../i18n";
import { applyLang, applyTheme, useStore } from "../state/store";
import { Attribution } from "./Dialogs";
import { Logo } from "./Logo";
import { IconButton } from "./ui/primitives";

/** How BRIDGE starts on a GPU workstation (README, "Start it"). */
const START = "powershell -ExecutionPolicy Bypass -File scripts\\bridge.ps1 -Open";

export function OfflinePage({ checking }: { checking: boolean }) {
  const t = useT();
  const theme = useStore((s) => s.theme);
  const lang = useStore((s) => s.lang);
  const setPref = useStore((s) => s.setPref);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const id = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(id);
  }, [copied]);

  const copy = () => {
    void navigator.clipboard?.writeText(START).then(
      () => setCopied(true),
      () => setCopied(false),
    );
  };

  return (
    <div className="offline">
      <header className="offline-bar">
        <div className="brand">
          <Logo />
          <span className="brand-divider" aria-hidden />
          <div className="brand-title">
            <strong>{t("app.title")}</strong>
            <span>{t("app.subtitle")}</span>
          </div>
        </div>
        <span className="grow" />
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
      </header>

      <main className="offline-main">
        {checking ? (
          <div className="loader offline-checking" role="status" aria-live="polite">
            <div className="bar" />
            <p>{t("offline.checking")}</p>
          </div>
        ) : (
          <article className="offline-card" aria-labelledby="offline-title">
            <div className="offline-copy">
              <span className="chip warn">
                <span className="dot" />
                {t("offline.badge")}
              </span>
              <h1 id="offline-title">{t("offline.title")}</h1>
              <p className="offline-lede">{t("offline.lede")}</p>

              <h2 className="section-title">{t("offline.whatTitle")}</h2>
              <p>{t("offline.what")}</p>

              <h2 className="section-title">{t("offline.runTitle")}</h2>
              <p>{t("offline.run")}</p>
              <div className="offline-command" dir="ltr">
                <code>{START}</code>
                <IconButton label={copied ? t("offline.copied") : t("offline.copy")} onClick={copy}>
                  {copied ? <Check size={15} /> : <Copy size={15} />}
                </IconButton>
                <span className="sr-only" role="status">
                  {copied ? t("offline.copied") : ""}
                </span>
              </div>
            </div>

            <figure className="offline-shot">
              <img src={factoryHall} alt={t("offline.shotAlt")} width={1564} height={1032} decoding="async" />
              <figcaption>{t("offline.shotCaption")}</figcaption>
            </figure>
          </article>
        )}
      </main>

      <footer className="offline-foot">
        <Attribution />
        <span aria-hidden>·</span>
        <span>{t("offline.foundation")}</span>
      </footer>
    </div>
  );
}
