// SPDX-License-Identifier: Apache-2.0
// The builder's draft, previewed: a translucent ghost per object at its document placement, and a
// gizmo that edits that placement. Ghosts are design-time geometry, not simulated state; Build
// turns the document into physics. Positions are the work-surface frame the document uses.

import * as THREE from "three";
import { TransformControls } from "three/addons/controls/TransformControls.js";
import type { OrbitControls } from "three/addons/controls/OrbitControls.js";
import type { ObjectDoc, Vec3, WorkcellDoc } from "../api/types";

const DEG = Math.PI / 180;

/** Geometry with its origin where the document's ``at`` is for that kind. */
function ghostGeometry(o: ObjectDoc): THREE.BufferGeometry {
  switch (o.kind) {
    case "box":
    case "slab": {
      const h = o.half ?? [0.025, 0.025, 0.025];
      return new THREE.BoxGeometry(2 * h[0], 2 * h[1], 2 * h[2]);
    }
    case "sphere":
      return new THREE.SphereGeometry(o.radius ?? 0.025, 28, 16);
    case "cylinder":
    case "post":
      return new THREE.CylinderGeometry(o.radius ?? 0.02, o.radius ?? 0.02, 2 * (o.halfHeight ?? 0.05), 28).rotateX(Math.PI / 2);
    case "capsule":
      return new THREE.CapsuleGeometry(o.radius ?? 0.015, 2 * (o.halfHeight ?? 0.04), 8, 20).rotateX(Math.PI / 2);
    case "sheet": {
      // ``at`` is the sheet's origin corner.
      const [w, h] = (o.size ?? [0.3, 0.3]) as [number, number];
      return new THREE.PlaneGeometry(w, h).translate(w / 2, h / 2, 0);
    }
    case "sponge": {
      // ``at`` is the centre of the bottom face.
      const [x, y, z] = (o.size ?? [0.09, 0.05, 0.035]) as [number, number, number];
      return new THREE.BoxGeometry(x, y, z).translate(0, 0, z / 2);
    }
    case "rope": {
      // ``at`` is the first end; the rope lies along the object's own +y.
      const length = o.length ?? 0.62;
      return new THREE.CylinderGeometry(o.radius ?? 0.011, o.radius ?? 0.011, length, 12).translate(0, length / 2, 0);
    }
  }
}

export class DraftLayer {
  readonly group = new THREE.Group();
  private gizmo: TransformControls;
  private meshes = new Map<string, THREE.Mesh>();
  private signature = "";
  private origin = new THREE.Vector3();
  private selected: string | null = null;
  private dragging = false;
  private visible = false;

  constructor(
    scene: THREE.Scene,
    camera: THREE.Camera,
    dom: HTMLElement,
    private orbit: OrbitControls,
    private onMove: (id: string, at: Vec3, rpy: Vec3) => void,
  ) {
    this.group.name = "draft";
    this.gizmo = new TransformControls(camera, dom);
    this.gizmo.setSize(0.7);
    this.gizmo.enabled = false;
    this.gizmo.getHelper().visible = false;
    this.gizmo.addEventListener("dragging-changed", (e) => {
      const active = Boolean((e as unknown as { value: boolean }).value);
      this.dragging = active;
      this.orbit.enabled = !active;
      if (!active) this.emit();
    });
    scene.add(this.group, this.gizmo.getHelper());
  }

  setCamera(camera: THREE.Camera): void {
    this.gizmo.camera = camera;
  }

  setMode(mode: "translate" | "rotate"): void {
    this.gizmo.setMode(mode);
  }

  get isDragging(): boolean {
    return this.dragging;
  }

  /** Show ``doc`` (or nothing) with ``selected`` (an object id) on the gizmo. */
  update(doc: WorkcellDoc | null, origin: Vec3 | null, visible: boolean, selected: string | null): void {
    this.visible = visible && Boolean(doc);
    this.group.visible = this.visible;
    if (origin) this.origin.set(origin[0], origin[1], origin[2]);
    if (doc && !this.dragging) {
      const signature = JSON.stringify([doc.objects, origin]);
      if (signature !== this.signature) {
        this.signature = signature;
        this.rebuild(doc.objects);
      }
    }
    this.selected = selected;
    const mesh = selected ? this.meshes.get(selected) : undefined;
    for (const [id, m] of this.meshes) {
      const edges = m.children[0] as THREE.LineSegments | undefined;
      if (edges) (edges.material as THREE.LineBasicMaterial).color.set(id === selected ? 0xff4713 : 0x8a96a3);
    }
    if (this.visible && mesh) {
      if (this.gizmo.object !== mesh) this.gizmo.attach(mesh);
      this.gizmo.enabled = true;
      this.gizmo.getHelper().visible = true;
    } else {
      this.gizmo.detach();
      this.gizmo.enabled = false;
      this.gizmo.getHelper().visible = false;
    }
  }

  private rebuild(objects: ObjectDoc[]): void {
    const keep = this.gizmo.object;
    this.gizmo.detach();
    for (const m of this.meshes.values()) {
      this.group.remove(m);
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
      const edges = m.children[0] as THREE.LineSegments | undefined;
      edges?.geometry.dispose();
      (edges?.material as THREE.Material | undefined)?.dispose();
    }
    this.meshes.clear();
    for (const o of objects) {
      const geometry = ghostGeometry(o);
      const material = new THREE.MeshStandardMaterial({
        color: new THREE.Color(o.color ?? "#9aa3ad"),
        transparent: true,
        opacity: 0.42,
        depthWrite: false,
        side: o.kind === "sheet" ? THREE.DoubleSide : THREE.FrontSide,
        roughness: 0.6,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.add(new THREE.LineSegments(new THREE.EdgesGeometry(geometry, 25), new THREE.LineBasicMaterial({ color: 0x8a96a3 })));
      mesh.position.set(this.origin.x + o.at[0], this.origin.y + o.at[1], this.origin.z + o.at[2]);
      mesh.rotation.set(o.rpy[0] * DEG, o.rpy[1] * DEG, o.rpy[2] * DEG, "ZYX");
      mesh.userData = { draft: o.id, owner: `draft:${o.id}` };
      mesh.renderOrder = 3;
      this.group.add(mesh);
      this.meshes.set(o.id, mesh);
    }
    if (keep && this.selected) {
      const again = this.meshes.get(this.selected);
      if (again) this.gizmo.attach(again);
    }
  }

  pickTargets(): THREE.Object3D[] {
    return this.visible ? [...this.meshes.values()] : [];
  }

  private emit(): void {
    const mesh = this.gizmo.object as THREE.Mesh | undefined;
    const id = mesh?.userData.draft as string | undefined;
    if (!mesh || !id) return;
    const r3 = (v: number) => Math.round(v * 10000) / 10000;
    const at: Vec3 = [r3(mesh.position.x - this.origin.x), r3(mesh.position.y - this.origin.y), r3(mesh.position.z - this.origin.z)];
    const e = new THREE.Euler().setFromQuaternion(mesh.quaternion, "ZYX");
    const rpy: Vec3 = [Math.round((e.x / DEG) * 10) / 10, Math.round((e.y / DEG) * 10) / 10, Math.round((e.z / DEG) * 10) / 10];
    this.onMove(id, at, rpy);
  }

  dispose(): void {
    this.gizmo.dispose();
    for (const m of this.meshes.values()) {
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
    }
  }
}
