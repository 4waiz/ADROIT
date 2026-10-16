// SPDX-License-Identifier: LicenseRef-BRIDGE
// What surrounds the simulated cell, laid out around its footprint. The arms BRIDGE drives are
// collaborative robots, which plants run without fences: the cell is a painted boundary with a
// keep-clear band at the operator's side and a laser scanner at the bench, a control cabinet
// whose stack light shows what the simulation is doing, an operator panel whose screen says it
// in words, and a conveyor behind that feeds it. Rebuilt whenever a different cell is loaded.

import * as THREE from "three";
import { faceToward, instances, place, type Props } from "./hall";
import { Batcher, cylinder, floorQuad, Materials, PAINT, rounded } from "./kit";
import { floorText, hazardStripes, ScreenTexture, sign } from "./textures";

export interface Footprint {
  x0: number;
  x1: number;
  y0: number;
  y1: number;
}

export type MachineState = "running" | "program" | "paused" | "fault" | "offline";

const LAMPS = ["red", "amber", "green", "blue"] as const;
type Lamp = (typeof LAMPS)[number];
const LAMP_COLOR: Record<Lamp, number> = { red: 0xff3b30, amber: 0xffa000, green: 0x34d058, blue: 0x3d8bff };

export class StackLight {
  readonly group = new THREE.Group();
  private segments = new Map<Lamp, THREE.Mesh>();
  private on = new Map<Lamp, THREE.Material>();
  private off = new Map<Lamp, THREE.Material>();
  private state: MachineState = "offline";
  private blink = 0;

  constructor(m: Materials) {
    const pole = new THREE.Mesh(cylinder(0.018, 0.22), m.steel(0xb9bec3, 0.3));
    pole.position.z = 0.11;
    this.group.add(pole);
    LAMPS.forEach((lamp, k) => {
      // Top to bottom: red, amber, green, blue (IEC 60204-1 colours).
      const z = 0.22 + (LAMPS.length - 1 - k) * 0.075 + 0.0375;
      this.on.set(lamp, m.glow(LAMP_COLOR[lamp], 9));
      this.off.set(lamp, new THREE.MeshStandardMaterial({ color: new THREE.Color(LAMP_COLOR[lamp]).multiplyScalar(0.3), roughness: 0.2, transparent: true, opacity: 0.92 }));
      const seg = new THREE.Mesh(cylinder(0.035, 0.07), this.off.get(lamp)!);
      seg.position.z = z;
      this.group.add(seg);
      this.segments.set(lamp, seg);
    });
    const cap = new THREE.Mesh(cylinder(0.036, 0.02), m.paint(0x202326, 0.4, 0.2));
    cap.position.z = 0.22 + LAMPS.length * 0.075 + 0.01;
    this.group.add(cap);
  }

  set(state: MachineState): void {
    this.state = state;
    this.apply();
  }

  update(dt: number): void {
    if (this.state !== "fault") return;
    this.blink = (this.blink + dt) % 1.0;
    this.apply();
  }

  private apply(): void {
    const lit: Record<MachineState, Lamp[]> = {
      running: ["green"],
      program: ["green", "blue"],
      paused: ["amber"],
      fault: this.blink < 0.5 ? ["red"] : [],
      offline: [],
    };
    const on = new Set(lit[this.state]);
    for (const lamp of LAMPS) this.segments.get(lamp)!.material = (on.has(lamp) ? this.on : this.off).get(lamp)!;
  }
}

export interface CellSurround {
  group: THREE.Group;
  stack: StackLight;
  screen: ScreenTexture;
  /** Where a camera standing at the operator's position should look from, and at. */
  operator: { eye: THREE.Vector3; target: THREE.Vector3 };
  dispose(): void;
}

