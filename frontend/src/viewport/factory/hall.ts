// SPDX-License-Identifier: Apache-2.0
// The building and what stands in it away from the cell: a 36 x 26 m portal-frame hall with
// window bands, skylights and LED high-bays; pallet racking, a CNC machine line, a work-bench
// wall, an AMR at its charger. Local frame: origin on the floor under the cell's centre, z up.
// Nothing here is simulated; it is the room the simulated cell stands in.

import * as THREE from "three";
import { Batcher, cylinder, floorQuad, Materials, PAINT, rounded } from "./kit";
import { blobShadow, cartonFace, floorArrow, floorText, hazardStripes, sign, wallGraphic } from "./textures";

export const HALL = { x0: -16, x1: 20, y0: -13, y1: 13, eave: 8.5, ridge: 10.2 } as const;

export interface Props {
  box?: THREE.Object3D;
  crate?: THREE.Object3D;
  drum?: THREE.Object3D;
  chest?: THREE.Object3D;
  cart?: THREE.Object3D;
  extinguisher?: THREE.Object3D;
  floorSign?: THREE.Object3D;
}

/** Copies of a loaded prop as instanced meshes: one draw call per prop part, however many copies. */
export function instances(prop: THREE.Object3D | undefined, placements: THREE.Matrix4[], size: [number, number, number] | null, cast = true): THREE.Group {
  const group = new THREE.Group();
  if (!prop || !placements.length) return group;
  prop.updateMatrixWorld(true);
  const box = new THREE.Box3().setFromObject(prop);
  const dims = box.getSize(new THREE.Vector3());
  // Normalise: centred on x/y, standing on z = 0, scaled to `size` (or kept at its own size).
  const fit = new THREE.Matrix4().compose(
    new THREE.Vector3(0, 0, 0),
    new THREE.Quaternion(),
    size ? new THREE.Vector3(size[0] / dims.x, size[1] / dims.y, size[2] / dims.z) : new THREE.Vector3(1, 1, 1),
  );
  const centre = new THREE.Matrix4().makeTranslation(-(box.min.x + box.max.x) / 2, -(box.min.y + box.max.y) / 2, -box.min.z);
  prop.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (!mesh.isMesh) return;
    const local = new THREE.Matrix4().multiplyMatrices(fit, centre).multiply(mesh.matrixWorld);
    const inst = new THREE.InstancedMesh(mesh.geometry, mesh.material, placements.length);
    placements.forEach((p, k) => inst.setMatrixAt(k, new THREE.Matrix4().multiplyMatrices(p, local)));
    inst.instanceMatrix.needsUpdate = true;
    inst.castShadow = cast;
    inst.receiveShadow = true;
    inst.computeBoundingSphere();
    group.add(inst);
  });
  return group;
}

export function place(x: number, y: number, z = 0, yaw = 0): THREE.Matrix4 {
  return new THREE.Matrix4().compose(new THREE.Vector3(x, y, z), new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 0, 1), yaw), new THREE.Vector3(1, 1, 1));
}

/** Stands a plane (x along its width, y up) upright, its face towards (nx, ny), its text unmirrored. */
export function faceToward(mesh: THREE.Object3D, nx: number, ny: number): void {
  mesh.rotation.set(Math.PI / 2, 0, Math.atan2(nx, -ny), "ZXY");
}

function hazard(repeatX: number, repeatY: number): THREE.MeshStandardMaterial {
  const tex = hazardStripes();
  tex.repeat.set(repeatX, repeatY);
  return new THREE.MeshStandardMaterial({ map: tex, roughness: 0.55, metalness: 0.1 });
}

function decal(tex: THREE.Texture, opacity = 0.92): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({ map: tex, transparent: true, opacity, roughness: 0.5, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2 });
}

