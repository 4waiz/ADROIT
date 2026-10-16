// SPDX-License-Identifier: Apache-2.0
// The robotics lab around the cell: robots that are scenery, not simulation. A palletizing arm
// stacks cartons off an infeed, a delta picker takes parts off a moving belt, two cobots pass parts
// between their trays and an AMR drives a loop around the cell. Each runs a loop of its own so the
// hall reads as a working lab; none of them is simulated, none reads or changes the simulation,
// and a station that would stand where a large cell's guarding goes is put away.
// The arms follow their tools through inverse kinematics, so grippers meet what they pick.

import * as THREE from "three";
import type { Footprint } from "./cell";
import { Batcher, cylinder, PAINT, rounded } from "./kit";
import { blobShadow, cartonFace, floorText, hazardStripes } from "./textures";

type V3 = [number, number, number];
type Rect = [number, number, number, number]; // x0, y0, x1, y1 in hall coordinates

const ease = (t: number) => (t <= 0 ? 0 : t >= 1 ? 1 : 0.5 - 0.5 * Math.cos(Math.PI * t));
const clamp01 = (t: number) => Math.min(1, Math.max(0, t));
const UP = new THREE.Vector3(0, 0, 1);
const Y_AXIS: V3 = [Math.PI / 2, 0, 0]; // turns a +z cylinder to lie along y
const X_AXIS: V3 = [0, Math.PI / 2, 0]; // turns a +z cylinder to lie along x

/** The lab's own materials and textures. */
class LabMaterials {
  readonly owned: THREE.Material[] = [];
  readonly textures: THREE.Texture[] = [];
  private keep<T extends THREE.Material>(m: T): T {
    this.owned.push(m);
    return m;
  }
  readonly robotOrange = this.keep(new THREE.MeshPhysicalMaterial({ color: 0xf0661a, roughness: 0.3, metalness: 0.05, clearcoat: 0.7, clearcoatRoughness: 0.22 }));
  readonly graphite = this.keep(new THREE.MeshStandardMaterial({ color: 0x2a2e33, roughness: 0.5, metalness: 0.35 }));
  readonly steel = this.keep(new THREE.MeshStandardMaterial({ color: PAINT.steelGrey, roughness: 0.38, metalness: 0.8 }));
  readonly aluminium = this.keep(new THREE.MeshStandardMaterial({ color: 0xc3c8cd, roughness: 0.3, metalness: 0.85 }));
  readonly cobotWhite = this.keep(new THREE.MeshPhysicalMaterial({ color: 0xe6e9ec, roughness: 0.28, metalness: 0.02, clearcoat: 0.5, clearcoatRoughness: 0.3 }));
  readonly accent = this.keep(new THREE.MeshStandardMaterial({ color: 0xff4713, roughness: 0.4, metalness: 0.1 }));
  readonly rubber = this.keep(new THREE.MeshStandardMaterial({ color: 0x16171a, roughness: 0.85, metalness: 0 }));
  readonly belt = this.keep(new THREE.MeshStandardMaterial({ color: 0x1c1f23, roughness: 0.8, metalness: 0.05 }));
  readonly tabletop = this.keep(new THREE.MeshStandardMaterial({ color: 0xdfe2e4, roughness: 0.55, metalness: 0.05 }));
  readonly tray = this.keep(new THREE.MeshStandardMaterial({ color: 0x30353b, roughness: 0.6, metalness: 0.1 }));
  readonly part = this.keep(new THREE.MeshStandardMaterial({ color: 0xff5a1f, roughness: 0.38, metalness: 0.15 }));
  readonly pallet = this.keep(new THREE.MeshStandardMaterial({ color: 0xb08a5a, roughness: 0.85, metalness: 0 }));
  readonly amrWhite = this.keep(new THREE.MeshPhysicalMaterial({ color: 0xe9eaea, roughness: 0.32, metalness: 0.05, clearcoat: 0.4 }));
  readonly yellow = this.keep(new THREE.MeshStandardMaterial({ color: PAINT.safetyYellow, roughness: 0.5, metalness: 0 }));
  readonly ledBlue = this.keep(new THREE.MeshBasicMaterial({ color: new THREE.Color(0x4b9dff).multiplyScalar(4) }));
  readonly ledGreen = this.keep(new THREE.MeshBasicMaterial({ color: new THREE.Color(0x3ddc84).multiplyScalar(4) }));
  readonly ledOrange = this.keep(new THREE.MeshBasicMaterial({ color: new THREE.Color(0xff4713).multiplyScalar(3.5) }));
  readonly carton: THREE.MeshStandardMaterial;
  readonly hazard: THREE.MeshStandardMaterial;
  readonly blob: THREE.MeshBasicMaterial;
  readonly lettering: THREE.MeshStandardMaterial;

  constructor() {
    const carton = cartonFace();
    const stripes = hazardStripes();
    stripes.repeat.set(6, 1);
    const blob = blobShadow();
    const text = floorText("ROBOTICS LAB", "#f2f2ee");
    this.textures.push(carton, stripes, blob, text);
    this.carton = this.keep(new THREE.MeshStandardMaterial({ map: carton, roughness: 0.85, metalness: 0 }));
    this.hazard = this.keep(new THREE.MeshStandardMaterial({ map: stripes, roughness: 0.5, polygonOffset: true, polygonOffsetFactor: -2 }));
    this.blob = this.keep(new THREE.MeshBasicMaterial({ map: blob, transparent: true, depthWrite: false, toneMapped: false, opacity: 0.85 }));
    this.lettering = this.keep(new THREE.MeshStandardMaterial({ map: text, transparent: true, depthWrite: false, roughness: 0.5, opacity: 0.8, polygonOffset: true, polygonOffsetFactor: -2 }));
  }

  dispose(): void {
    for (const m of this.owned) m.dispose();
    for (const t of this.textures) t.dispose();
  }
}

/** A rigid link: its parts merged by material under a pivot (the merged geometry is the lab's own). */
function link(parent: THREE.Object3D, at: V3, parts: (b: Batcher) => void, name: string): THREE.Group {
  const pivot = new THREE.Group();
  pivot.name = name;
  pivot.position.set(...at);
  const b = new Batcher();
  parts(b);
  const body = b.build(`${name}-parts`, true);
  body.traverse((o) => {
    const meshObj = o as THREE.Mesh;
    if (meshObj.isMesh) meshObj.castShadow = true;
  });
  pivot.add(body);
  parent.add(pivot);
  return pivot;
}

