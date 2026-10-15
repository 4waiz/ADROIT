// SPDX-License-Identifier: Apache-2.0
import "@fontsource-variable/figtree";
import "@fontsource/ibm-plex-mono/400.css";
import "@fontsource/ibm-plex-mono/600.css";
import "@fontsource/ibm-plex-sans-arabic/400.css";
import "@fontsource/ibm-plex-sans-arabic/600.css";
import "./styles/tokens.css";
import "./styles/app.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { getEngine } from "./components/Viewport";
import { live } from "./state/live";
import { useStore } from "./state/store";

// Read-only views for the browser journey test (tests/test_bridge_journey.py): state, the newest
// frame's header and the camera. Nothing here can change the simulation.
(window as unknown as { __BRIDGE__: unknown }).__BRIDGE__ = {
  state: () => {
    const s = useStore.getState();
    return { selected: s.selected, connection: s.connection, status: s.status, theme: s.theme, lang: s.lang, generation: s.manifest?.generation };
  },
  frame: () => live.frame?.header ?? null,
  camera: () => getEngine()?.cameraState() ?? null,
  manifest: () => {
    const m = live.manifest;
    return m ? { generation: m.generation, arms: m.arms.map((a) => ({ name: a.name, joints: a.joints.length })), subjects: m.subjects.map((x) => x.key) } : null;
  },
};

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