export function buildHall(m: Materials, props: Props, logoUrl: string): THREE.Group {
  const hall = new THREE.Group();
  hall.name = "factory-hall";
  const W = HALL.x1 - HALL.x0;
  const D = HALL.y1 - HALL.y0;
  const cx = (HALL.x0 + HALL.x1) / 2;

  // ------------------------------------------------------------- floor
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(W, D), m.floor(W, D, 0x939a9f));
  floor.position.set(cx, 0, 0);
  floor.receiveShadow = true;
  floor.name = "factory-floor";
  hall.add(floor);

  const b = new Batcher();
  const steel = m.paint(0x9aa1a8, 0.5, 0.55);
  const darkSteel = m.paint(0x3c4248, 0.55, 0.45);
  const frame = m.paint(0x5d6670, 0.45, 0.5);
  const sky = m.glow(0xe8f0f8, 3.2);
  const windowGlow = m.glow(0xdfe8f2, 2.4);
  const led = m.glow(0xfff6ea, 7.5);

  // ------------------------------------------------------------- walls
  const walls: { a: [number, number]; b: [number, number]; inward: [number, number] }[] = [
    { a: [HALL.x0, HALL.y0], b: [HALL.x1, HALL.y0], inward: [0, 1] }, // south
    { a: [HALL.x0, HALL.y1], b: [HALL.x1, HALL.y1], inward: [0, -1] }, // north
    { a: [HALL.x0, HALL.y0], b: [HALL.x0, HALL.y1], inward: [1, 0] }, // west
    { a: [HALL.x1, HALL.y0], b: [HALL.x1, HALL.y1], inward: [-1, 0] }, // east
  ];
  const dadoH = 1.2;
  const windowZ0 = 6.1;
  const windowZ1 = 7.3;
  for (const w of walls) {
    const len = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]);
    const mx = (w.a[0] + w.b[0]) / 2;
    const my = (w.a[1] + w.b[1]) / 2;
    const yaw = Math.atan2(w.b[1] - w.a[1], w.b[0] - w.a[0]);
    // Dado: a low concrete upstand.
    const dado = new THREE.Mesh(new THREE.BoxGeometry(len, 0.25, dadoH), m.concrete(len, dadoH, 0x8b9095));
    dado.position.set(mx - w.inward[0] * 0.05, my - w.inward[1] * 0.05, dadoH / 2);
    dado.rotation.z = yaw;
    dado.receiveShadow = true;
    hall.add(dado);
    // Cladding above, in two bands around the window strip.
    for (const [z0, z1] of [
      [dadoH, windowZ0],
      [windowZ1, HALL.eave + 0.6],
    ]) {
      const h = z1 - z0;
      const panel = new THREE.Mesh(new THREE.PlaneGeometry(len, h), m.cladding(len, h));
      panel.position.set(mx - w.inward[0] * 0.12, my - w.inward[1] * 0.12, (z0 + z1) / 2);
      faceToward(panel, w.inward[0], w.inward[1]);
      panel.receiveShadow = true;
      hall.add(panel);
    }
    // The window band: glazing that glows with daylight, mullions every 1.2 m.
    b.box(windowGlow, [len, 0.04, windowZ1 - windowZ0], [mx - w.inward[0] * 0.14, my - w.inward[1] * 0.14, (windowZ0 + windowZ1) / 2], [0, 0, yaw]);
    const n = Math.floor(len / 1.2);
    for (let k = 0; k <= n; k++) {
      const t = k / n;
      const x = w.a[0] + (w.b[0] - w.a[0]) * t;
      const y = w.a[1] + (w.b[1] - w.a[1]) * t;
      b.box(darkSteel, [0.08, 0.1, windowZ1 - windowZ0 + 0.1], [x, y, (windowZ0 + windowZ1) / 2], [0, 0, yaw]);
    }
    b.box(darkSteel, [len, 0.14, 0.1], [mx - w.inward[0] * 0.12, my - w.inward[1] * 0.12, windowZ0], [0, 0, yaw]);
    b.box(darkSteel, [len, 0.14, 0.1], [mx - w.inward[0] * 0.12, my - w.inward[1] * 0.12, windowZ1], [0, 0, yaw]);
    // A cable tray along the wall at 4.2 m.
    b.box(m.steel(0xb8bdc2, 0.4), [len, 0.3, 0.08], [mx + w.inward[0] * 0.25, my + w.inward[1] * 0.25, 4.2], [0, 0, yaw]);
  }

  // Pilasters (wall columns) every 6 m on the long walls, with hazard-striped guards at their feet.
  const guard = hazard(1, 3);
  const frames: number[] = [];
  for (let x = HALL.x0; x <= HALL.x1 + 1e-6; x += 6) frames.push(x);
  for (const x of frames) {
    for (const [y, dir] of [
      [HALL.y0 + 0.2, 1],
      [HALL.y1 - 0.2, -1],
    ] as const) {
      b.box(frame, [0.32, 0.32, HALL.eave], [x, y, HALL.eave / 2], [0, 0, 0], false);
      b.box(guard, [0.46, 0.46, 1.2], [x, y + dir * 0.02, 0.6]);
    }
  }

  // ------------------------------------------------------------- roof
  const slope = Math.atan2(HALL.ridge - HALL.eave, HALL.y1);
  const rafterLen = Math.hypot(HALL.y1, HALL.ridge - HALL.eave);
  // Rafters run along y from each eave up to the ridge: boxes tilted about x by the roof pitch.
  const roof = new Batcher();
  for (const x of frames) {
    for (const side of [-1, 1]) {
      const yMid = (side * HALL.y1) / 2;
      const zMid = (HALL.eave + HALL.ridge) / 2 - 0.3;
      roof.add(new THREE.BoxGeometry(0.18, rafterLen, 0.5).toNonIndexed(), frame, new THREE.Matrix4().compose(new THREE.Vector3(x, yMid, zMid), new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -side * slope), new THREE.Vector3(1, 1, 1)));
    }
  }
  // Purlins every 1.8 m up each slope, roof sheeting above them, skylight strips in each slope.
  const roofSheet = m.paint(0x4a5058, 0.7, 0.4);
  for (const side of [-1, 1]) {
    const q = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -side * slope);
    for (let s = 0.6; s < rafterLen; s += 1.8) {
      const y = side * (HALL.y1 - (s * HALL.y1) / rafterLen);
      const z = HALL.eave + (s / rafterLen) * (HALL.ridge - HALL.eave) - 0.02;
      roof.add(new THREE.BoxGeometry(W, 0.06, 0.16).toNonIndexed(), darkSteel, new THREE.Matrix4().compose(new THREE.Vector3(cx, y, z), q, new THREE.Vector3(1, 1, 1)));
    }
    const yMid = (side * HALL.y1) / 2;
    const zMid = (HALL.eave + HALL.ridge) / 2 + 0.12;
    roof.add(new THREE.BoxGeometry(W, rafterLen + 0.4, 0.05).toNonIndexed(), roofSheet, new THREE.Matrix4().compose(new THREE.Vector3(cx, yMid, zMid), q, new THREE.Vector3(1, 1, 1)));
    // Skylights: three long strips per slope, interrupted at each frame.
    for (const s of [0.36, 0.62]) {
      const y = side * (HALL.y1 * (1 - s));
      const z = HALL.eave + s * (HALL.ridge - HALL.eave) + 0.08;
      for (let i = 0; i < frames.length - 1; i++) {
        const x0 = frames[i] + 0.5;
        const x1 = frames[i + 1] - 0.5;
        roof.add(new THREE.BoxGeometry(x1 - x0, 1.1, 0.04).toNonIndexed(), sky, new THREE.Matrix4().compose(new THREE.Vector3((x0 + x1) / 2, y, z), q, new THREE.Vector3(1, 1, 1)));
      }
    }
  }
  hall.add(roof.build("factory-roof", false));

  // LED high-bay fixtures, hung on rods at 7.6 m.
  for (let x = HALL.x0 + 3; x < HALL.x1 - 1; x += 4) {
    for (const y of [-8.5, -3, 3, 8.5]) {
      b.box(darkSteel, [1.3, 0.3, 0.09], [x, y, 7.64]);
      b.box(led, [1.2, 0.22, 0.02], [x, y, 7.59]);
      b.box(steel, [0.012, 0.012, 1.6], [x - 0.5, y, 8.45]);
      b.box(steel, [0.012, 0.012, 1.6], [x + 0.5, y, 8.45]);
    }
  }

  // ------------------------------------------------------------- doors
  const doorFrame = hazard(1, 6);
  const slat = m.paint(0xb4b9bd, 0.45, 0.5);
  const doors: { x: number; y: number; yaw: number; w: number; h: number }[] = [
    { x: 9, y: HALL.y1 - 0.15, yaw: 0, w: 5, h: 5 },
    { x: HALL.x0 + 0.15, y: 5, yaw: Math.PI / 2, w: 4.5, h: 4.5 },
  ];
  for (const d of doors) {
    const c = Math.cos(d.yaw);
    const s = Math.sin(d.yaw);
    for (let z = 0.1; z < d.h; z += 0.12) b.box(slat, [d.w, 0.08, 0.1], [d.x, d.y, z + 0.05], [0, 0, d.yaw]);
    b.box(doorFrame, [0.3, 0.3, d.h + 0.3], [d.x - c * (d.w / 2 + 0.15), d.y - s * (d.w / 2 + 0.15), (d.h + 0.3) / 2], [0, 0, d.yaw]);
    b.box(doorFrame, [0.3, 0.3, d.h + 0.3], [d.x + c * (d.w / 2 + 0.15), d.y + s * (d.w / 2 + 0.15), (d.h + 0.3) / 2], [0, 0, d.yaw]);
    b.box(darkSteel, [d.w + 0.6, 0.5, 0.5], [d.x, d.y, d.h + 0.3], [0, 0, d.yaw]);
  }
  // Exit sign over a personnel door on the north wall.
  const exit = new THREE.Mesh(new THREE.PlaneGeometry(0.6, 0.3), new THREE.MeshBasicMaterial({ map: sign({ kind: "exit", title: "" }, 256, 128), color: new THREE.Color(1.6, 1.6, 1.6) }));
  exit.position.set(-9, HALL.y1 - 0.2, 2.6);
  faceToward(exit, 0, -1);
  hall.add(exit);
  b.box(m.paint(0x6c747c, 0.5, 0.3), [1.0, 0.06, 2.1], [-9, HALL.y1 - 0.18, 1.05]);

  // ------------------------------------------------------------- racking (west wall)
  const upright = m.paint(PAINT.signalBlue, 0.4, 0.4);
  const beam = m.paint(PAINT.orange, 0.4, 0.35);
  const pallet = m.paint(0xb08a5a, 0.85, 0);
  const wrap = new THREE.MeshPhysicalMaterial({ color: 0xdfe4e8, roughness: 0.25, metalness: 0, transparent: true, opacity: 0.85, clearcoat: 0.6, clearcoatRoughness: 0.2 });
  const rackX = HALL.x0 + 1.1;
  const levels = [0.15, 1.75, 3.35, 4.95];
  // Cartons on the racking are seen from metres away: a light procedural carton, not the scan.
  const carton = new THREE.MeshStandardMaterial({ map: cartonFace(), roughness: 0.85, metalness: 0 });
  const cratePlacements: THREE.Matrix4[] = [];
  let seed = 3;
  const rand = () => {
    seed = (seed * 16807) % 2147483647;
    return seed / 2147483647;
  };
  for (let y = HALL.y0 + 2.5; y < 1.5; y += 2.8) {
    for (const dy of [0, 2.8]) {
      if (y + dy > 1.6) continue;
      b.box(upright, [0.09, 0.07, 6.0], [rackX - 0.5, y + dy, 3.0], [0, 0, 0], false);
      b.box(upright, [0.09, 0.07, 6.0], [rackX + 0.5, y + dy, 3.0], [0, 0, 0], false);
      for (let z = 0.4; z < 6; z += 0.9) b.box(upright, [1.0, 0.03, 0.03], [rackX, y + dy, z], [0, 0, 0]);
    }
    for (const z of levels.slice(1)) {
      b.box(beam, [0.05, 2.75, 0.12], [rackX - 0.5, y + 1.4, z], [0, 0, 0]);
      b.box(beam, [0.05, 2.75, 0.12], [rackX + 0.5, y + 1.4, z], [0, 0, 0]);
    }
    for (const z of levels) {
      for (const py of [y + 0.75, y + 2.05]) {
        const zz = z + (z > 0.2 ? 0.06 : 0);
        b.box(pallet, [1.15, 0.78, 0.14], [rackX, py, zz + 0.07]);
        const r = rand();
        if (r < 0.45) {
          // A stack of cartons.
          for (const [ox, oy] of [
            [-0.29, -0.2],
            [0.29, -0.2],
            [-0.29, 0.2],
            [0.29, 0.2],
          ]) {
            b.box(carton, [0.56, 0.38, 0.4], [rackX + ox, py + oy, zz + 0.14 + 0.2], [0, 0, rand() * 0.06 - 0.03]);
            if (z < 4.9 && rand() < 0.7) b.box(carton, [0.56, 0.38, 0.4], [rackX + ox, py + oy, zz + 0.54 + 0.2], [0, 0, rand() * 0.06 - 0.03]);
          }
        } else if (r < 0.75) {
          // A shrink-wrapped load.
          b.box(wrap, [1.1, 0.76, 0.9 + rand() * 0.3], [rackX, py, zz + 0.14 + 0.5], [0, 0, 0]);
        } else {
          for (let k = 0; k < 4; k++) cratePlacements.push(place(rackX + (k % 2 ? 0.28 : -0.28), py + (k < 2 ? -0.18 : 0.18), zz + 0.14));
        }
      }
    }
  }
  hall.add(instances(props.crate, cratePlacements, [0.5, 0.34, 0.3], false));

  // ------------------------------------------------------------- CNC machine line (north side)
  const enclosure = m.panel(PAINT.lightGrey);
  const base = m.panel(PAINT.anthracite);
  const accent = m.paint(0x1f4f8f, 0.35, 0.3);
  const glass = m.glass();
  const screen = m.glow(0x4fa3ff, 1.6);
  const green = m.glow(0x3ddc84, 5);
  for (const x of [-2, 2.6, 7.2, 11.8]) {
    const y = HALL.y1 - 3.4;
    b.shape(rounded(3.2, 2.2, 0.55, 0.05), base, [x, y, 0.275], [0, 0, 0], [1, 1, 1], false);
    b.shape(rounded(3.2, 2.2, 2.05, 0.08), enclosure, [x, y, 0.55 + 1.025], [0, 0, 0], [1, 1, 1], false);
    b.box(accent, [3.22, 2.22, 0.12], [x, y, 2.66]);
    // The door, its window and its handle, on the aisle side.
    b.box(m.paint(0xd2d4d1, 0.38, 0.3), [1.6, 0.04, 1.6], [x - 0.35, y - 1.12, 1.55]);
    b.box(glass, [1.1, 0.02, 0.9], [x - 0.35, y - 1.145, 1.75]);
    b.box(m.steel(0xcfd3d6, 0.25), [0.04, 0.05, 0.5], [x + 0.35, y - 1.17, 1.5]);
    // The operator pendant with its screen.
    b.box(darkSteel, [0.06, 0.06, 0.5], [x + 1.25, y - 1.15, 2.25]);
    b.shape(rounded(0.5, 0.12, 0.62, 0.02), m.paint(0x2b3036, 0.4, 0.3), [x + 1.25, y - 1.22, 1.75], [0, 0, 0], [1, 1, 1], false);
    b.box(screen, [0.36, 0.01, 0.26], [x + 1.25, y - 1.285, 1.86]);
    // A stack light, running.
    b.shape(cylinder(0.03, 0.4), steel, [x + 1.45, y + 0.8, 2.92]);
    b.shape(cylinder(0.04, 0.08), green, [x + 1.45, y + 0.8, 3.16]);
    b.shape(cylinder(0.04, 0.16), m.paint(0x5a2020, 0.3, 0), [x + 1.45, y + 0.8, 3.28]);
    // Chip conveyor and coolant tank beside it.
    b.shape(rounded(0.7, 1.6, 0.9, 0.03), base, [x + 1.95, y, 0.45], [0, 0, 0], [1, 1, 1], false);
  }
  // Yellow keep-clear lines around the machine line, and a walkway in front of it.
  const yellowPaint = m.paint(PAINT.safetyYellow, 0.5, 0);
  const lineZ = 0.0025;
  const lineY = HALL.y1 - 3.4 - 1.6;
  b.box(yellowPaint, [16.8, 0.1, 0.004], [4.9, lineY, lineZ]);
  b.box(yellowPaint, [16.8, 0.1, 0.004], [4.9, HALL.y1 - 1.6, lineZ]);
  const keepClear = HALL.y1 - 1.6 - lineY;
  for (const x of [-3.7, 13.5]) b.box(yellowPaint, [0.1, keepClear, 0.004], [x, (lineY + HALL.y1 - 1.6) / 2, lineZ]);

  // Walkways: green lanes edged in white, with arrows.
  const walk = m.paint(0x2f7d4b, 0.55, 0);
  const white = m.paint(0xeeeeea, 0.5, 0);
  const lanes: { x: number; y: number; w: number; h: number }[] = [
    { x: 2, y: -7.8, w: 34, h: 1.6 },
    { x: -10.5, y: 0, w: 1.6, h: 24 },
  ];
  for (const l of lanes) {
    b.box(walk, [l.w, l.h, 0.003], [l.x, l.y, 0.0015]);
    if (l.w > l.h) {
      b.box(white, [l.w, 0.08, 0.004], [l.x, l.y - l.h / 2, lineZ]);
      b.box(white, [l.w, 0.08, 0.004], [l.x, l.y + l.h / 2, lineZ]);
    } else {
      b.box(white, [0.08, l.h, 0.004], [l.x - l.w / 2, l.y, lineZ]);
      b.box(white, [0.08, l.h, 0.004], [l.x + l.w / 2, l.y, lineZ]);
    }
  }
  const arrowMat = decal(floorArrow("#f2f2ee"), 0.85);
  for (const x of [-12, -4, 6, 14]) hall.add(floorQuad(arrowMat, 0.6, 1.2, x, -7.8, -Math.PI / 2, 0.004));
  for (const y of [-6, 6]) hall.add(floorQuad(arrowMat, 0.6, 1.2, -10.5, y, 0, 0.004));

  // ------------------------------------------------------------- bench wall (south side)
  const benchTop = m.paint(0x8b8f93, 0.35, 0.65);
  const benchLeg = m.paint(PAINT.anthracite, 0.5, 0.3);
  for (const x of [-8, -5.2, 6.5, 9.3]) {
    const y = HALL.y0 + 1.0;
    b.box(benchTop, [2.4, 0.8, 0.05], [x, y, 0.9]);
    for (const [ox, oy] of [
      [-1.12, -0.34],
      [1.12, -0.34],
      [-1.12, 0.34],
      [1.12, 0.34],
    ])
      b.box(benchLeg, [0.06, 0.06, 0.88], [x + ox, y + oy, 0.44]);
    b.box(benchLeg, [2.3, 0.7, 0.03], [x, y, 0.18]);
    // A perforated tool board above it.
    b.box(m.paint(0x9aa0a6, 0.6, 0.3), [2.4, 0.03, 1.0], [x, HALL.y0 + 0.4, 1.7]);
  }
  const chests: THREE.Matrix4[] = [place(-3.2, HALL.y0 + 0.9, 0, Math.PI / 2), place(11.6, HALL.y0 + 0.9, 0, Math.PI / 2)];
  hall.add(instances(props.chest, chests, null, false));
  hall.add(instances(props.cart, [place(4.2, HALL.y0 + 1.6, 0, 0.3)], null, false));
  hall.add(instances(props.floorSign, [place(-9.2, -6.6, 0, 0.5)], null, false));

  // Drums on a spill pallet in the south-west corner, cartons waiting by the door.
  b.box(m.paint(PAINT.safetyYellow, 0.55, 0.1), [1.3, 1.3, 0.3], [HALL.x0 + 2.4, HALL.y0 + 1.6, 0.15]);
  hall.add(
    instances(
      props.drum,
      [place(HALL.x0 + 2.1, HALL.y0 + 1.3, 0.3), place(HALL.x0 + 2.7, HALL.y0 + 1.3, 0.3, 1), place(HALL.x0 + 2.1, HALL.y0 + 1.9, 0.3, 2), place(HALL.x0 + 2.7, HALL.y0 + 1.9, 0.3, 3)],
      [0.58, 0.58, 0.92],
      false,
    ),
  );
  for (let k = 0; k < 6; k++) b.box(carton, [0.56, 0.38, 0.4], [7 + (k % 3) * 0.62, HALL.y1 - 1.0 - Math.floor(k / 3) * 0.42, 0.2], [0, 0, rand() * 0.2]);
  for (let k = 0; k < 2; k++) b.box(carton, [0.56, 0.38, 0.4], [7.3 + k * 0.62, HALL.y1 - 1.2, 0.6], [0, 0, rand() * 0.2]);

  // ------------------------------------------------------------- AMR at its charger (east side)
  const amrX = HALL.x1 - 2.2;
  const amrY = -2;
  b.shape(rounded(1.0, 0.7, 0.32, 0.06), m.paint(0xe9eaea, 0.35, 0.1), [amrX, amrY, 0.19]);
  b.shape(rounded(1.02, 0.72, 0.06, 0.03), m.rubber(), [amrX, amrY, 0.06]);
  b.box(m.glow(0x4b9dff, 4), [0.02, 0.5, 0.02], [amrX - 0.51, amrY, 0.26]);
  b.shape(cylinder(0.06, 0.08), m.rubber(), [amrX - 0.4, amrY, 0.39]);
  b.box(darkSteel, [0.3, 0.6, 1.2], [amrX + 0.75, amrY, 0.6]);
  b.box(m.glow(0x3ddc84, 3), [0.01, 0.06, 0.06], [amrX + 0.59, amrY, 1.0]);

  // ------------------------------------------------------------- safety furniture
  // Fire extinguishers on two pilasters, each under its red sign.
  const extPlaces = [place(-10, HALL.y1 - 0.55, 0, Math.PI), place(8, HALL.y0 + 0.55, 0, 0)];
  hall.add(instances(props.extinguisher, extPlaces, null, false));
  const fireSign = new THREE.MeshStandardMaterial({ map: sign({ kind: "plate", title: "FIRE", lines: ["EXTINGUISHER"] }, 256, 256), roughness: 0.6 });
  fireSign.color.set(0xff4444);
  for (const [x, y, ny] of [
    [-10, HALL.y1 - 0.42, -1],
    [8, HALL.y0 + 0.42, 1],
  ] as const) {
    const plate = new THREE.Mesh(new THREE.PlaneGeometry(0.4, 0.4), fireSign);
    plate.position.set(x, y, 1.6);
    faceToward(plate, 0, ny);
    hall.add(plate);
  }
  // Painted floor lettering for the machine line.
  hall.add(floorQuad(decal(floorText("MACHINING", "#f2f2ee"), 0.8), 4, 1, 4.9, HALL.y1 - 5.6, 0, 0.004));

  hall.add(b.build("factory-structure"));

  // Contact shadows where the key light's shadow map does not reach: under the machines, the
  // racking, the benches and the charger.
  const blob = new THREE.MeshBasicMaterial({ map: blobShadow(), transparent: true, depthWrite: false, toneMapped: false, opacity: 0.9 });
  const shadows: [number, number, number, number][] = [];
  for (const x of [-2, 2.6, 7.2, 11.8]) shadows.push([x, HALL.y1 - 3.4, 4.0, 3.0]);
  for (let y = HALL.y0 + 2.5; y < 1.5; y += 2.8) shadows.push([rackX, y + 1.4, 1.8, 3.4]);
  for (const x of [-8, -5.2, 6.5, 9.3]) shadows.push([x, HALL.y0 + 1.0, 3.0, 1.4]);
  shadows.push([amrX + 0.3, amrY, 2.0, 1.4]);
  for (const [x, y, w, d] of shadows) {
    const quad = floorQuad(blob, w, d, x, y, 0, 0.0035);
    quad.renderOrder = 1;
    hall.add(quad);
  }

  // The wordmark on a dark plate on the north wall, above the machine line.
  wallGraphic(logoUrl, (tex, aspect) => {
    const h = 1.3;
    const w = h * aspect;
    const plate = new THREE.Mesh(new THREE.BoxGeometry(w + 0.9, 0.06, h + 0.6), m.paint(0x1b1f24, 0.55, 0.3));
    plate.position.set(-6.5, HALL.y1 - 0.22, 5.0);
    hall.add(plate);
    const stripe = new THREE.Mesh(new THREE.BoxGeometry(w + 0.9, 0.065, 0.08), m.glow(0xff4713, 2.2));
    stripe.position.set(-6.5, HALL.y1 - 0.22, 5.0 - (h + 0.6) / 2 + 0.04);
    hall.add(stripe);
    const mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshStandardMaterial({ map: tex, transparent: true, roughness: 0.5, depthWrite: false }));
    mesh.position.set(-6.5, HALL.y1 - 0.26, 5.05);
    faceToward(mesh, 0, -1);
    hall.add(mesh);
  });
  return hall;
}
