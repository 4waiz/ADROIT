// SPDX-License-Identifier: Apache-2.0
// The contextual inspector: whatever is selected, with what the simulator knows about it.
// An arm gets the control panel; an object its live pose, velocity and physical properties;
// a camera its spec and a path-traced view through it; nothing selected, the workcell itself.

import { AlertTriangle, Aperture, Info } from "lucide-react";
import { useState } from "react";
import type { Manifest } from "../api/types";
import { fmt, useT } from "../i18n";
import { live } from "../state/live";
import { useStore } from "../state/store";
import { ArmInspector } from "./ArmInspector";

function bodyPose(body: number): number[] | null {
  const frame = live.frame;
  if (!frame || body < 0 || body * 7 + 7 > frame.bodyQ.length) return null;
  return Array.from(frame.bodyQ.subarray(body * 7, body * 7 + 7));
}

function Pose({ pose }: { pose: number[] | null }) {
  const t = useT();
  if (!pose) return <dd>—</dd>;
  return (
    <>
      <dt>{t("inspect.position")}</dt>
      <dd className="mono">{pose.slice(0, 3).map((v) => `${(v * 1000).toFixed(1)}`).join("  ")} mm</dd>
      <dt>{t("inspect.orientation")}</dt>
      <dd className="mono">{pose.slice(3).map((v) => v.toFixed(4)).join("  ")}</dd>
    </>
  );
}

function ObjectInspector({ manifest, subjectKey }: { manifest: Manifest; subjectKey: string }) {
  const t = useT();
  useStore((s) => s.telemetry); // re-render with the frame clock
  const subject = manifest.subjects.find((s) => s.key === subjectKey);
  if (!subject) return null;
  const body = subject.bodies[0] ?? -1;
  const pose = bodyPose(body);
  const shapes = manifest.shapes.filter((s) => s.owner === subjectKey);
  const colliders = shapes.filter((s) => s.collide).length;
  const visuals = shapes.filter((s) => s.visible).length;
  const kinds = [...new Set(shapes.map((s) => s.type))].join(", ");
  const first = shapes.find((s) => s.visible) ?? shapes[0];
  const contacts = useStore.getState().telemetry?.contacts;
  const shapeSet = new Set(shapes.map((s) => s.i));
  const touching = contacts?.items.filter((c) => shapeSet.has(c[0]) || shapeSet.has(c[1])) ?? [];
  const force = touching.reduce((sum, c) => sum + Math.hypot(c[5], c[6], c[7]), 0);
  return (
    <div>
      <div className="section">
        <div className="section-title">
          <span className="grow">{subject.name}</span>
          <span className="badge">{subject.kind === "static" || subject.static ? t("inspect.fixed") : t("inspect.dynamic")}</span>
        </div>
        <dl className="kv">
          <dt>{t("inspect.kind")}</dt>
          <dd>{subject.specKind || subject.kind}{subject.category ? ` · ${subject.category}` : ""}</dd>
          {subject.mass !== undefined && (
            <>
              <dt>{t("inspect.mass")}</dt>
              <dd className="mono">{(subject.mass * 1000).toFixed(1)} g</dd>
            </>
          )}
          <dt>{t("inspect.geometry")}</dt>
          <dd>{kinds || "—"} · {t("inspect.shapeCounts", { visuals, colliders })}</dd>
          {first?.type === "box" && (
            <>
              <dt>{t("inspect.size")}</dt>
              <dd className="mono">{first.size.map((v) => (v * 2000).toFixed(1)).join(" × ")} mm</dd>
            </>
          )}
          {first?.roughness !== undefined && (
            <>
              <dt>{t("inspect.finish")}</dt>
              <dd className="mono">roughness {first.roughness} · metallic {first.metallic}</dd>
            </>
          )}
        </dl>
      </div>
      {body >= 0 && (
        <div className="section">
          <div className="section-title">
            <span className="grow">{t("inspect.liveState")}</span>
            <span className="badge info">{t("inspect.simulated")}</span>
          </div>
          <dl className="kv">
            <Pose pose={pose} />
            <dt>{t("inspect.contacts")}</dt>
            <dd className="mono">
              {contacts?.available ? `${touching.length} · ${force.toFixed(2)} N` : t("inspect.contactsUnavailable")}
            </dd>
          </dl>
        </div>
      )}
      <div className="section">
        <div className="section-title">{t("inspect.editing")}</div>
        <p className="muted" style={{ margin: 0, fontSize: 11.5 }}>{t("inspect.editingNote")}</p>
      </div>
    </div>
  );
}

