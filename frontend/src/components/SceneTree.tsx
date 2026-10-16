// SPDX-License-Identifier: LicenseRef-BRIDGE
// The workcell as a hierarchy: robot arms (with their joints, tool centre and gripper),
// objects, deformables, fixtures, goal regions, cameras and the stage lights. Every row is
// a real entry of the compiled scene, except the lights, which are the viewport's own.

import type { ReactElement } from "react";
import {
  Box,
  Camera,
  ChevronDown,
  ChevronRight,
  CircleDot,
  Cog,
  Grip,
  Lightbulb,
  Search,
  Shapes,
  SquareDashed,
  Waves,
  Wrench,
} from "lucide-react";
import { useMemo, useState } from "react";
import type { Manifest } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";

interface Row {
  key: string;
  label: string;
  meta?: string;
  depth: number;
  icon: ReactElement;
  joint?: number;
  select: string | null;
}

function groups(manifest: Manifest, t: ReturnType<typeof useT>) {
  const out: { id: string; title: string; rows: Row[] }[] = [];
  const robotRows: Row[] = [];
  for (const arm of manifest.arms) {
    const key = `arm:${arm.name}`;
    robotRows.push({
      key,
      label: `${t("tree.arm")} · ${arm.name}`,
      meta: arm.role || `${arm.joints.length} DOF`,
      depth: 0,
      icon: <Cog size={14} />,
      select: key,
    });
    arm.joints.forEach((j, k) =>
      robotRows.push({
        key: `${key}#j${k}`,
        label: `J${k + 1} · ${j.name}`,
        meta: j.type === "revolute" ? "R" : j.type === "prismatic" ? "P" : "",
        depth: 1,
        icon: <CircleDot size={12} />,
        joint: k,
        select: key,
      }),
    );
    robotRows.push({
      key: `${key}#tcp`,
      label: t("tree.endEffector"),
      meta: manifest.bodies[arm.eeBody]?.label ?? "",
      depth: 1,
      icon: <Wrench size={12} />,
      select: key,
    });
    if (arm.gripper) {
      robotRows.push({
        key: `${key}#grip`,
        label: t("tree.gripper"),
        meta: `${(arm.gripper.span * 1000).toFixed(0)} mm`,
        depth: 1,
        icon: <Grip size={12} />,
        select: key,
      });
    }
  }
  out.push({ id: "robot", title: `${t("tree.robots")}${manifest.scenario.robot ? ` · ${manifest.scenario.robot}` : ""}`, rows: robotRows });
  const subjectRows = (kind: string, icon: ReactElement, filter?: (s: Manifest["subjects"][number]) => boolean) =>
    manifest.subjects
      .filter((s) => s.kind === kind && (!filter || filter(s)))
      .map((s) => ({
        key: s.key,
        label: s.name,
        meta: s.mass ? `${(s.mass * 1000).toFixed(0)} g` : s.particles ? `${s.particles} pts` : s.category || "",
        depth: 0,
        icon,
        select: s.key,
      }));
  out.push({ id: "objects", title: t("tree.objects"), rows: subjectRows("object", <Box size={14} />, (s) => !s.static) });
  out.push({ id: "deformables", title: t("tree.deformables"), rows: subjectRows("cloth", <Waves size={14} />) });
  out.push({
    id: "fixtures",
    title: t("tree.fixtures"),
    rows: [...subjectRows("static", <Shapes size={14} />), ...subjectRows("object", <Shapes size={14} />, (s) => Boolean(s.static))],
  });
  out.push({ id: "regions", title: t("tree.regions"), rows: subjectRows("region", <SquareDashed size={14} />) });
  out.push({
    id: "cameras",
    title: t("tree.cameras"),
    rows: manifest.cameras.map((c) => ({
      key: `camera:${c.name}`,
      label: c.name,
      meta: `${c.width}×${c.height}`,
      depth: 0,
      icon: <Camera size={14} />,
      select: `camera:${c.name}`,
    })),
  });
  out.push({
    id: "lights",
    title: t("tree.lights"),
    rows: ["key", "fill", "rim", "environment"].map((name) => ({
      key: `light:${name}`,
      label: t(`light.${name}` as "light.key"),
      meta: t("tree.viewportOnly"),
      depth: 0,
      icon: <Lightbulb size={14} />,
      select: `light:${name}`,
    })),
  });
  return out;
}

export function SceneTree() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const selected = useStore((s) => s.selected);
  const focusJoint = useStore((s) => s.focusJoint);
  const select = useStore((s) => s.select);
  const [query, setQuery] = useState("");
  const [closed, setClosed] = useState<Record<string, boolean>>({});

  const all = useMemo(() => (manifest ? groups(manifest, t) : []), [manifest, t]);
  if (!manifest) return <div className="empty">{t("tree.empty")}</div>;
  const q = query.trim().toLowerCase();

  return (
    <div>
      <div className="search">
        <Search size={14} />
        <input
          type="search"
          placeholder={t("tree.search")}
          aria-label={t("tree.search")}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      <ul className="tree" role="tree" aria-label={t("tree.label")}>
        {all.map((group) => {
          const rows = q ? group.rows.filter((r) => `${r.label} ${r.meta ?? ""}`.toLowerCase().includes(q)) : group.rows;
          if (q && !rows.length) return null;
          const isClosed = closed[group.id] && !q;
          return (
            <li key={group.id} role="treeitem" aria-expanded={!isClosed}>
              <div
                className="tree-group"
                onClick={() => setClosed({ ...closed, [group.id]: !closed[group.id] })}
                onKeyDown={(e) => e.key === "Enter" && setClosed({ ...closed, [group.id]: !closed[group.id] })}
                tabIndex={0}
                role="button"
              >
                {isClosed ? <ChevronRight size={12} className="flip-rtl" /> : <ChevronDown size={12} />}
                {group.title}
                <span className="count">{group.rows.filter((r) => r.depth === 0).length}</span>
              </div>
              {!isClosed && (
                <ul className="tree" role="group" style={{ padding: 0 }}>
                  {rows.length === 0 && <li className="tree-empty">{t("tree.none")}</li>}
                  {rows.map((row) => {
                    const isSelected =
                      row.joint !== undefined
                        ? selected === row.select && focusJoint === row.joint
                        : row.depth === 0 && selected === row.select;
                    return (
                      <li key={row.key} role="none">
                        <button
                          type="button"
                          role="treeitem"
                          className="tree-row"
                          aria-selected={isSelected}
                          style={{ ["--depth" as string]: row.depth }}
                          onClick={() => select(row.select, row.joint ?? null)}
                          onDoubleClick={() => useStore.getState().requestFocus()}
                        >
                          {row.icon}
                          <span className="label">{row.label}</span>
                          {row.meta && <span className="meta">{row.meta}</span>}
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
