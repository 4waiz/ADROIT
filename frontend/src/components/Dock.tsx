// SPDX-License-Identifier: Apache-2.0
// The engineering dock under the viewport: telemetry, timeline, events, commands, mission
// control, the real-to-sim reference workspace and diagnostics. Resizable and collapsible.

import { ChevronDown, ChevronUp } from "lucide-react";
import { useT } from "../i18n";
import { type DockTab, useStore } from "../state/store";
import { useResize } from "../hooks/useResize";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { CommandsPanel, EventsPanel, TimelinePanel } from "./LogPanels";
import { ReferencePanel } from "./ReferencePanel";
import { TaskPanel } from "./TaskPanel";
import { TelemetryPanel } from "./TelemetryPanel";
import { IconButton } from "./ui/primitives";

export function Dock() {
  const t = useT();
  const tab = useStore((s) => s.dockTab);
  const height = useStore((s) => s.dockHeight);
  const collapsed = useStore((s) => s.dockCollapsed);
  const setPref = useStore((s) => s.setPref);
  const events = useStore((s) => s.events.length);
  const commands = useStore((s) => s.commands.length);
  const running = useStore((s) => Boolean(s.status?.runner));
  const resize = useResize("y", height, (v) => setPref("dockHeight", Math.round(Math.min(Math.max(v, 140), window.innerHeight * 0.62))), -1);

  const tabs: { id: DockTab; label: string; count?: number | string }[] = [
    { id: "telemetry", label: t("dock.telemetry") },
    { id: "timeline", label: t("dock.timeline") },
    { id: "events", label: t("dock.events"), count: events },
    { id: "commands", label: t("dock.commands"), count: commands },
    { id: "tasks", label: t("dock.tasks"), count: running ? "●" : undefined },
    { id: "reference", label: t("dock.reference") },
    { id: "diagnostics", label: t("dock.diagnostics") },
  ];

  return (
    <section className="dock" style={{ height: collapsed ? 34 : height }} aria-label={t("dock.label")}>
      {!collapsed && <div className={`resizer y${resize.dragging ? " dragging" : ""}`} onPointerDown={resize.onPointerDown} role="separator" aria-orientation="horizontal" aria-label={t("dock.resize")} />}
      <div className="panel-head">
        <div className="tabs" role="tablist" aria-label={t("dock.label")}>
          {tabs.map((x) => (
            <button
              key={x.id}
              type="button"
              role="tab"
              className="tab"
              aria-selected={tab === x.id}
              onClick={() => {
                setPref("dockTab", x.id);
                if (collapsed) setPref("dockCollapsed", false);
              }}
            >
              {x.label}
              {x.count !== undefined && x.count !== 0 && <span className="count">{x.count}</span>}
            </button>
          ))}
        </div>
        <span className="grow" />
        <IconButton label={collapsed ? t("dock.expand") : t("dock.collapse")} onClick={() => setPref("dockCollapsed", !collapsed)} size="sm">
          {collapsed ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
        </IconButton>
      </div>
      {!collapsed && (
        <div className="dock-body" role="tabpanel">
          {tab === "telemetry" && <TelemetryPanel />}
          {tab === "timeline" && <TimelinePanel />}
          {tab === "events" && <EventsPanel />}
          {tab === "commands" && <CommandsPanel />}
          {tab === "tasks" && <TaskPanel />}
          {tab === "reference" && <ReferencePanel />}
          {tab === "diagnostics" && <DiagnosticsPanel />}
        </div>
      )}
    </section>
  );
}