function ClothInspector({ manifest, subjectKey }: { manifest: Manifest; subjectKey: string }) {
  const t = useT();
  useStore((s) => s.telemetry);
  const subject = manifest.subjects.find((s) => s.key === subjectKey);
  const info = manifest.deformables.find((d) => d.owner === subjectKey);
  const frame = live.frame;
  let centroid: number[] | null = null;
  let low = Number.NaN;
  if (info && frame && frame.particles.length >= (info.offset + info.count) * 3) {
    let x = 0, y = 0, z = 0;
    low = Number.POSITIVE_INFINITY;
    for (let i = 0; i < info.count; i++) {
      const o = (info.offset + i) * 3;
      x += frame.particles[o];
      y += frame.particles[o + 1];
      z += frame.particles[o + 2];
      low = Math.min(low, frame.particles[o + 2]);
    }
    centroid = [x / info.count, y / info.count, z / info.count];
  }
  return (
    <div className="section">
      <div className="section-title">
        <span className="grow">{subject?.name}</span>
        <span className="badge">{info?.kind === "tube" ? t("inspect.cable") : t("inspect.deformable")}</span>
      </div>
      <dl className="kv">
        <dt>{t("inspect.particles")}</dt>
        <dd className="mono">{info?.count ?? subject?.particles ?? "—"}</dd>
        {info?.thickness ? (
          <>
            <dt>{t("inspect.thickness")}</dt>
            <dd className="mono">{(info.thickness * 1000).toFixed(1)} mm</dd>
          </>
        ) : null}
        {info?.radius ? (
          <>
            <dt>{t("inspect.radius")}</dt>
            <dd className="mono">{(info.radius * 1000).toFixed(1)} mm</dd>
          </>
        ) : null}
        <dt>{t("inspect.centroid")}</dt>
        <dd className="mono">{centroid ? centroid.map((v) => (v * 1000).toFixed(1)).join("  ") + " mm" : "—"}</dd>
        <dt>{t("inspect.lowest")}</dt>
        <dd className="mono">{Number.isFinite(low) ? `${(low * 1000).toFixed(1)} mm` : "—"}</dd>
      </dl>
    </div>
  );
}

function RegionInspector({ manifest, subjectKey }: { manifest: Manifest; subjectKey: string }) {
  const t = useT();
  const region = manifest.regions.find((r) => r.key === subjectKey);
  if (!region) return null;
  const [x0, y0, x1, y1] = region.xy;
  return (
    <div className="section">
      <div className="section-title">
        <span className="grow">{region.name}</span>
        <span className="badge accent">{t("inspect.region")}</span>
      </div>
      <dl className="kv">
        <dt>{t("inspect.frame")}</dt>
        <dd>{region.frame || t("inspect.workSurface")}</dd>
        <dt>{t("inspect.extent")}</dt>
        <dd className="mono">{((x1 - x0) * 1000).toFixed(0)} × {((y1 - y0) * 1000).toFixed(0)} mm</dd>
        <dt>{t("inspect.height")}</dt>
        <dd className="mono">{(region.z[0] * 1000).toFixed(0)}–{(region.z[1] * 1000).toFixed(0)} mm</dd>
        <dt>{t("inspect.centre")}</dt>
        <dd className="mono">{[region.origin[0] + (x0 + x1) / 2, region.origin[1] + (y0 + y1) / 2].map((v) => (v * 1000).toFixed(1)).join("  ")} mm</dd>
      </dl>
    </div>
  );
}

function CameraInspector({ manifest, name }: { manifest: Manifest; name: string }) {
  const t = useT();
  const camera = manifest.cameras.find((c) => c.name === name);
  const [busy, setBusy] = useState(false);
  const [image, setImage] = useState<string | null>(null);
  const [note, setNote] = useState("");
  if (!camera) return null;
  const render = async () => {
    setBusy(true);
    try {
      const response = await fetch("/api/render", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ camera: camera.name }),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => ({}))).detail ?? response.statusText);
      setNote(`${response.headers.get("X-Bridge-Renderer")} · ${response.headers.get("X-Bridge-Seconds")} s`);
      setImage(URL.createObjectURL(await response.blob()));
    } catch (error) {
      setNote(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="section">
      <div className="section-title">
        <span className="grow">{camera.name}</span>
        <span className="badge">{t("inspect.camera")}</span>
      </div>
      <dl className="kv">
        <dt>{t("inspect.eye")}</dt>
        <dd className="mono">{camera.eye?.map((v) => v.toFixed(3)).join("  ") ?? t("inspect.mounted")}</dd>
        <dt>{t("inspect.lookAt")}</dt>
        <dd className="mono">{camera.lookAt?.map((v) => v.toFixed(3)).join("  ") ?? "—"}</dd>
        <dt>{t("inspect.fov")}</dt>
        <dd className="mono">{camera.fovY.toFixed(1)}°</dd>
        <dt>{t("inspect.resolution")}</dt>
        <dd className="mono">{camera.width} × {camera.height}</dd>
        {camera.parentBody >= 0 && (
          <>
            <dt>{t("inspect.mountedOn")}</dt>
            <dd>{manifest.bodies[camera.parentBody]?.label}</dd>
          </>
        )}
      </dl>
      <button type="button" className="btn sm" style={{ marginTop: 10 }} onClick={render} disabled={busy}>
        <Aperture size={13} /> {busy ? t("inspect.rendering") : t("inspect.renderThrough")}
      </button>
      {note && <p className="muted mono" style={{ fontSize: 10.5 }}>{note}</p>}
      {image && <img src={image} alt={t("inspect.renderAlt", { name: camera.name })} style={{ width: "100%", marginTop: 8, border: "1px solid var(--line)" }} />}
    </div>
  );
}