export function buildSurround(m: Materials, fp: Footprint, props: Props, logoUrl: string): CellSurround {
  const group = new THREE.Group();
  group.name = "factory-cell";
  const b = new Batcher();
  const yellow = m.paint(PAINT.safetyYellow, 0.45, 0.25);
  const owned: THREE.Material[] = [];
  const own = <T extends THREE.Material>(mat: T): T => {
    owned.push(mat);
    return mat;
  };

  // ---------------------------------------------------------- floor marking
  // The boundary is painted 0.9 m outside the footprint, all round; the east side is where the
  // operator steps in, so it carries a keep-clear band in hazard stripes.
  const g = 0.9;
  const bx0 = fp.x0 - g;
  const bx1 = fp.x1 + g;
  const by0 = fp.y0 - g;
  const by1 = fp.y1 + g;
  const lw = 0.1;
  const z = 0.0025;
  b.box(yellow, [bx1 - bx0 + lw, lw, 0.004], [(bx0 + bx1) / 2, by0, z]);
  b.box(yellow, [bx1 - bx0 + lw, lw, 0.004], [(bx0 + bx1) / 2, by1, z]);
  b.box(yellow, [lw, by1 - by0, 0.004], [bx0, (by0 + by1) / 2, z]);
  b.box(yellow, [lw, by1 - by0, 0.004], [bx1, (by0 + by1) / 2, z]);
  const stripes = hazardStripes();
  stripes.repeat.set(0.5, (by1 - by0) / 0.5);
  const band = new THREE.Mesh(new THREE.PlaneGeometry(0.4, by1 - by0), own(new THREE.MeshStandardMaterial({ map: stripes, roughness: 0.5, polygonOffset: true, polygonOffsetFactor: -2 })));
  band.position.set(bx1 + 0.25, (by0 + by1) / 2, 0.003);
  band.receiveShadow = true;
  group.add(band);
  const label = own(new THREE.MeshStandardMaterial({ map: floorText("CELL 01", "#f2b705"), transparent: true, depthWrite: false, roughness: 0.5, polygonOffset: true, polygonOffsetFactor: -2 }));
  group.add(floorQuad(label, 1.6, 0.4, bx1 - 1.0, by0 - 0.42, 0, 0.004));

  // A safety laser scanner at the bench's operator corner, watching the boundary.
  b.shape(rounded(0.14, 0.14, 0.15, 0.015), yellow, [fp.x1 - 0.02, fp.y0 - 0.02, 0.075], [0, 0, 0], [1, 1, 1], true);
  b.shape(cylinder(0.055, 0.06), m.paint(0x111214, 0.15, 0.2), [fp.x1 - 0.02, fp.y0 - 0.02, 0.18]);

  // ---------------------------------------------------------- control cabinet
  const cabX = bx0 - 0.75;
  const cabY = by0 + 0.35;
  const cabinet = m.panel(PAINT.lightGrey);
  b.shape(rounded(0.62, 0.82, 2.0, 0.02), cabinet, [cabX, cabY, 1.0 + 0.1], [0, 0, 0], [1, 1, 1], true);
  b.box(m.paint(0x2a2e33, 0.6, 0.2), [0.64, 0.84, 0.1], [cabX, cabY, 0.05]);
  b.box(m.paint(0xb9bdc0, 0.4, 0.3), [0.012, 0.76, 1.9], [cabX + 0.316, cabY, 1.1]);
  b.box(m.steel(0x9ea4aa, 0.3), [0.03, 0.03, 0.22], [cabX + 0.33, cabY + 0.28, 1.15]);
  // Main switch (red on yellow).
  b.box(yellow, [0.012, 0.16, 0.16], [cabX + 0.322, cabY - 0.22, 1.45]);
  b.shape(cylinder(0.035, 0.05), m.paint(0xc8102e, 0.35, 0.1), [cabX + 0.345, cabY - 0.22, 1.45], [0, Math.PI / 2, 0]);
  const warn = new THREE.Mesh(
    new THREE.PlaneGeometry(0.26, 0.195),
    own(new THREE.MeshStandardMaterial({ map: sign({ kind: "warning", title: "ROBOT CELL", lines: ["Authorised personnel only"] }), roughness: 0.55 })),
  );
  warn.position.set(cabX + 0.324, cabY + 0.05, 1.68);
  faceToward(warn, 1, 0);
  group.add(warn);
  const stack = new StackLight(m);
  stack.group.position.set(cabX + 0.18, cabY + 0.28, 2.1);
  group.add(stack.group);

  // ---------------------------------------------------------- operator panel
  const hmiX = bx1 + 0.85;
  const hmiY = fp.y1 + 0.1;
  b.shape(cylinder(0.035, 1.05), m.steel(0xaab0b6, 0.35), [hmiX, hmiY, 0.525], [0, 0, 0], [1, 1, 1], true);
  b.shape(rounded(0.36, 0.36, 0.02, 0.005), m.paint(0x2a2e33, 0.5, 0.3), [hmiX, hmiY, 0.01]);
  const screen = new ScreenTexture(logoUrl);
  screen.draw({ title: "CELL 01", status: "OFFLINE", tone: "idle", detail: "Waiting for the simulation" });
  const head = new THREE.Group();
  head.position.set(hmiX, hmiY, 1.22);
  // Face the default viewing direction (towards +x, -y), tilted back for a standing operator.
  head.rotation.set(0, 0, Math.atan2(1.0, 0.95), "ZXY");
  const housing = new THREE.Mesh(rounded(0.58, 0.08, 0.5, 0.02), m.paint(0x30353b, 0.45, 0.3));
  housing.castShadow = true;
  // The screen on the housing's front (local -y), upright and unmirrored.
  const face = new THREE.Mesh(new THREE.PlaneGeometry(0.5, 0.31), own(new THREE.MeshBasicMaterial({ map: screen.texture, color: new THREE.Color(1.15, 1.15, 1.15) })));
  face.position.set(0, -0.0405, 0.06);
  face.rotation.x = Math.PI / 2;
  const tilt = new THREE.Group();
  tilt.rotation.x = -0.32;
  tilt.add(housing, face);
  // A stop button under the screen: scenery, like the rest of the room. The page's own Stop acts
  // on the simulation and is not an emergency stop for any machine (see the Twin panel).
  const button = new THREE.Mesh(cylinder(0.032, 0.03), m.paint(0xd0021b, 0.3, 0.1));
  button.position.set(0.0, -0.06, -0.17);
  button.rotation.x = Math.PI / 2;
  const collar = new THREE.Mesh(cylinder(0.048, 0.012), yellow);
  collar.position.set(0.0, -0.046, -0.17);
  collar.rotation.x = Math.PI / 2;
  tilt.add(button, collar);
  head.add(tilt);
  group.add(head);

  // ---------------------------------------------------------- conveyor
  // A roller conveyor behind the cell, running east, carrying cartons.
  const cy = by1 + 1.3;
  const c0 = bx0 - 1.2;
  const c1 = bx1 + 4.5;
  const cLen = c1 - c0;
  const cz = 0.82;
  const rail = m.paint(0x8e969d, 0.4, 0.6);
  for (const dy of [-0.32, 0.32]) b.box(rail, [cLen, 0.05, 0.12], [(c0 + c1) / 2, cy + dy, cz], [0, 0, 0], true);
  const roller = m.steel(0xc9ced2, 0.25);
  // Rollers lie across the conveyor (along y): a z-axis cylinder turned a quarter about x.
  for (let x = c0 + 0.08; x < c1 - 0.05; x += 0.12) b.shape(cylinder(0.024, 0.6, 12), roller, [x, cy, cz + 0.02], [Math.PI / 2, 0, 0], [1, 1, 1], false);
  for (let x = c0 + 0.3; x < c1; x += 1.6) {
    for (const dy of [-0.3, 0.3]) b.box(m.paint(0x5c6570, 0.5, 0.4), [0.05, 0.05, cz - 0.05], [x, cy + dy, (cz - 0.05) / 2], [0, 0, 0], true);
    b.box(m.rubber(), [0.12, 0.12, 0.02], [x, cy - 0.3, 0.01]);
    b.box(m.rubber(), [0.12, 0.12, 0.02], [x, cy + 0.3, 0.01]);
  }
  const cartons = [c0 + 0.9, c0 + 2.4, c1 - 1.5].map((x, k) => place(x, cy, cz + 0.045, k * 0.04 - 0.03));
  group.add(instances(props.box, cartons, [0.5, 0.36, 0.34], true));

  // ---------------------------------------------------------- props, kept back from the bench
  group.add(instances(props.crate, [place(bx1 + 2.8, by1 + 0.4, 0, 0.1), place(bx1 + 2.8, by1 + 0.4, 0.3, 0.12), place(bx1 + 3.35, by1 + 0.4, 0, -0.05)], [0.5, 0.34, 0.3], true));

  group.add(b.build("factory-cell-parts"));

  const centre = new THREE.Vector3((fp.x0 + fp.x1) / 2, (fp.y0 + fp.y1) / 2, 0.9);
  return {
    group,
    stack,
    screen,
    operator: { eye: new THREE.Vector3(bx1 + 1.6, by0 - 1.2, 1.65), target: centre },
    dispose() {
      screen.dispose();
      for (const mat of owned) {
        (mat as THREE.MeshStandardMaterial).map?.dispose();
        mat.dispose();
      }
      group.traverse((o) => {
        const meshObj = o as THREE.Mesh;
        if (meshObj.isMesh && !(meshObj as THREE.InstancedMesh).isInstancedMesh) meshObj.geometry.dispose();
      });
    },
  };
}
