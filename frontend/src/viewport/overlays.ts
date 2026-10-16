// SPDX-License-Identifier: LicenseRef-BRIDGE
// Engineering overlays, each drawn from what the frame carries: link frames at the bodies'
// poses, the tool-centre target against the measured tool centre, contact points and their
// forces from MuJoCo's contact table, centres of mass from the model, regions from the scene.

import * as THREE from "three";
import { Line2 } from "three/addons/lines/Line2.js";
import { LineGeometry } from "three/addons/lines/LineGeometry.js";
import { LineMaterial } from "three/addons/lines/LineMaterial.js";
import type { Frame, Manifest } from "../api/types";
import type { Overlays } from "../state/store";
import type { BuiltScene } from "./builder";

const ACCENT = 0xff4713;
const MAX_CONTACTS = 64;

function triad(size: number, opacity = 1): THREE.Group {
  const g = new THREE.Group();
  const colors = [0xff5a52, 0x4cd07d, 0x4b9dff];
  const dirs = [new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 1)];
  dirs.forEach((d, k) => {
    const geometry = new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), d.clone().multiplyScalar(size)]);
    const line = new THREE.Line(
      geometry,
      new THREE.LineBasicMaterial({ color: colors[k], transparent: opacity < 1, opacity, depthTest: false }),
    );
    line.renderOrder = 10;
    g.add(line);
  });
  return g;
}

export class OverlayLayer {
  readonly group = new THREE.Group();
  private frames = new THREE.Group();
  private targets = new THREE.Group();
  private regions = new THREE.Group();
  private workspace = new THREE.Group();
  private com: THREE.InstancedMesh;
  private contactPoints: THREE.InstancedMesh;
  private forceLines: THREE.LineSegments;
  private forcePositions = new Float32Array(MAX_CONTACTS * 6);
  private linkTriads: { body: number; object: THREE.Group }[] = [];
  private armTargets = new Map<
    string,
    { cursor: THREE.Group; ee: THREE.Group; link: Line2; linkGeometry: LineGeometry; ghost: THREE.Mesh }
  >();
  private comBodies: number[] = [];
  private comLocal: THREE.Vector3[] = [];
  private lineMaterial: LineMaterial;
  private tmpM = new THREE.Matrix4();
  private tmpQ = new THREE.Quaternion();
  private tmpP = new THREE.Vector3();
  private tmpS = new THREE.Vector3(1, 1, 1);

  constructor(
    private manifest: Manifest,
    private scene: BuiltScene,
  ) {
    this.group.name = "overlays";
    this.group.add(this.frames, this.targets, this.regions, this.workspace);

    // Link frames: every arm body, a small triad; the tool centre's is drawn by the target view.
    for (const arm of manifest.arms) {
      for (const b of arm.bodies) {
        const t = triad(0.045, 0.9);
        this.frames.add(t);
        this.linkTriads.push({ body: b, object: t });
      }
    }

    this.lineMaterial = new LineMaterial({
      color: ACCENT,
      linewidth: 2,
      dashed: true,
      dashSize: 0.01,
      gapSize: 0.006,
      transparent: true,
      depthTest: false,
    });
    for (const arm of manifest.arms) {
      const cursor = triad(0.07);
      const ghost = new THREE.Mesh(
        new THREE.SphereGeometry(0.009, 20, 12),
        new THREE.MeshBasicMaterial({ color: ACCENT, transparent: true, opacity: 0.85, depthTest: false }),
      );
      ghost.renderOrder = 11;
      cursor.add(ghost);
      const ee = triad(0.05, 0.75);
      const linkGeometry = new LineGeometry();
      linkGeometry.setPositions([0, 0, 0, 0, 0, 0]);
      const link = new Line2(linkGeometry, this.lineMaterial);
      link.renderOrder = 11;
      link.visible = false;
      this.targets.add(cursor, ee, link);
      this.armTargets.set(arm.name, { cursor, ee, link, linkGeometry, ghost });
    }

    // Centres of mass: dynamic objects and arm links.
    const dynamic = new Set<number>();
    for (const s of manifest.subjects) if (s.kind === "object" && !s.static) for (const b of s.bodies) dynamic.add(b);
    for (const arm of manifest.arms) for (const b of arm.bodies) dynamic.add(b);
    for (const b of manifest.bodies) {
      if (!dynamic.has(b.i) || b.mass <= 0) continue;
      this.comBodies.push(b.i);
      this.comLocal.push(new THREE.Vector3(...b.com));
    }
    this.com = new THREE.InstancedMesh(
      new THREE.OctahedronGeometry(0.008),
      new THREE.MeshBasicMaterial({ color: 0xf0b429, depthTest: false }),
      Math.max(1, this.comBodies.length),
    );
    this.com.count = this.comBodies.length;
    this.com.renderOrder = 12;
    this.group.add(this.com);

    this.contactPoints = new THREE.InstancedMesh(
      new THREE.SphereGeometry(0.004, 10, 8),
      new THREE.MeshBasicMaterial({ color: 0x35d0ff, depthTest: false }),
      MAX_CONTACTS,
    );
    this.contactPoints.count = 0;
    this.contactPoints.renderOrder = 12;
    const forceGeometry = new THREE.BufferGeometry();
    forceGeometry.setAttribute("position", new THREE.BufferAttribute(this.forcePositions, 3).setUsage(THREE.DynamicDrawUsage));
    forceGeometry.setDrawRange(0, 0);
    this.forceLines = new THREE.LineSegments(
      forceGeometry,
      new THREE.LineBasicMaterial({ color: 0x35d0ff, depthTest: false, transparent: true, opacity: 0.9 }),
    );
    this.forceLines.frustumCulled = false;
    this.forceLines.renderOrder = 12;
    this.group.add(this.contactPoints, this.forceLines);

    this.buildRegions();
    this.buildWorkspace();
  }

