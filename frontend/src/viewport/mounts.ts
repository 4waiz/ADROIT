// SPDX-License-Identifier: Apache-2.0
// A stand under any arm the simulation holds in the air. A fixed-base arm is welded to the world
// wherever its scene puts it; when nothing static is drawn beneath its base (a Panda set beside
// the bench rather than on it), the page draws the pedestal it would stand on in a real cell.
// The stand is scenery measured from the arm's own geometry: it is not in the simulation and
// nothing collides with it.

import * as THREE from "three";
import type { Manifest } from "../api/types";
import type { BuiltScene } from "./builder";

const STEEL = 0x8d9399;
const COAT = 0x2b3036; // anthracite powder coat
const PLATE = 0xb8bec4; // machined aluminium
const ACCENT = 0xff4713; // the BRIDGE orange

/** Stands for the arms that float; null when every arm already rests on something. */
export function buildMounts(manifest: Manifest, built: BuiltScene): THREE.Group | null {
  const ground = manifest.cell.groundZ ?? 0;
  built.root.updateMatrixWorld(true);
  // Static shapes (the bench, fixtures) are the only things an arm can stand on.
  const supports = built.visuals.filter((m) => m.userData.body === -1);
  const group = new THREE.Group();
  group.name = "arm-stands";
  const ray = new THREE.Raycaster();
  const down = new THREE.Vector3(0, 0, -1);

  for (const arm of manifest.arms) {
    const box = new THREE.Box3();
    for (const mesh of built.visuals) if (mesh.userData.body === arm.baseBody) box.expandByObject(mesh);
    if (box.isEmpty()) continue;
    const bottom = box.min.z;
    const height = bottom - ground;
    if (height < 0.06) continue; // on the floor already
    const cx = (box.min.x + box.max.x) / 2;
    const cy = (box.min.y + box.max.y) / 2;
    ray.set(new THREE.Vector3(cx, cy, bottom + 0.02), down);
    ray.far = 0.1;
    if (ray.intersectObjects(supports, false).length) continue; // it stands on the bench
    const size = box.getSize(new THREE.Vector3());
    group.add(stand(cx, cy, ground, height, size.x, size.y));
  }
  return group.children.length ? group : null;
}

function stand(x: number, y: number, ground: number, height: number, sx: number, sy: number): THREE.Group {
  const g = new THREE.Group();
  g.name = "arm-stand";
  g.position.set(x, y, ground);
  const column = THREE.MathUtils.clamp(Math.min(sx, sy) * 0.82, 0.14, 0.3);
  const top = THREE.MathUtils.clamp(Math.max(sx, sy) + 0.03, 0.22, 0.5);
  const foot = top + 0.2;
  const coat = new THREE.MeshStandardMaterial({ color: COAT, roughness: 0.55, metalness: 0.25 });
  const steel = new THREE.MeshStandardMaterial({ color: STEEL, roughness: 0.42, metalness: 0.75 });
  const plate = new THREE.MeshStandardMaterial({ color: PLATE, roughness: 0.3, metalness: 0.85 });
  const accent = new THREE.MeshStandardMaterial({ color: ACCENT, roughness: 0.45, metalness: 0.1 });
  const add = (geometry: THREE.BufferGeometry, material: THREE.Material, px: number, py: number, pz: number, rz = 0) => {
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.set(px, py, pz);
    mesh.rotation.z = rz;
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    g.add(mesh);
    return mesh;
  };
  const footH = 0.025;
  const topH = 0.03;
  // The floor plate, its four anchor bolts, the column, its gussets, the mounting plate.
  add(new THREE.BoxGeometry(foot, foot, footH), steel, 0, 0, footH / 2);
  const bolt = new THREE.CylinderGeometry(0.014, 0.014, 0.022, 12).rotateX(Math.PI / 2);
  for (const [bx, by] of [
    [1, 1],
    [1, -1],
    [-1, 1],
    [-1, -1],
  ])
    add(bolt, plate, (bx * (foot - 0.07)) / 2, (by * (foot - 0.07)) / 2, footH + 0.011);
  const columnH = Math.max(0.02, height - footH - topH);
  add(new THREE.BoxGeometry(column, column, columnH), coat, 0, 0, footH + columnH / 2);
  const gusset = new THREE.Shape();
  const gw = (foot - column) / 2 - 0.02;
  const gh = Math.min(0.16, columnH * 0.5);
  gusset.moveTo(0, 0);
  gusset.lineTo(gw, 0);
  gusset.lineTo(0, gh);
  gusset.lineTo(0, 0);
  const gussetGeometry = new THREE.ExtrudeGeometry(gusset, { depth: 0.012, bevelEnabled: false }).translate(0, 0, -0.006).rotateX(Math.PI / 2);
  for (let k = 0; k < 4; k++) {
    const a = (k * Math.PI) / 2;
    add(gussetGeometry, coat, Math.cos(a) * (column / 2), Math.sin(a) * (column / 2), footH, a);
  }
  add(new THREE.BoxGeometry(column + 0.004, column + 0.004, 0.022), accent, 0, 0, height - topH - 0.05);
  add(new THREE.BoxGeometry(top, top, topH), plate, 0, 0, height - topH / 2);
  g.userData.dispose = () => {
    g.traverse((o) => {
      const mesh = o as THREE.Mesh;
      if (mesh.isMesh) mesh.geometry.dispose();
    });
    for (const m of [coat, steel, plate, accent]) m.dispose();
  };
  return g;
}

/** Frees what buildMounts made. */
export function disposeMounts(group: THREE.Group | null): void {
  if (!group) return;
  for (const child of group.children) (child.userData.dispose as (() => void) | undefined)?.();
}
