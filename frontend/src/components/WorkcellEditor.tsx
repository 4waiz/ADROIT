// SPDX-License-Identifier: Apache-2.0
// The workcell builder. You edit a document (bridge.workcell/1); Build sends it to the host,
// which validates it, compiles it with the public constructors and swaps it in. The library only
// offers what the chosen physics preset can simulate, and says why the rest is unavailable.
// Draft objects are previewed in the viewport and can be moved there with the gizmo.

import type { ReactElement } from "react";
import { Box, Circle, Copy, Cylinder, Hammer, Minus, Plus, Redo2, RotateCcw, Square, Trash2, Undo2, Waves } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { bridge } from "../api/socket";
import { getJson, sendJson } from "../api/rest";
import type { ObjectDoc, ObjectKind, WorkcellCatalog, WorkcellDoc } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { NumberField } from "./ui/primitives";

const ICONS: Record<ObjectKind, ReactElement> = {
  box: <Box size={15} />,
  sphere: <Circle size={15} />,
  cylinder: <Cylinder size={15} />,
  capsule: <Cylinder size={15} />,
  slab: <Square size={15} />,
  post: <Minus size={15} style={{ transform: "rotate(90deg)" }} />,
  sheet: <Waves size={15} />,
  sponge: <Box size={15} />,
  rope: <Waves size={15} />,
};

let counter = 0;
const uid = () => `o${Date.now().toString(36)}${(counter++).toString(36)}`;

function uniqueName(doc: WorkcellDoc, base: string): string {
  const taken = new Set([...doc.objects.map((o) => o.name), ...doc.regions.map((r) => r.name)]);
  if (!taken.has(base)) return base;
  for (let i = 2; i < 999; i++) if (!taken.has(`${base}_${i}`)) return `${base}_${i}`;
  return `${base}_${Date.now() % 1000}`;
}

function freshObject(doc: WorkcellDoc, kind: ObjectKind): ObjectDoc {
  const base: ObjectDoc = { id: uid(), kind, name: uniqueName(doc, kind), at: [0, 0.1, 0.05], rpy: [0, 0, 0] };
  switch (kind) {
    case "box":
      return { ...base, half: [0.025, 0.025, 0.025], at: [0, 0.1, 0.027], mass: 0.1, color: "#2f86e0" };
    case "sphere":
      return { ...base, radius: 0.025, at: [0, 0.1, 0.027], mass: 0.06, color: "#37c088" };
    case "cylinder":
      return { ...base, radius: 0.02, halfHeight: 0.05, at: [0, 0.1, 0.052], mass: 0.1, color: "#f0b429" };
    case "capsule":
      return { ...base, radius: 0.015, halfHeight: 0.04, at: [0, 0.1, 0.06], mass: 0.05, color: "#c78bff" };
    case "slab":
      return { ...base, half: [0.08, 0.08, 0.015], at: [0.05, 0.15, 0.015], color: "#5b6670" };
    case "post":
      return { ...base, radius: 0.015, halfHeight: 0.05, at: [0.05, -0.1, 0.05], color: "#8a96a3" };
    case "sheet":
      return { ...base, size: [0.3, 0.3], cells: [20, 20], at: [-0.15, -0.15, 0.002], material: "plain", color: "#e8e2d6" };
    case "sponge":
      return { ...base, size: [0.09, 0.05, 0.035], cells: [9, 5, 4], at: [0, 0.1, 0], material: "firm_foam", color: "#f2e46f" };
    case "rope":
      return { ...base, length: 0.62, radius: 0.011, segments: 28, at: [-0.05, -0.3, 0.012], color: "#ff6a3d" };
  }
}