/** A single moving part; its geometry is shared (the kit's cache, or a station's own), never freed here. */
function mesh(geometry: THREE.BufferGeometry, material: THREE.Material, parent: THREE.Object3D, at: V3 = [0, 0, 0], rot: V3 = [0, 0, 0]): THREE.Mesh {
  const m = new THREE.Mesh(geometry, material);
  m.position.set(...at);
  m.rotation.set(...rot);
  m.castShadow = true;
  m.receiveShadow = true;
  m.userData.sharedGeometry = true;
  parent.add(m);
  return m;
}

function decal(parent: THREE.Object3D, material: THREE.Material, x: number, y: number, w: number, d: number, z = 0.0035): THREE.Mesh {
  const quad = new THREE.Mesh(new THREE.PlaneGeometry(w, d), material);
  quad.position.set(x, y, z);
  quad.renderOrder = 1;
  quad.receiveShadow = true;
  parent.add(quad);
  return quad;
}

interface Station {
  group: THREE.Group;
  area: Rect;
  update(t: number): void;
  dispose(): void;
}

// ================================================================== the industrial arm
/** A 1.6 m-reach six-axis arm, to the proportions of a KR 16 class robot; the tool points along +x of its flange. */
class IndustrialArm {
  static readonly FOOT = 0.1;
  static readonly SH_X = 0.16;
  static readonly SH_Z = 0.675;
  static readonly L1 = 0.68;
  static readonly A3 = 0.035;
  static readonly L2A = 0.3;
  static readonly L2B = 0.37;
  static readonly W = 0.115;
  static readonly TOOL = 0.19;
  readonly root = new THREE.Group();
  readonly tool = new THREE.Group();
  private j: THREE.Group[];

  constructor(m: LabMaterials) {
    const A = IndustrialArm;
    link(
      this.root,
      [0, 0, 0],
      (b) => {
        b.box(m.steel, [0.64, 0.64, 0.025], [0, 0, 0.0125]);
        b.shape(cylinder(0.27, A.FOOT - 0.025, 40), m.graphite, [0, 0, 0.025 + (A.FOOT - 0.025) / 2]);
        for (const [x, y] of [
          [0.27, 0.27],
          [0.27, -0.27],
          [-0.27, 0.27],
          [-0.27, -0.27],
        ])
          b.shape(cylinder(0.018, 0.024, 12), m.aluminium, [x, y, 0.037]);
      },
      "arm-foot",
    );
    const j1 = link(
      this.root,
      [0, 0, A.FOOT],
      (b) => {
        b.shape(rounded(0.52, 0.46, 0.22, 0.05), m.robotOrange, [0.02, 0, 0.11]);
        b.shape(rounded(0.32, 0.36, 0.46, 0.07), m.robotOrange, [A.SH_X - 0.04, 0, 0.36]);
        b.shape(cylinder(0.075, 0.2, 20), m.graphite, [-0.19, 0.13, 0.2]);
        b.shape(cylinder(0.11, 0.15, 24), m.graphite, [A.SH_X, 0.255, A.SH_Z - A.FOOT], Y_AXIS);
        b.shape(cylinder(0.06, 0.02, 20), m.aluminium, [A.SH_X, 0.335, A.SH_Z - A.FOOT], Y_AXIS);
      },
      "arm-a1",
    );
    const j2 = link(
      j1,
      [A.SH_X, 0, A.SH_Z - A.FOOT],
      (b) => {
        b.shape(cylinder(0.155, 0.34, 32), m.robotOrange, [0, 0, 0], Y_AXIS);
        b.shape(rounded(0.18, 0.21, A.L1 + 0.1, 0.06), m.robotOrange, [0, -0.02, A.L1 / 2]);
      },
      "arm-a2",
    );
    const j3 = link(
      j2,
      [0, 0, A.L1],
      (b) => {
        b.shape(cylinder(0.125, 0.3, 32), m.robotOrange, [0, 0, 0], Y_AXIS);
        b.shape(rounded(0.26, 0.24, 0.2, 0.05), m.graphite, [-0.22, 0, A.A3]);
        b.shape(rounded(A.L2A + 0.08, 0.2, 0.19, 0.06), m.robotOrange, [A.L2A / 2 - 0.02, 0, A.A3]);
        b.shape(cylinder(0.05, 0.02, 16), m.accent, [0, 0.155, 0], Y_AXIS);
      },
      "arm-a3",
    );
    const j4 = link(j3, [A.L2A, 0, A.A3], (b) => b.shape(cylinder(0.078, A.L2B - 0.05, 28), m.robotOrange, [(A.L2B - 0.05) / 2, 0, 0], X_AXIS), "arm-a4");
    const j5 = link(
      j4,
      [A.L2B, 0, 0],
      (b) => {
        b.shape(rounded(0.14, 0.13, 0.15, 0.05), m.robotOrange, [0.015, 0, 0]);
        b.shape(cylinder(0.056, 0.17, 24), m.graphite, [0, 0, 0], Y_AXIS);
      },
      "arm-a5",
    );
    const j6 = link(
      j5,
      [A.W, 0, 0],
      (b) => {
        // The flange and a four-cup vacuum gripper.
        b.shape(cylinder(0.05, 0.03, 24), m.aluminium, [0.015, 0, 0], X_AXIS);
        b.shape(cylinder(0.04, 0.06, 20), m.graphite, [0.06, 0, 0], X_AXIS);
        b.box(m.graphite, [0.035, 0.38, 0.3], [0.105, 0, 0]);
        b.box(m.accent, [0.036, 0.38, 0.03], [0.105, 0, 0.12]);
        for (const [y, z] of [
          [0.13, 0.095],
          [0.13, -0.095],
          [-0.13, 0.095],
          [-0.13, -0.095],
        ])
          b.shape(cylinder(0.026, 0.07, 16, 0.034), m.rubber, [0.155, y, z], X_AXIS);
      },
      "arm-a6",
    );
    this.tool.position.set(A.TOOL, 0, 0);
    j6.add(this.tool);
    this.j = [j1, j2, j3, j4, j5, j6];
  }

