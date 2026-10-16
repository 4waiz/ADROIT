// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE | Digital Twin Studio: the command centre layout.

import { useEffect } from "react";
import { bridge } from "./api/socket";
import { AboutDialog, HelpDialog, SettingsDialog } from "./components/Dialogs";
import { Dock } from "./components/Dock";
import { OfflinePage } from "./components/OfflinePage";
import { LeftPanel, RightPanel } from "./components/Panels";
import { GalleryDialog, LoadDialog, SaveDialog } from "./components/Scenarios";
import { SimplePanel } from "./components/simple/SimplePanel";
import { Tutorial, useFirstVisitTour } from "./components/simple/Tutorial";
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
  const uiMode = useStore((s) => s.uiMode);
  const tutorial = useStore((s) => s.tutorialOpen);
  const presence = useStore((s) => s.presence);
  useControls();
  useFirstVisitTour();

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);
  useEffect(() => {
    applyLang(lang);
  }, [lang]);

  useEffect(() => {
    bridge.start();
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

  // Served away from the simulation's machine, the studio opens only once a service answers;
  // until then the page says plainly that the simulation is offline. Locally this is always live.
  if (presence !== "live") return <OfflinePage checking={presence === "checking"} />;

  return (
    <div className={`app mode-${uiMode}`}>
      <a href="#viewport" className="sr-only">{t("a11y.skip")}</a>
      <TopBar />
      <div className="workspace">
        {/* Simple mode keeps the factory and one plain panel; Expert adds every engineering panel. */}
        {uiMode === "expert" ? <LeftPanel /> : <span aria-hidden />}
        <main className="center" id="viewport" aria-label={t("a11y.main")}>
          <Viewport />
          {uiMode === "expert" && <Dock />}
        </main>
        {uiMode === "expert" ? <RightPanel /> : <SimplePanel />}
      </div>
      {tutorial && <Tutorial />}
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
