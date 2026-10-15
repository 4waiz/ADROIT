// SPDX-License-Identifier: Apache-2.0
// The record of the session: the host's event log, every command with who sent it and how it
// ended, and a timeline that places both on simulated time.

import { useMemo, useState } from "react";
import type { CommandRecord, EventRecord } from "../api/types";
import { useT } from "../i18n";
import { useStore } from "../state/store";
import { Segmented } from "./ui/primitives";

const clock = (wall: number) => new Date(wall * 1000).toLocaleTimeString([], { hour12: false });

const LEVEL: Record<EventRecord["level"], string> = { info: "info", warning: "warn", error: "err", success: "ok" };
const STATUS: Record<string, string> = { completed: "ok", accepted: "info", rejected: "warn", failed: "err", interrupted: "warn", pending: "" };

export function EventsPanel() {
  const t = useT();
  const events = useStore((s) => s.events);
  const [level, setLevel] = useState<"all" | "warning" | "error">("all");
  const [query, setQuery] = useState("");
  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return [...events]
      .reverse()
      .filter((e) => (level === "all" ? true : level === "warning" ? e.level !== "info" && e.level !== "success" : e.level === "error"))
      .filter((e) => !q || `${e.source} ${e.message}`.toLowerCase().includes(q));
  }, [events, level, query]);
  return (
    <div style={{ display: "flex", flexDirection: "column", minHeight: 0 }}>
      <div className="row" style={{ padding: "6px 10px", borderBottom: "1px solid var(--line)" }}>
        <Segmented
          label={t("events.filter")}
          value={level}
          onChange={setLevel}
          options={[
            { value: "all", label: t("events.all") },
            { value: "warning", label: t("events.warnings") },
            { value: "error", label: t("events.errors") },
          ]}
        />
        <input className="input" style={{ maxWidth: 260 }} placeholder={t("events.search")} aria-label={t("events.search")} value={query} onChange={(e) => setQuery(e.target.value)} />
        <span className="grow" />
        <span className="muted mono" style={{ fontSize: 11 }}>{rows.length} / {events.length}</span>
      </div>
      <div className="table-wrap" style={{ flex: 1 }}>
        <table className="data">
          <thead>
            <tr>
              <th>{t("events.time")}</th>
              <th>{t("events.simTime")}</th>
              <th>{t("events.level")}</th>
              <th>{t("events.source")}</th>
              <th style={{ width: "60%" }}>{t("events.message")}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((e) => (
              <tr key={e.seq}>
                <td className="mono">{clock(e.wall)}</td>
                <td className="mono">{e.simTime !== null ? `${e.simTime.toFixed(2)} s` : "—"}</td>
                <td><span className={`badge ${LEVEL[e.level]}`}>{t(`level.${e.level}` as "level.info")}</span></td>
                <td className="mono">{e.source}</td>
                <td>{e.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {!rows.length && <div className="empty">{t("events.empty")}</div>}
      </div>
    </div>
  );
}

function describe(args: Record<string, unknown>): string {
  return Object.entries(args)
    .filter(([k]) => k !== "generation")
    .map(([k, v]) => `${k}=${Array.isArray(v) ? `[${v.join(", ")}]` : String(v)}`)
    .join(" ");
}

export function CommandsPanel() {
  const t = useT();
  const commands = useStore((s) => s.commands);
  const clientId = useStore((s) => s.clientId);
  const rows = useMemo(() => [...commands].reverse(), [commands]);
  return (
    <div className="table-wrap">
      <table className="data">
        <thead>
          <tr>
            <th>{t("commands.time")}</th>
            <th>{t("commands.source")}</th>
            <th>{t("commands.command")}</th>
            <th>{t("commands.target")}</th>
            <th>{t("commands.args")}</th>
            <th>{t("commands.status")}</th>
            <th>{t("commands.duration")}</th>
            <th style={{ width: "34%" }}>{t("commands.result")}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((c: CommandRecord) => (
            <tr key={c.id}>
              <td className="mono">{clock(c.issuedAt)}</td>
              <td className="mono">{c.client === clientId ? t("commands.thisPage") : c.client || "—"}</td>
              <td className="mono">{c.name}</td>
              <td className="mono">{c.target || "—"}</td>
              <td className="mono" style={{ maxWidth: 260, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={describe(c.args)}>
                {describe(c.args)}
              </td>
              <td><span className={`badge ${STATUS[c.status] ?? ""}`}>{t(`ack.${c.status}` as "ack.completed")}</span></td>
              <td className="mono">{c.finishedAt ? `${Math.max(0, (c.finishedAt - c.issuedAt) * 1000).toFixed(0)} ms` : "…"}</td>
              <td>{c.message}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {!rows.length && <div className="empty">{t("commands.empty")}</div>}
    </div>
  );
}

export function TimelinePanel() {
  const t = useT();
  const events = useStore((s) => s.events);
  const status = useStore((s) => s.status);
  const telemetryT = useStore((s) => s.telemetry?.t ?? 0);
  const generationEvents = events.filter((e) => e.simTime !== null);
  const now = Math.max(telemetryT, status?.simTime ?? 0, 1);
  const span = Math.max(10, now * 1.05);
  const [hover, setHover] = useState<EventRecord | null>(null);
  const runner = status?.runner ?? status?.lastRunner;
  return (
    <div style={{ padding: "14px 16px", display: "grid", gap: 14, alignContent: "start", overflow: "auto" }}>
      <div className="row" style={{ justifyContent: "space-between" }}>
        <span className="section-title" style={{ margin: 0 }}>{t("timeline.title")}</span>
        <span className="mono muted" style={{ fontSize: 11 }}>
          t = {now.toFixed(2)} s · {t("timeline.events", { n: generationEvents.length })}
        </span>
      </div>
      <div style={{ position: "relative", height: 54, borderTop: "1px solid var(--line-strong)", borderBottom: "1px solid var(--line)" }}>
        {Array.from({ length: 11 }, (_, k) => (
          <div key={k} style={{ position: "absolute", insetInlineStart: `${k * 10}%`, top: 0, bottom: 0, borderInlineStart: "1px dashed var(--line)" }}>
            <span className="mono muted" style={{ position: "absolute", bottom: -16, fontSize: 9.5, transform: "translateX(-50%)" }}>{((span * k) / 10).toFixed(0)}s</span>
          </div>
        ))}
        <div style={{ position: "absolute", top: 0, bottom: 0, insetInlineStart: 0, width: `${(now / span) * 100}%`, background: "var(--accent-soft)" }} />
        <div style={{ position: "absolute", top: -3, bottom: -3, insetInlineStart: `${(now / span) * 100}%`, width: 2, background: "var(--accent)" }} title={t("timeline.now")} />
        {generationEvents.map((e) => (
          <button
            key={e.seq}
            type="button"
            aria-label={`${e.simTime?.toFixed(2)} s: ${e.message}`}
            onMouseEnter={() => setHover(e)}
            onFocus={() => setHover(e)}
            onMouseLeave={() => setHover(null)}
            style={{
              position: "absolute",
              insetInlineStart: `calc(${((e.simTime ?? 0) / span) * 100}% - 4px)`,
              top: e.level === "info" ? 30 : e.level === "success" ? 6 : 18,
              width: 8,
              height: 8,
              padding: 0,
              border: 0,
              borderRadius: 2,
              transform: "rotate(45deg)",
              cursor: "pointer",
              background: `var(--${LEVEL[e.level] === "err" ? "err" : LEVEL[e.level] === "warn" ? "warn" : LEVEL[e.level] === "ok" ? "ok" : "text-3"})`,
            }}
          />
        ))}
      </div>
      <div style={{ minHeight: 34, fontSize: 12, color: "var(--text-1)", paddingTop: 8 }}>
        {hover ? (
          <>
            <span className="mono">{hover.simTime?.toFixed(3)} s</span> · <b>{hover.source}</b> · {hover.message}
          </>
        ) : (
          <span className="muted">{t("timeline.hint")}</span>
        )}
      </div>
      {runner && runner.plan?.length > 0 && (
        <div>
          <div className="section-title">{t("timeline.plan", { name: runner.name })}</div>
          <div className="row" style={{ gap: 2 }}>
            {runner.plan.map((step) => (
              <div
                key={step.index}
                title={`${step.index + 1}. ${step.label}${step.seconds ? ` (${step.seconds.toFixed(2)} s)` : ""}`}
                style={{
                  flex: Math.max(0.6, step.seconds ?? 1),
                  height: 10,
                  borderRadius: 2,
                  background:
                    step.status === "done" ? "var(--ok)" : step.status === "running" ? "var(--accent)" : step.status === "failed" ? "var(--err)" : "var(--bg-4)",
                }}
              />
            ))}
          </div>
        </div>
      )}
      <p className="muted" style={{ margin: 0, fontSize: 11 }}>{t("timeline.note")}</p>
    </div>
  );
}