  set(q: number[]): void {
    this.j[0].rotation.z = q[0];
    this.j[1].rotation.y = q[1];
    this.j[2].rotation.y = q[2];
    this.j[3].rotation.x = q[3];
    this.j[4].rotation.y = q[4];
    this.j[5].rotation.x = q[5];
  }

  /** Joints that put the tool face at p (arm frame) pointing straight down, its long side along yaw. */
  static solve(p: THREE.Vector3, yaw: number): number[] {
    const A = IndustrialArm;
    const q1 = Math.atan2(p.y, p.x);
    const dr = Math.hypot(p.x, p.y) - A.SH_X;
    const dz = p.z + A.W + A.TOOL - A.SH_Z; // the wrist centre sits straight above the tool face
    const lf = Math.hypot(A.L2A + A.L2B, A.A3);
    const delta = Math.atan2(A.A3, A.L2A + A.L2B);
    const d = Math.min(Math.hypot(dr, dz), A.L1 + lf - 1e-4);
    const beta = Math.acos(THREE.MathUtils.clamp((A.L1 * A.L1 + d * d - lf * lf) / (2 * A.L1 * d), -1, 1));
    const a1 = Math.atan2(dz, dr) + beta; // the upper arm's angle from horizontal, elbow up
    const a3 = Math.atan2(dz - A.L1 * Math.sin(a1), dr - A.L1 * Math.cos(a1)) - delta; // the forearm's
    const q2 = Math.PI / 2 - a1;
    return [q1, q2, -q2 - a3, 0, a3 + Math.PI / 2, q1 - yaw];
  }
}

/** Interpolates in cylindrical coordinates about the base, so a long move swings round like a real arm. */
function swing(a: THREE.Vector3, b: THREE.Vector3, t: number, lift: number, out: THREE.Vector3): THREE.Vector3 {
  const ra = Math.hypot(a.x, a.y);
  const rb = Math.hypot(b.x, b.y);
  const pa = Math.atan2(a.y, a.x);
  let pb = Math.atan2(b.y, b.x);
  if (pb - pa > Math.PI) pb -= 2 * Math.PI;
  if (pa - pb > Math.PI) pb += 2 * Math.PI;
  const k = ease(t);
  const r = ra + (rb - ra) * k;
  const phi = pa + (pb - pa) * k;
  return out.set(r * Math.cos(phi), r * Math.sin(phi), a.z + (b.z - a.z) * k + Math.sin(Math.PI * k) * lift);
}

// ================================================================== the palletizer
function palletizer(m: LabMaterials, x: number, y: number): Station {
  const group = new THREE.Group();
  group.name = "lab-palletizer";
  group.position.set(x, y, 0);
  const arm = new IndustrialArm(m);
  group.add(arm.root);
  const BOX: V3 = [0.4, 0.3, 0.26];
  const boxGeometry = new THREE.BoxGeometry(...BOX);
  const ROLL_Z = 0.7;
  const C0 = 0.82;
  const C1 = 3.1;
  const pick = new THREE.Vector3(0, 1.08, ROLL_Z + BOX[2]);
  const pallet = new THREE.Vector3(1.18, 0, 0.144);
  const slots: THREE.Vector3[] = [];
  for (const layer of [0, 1])
    for (const [sx, sy] of [
      [-0.21, -0.155],
      [0.21, -0.155],
      [-0.21, 0.155],
      [0.21, 0.155],
    ])
      slots.push(new THREE.Vector3(pallet.x + sx, pallet.y + sy, pallet.z + BOX[2] * (layer + 1)));

  const s = new Batcher();
  // The infeed: a roller conveyor running in along +y.
  for (const dx of [-0.26, 0.26]) s.box(m.steel, [0.05, C1 - C0, 0.1], [dx, (C0 + C1) / 2, ROLL_Z - 0.03], [0, 0, 0], true);
  for (let yy = C0 + 0.06; yy < C1 - 0.04; yy += 0.11) s.shape(cylinder(0.022, 0.5, 12), m.aluminium, [0, yy, ROLL_Z - 0.022], X_AXIS);
  for (const yy of [C0 + 0.2, C1 - 0.2]) for (const dx of [-0.26, 0.26]) s.box(m.graphite, [0.05, 0.05, ROLL_Z - 0.08], [dx, yy, (ROLL_Z - 0.08) / 2], [0, 0, 0], true);
  // A EUR pallet on the floor.
  s.box(m.pallet, [1.2, 0.8, 0.022], [pallet.x, pallet.y, 0.133], [0, 0, 0], true);
  for (const py of [-0.36, 0, 0.36]) s.box(m.pallet, [1.2, 0.1, 0.1], [pallet.x, py, 0.072], [0, 0, 0], true);
  for (const px of [-0.55, 0, 0.55]) for (const py of [-0.36, 0, 0.36]) s.box(m.pallet, [0.14, 0.1, 0.022], [pallet.x + px, py, 0.011]);
  // The arm's reach, painted on the floor.
  const lz = 0.0025;
  s.box(m.yellow, [3.0, 0.08, 0.004], [0.35, -1.25, lz]);
  s.box(m.yellow, [3.0, 0.08, 0.004], [0.35, 1.45, lz]);
  s.box(m.yellow, [0.08, 2.7, 0.004], [-1.15, 0.1, lz]);
  s.box(m.yellow, [0.08, 2.7, 0.004], [1.85, 0.1, lz]);
  group.add(s.build("palletizer-static"));
  decal(group, m.hazard, 0.35, -1.45, 3.0, 0.32, 0.003);
  decal(group, m.blob, 0.35, 0.4, 3.4, 3.0);

  const waiting = mesh(boxGeometry, m.carton, group, [pick.x, pick.y, ROLL_Z + BOX[2] / 2], [0, 0, Math.PI / 2]);
  const carried = mesh(boxGeometry, m.carton, arm.tool, [BOX[2] / 2, 0, 0], [0, -Math.PI / 2, 0]);
  const stacked = slots.map((p) => {
    const b = mesh(boxGeometry, m.carton, group, [p.x, p.y, p.z - BOX[2] / 2]);
    b.visible = false;
    return b;
  });

  const CYCLE = 6.4;
  const target = new THREE.Vector3();
  const pickUp = pick.clone().setZ(pick.z + 0.3);
  return {
    group,
    area: [x - 1.3, y - 1.7, x + 2.0, y + 3.2],
    update(time: number) {
      const n = Math.floor(time / CYCLE);
      const t = time - n * CYCLE;
      const k = n % slots.length; // boxes already on the pallet this round
      const slot = slots[k];
      const slotUp = slot.clone().setZ(slot.z + 0.3);
      // Boxes ride the infeed lengthways (yaw 90 degrees) and go down on the pallet crossways.
      let yaw = Math.PI / 2;
      if (t < 0.55) target.lerpVectors(pickUp, pick, ease(t / 0.55));
      else if (t < 0.8) target.copy(pick);
      else if (t < 1.4) target.lerpVectors(pick, pickUp, ease((t - 0.8) / 0.6));
      else if (t < 3.0) {
        swing(pickUp, slotUp, (t - 1.4) / 1.6, 0.08, target);
        yaw = (Math.PI / 2) * (1 - ease((t - 1.4) / 1.6));
      } else if (t < 4.4) {
        yaw = 0;
        if (t < 3.6) target.lerpVectors(slotUp, slot, ease((t - 3.0) / 0.6));
        else if (t < 3.85) target.copy(slot);
        else target.lerpVectors(slot, slotUp, ease((t - 3.85) / 0.55));
      } else if (t < 6.0) {
        swing(slotUp, pickUp, (t - 4.4) / 1.6, 0.06, target);
        yaw = (Math.PI / 2) * ease((t - 4.4) / 1.6);
      } else target.copy(pickUp);
      arm.set(IndustrialArm.solve(target, yaw));
      // Carried from the pick (0.65 s) to the release (3.7 s); the next box rolls in meanwhile.
      carried.visible = t >= 0.65 && t < 3.7;
      stacked.forEach((b, i) => {
        b.visible = i < k || (i === k && t >= 3.7);
      });
      waiting.visible = t < 0.65 || t >= 1.6;
      const travel = t >= 1.6 ? 1 - ease(clamp01((t - 1.6) / 2.2)) : 0;
      waiting.position.y = pick.y + travel * (C1 - 0.3 - pick.y);
    },
    dispose() {
      boxGeometry.dispose();
    },
  };
}

