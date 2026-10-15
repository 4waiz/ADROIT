// SPDX-License-Identifier: Apache-2.0
// BRIDGE | Digital Twin Studio: the command centre layout.

import { useEffect } from "react";
import { bridge } from "./api/socket";
import { AboutDialog, HelpDialog, SettingsDialog } from "./components/Dialogs";
import { Dock } from "./components/Dock";
import { LeftPanel, RightPanel } from "./components/Panels";
import { GalleryDialog, LoadDialog, SaveDialog } from "./components/Scenarios";
import { Toasts } from "./components/Toasts";
import { TopBar } from "./components/TopBar";
import { Viewport } from "./components/Viewport";
import { useControls } from "./hooks/useControls";
import { useT } from "./i18n";
import { applyLang, applyTheme, useStore } from "./state/store";

export function App() {
  const t = useT();
  const dialog = useStore((s) => s.dialog);
  const theme = useStore((s) => s.theme);
  const lang = useStore((s) => s.lang);
  useControls();

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);
  useEffect(() => {
    applyLang(lang);
  }, [lang]);

  useEffect(() => {
    bridge.connect();
    const beforeUnload = (e: BeforeUnloadEvent) => {
      if (useStore.getState().draftDirty) {
        e.preventDefault();
        e.returnValue = "";
      }
    };
    window.addEventListener("beforeunload", beforeUnload);
    return () => {
      window.removeEventListener("beforeunload", beforeUnload);
      bridge.close();
    };
  }, []);

  return (
    <div className="app">
      <a href="#viewport" className="sr-only">{t("a11y.skip")}</a>
      <TopBar />
      <div className="workspace">
        <LeftPanel />
        <main className="center" id="viewport" aria-label={t("a11y.main")}>
          <Viewport />
          <Dock />
        </main>
        <RightPanel />
      </div>
      {dialog === "gallery" && <GalleryDialog />}
      {dialog === "about" && <AboutDialog />}
      {dialog === "settings" && <SettingsDialog />}
      {dialog === "help" && <HelpDialog />}
      {dialog === "save" && <SaveDialog />}
      {dialog === "load" && <LoadDialog />}
      <Toasts />
    </div>
  );
}