function LightInspector({ name }: { name: string }) {
  const t = useT();
  return (
    <div className="section">
      <div className="section-title">{t(`light.${name}` as "light.key")}</div>
      <div className="notice">
        <Info size={14} />
        <span>{t("inspect.lightNote")}</span>
      </div>
    </div>
  );
}

function WorkcellSummary({ manifest }: { manifest: Manifest }) {
  const t = useT();
  const status = useStore((s) => s.status);
  return (
    <div>
      <div className="section">
        <div className="section-title">{t("inspect.workcell")}</div>
        <h3 style={{ margin: "0 0 4px", fontSize: 15 }}>{manifest.scenario.title}</h3>
        <p className="muted" style={{ margin: "0 0 10px", fontSize: 12 }}>{manifest.scenario.summary}</p>
        <dl className="kv">
          <dt>{t("inspect.robot")}</dt>
          <dd>{manifest.scenario.robot || "—"}</dd>
          <dt>{t("inspect.cell")}</dt>
          <dd>{manifest.cell.name || "—"} · {t("inspect.surfaceAt", { z: (manifest.cell.surfaceZ * 1000).toFixed(0) })}</dd>
          <dt>{t("inspect.counts")}</dt>
          <dd className="mono">{manifest.counts.bodies} bodies · {manifest.counts.shapes} shapes · {manifest.counts.particles} particles</dd>
          {manifest.scenario.successLabel && (
            <>
              <dt>{t("inspect.goal")}</dt>
              <dd>{manifest.scenario.successLabel}</dd>
            </>
          )}
          {manifest.scenario.source && (
            <>
              <dt>{t("inspect.source")}</dt>
              <dd className="mono" style={{ fontSize: 11 }}>{manifest.scenario.source}</dd>
            </>
          )}
        </dl>
      </div>
      {(status?.notes ?? manifest.notes).length > 0 && (
        <div className="section">
          <div className="section-title">{t("inspect.howSimulated")}</div>
          {(status?.notes ?? manifest.notes).map((n) => (
            <p key={n} style={{ margin: "0 0 6px", fontSize: 12, color: "var(--text-1)" }}>{n}</p>
          ))}
        </div>
      )}
      {manifest.physics.problems.length > 0 && (
        <div className="section">
          {manifest.physics.problems.map((p) => (
            <div key={p} className="notice warn">
              <AlertTriangle size={14} />
              <span>{p}</span>
            </div>
          ))}
        </div>
      )}
      <div className="section">
        <p className="muted" style={{ margin: 0, fontSize: 11.5 }}>{t("inspect.selectHint")}</p>
      </div>
    </div>
  );
}

export function Inspector() {
  const t = useT();
  const manifest = useStore((s) => s.manifest);
  const selected = useStore((s) => s.selected);
  if (!manifest) return <div className="empty">{t("inspect.noScene")}</div>;
  if (selected?.startsWith("arm:")) {
    const arm = manifest.arms.find((a) => a.name === selected.slice(4));
    if (arm) return <ArmInspector key={arm.name} arm={arm} />;
  }
  if (selected?.startsWith("object:") || selected?.startsWith("static:")) {
    return <ObjectInspector manifest={manifest} subjectKey={selected} />;
  }
  if (selected?.startsWith("cloth:")) return <ClothInspector manifest={manifest} subjectKey={selected} />;
  if (selected?.startsWith("region:")) return <RegionInspector manifest={manifest} subjectKey={selected} />;
  if (selected?.startsWith("camera:")) return <CameraInspector manifest={manifest} name={selected.slice(7)} />;
  if (selected?.startsWith("light:")) return <LightInspector name={selected.slice(6)} />;
  return <WorkcellSummary manifest={manifest} />;
}

export { fmt };