// ================================================================== the delta picker
function deltaPicker(m: LabMaterials, x: number, y: number): Station {
  const group = new THREE.Group();
  group.name = "lab-delta";
  group.position.set(x, y, 0);
  const BASE_Z = 1.72;
  const RB = 0.16;
  const RP = 0.05;
  const LA = 0.3;
  const LB = 0.7;
  const BELT_Z = 0.82;
  const BELT_Y = -0.05;
  const CUP = 0.0925; // platform centre to the suction cup's face

  const s = new Batcher();
  for (const [px, py] of [
    [-0.8, -0.48],
    [0.8, -0.48],
    [-0.8, 0.48],
    [0.8, 0.48],
  ]) {
    s.box(m.aluminium, [0.06, 0.06, 1.98], [px, py, 0.99], [0, 0, 0], true);
    s.box(m.graphite, [0.16, 0.16, 0.012], [px, py, 0.006]);
  }
  for (const py of [-0.48, 0.48]) s.box(m.aluminium, [1.66, 0.06, 0.06], [0, py, 1.95], [0, 0, 0], true);
  for (const px of [-0.8, 0, 0.8]) s.box(m.aluminium, [0.06, 1.02, 0.06], [px, 0, 1.95], [0, 0, 0], true);
  s.box(m.graphite, [0.5, 0.5, 0.04], [0, 0, 1.9], [0, 0, 0], true);
  s.box(m.graphite, [0.06, 0.06, 0.12], [0, 0, BASE_Z + 0.12], [0, 0, 0], true);
  s.shape(cylinder(0.25, 0.04, 6), m.aluminium, [0, 0, BASE_Z + 0.06], [0, 0, 0], [1, 1, 1], true);
  for (let i = 0; i < 3; i++) {
    const phi = (i * 2 * Math.PI) / 3;
    // Each motor's shaft lies along its arm's pivot, tangent to the base ring.
    s.shape(cylinder(0.055, 0.14, 20), m.graphite, [Math.cos(phi) * (RB - 0.03), Math.sin(phi) * (RB - 0.03), BASE_Z + 0.02], [Math.PI / 2, 0, phi], [1, 1, 1], true);
  }
  // The belt under it, and the tray it sorts into.
  s.box(m.belt, [1.7, 0.4, 0.04], [0, BELT_Y, BELT_Z - 0.02], [0, 0, 0], true);
  for (const py of [BELT_Y - 0.22, BELT_Y + 0.22]) s.box(m.steel, [1.7, 0.04, 0.08], [0, py, BELT_Z - 0.005], [0, 0, 0], true);
  for (const px of [-0.7, 0.7]) for (const py of [BELT_Y - 0.22, BELT_Y + 0.22]) s.box(m.graphite, [0.05, 0.05, BELT_Z - 0.05], [px, py, (BELT_Z - 0.05) / 2], [0, 0, 0], true);
  s.box(m.tray, [0.4, 0.17, 0.025], [0, 0.3, BELT_Z + 0.0125], [0, 0, 0], true);
  s.box(m.graphite, [0.05, 0.05, BELT_Z], [0, 0.3, BELT_Z / 2], [0, 0, 0], true);
  group.add(s.build("delta-static"));
  decal(group, m.blob, 0, 0, 2.2, 1.5);

  const upper: THREE.Group[] = [];
  const hinges: THREE.Vector3[] = [];
  for (let i = 0; i < 3; i++) {
    const phi = (i * 2 * Math.PI) / 3;
    const hinge = new THREE.Group();
    hinge.position.set(Math.cos(phi) * RB, Math.sin(phi) * RB, BASE_Z);
    hinge.rotation.z = phi;
    group.add(hinge);
    hinges.push(hinge.position.clone());
    upper.push(
      link(
        hinge,
        [0, 0, 0],
        (b) => {
          b.shape(rounded(LA + 0.04, 0.04, 0.035, 0.012), m.robotOrange, [LA / 2, 0, 0]);
          b.shape(cylinder(0.022, 0.09, 16), m.aluminium, [LA, 0, 0], Y_AXIS);
        },
        `delta-arm-${i}`,
      ),
    );
  }
  const rodGeometry = new THREE.CylinderGeometry(0.008, 0.008, LB, 10).rotateX(Math.PI / 2);
  const rods: THREE.Mesh[] = [];
  for (let i = 0; i < 6; i++) rods.push(mesh(rodGeometry, m.graphite, group));
  const platform = new THREE.Group();
  mesh(cylinder(0.075, 0.025, 6), m.aluminium, platform);
  mesh(cylinder(0.012, 0.07, 10), m.graphite, platform, [0, 0, -0.045]);
  mesh(cylinder(0.018, 0.015, 12, 0.012), m.rubber, platform, [0, 0, -0.085]);
  group.add(platform);

  // Parts ride the belt along +x at 0.25 m/s, one every 1.2 s; the picker takes each at the pick line.
  const PART_H = 0.03;
  const partGeometry = cylinder(0.026, PART_H, 18);
  const SPEED = 0.25;
  const PERIOD = 1.2;
  const X0 = -0.82;
  const PICK_X = -0.05;
  const belt = [0, 1, 2, 3].map(() => mesh(partGeometry, m.part, group));
  const carried = mesh(partGeometry, m.part, platform, [0, 0, -CUP - PART_H / 2]);
  const tray: THREE.Vector3[] = [];
  for (const ty of [0.265, 0.335]) for (const tx of [-0.135, -0.045, 0.045, 0.135]) tray.push(new THREE.Vector3(tx, ty, BELT_Z + 0.025 + PART_H / 2));
  const placed = tray.map((p) => {
    const part = mesh(partGeometry, m.part, group, [p.x, p.y, p.z]);
    part.visible = false;
    return part;
  });

  const radial = new THREE.Vector3();
  const tangent = new THREE.Vector3();
  const joint = new THREE.Vector3();
  const d = new THREE.Vector3();
  const elbow = new THREE.Vector3();
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  const dir = new THREE.Vector3();
  const Z = new THREE.Vector3(0, 0, 1);

  /** Each arm's pivot angle puts its elbow one lower-arm length from its platform joint. */
  function reach(target: THREE.Vector3): void {
    for (let i = 0; i < 3; i++) {
      const phi = (i * 2 * Math.PI) / 3;
      radial.set(Math.cos(phi), Math.sin(phi), 0);
      tangent.set(-Math.sin(phi), Math.cos(phi), 0);
      joint.copy(target).addScaledVector(radial, RP);
      d.copy(joint).sub(hinges[i]);
      const dr = d.dot(radial);
      const dt = d.dot(tangent);
      const A = -2 * LA * dr;
      const B = 2 * LA * d.z;
      const C = LB * LB - LA * LA - (dr * dr + dt * dt + d.z * d.z);
      const off = Math.acos(THREE.MathUtils.clamp(C / Math.hypot(A, B), -1, 1));
      const mid = Math.atan2(B, A);
      const wrap = (th: number) => Math.atan2(Math.sin(th), Math.cos(th));
      const t1 = wrap(mid + off);
      const t2 = wrap(mid - off);
      const theta = Math.abs(t1 - 0.55) < Math.abs(t2 - 0.55) ? t1 : t2; // elbows out and down
      upper[i].rotation.y = theta;
      elbow.copy(hinges[i]).addScaledVector(radial, LA * Math.cos(theta));
      elbow.z -= LA * Math.sin(theta);
      for (const [k, side] of [
        [0, 1],
        [1, -1],
      ]) {
        a.copy(elbow).addScaledVector(tangent, 0.035 * side);
        b.copy(joint).addScaledVector(tangent, 0.035 * side);
        const rod = rods[i * 2 + k];
        rod.position.copy(a).add(b).multiplyScalar(0.5);
        rod.quaternion.setFromUnitVectors(Z, dir.copy(b).sub(a).normalize());
      }
    }
    platform.position.copy(target);
  }

  const tip = (p: THREE.Vector3, z: number) => new THREE.Vector3(p.x, p.y, z);
  const atBelt = BELT_Z + PART_H + CUP;
  const hover = new THREE.Vector3(PICK_X - 0.04, BELT_Y, atBelt + 0.11);
  const target = new THREE.Vector3();
  return {
    group,
    area: [x - 0.95, y - 0.6, x + 0.95, y + 0.6],
    update(time: number) {
      const n = Math.floor(time / PERIOD);
      const t = (time - n * PERIOD) / PERIOD;
      const crossing = (n + 0.25) * PERIOD; // when this cycle's part reaches the pick line
      const part = new THREE.Vector3(PICK_X + SPEED * (time - crossing), BELT_Y, atBelt);
      const slot = tray[n % tray.length];
      const drop = tip(slot, slot.z + PART_H / 2 + CUP);
      if (t < 0.25) target.lerpVectors(hover, tip(part, atBelt), ease(t / 0.25));
      else if (t < 0.33) target.copy(part); // tracking the part while the cup takes it
      else if (t < 0.62) {
        const at33 = tip(new THREE.Vector3(PICK_X + SPEED * 0.08 * PERIOD, BELT_Y, 0), atBelt);
        const k = ease((t - 0.33) / 0.29);
        target.lerpVectors(at33, drop, k).setZ(target.z + Math.sin(Math.PI * k) * 0.1);
      } else if (t < 0.7) target.copy(drop);
      else {
        const k = ease((t - 0.7) / 0.3);
        target.lerpVectors(drop, hover, k).setZ(target.z + Math.sin(Math.PI * k) * 0.05);
      }
      reach(target);
      carried.visible = t >= 0.29 && t < 0.66;
      // Belt parts: this cycle's part and the next three, each gone once the cup has it.
      for (let i = 0; i < belt.length; i++) {
        const j = n + i;
        const xj = PICK_X + SPEED * (time - (j + 0.25) * PERIOD);
        belt[i].position.set(xj, BELT_Y, BELT_Z + PART_H / 2);
        belt[i].visible = xj >= X0 && time < (j + 0.29) * PERIOD;
      }
      const filled = n % tray.length;
      placed.forEach((p, k) => {
        p.visible = k < filled || (k === filled && t >= 0.66);
      });
    },
    dispose() {
      rodGeometry.dispose();
    },
  };
}

