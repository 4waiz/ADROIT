// SPDX-License-Identifier: LicenseRef-BRIDGE
// What can be launched: the scenario gallery (built-in, each with its physics, robot, success
// measure and availability on this machine), saved workcells, and what was opened recently.

import { AlertTriangle, CheckCircle2, Clock, FolderOpen, Play, RotateCcw, Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { bridge } from "../api/socket";
import { getJson, sendJson } from "../api/rest";
import type { SavedWorkcell, ScenarioInfo, WorkcellDoc } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { Dialog } from "./ui/primitives";
import { rememberRecent, stripIds, withIds } from "./WorkcellEditor";

export function useScenarios() {
  const [list, setList] = useState<ScenarioInfo[]>([]);
  const [error, setError] = useState("");
  const load = useCallback(() => {
    getJson<ScenarioInfo[]>("/api/scenarios")
      .then(setList)
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
  }, []);
  useEffect(load, [load]);
  return { list, error, reload: load };
}

function confirmLeave(t: ReturnType<typeof useT>): boolean {
  const s = useStore.getState();
  if (s.draftDirty && !window.confirm(t("scenarios.confirmDirty"))) return false;
  if (s.status?.runner && !window.confirm(t("scenarios.confirmRunner", { name: s.status.runner.name }))) return false;
  return true;
}

export async function launchScenario(id: string, title: string, t: ReturnType<typeof useT>): Promise<void> {
  if (!confirmLeave(t)) return;
  const ack = await bridge.command("scenario.load", { id }, { terminal: true });
  if (ack.status === "completed") {
    rememberRecent({ kind: "scenario", id, title });
    useStore.getState().toast("ok", t("scenarios.loaded"), ack.message);
  }
}

function ScenarioCard({ s, current, compact }: { s: ScenarioInfo; current: boolean; compact?: boolean }) {
  const t = useT();
  const building = useStore((st) => st.status?.phase === "building");
  const simple = useStore((st) => st.uiMode === "simple");
  const title = t(`scenario.${s.id}.title` as "scenario.pick_place.title") || s.title;
  return (
    <article className={`card${current ? " current" : ""}`}>
      {!compact && (
        <div className="card-media">
          {s.media ? <img src={s.media} alt="" loading="lazy" /> : <span className="mono">{s.robot}</span>}
          <span className="tag badge accent">{t(`category.${s.category}` as "category.manipulation")}</span>
        </div>
      )}
      <div className="card-body">
        <h3>{title.startsWith("scenario.") ? s.title : title}</h3>
        <p>{s.summary}</p>
        <dl className="kv" style={{ fontSize: 11 }}>
          {!simple && (
            <>
              <dt>{t("scenarios.robot")}</dt>
              <dd className="mono">{s.robot}</dd>
              <dt>{t("scenarios.physics")}</dt>
              <dd>{s.physics}</dd>
            </>
          )}
          {s.successLabel && (
            <>
              <dt>{t("scenarios.success")}</dt>
              <dd>{s.successLabel}</dd>
            </>
          )}
        </dl>
        {!s.available && (
          <div className="notice warn">
            <AlertTriangle size={14} />
            <span>{s.unavailableReason}</span>
          </div>
        )}
      </div>
      <div className="card-foot">
        {s.available ? (
          <span className="badge ok"><CheckCircle2 size={10} /> {t("scenarios.available")}</span>
        ) : (
          <span className="badge warn">{t("scenarios.unavailable")}</span>
        )}
        <span className="grow" />
        {current && (
          <button type="button" className="btn sm" onClick={() => void bridge.command("sim.reset")} disabled={building}>
            <RotateCcw size={12} /> {t("scenarios.reset")}
          </button>
        )}
        <button type="button" className="btn sm primary" disabled={!s.available || building} onClick={() => void launchScenario(s.id, s.title, t)}>
          <Play size={12} /> {current ? t("scenarios.relaunch") : t("scenarios.launch")}
        </button>
      </div>
    </article>
  );
}

export function ScenarioList() {
  const t = useT();
  const { list, error } = useScenarios();
  const current = useStore((s) => (s.status?.scenario && "id" in s.status.scenario ? s.status.scenario.id : ""));
  const [saved, setSaved] = useState<SavedWorkcell[]>([]);
  const recent = readRecent();
  useEffect(() => {
    void getJson<SavedWorkcell[]>("/api/workcells").then(setSaved).catch(() => setSaved([]));
  }, []);
  return (
    <div>
      <div className="section">
        <div className="section-title">{t("scenarios.gallery")}</div>
        {error && <div className="notice err">{error}</div>}
        <div style={{ display: "grid", gap: 10 }}>
          {list.map((s) => <ScenarioCard key={s.id} s={s} current={s.id === current} compact />)}
        </div>
      </div>
      <div className="section">
        <div className="section-title">{t("scenarios.saved")}</div>
        {saved.length ? (
          <ul className="tree" style={{ margin: "0 calc(-1 * var(--pad))" }}>
            {saved.map((w) => (
              <li key={w.slug}>
                <button type="button" className="tree-row" onClick={() => void openWorkcell(w.slug, t)}>
                  <FolderOpen size={13} />
                  <span className="label">{w.name}</span>
                  <span className="meta">{w.objects} obj</span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted" style={{ fontSize: 12, margin: 0 }}>{t("scenarios.noSaved")}</p>
        )}
      </div>
      <div className="section">
        <div className="section-title">{t("scenarios.recent")}</div>
        {recent.length ? (
          <ul className="tree" style={{ margin: "0 calc(-1 * var(--pad))" }}>
            {recent.map((r) => (
              <li key={`${r.kind}:${r.title}:${r.at}`}>
                <button
                  type="button"
                  className="tree-row"
                  onClick={() => {
                    if (r.kind === "scenario" && r.id) void launchScenario(r.id, r.title, t);
                    else if (r.slug) void openWorkcell(r.slug, t);
                  }}
                  disabled={r.kind === "workcell" && !r.slug}
                >
                  <Clock size={13} />
                  <span className="label">{r.title}</span>
                  <span className="meta">{new Date(r.at).toLocaleDateString()}</span>
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted" style={{ fontSize: 12, margin: 0 }}>{t("scenarios.noRecent")}</p>
        )}
      </div>
    </div>
  );
}

function readRecent(): { kind: "scenario" | "workcell"; id?: string; slug?: string; title: string; at: number }[] {
  try {
    return JSON.parse(localStorage.getItem("bridge.recent") ?? "[]");
  } catch {
    return [];
  }
}

export async function openWorkcell(slug: string, t: ReturnType<typeof useT>): Promise<void> {
  if (!confirmLeave(t)) return;
  try {
    const doc = await getJson<WorkcellDoc>(`/api/workcells/${slug}`);
    useStore.getState().setDraft(withIds(doc), { record: true, dirty: false });
    const ack = await bridge.command("workcell.build", { doc: stripIds(doc) }, { terminal: true });
    if (ack.status === "completed") {
      rememberRecent({ kind: "workcell", slug, title: doc.name });
      useStore.getState().toast("ok", t("scenarios.opened", { name: doc.name }), ack.message);
    }
  } catch (error) {
    useStore.getState().toast("err", t("scenarios.openFailed"), error instanceof Error ? error.message : String(error));
  }
}

export function GalleryDialog() {
  const t = useT();
  const { list, error } = useScenarios();
  const current = useStore((s) => (s.status?.scenario && "id" in s.status.scenario ? s.status.scenario.id : ""));
  return (
    <Dialog title={t("scenarios.gallery")} onClose={() => useStore.getState().openDialog(null)} size="wide">
      <p className="muted" style={{ marginTop: 0 }}>{t("scenarios.galleryIntro")}</p>
      {error && <div className="notice err">{error}</div>}
      <div className="cards">
        {list.map((s) => <ScenarioCard key={s.id} s={s} current={s.id === current} />)}
      </div>
    </Dialog>
  );
}

export function SaveDialog() {
  const t = useT();
  const draft = useStore((s) => s.draft);
  const status = useStore((s) => s.status);
  const [name, setName] = useState(draft?.name ?? status?.document?.name ?? "");
  const [busy, setBusy] = useState(false);
  const close = () => useStore.getState().openDialog(null);
  const doc = draft ?? (status?.document ? withIds(status.document) : null);
  const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 64) || "workcell";
  const save = async () => {
    if (!doc) return;
    setBusy(true);
    try {
      const body = { ...stripIds(doc), name: name || doc.name };
      await sendJson(`/api/workcells/${slug}`, "PUT", body);
      useStore.setState({ draftDirty: false });
      useStore.getState().toast("ok", t("save.saved"), `${slug}.json`);
      rememberRecent({ kind: "workcell", slug, title: body.name });
      close();
    } catch (error) {
      useStore.getState().toast("err", t("save.failed"), error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog
      title={t("save.title")}
      onClose={close}
      size="narrow"
      footer={
        <>
          <button type="button" className="btn" onClick={close}>{t("common.cancel")}</button>
          <button type="button" className="btn primary" onClick={() => void save()} disabled={!doc || busy}>{t("save.save")}</button>
        </>
      }
    >
      {doc ? (
        <>
          <label className="field">
            <span className="field-label">{t("editor.name")}</span>
            <input className="input" value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <p className="muted" style={{ fontSize: 12 }}>{t("save.where", { file: `outputs/bridge/workcells/${slug}.json` })}</p>
          <p className="muted" style={{ fontSize: 12 }}>{t("save.what", { n: doc.objects.length })}</p>
        </>
      ) : (
        <div className="notice">
          <span>{t("save.noDocument")}</span>
        </div>
      )}
    </Dialog>
  );
}

export function LoadDialog() {
  const t = useT();
  const [saved, setSaved] = useState<SavedWorkcell[] | null>(null);
  const close = () => useStore.getState().openDialog(null);
  const reload = () => void getJson<SavedWorkcell[]>("/api/workcells").then(setSaved).catch(() => setSaved([]));
  useEffect(reload, []);
  return (
    <Dialog title={t("load.title")} onClose={close}>
      {saved === null ? (
        <p className="muted">{t("common.loading")}</p>
      ) : saved.length === 0 ? (
        <p className="muted">{t("scenarios.noSaved")}</p>
      ) : (
        <table className="data">
          <thead>
            <tr>
              <th>{t("load.name")}</th>
              <th>{t("load.robot")}</th>
              <th>{t("load.objects")}</th>
              <th>{t("load.physics")}</th>
              <th>{t("load.saved")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {saved.map((w) => (
              <tr key={w.slug}>
                <td><b>{w.name}</b><div className="muted mono" style={{ fontSize: 10.5 }}>{w.slug}.json</div></td>
                <td className="mono">{w.robot || "—"}</td>
                <td className="mono">{w.objects}</td>
                <td className="mono">{w.physics}</td>
                <td className="mono">{new Date(w.savedAt * 1000).toLocaleString()}</td>
                <td>
                  <div className="row" style={{ justifyContent: "flex-end" }}>
                    <button type="button" className="btn sm primary" onClick={() => { close(); void openWorkcell(w.slug, t); }}>
                      {t("load.open")}
                    </button>
                    <button
                      type="button"
                      className="icon-btn sm"
                      aria-label={t("load.delete")}
                      onClick={async () => {
                        if (!window.confirm(t("load.confirmDelete", { name: w.name }))) return;
                        await sendJson(`/api/workcells/${w.slug}`, "DELETE");
                        reload();
                      }}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Dialog>
  );
}