  private buildRegions(): void {
    for (const r of this.manifest.regions) {
      const [x0, y0, x1, y1] = r.xy;
      const [z0, z1] = r.z;
      const size = new THREE.Vector3(x1 - x0, y1 - y0, Math.max(z1 - z0, 0.002));
      const centre = new THREE.Vector3(r.origin[0] + (x0 + x1) / 2, r.origin[1] + (y0 + y1) / 2, r.origin[2] + (z0 + z1) / 2);
      const box = new THREE.Mesh(
        new THREE.BoxGeometry(size.x, size.y, size.z),
        new THREE.MeshBasicMaterial({ color: ACCENT, transparent: true, opacity: 0.07, depthWrite: false }),
      );
      box.position.copy(centre);
      const edges = new THREE.LineSegments(
        new THREE.EdgesGeometry(box.geometry),
        new THREE.LineBasicMaterial({ color: ACCENT, transparent: true, opacity: 0.75 }),
      );
      edges.position.copy(centre);
      // The footprint on the bench, which is what an operator lines a placement up against.
      const pad = new THREE.Mesh(
        new THREE.PlaneGeometry(size.x, size.y),
        new THREE.MeshBasicMaterial({ color: ACCENT, transparent: true, opacity: 0.16, depthWrite: false }),
      );
      pad.position.set(centre.x, centre.y, r.origin[2] + z0 + 0.0008);
      box.userData = { owner: r.key };
      this.regions.add(box, edges, pad);
    }
  }

  private buildWorkspace(): void {
    const cell = this.manifest.cell;
    if (!cell.origin || !cell.half) return;
    const [ox, oy, oz] = cell.origin;
    const [hx, hy] = cell.half;
    const points = [
      new THREE.Vector3(ox - hx, oy - hy, oz + 0.0015),
      new THREE.Vector3(ox + hx, oy - hy, oz + 0.0015),
      new THREE.Vector3(ox + hx, oy + hy, oz + 0.0015),
      new THREE.Vector3(ox - hx, oy + hy, oz + 0.0015),
      new THREE.Vector3(ox - hx, oy - hy, oz + 0.0015),
    ];
    const outline = new THREE.Line(
      new THREE.BufferGeometry().setFromPoints(points),
      new THREE.LineDashedMaterial({ color: 0x8a96a3, dashSize: 0.02, gapSize: 0.014, transparent: true, opacity: 0.8 }),
    );
    outline.computeLineDistances();
    this.workspace.add(outline);
    // Corner ticks: the work surface's frame origin and axes.
    const origin = triad(0.08, 0.8);
    origin.position.set(ox, oy, oz + 0.002);
    this.workspace.add(origin);
  }