// ================================================================== the cobots
/** A collaborative arm in the style of a UR5: coplanar links and a two-finger gripper, pointing down. */
class Cobot {
  static readonly D1 = 0.16;
  static readonly A2 = 0.42;
  static readonly A3 = 0.39;
  static readonly W1 = 0.1;
  static readonly W2 = 0.09;
  static readonly TOOL = 0.135;
  readonly root = new THREE.Group();
  readonly tip = new THREE.Group();
  private j: THREE.Group[];
  private fingers: THREE.Mesh[] = [];

  constructor(m: LabMaterials) {
    const C = Cobot;
    const ring = (b: Batcher, at: V3, rot: V3, r: number) => b.shape(cylinder(r + 0.002, 0.012, 32), m.accent, at, rot);
    const cap = (b: Batcher, at: V3, rot: V3, r: number) => b.shape(cylinder(r * 0.82, 0.006, 32), m.graphite, at, rot);
    link(
      this.root,
      [0, 0, 0],
      (b) => {
        b.shape(cylinder(0.078, 0.05, 32), m.graphite, [0, 0, 0.025]);
        ring(b, [0, 0, 0.05], [0, 0, 0], 0.076);
      },
      "cobot-base",
    );
    const j1 = link(
      this.root,
      [0, 0, 0.05],
      (b) => {
        b.shape(cylinder(0.062, 0.1, 32), m.cobotWhite, [0, 0, 0.05]);
        b.shape(cylinder(0.064, 0.13, 32), m.cobotWhite, [0, 0, C.D1 - 0.05], Y_AXIS);
        cap(b, [0, 0.066, C.D1 - 0.05], Y_AXIS, 0.064);
        ring(b, [0, -0.06, C.D1 - 0.05], Y_AXIS, 0.064);
      },
      "cobot-j1",
    );
    const j2 = link(j1, [0, 0, C.D1 - 0.05], (b) => b.shape(cylinder(0.044, C.A2, 28), m.cobotWhite, [0, 0, C.A2 / 2]), "cobot-j2");
    const j3 = link(
      j2,
      [0, 0, C.A2],
      (b) => {
        b.shape(cylinder(0.054, 0.115, 32), m.cobotWhite, [0, 0, 0], Y_AXIS);
        cap(b, [0, 0.058, 0], Y_AXIS, 0.054);
        ring(b, [0, -0.052, 0], Y_AXIS, 0.054);
        b.shape(cylinder(0.037, C.A3, 28), m.cobotWhite, [0, 0, C.A3 / 2]);
      },
      "cobot-j3",
    );
    const j4 = link(
      j3,
      [0, 0, C.A3],
      (b) => {
        b.shape(cylinder(0.043, 0.1, 28), m.cobotWhite, [0, 0, 0], Y_AXIS);
        cap(b, [0, 0.051, 0], Y_AXIS, 0.043);
        b.shape(cylinder(0.036, C.W1, 24), m.cobotWhite, [0, 0, C.W1 / 2]);
      },
      "cobot-j4",
    );
    const j5 = link(
      j4,
      [0, 0, C.W1],
      (b) => {
        b.shape(cylinder(0.043, 0.09, 28), m.cobotWhite, [0, 0, 0.045]);
        ring(b, [0, 0, 0.004], [0, 0, 0], 0.043);
      },
      "cobot-j5",
    );
    const j6 = link(
      j5,
      [0, 0, C.W2],
      (b) => {
        b.shape(cylinder(0.034, 0.012, 24), m.aluminium, [0, 0, 0.006]);
        b.shape(rounded(0.075, 0.085, 0.06, 0.012), m.graphite, [0, 0, 0.042]);
      },
      "cobot-j6",
    );
    const finger = rounded(0.014, 0.024, 0.056, 0.004);
    for (const side of [1, -1]) {
      const f = mesh(finger, m.aluminium, j6, [0.02 * side, 0, 0.098]);
      f.userData.side = side;
      this.fingers.push(f);
    }
    this.tip.position.set(0, 0, C.TOOL);
    j6.add(this.tip);
    this.j = [j1, j2, j3, j4, j5, j6];
  }