function Vec({ label, values, unit, scale, onCommit, step }: { label: string; values: number[]; unit: string; scale: number; onCommit: (v: number[]) => void; step?: number }) {
  return (
    <div className="field">
      <span className="field-label">{label} ({unit})</span>
      <div className={values.length === 3 ? "grid-3" : "grid-2"}>
        {values.map((v, k) => (
          <NumberField
            key={k}
            label={`${label} ${"xyz"[k] ?? k}`}
            value={v * scale}
            digits={step && step >= 1 ? 0 : 1}
            onCommit={(n) => {
              const next = [...values];
              next[k] = n / scale;
              onCommit(next);
            }}
          />
        ))}
      </div>
    </div>
  );
}

export function WorkcellEditor() {
  const t = useT();
  const draft = useStore((s) => s.draft);
  const past = useStore((s) => s.past);
  const future = useStore((s) => s.future);
  const dirty = useStore((s) => s.draftDirty);
  const status = useStore((s) => s.status);
  const selected = useStore((s) => s.selected);
  const setDraft = useStore((s) => s.setDraft);
  const select = useStore((s) => s.select);
  const [catalog, setCatalog] = useState<WorkcellCatalog | null>(null);
  const [check, setCheck] = useState<{ ok: boolean; message: string } | null>(null);
  const timer = useRef(0);

  useEffect(() => {
    void getJson<WorkcellCatalog>("/api/workcell/catalog")
      .then((c) => {
        setCatalog(c);
        useStore.setState({ catalog: c });
      })
      .catch(() => setCatalog(null));
    if (!useStore.getState().draft) {
      void getJson<WorkcellDoc>("/api/workcell/current").then((doc) => {
        if (!useStore.getState().draft) setDraft(withIds(doc), { record: false, dirty: false });
      });
    }
  }, [setDraft]);

  useEffect(() => {
    if (!draft) return;
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => {
      void sendJson<{ ok: boolean; message: string }>("/api/workcell/validate", "POST", stripIds(draft)).then(setCheck).catch(() => setCheck(null));
    }, 300);
  }, [draft]);

  const editing = draft?.objects.find((o) => `draft:${o.id}` === selected) ?? null;
  const preset = draft && catalog ? catalog.presets[draft.physics] : null;
  const groups = useMemo(() => {
    if (!catalog) return [];
    const out = new Map<string, ObjectKind[]>();
    for (const [kind, info] of Object.entries(catalog.kinds)) {
      const list = out.get(info.group) ?? [];
      list.push(kind as ObjectKind);
      out.set(info.group, list);
    }
    return [...out.entries()];
  }, [catalog]);

  if (!draft || !catalog) return <div className="empty">{t("editor.loading")}</div>;

  const update = (patch: Partial<WorkcellDoc>) => setDraft({ ...draft, ...patch });
  const updateObject = (id: string, patch: Partial<ObjectDoc>) =>
    setDraft({ ...draft, objects: draft.objects.map((o) => (o.id === id ? { ...o, ...patch } : o)) });
  const building = status?.phase === "building";

  const build = async () => {
    const ack = await bridge.command("workcell.build", { doc: stripIds(draft) }, { terminal: true });
    if (ack.status === "completed") {
      useStore.setState({ draftDirty: false });
      useStore.getState().toast("ok", t("editor.built"), ack.message);
      rememberRecent({ kind: "workcell", title: draft.name });
    }
  };

  return (
    <div>
      <div className="section">
        <div className="section-title">
          <span className="grow">{t("editor.document")}</span>
          {dirty && <span className="badge warn">{t("editor.unsaved")}</span>}
        </div>
        <label className="field">
          <span className="field-label">{t("editor.name")}</span>
          <input className="input" value={draft.name} maxLength={80} onChange={(e) => update({ name: e.target.value })} />
        </label>
        <div className="grid-2">
          <label className="field">
            <span className="field-label">{t("editor.cell")}</span>
            <select className="select" value={draft.cell} onChange={(e) => update({ cell: e.target.value })}>
              {catalog.cells.map((c) => <option key={c}>{c}</option>)}
            </select>
          </label>
          <label className="field">
            <span className="field-label">{t("editor.robot")}</span>
            <select className="select" value={draft.robot?.model ?? ""} onChange={(e) => update({ robot: e.target.value ? { model: e.target.value, at: null } : null })}>
              <option value="">{t("editor.noRobot")}</option>
              {catalog.robots.map((r) => <option key={r}>{r}</option>)}
            </select>
          </label>
        </div>
        <label className="field" style={{ marginBottom: 4 }}>
          <span className="field-label">{t("editor.physics")}</span>
          <select className="select" value={draft.physics} onChange={(e) => update({ physics: e.target.value as WorkcellDoc["physics"] })}>
            {Object.entries(catalog.presets).map(([key, p]) => (
              <option key={key} value={key}>{p.label}</option>
            ))}
          </select>
        </label>
        {preset && <p className="muted" style={{ fontSize: 11, margin: "0 0 4px" }}>{preset.summary}</p>}
      </div>

      <div className="section">
        <div className="section-title">{t("editor.library")}</div>
        {groups.map(([group, kinds]) => (
          <div key={group} style={{ marginBottom: 8 }}>
            <div className="field-label" style={{ marginBottom: 4 }}>{group}</div>
            <div className="grid-2">
              {kinds.map((kind) => {
                const allowed = preset?.kinds.includes(kind) ?? false;
                return (
                  <button
                    key={kind}
                    type="button"
                    className="lib-item"
                    disabled={!allowed}
                    title={allowed ? t("editor.addHint") : t("editor.notInPreset", { preset: preset?.label ?? "" })}
                    onClick={() => {
                      const obj = freshObject(draft, kind);
                      setDraft({ ...draft, objects: [...draft.objects, obj] });
                      select(`draft:${obj.id}`);
                    }}
                  >
                    <span className="glyph">{ICONS[kind]}</span>
                    <span>
                      {catalog.kinds[kind].label}
                      <small>{allowed ? t("editor.add") : t("editor.unavailable")}</small>
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("editor.objects", { n: draft.objects.length })}</span>
        </div>
        <ul className="tree" style={{ margin: "0 calc(-1 * var(--pad))" }}>
          {draft.objects.map((o) => (
            <li key={o.id}>
              <button type="button" className="tree-row" aria-selected={selected === `draft:${o.id}`} onClick={() => select(`draft:${o.id}`)}>
                {ICONS[o.kind]}
                <span className="label">{o.name}</span>
                <span className="meta">{o.kind}</span>
              </button>
            </li>
          ))}
          {!draft.objects.length && <li className="tree-empty">{t("editor.noObjects")}</li>}
        </ul>
      </div>

      {editing && (
        <div className="section">
          <div className="section-title">
            <span className="grow">{editing.name}</span>
            <button
              type="button"
              className="icon-btn sm"
              aria-label={t("editor.duplicate")}
              title={t("editor.duplicate")}
              onClick={() => {
                const copy = { ...structuredClone(editing), id: uid(), name: uniqueName(draft, editing.name), at: [editing.at[0] + 0.06, editing.at[1], editing.at[2]] as [number, number, number] };
                setDraft({ ...draft, objects: [...draft.objects, copy] });
                select(`draft:${copy.id}`);
              }}
            >
              <Copy size={13} />
            </button>
            <button
              type="button"
              className="icon-btn sm"
              aria-label={t("editor.remove")}
              title={t("editor.remove")}
              onClick={() => {
                setDraft({ ...draft, objects: draft.objects.filter((o) => o.id !== editing.id) });
                select(null);
              }}
            >
              <Trash2 size={13} />
            </button>
          </div>
          <label className="field">
            <span className="field-label">{t("editor.objName")}</span>
            <input className="input" value={editing.name} onChange={(e) => updateObject(editing.id, { name: e.target.value.replace(/[^A-Za-z0-9_]/g, "_") })} />
          </label>
          <Vec label={t("editor.position")} values={editing.at} unit="mm" scale={1000} onCommit={(v) => updateObject(editing.id, { at: v as [number, number, number] })} />
          <Vec label={t("editor.rotation")} values={editing.rpy} unit="°" scale={1} onCommit={(v) => updateObject(editing.id, { rpy: v as [number, number, number] })} />
          {editing.half && (editing.kind === "box" || editing.kind === "slab") && (
            <Vec label={t("editor.halfExtents")} values={editing.half} unit="mm" scale={1000} onCommit={(v) => updateObject(editing.id, { half: v as [number, number, number] })} />
          )}
          {editing.radius !== undefined && editing.radius !== null && (
            <div className="grid-2">
              <label className="field">
                <span className="field-label">{t("editor.radius")} (mm)</span>
                <NumberField label={t("editor.radius")} value={editing.radius * 1000} digits={1} onCommit={(v) => updateObject(editing.id, { radius: v / 1000 })} />
              </label>
              {editing.halfHeight !== undefined && editing.halfHeight !== null && (
                <label className="field">
                  <span className="field-label">{t("editor.halfHeight")} (mm)</span>
                  <NumberField label={t("editor.halfHeight")} value={editing.halfHeight * 1000} digits={1} onCommit={(v) => updateObject(editing.id, { halfHeight: v / 1000 })} />
                </label>
              )}
            </div>
          )}
          {editing.size && (
            <Vec label={t("editor.size")} values={editing.size} unit="mm" scale={1000} onCommit={(v) => updateObject(editing.id, { size: v })} />
          )}
          {editing.cells && (
            <Vec label={t("editor.cells")} values={editing.cells} unit="#" scale={1} step={1} onCommit={(v) => updateObject(editing.id, { cells: v.map((x) => Math.round(x)) })} />
          )}
          {editing.kind === "rope" && (
            <div className="grid-2">
              <label className="field">
                <span className="field-label">{t("editor.length")} (mm)</span>
                <NumberField label={t("editor.length")} value={(editing.length ?? 0.6) * 1000} digits={0} onCommit={(v) => updateObject(editing.id, { length: v / 1000 })} />
              </label>
              <label className="field">
                <span className="field-label">{t("editor.segments")}</span>
                <NumberField label={t("editor.segments")} value={editing.segments ?? 28} digits={0} onCommit={(v) => updateObject(editing.id, { segments: Math.round(v) })} />
              </label>
            </div>
          )}
          {["box", "sphere", "cylinder", "capsule"].includes(editing.kind) && (
            <div className="grid-2">
              <label className="field">
                <span className="field-label">{t("editor.mass")} (g)</span>
                <NumberField label={t("editor.mass")} value={(editing.mass ?? 0.1) * 1000} digits={0} onCommit={(v) => updateObject(editing.id, { mass: v / 1000 })} />
              </label>
              <label className="field">
                <span className="field-label">{t("editor.friction")}</span>
                <NumberField label={t("editor.friction")} value={editing.friction ?? null} digits={2} onCommit={(v) => updateObject(editing.id, { friction: v })} />
              </label>
            </div>
          )}
          {(editing.kind === "sheet" || editing.kind === "sponge") && (
            <label className="field">
              <span className="field-label">{t("editor.material")}</span>
              <select className="select" value={editing.material ?? ""} onChange={(e) => updateObject(editing.id, { material: e.target.value })}>
                {(catalog.materials[editing.kind] ?? []).map((m) => <option key={m}>{m}</option>)}
              </select>
            </label>
          )}
          <label className="field" style={{ marginBottom: 0 }}>
            <span className="field-label">{t("editor.colour")}</span>
            <input type="color" className="input" style={{ padding: 2 }} value={editing.color ?? "#999999"} onChange={(e) => updateObject(editing.id, { color: e.target.value })} />
          </label>
          <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("editor.gizmoHint")}</p>
        </div>
      )}

      <div className="section">
        <div className="section-title">
          <span className="grow">{t("editor.regions", { n: draft.regions.length })}</span>
          <button
            type="button"
            className="btn sm"
            onClick={() => setDraft({ ...draft, regions: [...draft.regions, { name: uniqueName(draft, "zone"), xy: [0.0, 0.08, 0.08, 0.16], z: [0, 0.1] }] })}
          >
            <Plus size={12} /> {t("editor.addRegion")}
          </button>
        </div>
        {draft.regions.map((r, k) => (
          <div key={k} className="field">
            <div className="row">
              <input className="input" style={{ width: 110 }} value={r.name} aria-label={t("editor.regionName")} onChange={(e) => setDraft({ ...draft, regions: draft.regions.map((x, j) => (j === k ? { ...x, name: e.target.value.replace(/[^A-Za-z0-9_]/g, "_") } : x)) })} />
              <span className="grow" />
              <button type="button" className="icon-btn sm" aria-label={t("editor.remove")} onClick={() => setDraft({ ...draft, regions: draft.regions.filter((_, j) => j !== k) })}>
                <Trash2 size={12} />
              </button>
            </div>
            <Vec label={t("editor.regionXY")} values={[...r.xy]} unit="mm" scale={1000} onCommit={(v) => setDraft({ ...draft, regions: draft.regions.map((x, j) => (j === k ? { ...x, xy: v as [number, number, number, number] } : x)) })} />
          </div>
        ))}
      </div>

      <div className="section" style={{ position: "sticky", bottom: 0, background: "var(--bg-1)", borderTop: "1px solid var(--line)" }}>
        {check && !check.ok && (
          <div className="notice err" style={{ marginBottom: 8 }}>
            <span>{check.message}</span>
          </div>
        )}
        <div className="row">
          <button type="button" className="icon-btn" aria-label={t("editor.undo")} title={`${t("editor.undo")} (Ctrl+Z)`} disabled={!past.length} onClick={() => useStore.getState().undo()}>
            <Undo2 size={15} />
          </button>
          <button type="button" className="icon-btn" aria-label={t("editor.redo")} title={`${t("editor.redo")} (Ctrl+Y)`} disabled={!future.length} onClick={() => useStore.getState().redo()}>
            <Redo2 size={15} />
          </button>
          <button
            type="button"
            className="icon-btn"
            aria-label={t("editor.revert")}
            title={t("editor.revert")}
            onClick={() => {
              if (dirty && !window.confirm(t("editor.confirmRevert"))) return;
              void getJson<WorkcellDoc>("/api/workcell/current").then((doc) => setDraft(withIds(doc), { record: true, dirty: false }));
            }}
          >
            <RotateCcw size={15} />
          </button>
          <span className="grow" />
          <button type="button" className="btn primary" disabled={building || (check !== null && !check.ok)} onClick={() => void build()}>
            <Hammer size={14} /> {building ? t("editor.building") : t("editor.build")}
          </button>
        </div>
        <p className="muted" style={{ fontSize: 10.5, margin: "8px 0 0" }}>{t("editor.buildNote")}</p>
      </div>
    </div>
  );
}

export function withIds(doc: WorkcellDoc): WorkcellDoc {
  return { ...doc, objects: doc.objects.map((o) => ({ ...o, id: o.id || uid() })) };
}

export function stripIds(doc: WorkcellDoc): WorkcellDoc {
  const { savedAt: _savedAt, ...rest } = doc as WorkcellDoc & { savedAt?: number; savedBy?: string };
  delete (rest as { savedBy?: string }).savedBy;
  return rest;
}

export function rememberRecent(entry: { kind: "scenario" | "workcell"; id?: string; slug?: string; title: string }): void {
  try {
    const raw = localStorage.getItem("bridge.recent");
    const list = raw ? (JSON.parse(raw) as (typeof entry & { at: number })[]) : [];
    const next = [{ ...entry, at: Date.now() }, ...list.filter((e) => !(e.kind === entry.kind && e.title === entry.title))].slice(0, 8);
    localStorage.setItem("bridge.recent", JSON.stringify(next));
  } catch {
    /* ignore */
  }
}