  setVisibility(o: Overlays): void {
    this.frames.visible = o.frames;
    this.targets.visible = o.target;
    this.regions.visible = o.regions;
    this.workspace.visible = o.workspace;
    this.com.visible = o.com;
    this.contactPoints.visible = o.contacts;
    this.forceLines.visible = o.forces;
    for (const c of this.scene.colliders) c.visible = o.collision;
  }

  setResolution(width: number, height: number): void {
    this.lineMaterial.resolution.set(width, height);
  }

  update(frame: Frame, hiddenCursorArm: string | null): void {
    const q = frame.bodyQ;
    const pose = (b: number, target: THREE.Object3D) => {
      const o = b * 7;
      target.position.set(q[o], q[o + 1], q[o + 2]);
      target.quaternion.set(q[o + 3], q[o + 4], q[o + 5], q[o + 6]);
    };
    if (this.frames.visible) for (const { body, object } of this.linkTriads) pose(body, object);

    if (this.targets.visible) {
      for (const [name, view] of this.armTargets) {
        const arm = frame.header.arms[name];
        if (!arm || arm.error) continue;
        const [ex, ey, ez, eqx, eqy, eqz, eqw] = arm.ee;
        view.ee.position.set(ex, ey, ez);
        view.ee.quaternion.set(eqx, eqy, eqz, eqw);
        const [cx, cy, cz, cqx, cqy, cqz, cqw] = arm.cursor;
        view.cursor.position.set(cx, cy, cz);
        view.cursor.quaternion.set(cqx, cqy, cqz, cqw);
        // The cursor is the gizmo's to draw while that arm is selected.
        view.cursor.visible = arm.mode === "cartesian" && name !== hiddenCursorArm;
        const far = arm.mode === "cartesian" && arm.gap > 0.004;
        view.link.visible = far;
        if (far) {
          view.linkGeometry.setPositions([ex, ey, ez, cx, cy, cz]);
          view.link.computeLineDistances();
        }
        const unreachable = arm.reach && arm.reach.reachable === false;
        (view.ghost.material as THREE.MeshBasicMaterial).color.set(unreachable ? 0xff5c5c : ACCENT);
      }
    }

    if (this.com.visible) {
      this.comBodies.forEach((b, k) => {
        const o = b * 7;
        this.tmpQ.set(q[o + 3], q[o + 4], q[o + 5], q[o + 6]);
        this.tmpP.copy(this.comLocal[k]).applyQuaternion(this.tmpQ).add(new THREE.Vector3(q[o], q[o + 1], q[o + 2]));
        this.tmpM.compose(this.tmpP, this.tmpQ, this.tmpS);
        this.com.setMatrixAt(k, this.tmpM);
      });
      this.com.instanceMatrix.needsUpdate = true;
    }

    const contacts = frame.header.contacts;
    if ((this.contactPoints.visible || this.forceLines.visible) && contacts.available) {
      const items = contacts.items.slice(0, MAX_CONTACTS);
      this.contactPoints.count = items.length;
      this.tmpQ.identity();
      items.forEach((c, k) => {
        this.tmpP.set(c[2], c[3], c[4]);
        this.tmpM.compose(this.tmpP, this.tmpQ, this.tmpS);
        this.contactPoints.setMatrixAt(k, this.tmpM);
        // 10 mm per newton, capped at 150 mm so a stacked load stays readable.
        const f = new THREE.Vector3(c[5], c[6], c[7]);
        const length = Math.min(f.length() * 0.01, 0.15);
        f.normalize().multiplyScalar(length);
        this.forcePositions.set([c[2], c[3], c[4], c[2] + f.x, c[3] + f.y, c[4] + f.z], k * 6);
      });
      this.contactPoints.instanceMatrix.needsUpdate = true;
      this.forceLines.geometry.setDrawRange(0, items.length * 2);
      (this.forceLines.geometry.attributes.position as THREE.BufferAttribute).needsUpdate = true;
    } else {
      this.contactPoints.count = 0;
      this.forceLines.geometry.setDrawRange(0, 0);
    }
  }

  dispose(): void {
    this.group.traverse((o) => {
      const m = o as THREE.Mesh;
      if (m.geometry) m.geometry.dispose();
      const mat = m.material as THREE.Material | THREE.Material[] | undefined;
      if (Array.isArray(mat)) mat.forEach((x) => x.dispose());
      else mat?.dispose();
    });
  }
}