  set(q: number[], open: number): void {
    this.j[0].rotation.z = q[0];
    this.j[1].rotation.y = q[1];
    this.j[2].rotation.y = q[2];
    this.j[3].rotation.y = q[3];
    for (const f of this.fingers) f.position.x = (0.013 + 0.017 * open) * (f.userData.side as number);
  }

  /** Joints that put the fingertips at p (the arm's base frame), pointing straight down. */
  static solve(p: THREE.Vector3): number[] {
    const C = Cobot;
    const q1 = Math.atan2(p.y, p.x);
    const r = Math.hypot(p.x, p.y);
    const dz = p.z + C.W1 + C.W2 + C.TOOL - C.D1;
    const d = Math.min(Math.hypot(r, dz), C.A2 + C.A3 - 1e-4);
    const beta = Math.acos(THREE.MathUtils.clamp((C.A2 * C.A2 + d * d - C.A3 * C.A3) / (2 * C.A2 * d), -1, 1));
    const a1 = Math.atan2(dz, r) + beta;
    const a2 = Math.atan2(dz - C.A2 * Math.sin(a1), r - C.A2 * Math.cos(a1));
    return [q1, Math.PI / 2 - a1, a1 - a2, a2 + Math.PI / 2];
  }
}

function cobotTable(m: LabMaterials, x: number, y: number, yaw: number): Station {
  const group = new THREE.Group();
  group.name = "lab-cobots";
  group.position.set(x, y, 0);
  group.rotation.z = yaw;
  const TOP = 0.86;
  const s = new Batcher();
  s.box(m.tabletop, [1.9, 0.95, 0.04], [0, 0, TOP - 0.02], [0, 0, 0], true);
  s.box(m.graphite, [1.86, 0.91, 0.05], [0, 0, TOP - 0.065], [0, 0, 0], true);
  for (const [px, py] of [
    [-0.88, -0.42],
    [0.88, -0.42],
    [-0.88, 0.42],
    [0.88, 0.42],
  ])
    s.box(m.aluminium, [0.05, 0.05, TOP - 0.09], [px, py, (TOP - 0.09) / 2], [0, 0, 0], true);
  for (const py of [-0.4, 0.4]) s.box(m.aluminium, [1.7, 0.04, 0.04], [0, py, 0.2]);
  // A tray each side, the fixture in the middle where parts change hands, and a status screen.
  for (const tx of [-0.62, 0.62]) s.box(m.tray, [0.34, 0.24, 0.025], [tx, -0.24, TOP + 0.0125]);
  s.box(m.aluminium, [0.12, 0.12, 0.04], [0, 0.2, TOP + 0.02]);
  // The station's status screen, at the back corner behind arm B's reach.
  const SX = 0.84;
  const SY = 0.36;
  s.box(m.graphite, [0.04, 0.04, 0.4], [SX, SY, TOP + 0.2]);
  s.box(m.graphite, [0.03, 0.56, 0.34], [SX, SY, TOP + 0.52], [0, 0, 0], true);
  for (const face of [SX - 0.017, SX + 0.017]) {
    // The same status lines on both faces of the screen.
    s.box(m.ledGreen, [0.005, 0.5, 0.02], [face, SY, TOP + 0.38]);
    s.box(m.ledBlue, [0.005, 0.36, 0.012], [face, SY - 0.06, TOP + 0.6]);
    s.box(m.ledOrange, [0.005, 0.22, 0.012], [face, SY - 0.13, TOP + 0.56]);
    s.box(m.ledBlue, [0.005, 0.3, 0.012], [face, SY - 0.09, TOP + 0.52]);
  }
  group.add(s.build("cobot-table"));
  decal(group, m.blob, 0, 0, 2.4, 1.4);

  const arms = [new Cobot(m), new Cobot(m)];
  arms[0].root.position.set(-0.6, 0.12, TOP);
  arms[1].root.position.set(0.6, 0.12, TOP);
  arms[1].root.rotation.z = Math.PI;
  group.add(arms[0].root, arms[1].root);
  // Each arm rests poised in front of itself.
  const homes = arms.map((arm) => new THREE.Vector3(0.32, 0, 0.3).applyAxisAngle(UP, arm.root.rotation.z).add(arm.root.position));

  const PART_H = 0.05;
  const partGeometry = cylinder(0.021, PART_H, 18);
  const sides = [-1, 1]; // arm 0 works the left tray, arm 1 the right
  const slot = (side: number, k: number) => new THREE.Vector3(0.62 * side + ((k % 3) - 1) * 0.09, -0.24 + (Math.floor(k / 3) - 0.5) * 0.09, TOP + 0.025 + PART_H / 2);
  const fixture = new THREE.Vector3(0, 0.2, TOP + 0.04 + PART_H / 2);
  const parts = [0, 1, 2, 3, 4, 5].map(() => mesh(partGeometry, m.part, group));

  const P = 4.9; // one handover
  const ROUND = 6 * P + 3.5; // six parts one way, then back the other way
  const lift = (p: THREE.Vector3) => p.clone().setZ(p.z + 0.14);
  const tween = (a: THREE.Vector3, b: THREE.Vector3, k: number) => a.clone().lerp(b, ease(k));

  /** Fingertip target (table frame) and opening for the arm handing a part from `from` to the fixture. */
  function giver(tau: number, from: THREE.Vector3, home: THREE.Vector3): [THREE.Vector3, number] {
    const upFrom = lift(from);
    const upFix = lift(fixture);
    if (tau < 0.9) return [tween(home, upFrom, tau / 0.9), 1];
    if (tau < 1.4) return [tween(upFrom, from, (tau - 0.9) / 0.5), 1];
    if (tau < 1.6) return [from.clone(), 1 - ease((tau - 1.4) / 0.2)];
    if (tau < 2.1) return [tween(from, upFrom, (tau - 1.6) / 0.5), 0];
    if (tau < 3.0) return [tween(upFrom, upFix, (tau - 2.1) / 0.9), 0];
    if (tau < 3.4) return [tween(upFix, fixture, (tau - 3.0) / 0.4), 0];
    if (tau < 3.6) return [fixture.clone(), ease((tau - 3.4) / 0.2)];
    if (tau < 4.0) return [tween(fixture, upFix, (tau - 3.6) / 0.4), 1];
    return [tween(upFix, home, (tau - 4.0) / 0.9), 1];
  }
  /** The arm taking the part off the fixture and into its tray at `to`. */
  function taker(tau: number, to: THREE.Vector3, home: THREE.Vector3): [THREE.Vector3, number] {
    const upTo = lift(to);
    const upFix = lift(fixture);
    if (tau < 4.3) return [tween(home, upFix, (tau - 3.4) / 0.9), 1];
    if (tau < 4.7) return [tween(upFix, fixture, (tau - 4.3) / 0.4), 1];
    if (tau < 4.9) return [fixture.clone(), 1 - ease((tau - 4.7) / 0.2)];
    if (tau < 5.4) return [tween(fixture, upFix, (tau - 4.9) / 0.5), 0];
    if (tau < 6.3) return [tween(upFix, upTo, (tau - 5.4) / 0.9), 0];
    if (tau < 6.7) return [tween(upTo, to, (tau - 6.3) / 0.4), 0];
    if (tau < 6.9) return [to.clone(), ease((tau - 6.7) / 0.2)];
    if (tau < 7.3) return [tween(to, upTo, (tau - 6.9) / 0.4), 1];
    return [tween(upTo, home, (tau - 7.3) / 0.9), 1];
  }

  const local = new THREE.Vector3();
  const world = new THREE.Vector3();
  return {
    group,
    area: [x - 1.2, y - 1.2, x + 1.2, y + 1.2],
    update(time: number) {
      const tasks: ([THREE.Vector3, number] | null)[] = [null, null];
      const holder: (number | null)[] = parts.map(() => null);
      const round = Math.floor(time / ROUND);
      const inRound = time - round * ROUND;
      const g = round % 2; // which arm gives this round
      const tk = 1 - g;
      for (let k = 0; k < 6; k++) {
        const tau = inRound - k * P;
        const from = slot(sides[g], k);
        const to = slot(sides[tk], k);
        if (tau >= 0 && tau < P) tasks[g] = giver(tau, from, homes[g]);
        if (tau >= 3.4 && tau < 8.2) tasks[tk] = taker(tau, to, homes[tk]);
        if (tau < 1.5) parts[k].position.copy(from);
        else if (tau < 3.5) holder[k] = g;
        else if (tau < 4.8) parts[k].position.copy(fixture);
        else if (tau < 6.8) holder[k] = tk;
        else parts[k].position.copy(to);
      }
      arms.forEach((arm, i) => {
        const [tipAt, open] = tasks[i] ?? [homes[i], 1];
        local.copy(tipAt).sub(arm.root.position).applyAxisAngle(UP, -arm.root.rotation.z);
        arm.set(Cobot.solve(local), open);
      });
      group.updateMatrixWorld(true);
      parts.forEach((part, k) => {
        const h = holder[k];
        if (h === null) return;
        arms[h].tip.getWorldPosition(world);
        part.position.copy(group.worldToLocal(world));
      });
    },
    dispose() {},
  };
}

