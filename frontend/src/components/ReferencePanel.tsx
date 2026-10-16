// SPDX-License-Identifier: LicenseRef-BRIDGE
// Real to sim: import a clip or a photo, cut stills at the moments that matter, and hold the
// scene against a still through the viewport's own camera. The measurement is the real2sim
// agent's (colour-keyed masks against segmentation): size, offset and overlap per keyed thing.
// The overlay sheet is always shown beside the numbers, because a key can miss.

import { Crop, FileUp, ImageIcon, Scale, Trash2 } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { getJson, sendJson } from "../api/rest";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { getEngine } from "./Viewport";

interface Still {
  t: number;
  file: string;
  label: string;
}
interface Comparison {
  at: number;
  still: string;
  simTime: number;
  score: number;
  stretch: number;
  objects: Record<string, { iou?: number | null; photo?: unknown; sim?: unknown; [k: string]: unknown }>;
  lines: string[];
  render: string;
  beside: string;
  look: string;
  seconds: number;
  keys: string[];
}
interface Reference {
  id: string;
  name: string;
  kind: "video" | "image";
  bytes: number;
  width?: number;
  height?: number;
  fps?: number;
  duration?: number;
  stills: Still[];
  comparisons: Comparison[];
}

export function ReferencePanel() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const toast = useStore((s) => s.toast);
  const [list, setList] = useState<Reference[]>([]);
  const [active, setActive] = useState<string | null>(null);
  const [still, setStill] = useState<string | null>(null);
  const [busy, setBusy] = useState<"" | "upload" | "cut" | "compare">("");
  const [keys, setKeys] = useState("");
  const [result, setResult] = useState<Comparison | null>(null);
  const video = useRef<HTMLVideoElement>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const refresh = useCallback(async () => {
    try {
      const items = await getJson<Reference[]>("/api/reference");
      setList(items);
      if (!active && items[0]) setActive(items[0].id);
    } catch {
      /* the list stays as it was */
    }
  }, [active]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const ref = list.find((r) => r.id === active) ?? null;
  useEffect(() => {
    setStill(ref?.stills[0]?.file ?? null);
    setResult(ref?.comparisons[ref.comparisons.length - 1] ?? null);
    if (ref?.comparisons.length) setKeys(ref.comparisons[ref.comparisons.length - 1].keys.join("\n"));
  }, [ref?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const upload = async (file: File) => {
    setBusy("upload");
    try {
      const response = await fetch(`/api/reference/upload?name=${encodeURIComponent(file.name)}`, { method: "PUT", body: file });
      if (!response.ok) throw new Error((await response.json().catch(() => ({}))).detail ?? response.statusText);
      const meta = (await response.json()) as Reference;
      await refresh();
      setActive(meta.id);
      toast("ok", t("ref.imported"), meta.name);
    } catch (error) {
      toast("err", t("ref.importFailed"), error instanceof Error ? error.message : String(error));
    } finally {
      setBusy("");
    }
  };

  const cut = async () => {
    if (!ref || !video.current) return;
    setBusy("cut");
    try {
      const meta = await sendJson<Reference>(`/api/reference/${ref.id}/stills`, "POST", { times: [video.current.currentTime] });
      setList(list.map((r) => (r.id === meta.id ? meta : r)));
      const latest = meta.stills.reduce((a, b) => (Math.abs(b.t - (video.current?.currentTime ?? 0)) < Math.abs(a.t - (video.current?.currentTime ?? 0)) ? b : a));
      setStill(latest.file);
    } catch (error) {
      toast("err", t("ref.cutFailed"), error instanceof Error ? error.message : String(error));
    } finally {
      setBusy("");
    }
  };

  const compare = async () => {
    if (!ref || !still) return;
    const view = getEngine()?.cameraState();
    if (!view) return;
    setBusy("compare");
    try {
      const record = await sendJson<Comparison>(`/api/reference/${ref.id}/compare`, "POST", {
        still,
        view,
        keys: keys.split(/\n|;/).map((k) => k.trim()).filter(Boolean),
      });
      setResult(record);
      await refresh();
    } catch (error) {
      toast("err", t("ref.compareFailed"), error instanceof Error ? error.message : String(error));
    } finally {
      setBusy("");
    }
  };

  const remove = async () => {
    if (!ref || !window.confirm(t("ref.confirmDelete", { name: ref.name }))) return;
    await sendJson(`/api/reference/${ref.id}`, "DELETE");
    setActive(null);
    await refresh();
  };

  return (
    <div style={{ display: "grid", gridTemplateColumns: "220px minmax(0,1.2fr) minmax(0,1fr)", gap: 1, background: "var(--line)", height: "100%" }}>
      <div style={{ background: "var(--bg-1)", overflow: "auto" }}>
        <div className="section">
          <div className="section-title">{t("ref.media")}</div>
          <input
            ref={fileInput}
            type="file"
            accept="video/*,image/*,.mp4,.mov,.webm,.mkv,.avi,.png,.jpg,.jpeg,.webp"
            style={{ display: "none" }}
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) void upload(file);
              e.target.value = "";
            }}
          />
          <button type="button" className="btn sm primary block" onClick={() => fileInput.current?.click()} disabled={busy === "upload"}>
            <FileUp size={13} /> {busy === "upload" ? t("ref.importing") : t("ref.import")}
          </button>
          <p className="muted" style={{ fontSize: 10.5, margin: "6px 0 0" }}>{t("ref.importHint")}</p>
        </div>
        <ul className="tree">
          {list.map((r) => (
            <li key={r.id}>
              <button type="button" className="tree-row" aria-selected={r.id === active} onClick={() => setActive(r.id)}>
                <ImageIcon size={13} />
                <span className="label">{r.name}</span>
                <span className="meta">{r.kind === "video" ? `${(r.duration ?? 0).toFixed(1)}s` : `${r.width}×${r.height}`}</span>
              </button>
            </li>
          ))}
          {!list.length && <li className="tree-empty">{t("ref.none")}</li>}
        </ul>
      </div>

      <div style={{ background: "var(--bg-1)", overflow: "auto", padding: 10, display: "grid", gap: 8, alignContent: "start" }}>
        {ref ? (
          <>
            <div className="row">
              <b style={{ fontSize: 12.5 }}>{ref.name}</b>
              <span className="mono muted" style={{ fontSize: 10.5 }}>
                {ref.width}×{ref.height}
                {ref.kind === "video" ? ` · ${ref.fps?.toFixed(1)} fps · ${ref.duration?.toFixed(2)} s` : ""}
              </span>
              <span className="grow" />
              <button type="button" className="icon-btn sm" aria-label={t("ref.delete")} onClick={() => void remove()}>
                <Trash2 size={13} />
              </button>
            </div>
            {ref.kind === "video" ? (
              <video ref={video} src={`/api/reference/${ref.id}/media`} controls style={{ width: "100%", maxHeight: 260, background: "#000" }} />
            ) : (
              <img src={`/api/reference/${ref.id}/media`} alt={ref.name} style={{ width: "100%", maxHeight: 260, objectFit: "contain", background: "#000" }} />
            )}
            {ref.kind === "video" && (
              <button type="button" className="btn sm" onClick={() => void cut()} disabled={busy === "cut"}>
                <Crop size={13} /> {t("ref.cut")}
              </button>
            )}
            <div className="row wrap" style={{ gap: 6 }}>
              {ref.stills.map((s) => (
                <button
                  key={s.file}
                  type="button"
                  onClick={() => setStill(s.file)}
                  style={{ padding: 0, border: `2px solid ${s.file === still ? "var(--accent)" : "var(--line)"}`, background: "none", cursor: "pointer", width: 110 }}
                  aria-pressed={s.file === still}
                  aria-label={t("ref.still", { t: s.t.toFixed(2) })}
                >
                  <img src={`/api/reference/${ref.id}/still/${s.file}`} alt="" style={{ width: "100%", display: "block" }} />
                  <span className="mono" style={{ fontSize: 10, display: "block", padding: 2 }}>{s.label || `${s.t.toFixed(2)} s`}</span>
                </button>
              ))}
            </div>
          </>
        ) : (
          <div className="empty">{t("ref.pick")}</div>
        )}
      </div>

      <div style={{ background: "var(--bg-1)", overflow: "auto", padding: 10, display: "grid", gap: 8, alignContent: "start" }}>
        <div className="section-title" style={{ margin: 0 }}>{t("ref.compare")}</div>
        <label className="field" style={{ marginBottom: 0 }}>
          <span className="field-label">{t("ref.keys")}</span>
          <textarea
            className="input"
            style={{ height: 64, paddingBlock: 6, fontFamily: "var(--font-mono)", fontSize: 11 }}
            value={keys}
            placeholder={manifest?.subjects.filter((s) => s.kind === "object").map((s) => `${s.name}=orange`).slice(0, 2).join("\n") || "cube=orange"}
            onChange={(e) => setKeys(e.target.value)}
          />
        </label>
        <button type="button" className="btn sm primary" disabled={!ref || !still || busy === "compare" || !manifest} onClick={() => void compare()}>
          <Scale size={13} /> {busy === "compare" ? t("ref.comparing") : t("ref.compareNow")}
        </button>
        <p className="muted" style={{ fontSize: 10.5, margin: 0 }}>{t("ref.compareHint")}</p>
        {result && ref && (
          <div style={{ display: "grid", gap: 8 }}>
            <div className="row">
              <span className="badge accent">{t("ref.meanIou")} {result.score.toFixed(3)}</span>
              <span className="mono muted" style={{ fontSize: 10.5 }}>t = {result.simTime.toFixed(2)} s · {result.seconds.toFixed(1)} s</span>
            </div>
            {Object.keys(result.objects).length > 0 && (
              <table className="data">
                <thead>
                  <tr>
                    <th>{t("ref.thing")}</th>
                    <th>IoU</th>
                    <th>{t("ref.found")}</th>
                  </tr>
                </thead>
                <tbody>
                  {Object.entries(result.objects).map(([name, o]) => (
                    <tr key={name}>
                      <td className="mono">{name}</td>
                      <td className="mono">{o.iou === null || o.iou === undefined ? "—" : Number(o.iou).toFixed(3)}</td>
                      <td className="mono">{o.photo ? t("ref.inPhoto") : t("ref.notInPhoto")} / {o.sim ? t("ref.inRender") : t("ref.notInRender")}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <ul style={{ margin: 0, paddingInlineStart: 16, fontSize: 11.5, color: "var(--text-1)" }}>
              {result.lines.map((line) => <li key={line}>{line}</li>)}
            </ul>
            <a href={`/api/reference/${ref.id}/artifact/${result.beside}`} target="_blank" rel="noreferrer">
              <img src={`/api/reference/${ref.id}/artifact/${result.beside}`} alt={t("ref.besideAlt")} style={{ width: "100%", border: "1px solid var(--line)" }} />
            </a>
            <a href={`/api/reference/${ref.id}/artifact/${result.look}`} target="_blank" rel="noreferrer" className="muted" style={{ fontSize: 11 }}>
              {t("ref.lookSheet")}
            </a>
            <div className="notice">
              <span>{t("ref.assumptions")}</span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
