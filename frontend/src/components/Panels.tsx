// SPDX-License-Identifier: Apache-2.0
// The two side panels: the scene and its builder on the left, the inspector on the right. Both
// resize by their inner edge and collapse to a rail.

import type { ReactElement } from "react";
import { Boxes, ChevronsLeft, ChevronsRight, Cpu, Hammer, LayoutGrid, Network, SlidersHorizontal } from "lucide-react";
import { useT } from "../i18n";
import { type LeftTab, type RightTab, useStore } from "../state/store";
import { useResize } from "../hooks/useResize";
import { Attribution } from "./Dialogs";
import { Inspector } from "./Inspector";
import { PhysicsPanel } from "./PhysicsPanel";
import { ScenarioList } from "./Scenarios";
import { SceneTree } from "./SceneTree";
import { TwinPanel } from "./TwinPanel";
import { IconButton } from "./ui/primitives";
import { WorkcellEditor } from "./WorkcellEditor";

export function LeftPanel() {
  const t = useT();
  const tab = useStore((s) => s.leftTab);
  const width = useStore((s) => s.leftWidth);
  const collapsed = useStore((s) => s.leftCollapsed);
  const setPref = useStore((s) => s.setPref);
  const resize = useResize("x", width, (v) => setPref("leftWidth", Math.round(Math.min(Math.max(v, 220), 460))), 1);
  const tabs: { id: LeftTab; label: string; icon: ReactElement }[] = [
    { id: "scene", label: t("left.scene"), icon: <Boxes size={15} /> },
    { id: "library", label: t("left.builder"), icon: <Hammer size={15} /> },
    { id: "scenarios", label: t("left.scenarios"), icon: <LayoutGrid size={15} /> },
  ];
  if (collapsed) {
    return (
      <aside className="panel left collapsed" aria-label={t("left.label")}>
        <div className="rail">
          <IconButton label={t("panel.expand")} onClick={() => setPref("leftCollapsed", false)}>
            <ChevronsRight size={15} className="flip-rtl" />
          </IconButton>
          {tabs.map((x) => (
            <IconButton key={x.id} label={x.label} active={tab === x.id} onClick={() => { setPref("leftTab", x.id); setPref("leftCollapsed", false); }}>
              {x.icon}
            </IconButton>
          ))}
        </div>
      </aside>
    );
  }
  return (
    <aside className="panel left" style={{ width }} aria-label={t("left.label")}>
      <div className={`resizer x${resize.dragging ? " dragging" : ""}`} onPointerDown={resize.onPointerDown} role="separator" aria-orientation="vertical" aria-label={t("panel.resize")} />
      <div className="panel-head">
        <div className="tabs" role="tablist">
          {tabs.map((x) => (
            <button key={x.id} type="button" role="tab" className="tab" aria-selected={tab === x.id} onClick={() => setPref("leftTab", x.id)}>
              {x.label}
            </button>
          ))}
        </div>
        <span className="grow" />
        <IconButton label={t("panel.collapse")} size="sm" onClick={() => setPref("leftCollapsed", true)}>
          <ChevronsLeft size={14} className="flip-rtl" />
        </IconButton>
      </div>
      <div className="panel-body" role="tabpanel">
        {tab === "scene" && <SceneTree />}
        {tab === "library" && <WorkcellEditor />}
        {tab === "scenarios" && <ScenarioList />}
      </div>
      <div className="footer-attrib">
        <Attribution />
      </div>
    </aside>
  );
}

export function RightPanel() {
  const t = useT();
  const tab = useStore((s) => s.rightTab);
  const width = useStore((s) => s.rightWidth);
  const collapsed = useStore((s) => s.rightCollapsed);
  const setPref = useStore((s) => s.setPref);
  const resize = useResize("x", width, (v) => setPref("rightWidth", Math.round(Math.min(Math.max(v, 280), 520))), -1);
  const tabs: { id: RightTab; label: string; icon: ReactElement }[] = [
    { id: "inspector", label: t("right.inspector"), icon: <SlidersHorizontal size={15} /> },
    { id: "physics", label: t("right.physics"), icon: <Cpu size={15} /> },
    { id: "twin", label: t("right.twin"), icon: <Network size={15} /> },
  ];
  if (collapsed) {
    return (
      <aside className="panel right collapsed" aria-label={t("right.label")}>
        <div className="rail">
          <IconButton label={t("panel.expand")} onClick={() => setPref("rightCollapsed", false)}>
            <ChevronsLeft size={15} className="flip-rtl" />
          </IconButton>
          {tabs.map((x) => (
            <IconButton key={x.id} label={x.label} active={tab === x.id} onClick={() => { setPref("rightTab", x.id); setPref("rightCollapsed", false); }}>
              {x.icon}
            </IconButton>
          ))}
        </div>
      </aside>
    );
  }
  return (
    <aside className="panel right" style={{ width }} aria-label={t("right.label")}>
      <div className={`resizer x${resize.dragging ? " dragging" : ""}`} onPointerDown={resize.onPointerDown} role="separator" aria-orientation="vertical" aria-label={t("panel.resize")} />
      <div className="panel-head">
        <div className="tabs" role="tablist">
          {tabs.map((x) => (
            <button key={x.id} type="button" role="tab" className="tab" aria-selected={tab === x.id} onClick={() => setPref("rightTab", x.id)}>
              {x.label}
            </button>
          ))}
        </div>
        <span className="grow" />
        <IconButton label={t("panel.collapse")} size="sm" onClick={() => setPref("rightCollapsed", true)}>
          <ChevronsRight size={14} className="flip-rtl" />
        </IconButton>
      </div>
      <div className="panel-body" role="tabpanel">
        {tab === "inspector" && <Inspector />}
        {tab === "physics" && <PhysicsPanel />}
        {tab === "twin" && <TwinPanel />}
      </div>
    </aside>
  );
}