// ================================================================== the AMR
function amr(m: LabMaterials, curve: THREE.CatmullRomCurve3): Station {
  const group = new THREE.Group();
  group.name = "lab-amr";
  const body = new THREE.Group();
  group.add(body);
  const b = new Batcher();
  b.shape(rounded(0.95, 0.66, 0.26, 0.07), m.amrWhite, [0, 0, 0.19], [0, 0, 0], [1, 1, 1], true);
  b.shape(rounded(0.97, 0.68, 0.07, 0.03), m.rubber, [0, 0, 0.055], [0, 0, 0], [1, 1, 1], true);
  b.box(m.graphite, [0.86, 0.56, 0.02], [0, 0, 0.33]);
  b.box(m.ledBlue, [0.012, 0.44, 0.02], [0.485, 0, 0.24]);
  b.box(m.ledOrange, [0.012, 0.44, 0.02], [-0.485, 0, 0.24]);
  for (const py of [0.335, -0.335]) b.box(m.ledBlue, [0.7, 0.012, 0.016], [0, py, 0.27]);
  b.box(m.carton, [0.56, 0.4, 0.36], [-0.05, 0, 0.52], [0, 0, 0], true);
  body.add(b.build("amr-body"));
  const lidar = mesh(cylinder(0.055, 0.07, 20), m.graphite, body, [0.36, 0, 0.375]);
  mesh(cylinder(0.04, 0.02, 20), m.ledGreen, lidar, [0, 0, 0.04]);
  decal(body, m.blob, 0, 0, 1.5, 1.1);
  const length = curve.getLength();
  const SPEED = 0.85;
  const p = new THREE.Vector3();
  const tan = new THREE.Vector3();
  return {
    group,
    area: [0, 0, 0, 0],
    update(time: number) {
      const u = ((time * SPEED) / length) % 1;
      curve.getPointAt(u, p);
      curve.getTangentAt(u, tan);
      body.position.set(p.x, p.y, 0);
      body.rotation.z = Math.atan2(tan.y, tan.x);
      lidar.rotation.z = time * 9;
    },
    dispose() {},
  };
}

// ================================================================== the lab
export interface Lab {
  group: THREE.Group;
  update(dt: number): void;
  fit(fp: Footprint): void;
  setMotion(on: boolean): void;
  dispose(): void;
}

/** Hall-local layout, kept clear of the walkways, the machine line and the cell (cell.ts) at its usual size. */
export function buildLab(): Lab {
  const m = new LabMaterials();
  const group = new THREE.Group();
  group.name = "factory-lab";
  const stations: Station[] = [palletizer(m, -7.6, 2.6), deltaPicker(m, -1.4, 6.65), cobotTable(m, 10.7, 2.4, Math.PI / 2)];
  const loop = new THREE.CatmullRomCurve3(
    [
      [-4.6, -3.2],
      [-4.6, 3.7],
      [-4.25, 4.55],
      [-3.4, 4.9],
      [7.4, 4.9],
      [8.25, 4.55],
      [8.6, 3.7],
      [8.6, -3.2],
      [8.25, -4.05],
      [7.4, -4.4],
      [-3.4, -4.4],
      [-4.25, -4.05],
    ].map(([px, py]) => new THREE.Vector3(px, py, 0)),
    true,
    "centripetal",
  );
  const robot = amr(m, loop);
  const all = [...stations, robot];
  for (const s of all) group.add(s.group);
  decal(group, m.lettering, -4.9, 7.05, 4, 1, 0.004);

  let time = 0;
  let moving = true;
  const tick = () => {
    for (const s of all) if (s.group.visible) s.update(time);
  };
  tick();
  return {
    group,
    update(dt: number) {
      if (!moving) return;
      time += Math.min(dt, 0.1);
      tick();
    },
    fit(fp: Footprint) {
      // The cell's guarding, cabinet, panel and conveyor, as cell.ts lays them out, with a margin.
      const g = 0.9;
      const cell: Rect = [fp.x0 - g - 1.6, fp.y0 - g - 0.6, fp.x1 + g + 4.9, fp.y1 + g + 1.9];
      const overlaps = (a: Rect, b: Rect) => a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1];
      for (const s of stations) s.group.visible = !overlaps(s.area, cell);
      // The AMR's loop has to run outside the cell on every side.
      robot.group.visible = cell[0] > -4.15 && cell[1] > -3.95 && cell[2] < 8.15 && cell[3] < 4.45;
      tick();
    },
    setMotion(on: boolean) {
      moving = on;
    },
    dispose() {
      for (const s of all) s.dispose();
      group.traverse((o) => {
        const meshObj = o as THREE.Mesh;
        if (meshObj.isMesh && !meshObj.userData.sharedGeometry) meshObj.geometry.dispose();
      });
      m.dispose();
    },
  };
}
