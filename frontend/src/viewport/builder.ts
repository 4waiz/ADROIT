// SPDX-License-Identifier: Apache-2.0
// Manifest + geometry blob -> three.js objects. One Group per simulated body, each shape a child
// at its local transform; per frame only the body groups move. Geometry is what the simulator
// holds: primitives from their parameters, meshes from the importer's vertices.

import * as THREE from "three";
import type { DeformableInfo, Manifest, ShapeInfo } from "../api/types";

export interface BuiltScene {
  root: THREE.Group;
  bodies: THREE.Group[];
  visuals: THREE.Mesh[];
  colliders: THREE.Mesh[];
  deformables: DeformableView[];
  byOwner: Map<string, THREE.Object3D[]>;
  bodyToArm: Map<number, string>;
  bounds: THREE.Box3;
  setBenchTheme(theme: BenchLook): void;
  dispose(): void;
}

/** How the cell's bench is finished: the studio's grid top in either theme, or the factory's steel fixture table. */
export type BenchLook = "dark" | "light" | "factory";

export interface DeformableView {
  info: DeformableInfo;
  object: THREE.Mesh | THREE.Points;
  update(particles: Float32Array): void;
}

const srgb = (c: number[] | undefined, fallback = [0.72, 0.74, 0.77]) => {
  const v = c && c.length >= 3 ? c : fallback;
  return new THREE.Color().setRGB(v[0], v[1], v[2], THREE.SRGBColorSpace);
};

class MaterialCache {
  private cache = new Map<string, THREE.MeshStandardMaterial | THREE.MeshPhysicalMaterial>();
  private textures = new Map<string, THREE.Texture>();
  private loader = new THREE.TextureLoader();

  constructor(private premium: boolean) {}

  get(shape: ShapeInfo, robot: boolean): THREE.MeshStandardMaterial {
    const color = shape.color ?? [0.72, 0.74, 0.77];
    const roughness = shape.roughness ?? 0.55;
    const metallic = shape.metallic ?? 0;
    const opacity = shape.opacity ?? 1;
    const texture = shape.texture ?? "";
    const key = [color.map((v) => v.toFixed(3)).join(","), roughness, metallic, opacity, texture, robot].join("|");
    let material = this.cache.get(key);
    if (!material) {
      const params: THREE.MeshPhysicalMaterialParameters = {
        color: srgb(color),
        roughness: THREE.MathUtils.clamp(roughness, 0.04, 1),
        metalness: THREE.MathUtils.clamp(metallic, 0, 1),
        transparent: opacity < 0.999,
        opacity,
        depthWrite: opacity >= 0.999,
        envMapIntensity: 0.85,
      };
      if (texture) {
        const map = this.texture(texture, shape);
        if (map) {
          params.map = map;
          params.color = new THREE.Color(0xffffff);
        }
      }
      if (this.premium && robot && metallic < 0.5) {
        // Painted robot shells: a thin clear coat over the base colour, as on the real arms.
        material = new THREE.MeshPhysicalMaterial({ ...params, clearcoat: 0.35, clearcoatRoughness: 0.35 });
      } else {
        material = new THREE.MeshStandardMaterial(params);
      }
      this.cache.set(key, material);
    }
    return material;
  }

  private texture(key: string, shape: ShapeInfo): THREE.Texture | null {
    const id = `${key}|${shape.uvScale?.join(",")}|${shape.uvOffset?.join(",")}`;
    let tex = this.textures.get(id);
    if (!tex) {
      tex = this.loader.load(`/api/scene/texture/${encodeURIComponent(key)}`);
      tex.colorSpace = THREE.SRGBColorSpace;
      tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
      tex.anisotropy = 8;
      if (shape.uvScale) tex.repeat.set(shape.uvScale[0] ?? 1, shape.uvScale[1] ?? 1);
      if (shape.uvOffset) tex.offset.set(shape.uvOffset[0] ?? 0, shape.uvOffset[1] ?? 0);
      this.textures.set(id, tex);
    }
    return tex;
  }

  dispose(): void {
    for (const m of this.cache.values()) m.dispose();
    for (const t of this.textures.values()) t.dispose();
    this.cache.clear();
    this.textures.clear();
  }
}


/** A work-surface grid: 50 mm minor, 250 mm major lines, drawn once per theme. */
function benchTexture(theme: "dark" | "light"): THREE.CanvasTexture {
  const size = 1024;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = size;
  const g = canvas.getContext("2d")!;
  const dark = theme === "dark";
  g.fillStyle = dark ? "#262c33" : "#b9c0c7";
  g.fillRect(0, 0, size, size);
  // A faint brushed grain, so the top is not a flat colour field under a raking light.
  for (let i = 0; i < 2600; i++) {
    const y = Math.random() * size;
    g.strokeStyle = dark ? `rgba(255,255,255,${0.012 + Math.random() * 0.015})` : `rgba(0,0,0,${0.012 + Math.random() * 0.012})`;
    g.lineWidth = 1;
    g.beginPath();
    const x = Math.random() * size;
    g.moveTo(x, y);
    g.lineTo(x + 40 + Math.random() * 160, y);
    g.stroke();
  }
  const minor = size / 5;
  g.strokeStyle = dark ? "rgba(255,255,255,0.075)" : "rgba(20,28,36,0.14)";
  g.lineWidth = 2;
  for (let k = 1; k < 5; k++) {
    g.beginPath();
    g.moveTo(k * minor, 0);
    g.lineTo(k * minor, size);
    g.moveTo(0, k * minor);
    g.lineTo(size, k * minor);
    g.stroke();
  }
  g.strokeStyle = dark ? "rgba(255,255,255,0.16)" : "rgba(20,28,36,0.26)";
  g.lineWidth = 4;
  g.strokeRect(0, 0, size, size);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.anisotropy = 8;
  return tex;
}

/** A machined steel fixture table: ground finish, a 100 mm grid of bored holes (one 250 mm tile). */
function fixtureTexture(): THREE.CanvasTexture {
  const size = 1024;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = size;
  const g = canvas.getContext("2d")!;
  g.fillStyle = "#8f959b";
  g.fillRect(0, 0, size, size);
  // Grinding marks: fine arcs and streaks.
  for (let i = 0; i < 5200; i++) {
    const y = Math.random() * size;
    const x = Math.random() * size;
    g.strokeStyle = Math.random() < 0.5 ? `rgba(255,255,255,${0.02 + Math.random() * 0.05})` : `rgba(0,0,0,${0.02 + Math.random() * 0.05})`;
    g.lineWidth = 1;
    g.beginPath();
    g.moveTo(x, y);
    g.lineTo(x + 30 + Math.random() * 120, y + (Math.random() - 0.5) * 3);
    g.stroke();
  }
  // Bored holes on a 100 mm pitch: 2.5 per tile, so place them at 0, 400, 800 px of a 1000 px tile.
  const pitch = size / 2.5;
  for (let gx = pitch / 2; gx < size; gx += pitch) {
    for (let gy = pitch / 2; gy < size; gy += pitch) {
      const r = size * 0.017;
      const ring = g.createRadialGradient(gx, gy, r * 0.6, gx, gy, r * 1.35);
      ring.addColorStop(0, "rgba(0,0,0,0)");
      ring.addColorStop(0.7, "rgba(255,255,255,0.18)");
      ring.addColorStop(1, "rgba(0,0,0,0)");
      g.fillStyle = ring;
      g.beginPath();
      g.arc(gx, gy, r * 1.35, 0, Math.PI * 2);
      g.fill();
      g.fillStyle = "#26292d";
      g.beginPath();
      g.arc(gx, gy, r, 0, Math.PI * 2);
      g.fill();
    }
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.anisotropy = 8;
  return tex;
}

class BenchStyle {
  readonly top = new THREE.MeshStandardMaterial({ roughness: 0.62, metalness: 0.15 });
  readonly side = new THREE.MeshStandardMaterial({ roughness: 0.5, metalness: 0.35 });
  private textures = new Map<string, THREE.CanvasTexture>();
  private repeat = new THREE.Vector2(1, 1);

  setRepeat(x: number, y: number): void {
    this.repeat.set(x, y);
  }

  apply(look: BenchLook): void {
    let tex = this.textures.get(look);
    if (!tex) {
      tex = look === "factory" ? fixtureTexture() : benchTexture(look);
      this.textures.set(look, tex);
    }
    tex.repeat.copy(this.repeat);
    this.top.map = tex;
    this.top.color.set(0xffffff);
    if (look === "factory") {
      this.top.roughness = 0.42;
      this.top.metalness = 0.7;
      this.side.color.set(0x2c3238);
      this.side.roughness = 0.55;
      this.side.metalness = 0.3;
    } else {
      this.top.roughness = 0.62;
      this.top.metalness = 0.15;
      this.side.color.set(look === "dark" ? 0x1a1f24 : 0x7d8791);
      this.side.roughness = 0.5;
      this.side.metalness = 0.35;
    }
    this.top.needsUpdate = true;
    this.side.needsUpdate = true;
  }

  dispose(): void {
    this.top.dispose();
    this.side.dispose();
    for (const t of this.textures.values()) t.dispose();
  }
}

/** A primitive along local +z (Newton's convention) built from three's +y primitives. */
function primitive(shape: ShapeInfo, geometryCache: Map<string, THREE.BufferGeometry>): THREE.BufferGeometry | null {
  const s = shape.size;
  const key = `${shape.type}:${s.map((v) => v.toFixed(5)).join(",")}`;
  const cached = geometryCache.get(key);
  if (cached) return cached;
  let geometry: THREE.BufferGeometry | null = null;
  switch (shape.type) {
    case "box":
      geometry = new THREE.BoxGeometry(2 * s[0], 2 * s[1], 2 * s[2]);
      break;
    case "sphere":
      geometry = new THREE.SphereGeometry(s[0], 40, 24);
      break;
    case "ellipsoid":
      geometry = new THREE.SphereGeometry(1, 40, 24).scale(s[0], s[1], s[2]);
      break;
    case "capsule":
      geometry = new THREE.CapsuleGeometry(s[0], 2 * s[1], 10, 32).rotateX(Math.PI / 2);
      break;
    case "cylinder":
      geometry = new THREE.CylinderGeometry(s[0], s[0], 2 * s[1], 40, 1).rotateX(Math.PI / 2);
      break;
    case "cone":
      geometry = new THREE.ConeGeometry(s[0], 2 * s[1], 40, 1).rotateX(Math.PI / 2);
      break;
    case "plane":
      if (s[0] > 0 && s[1] > 0) geometry = new THREE.PlaneGeometry(s[0], s[1]);
      break;
  }
  if (geometry) geometryCache.set(key, geometry);
  return geometry;
}

function meshGeometry(manifest: Manifest, blob: ArrayBuffer, id: number, cache: Map<number, THREE.BufferGeometry>) {
  const cached = cache.get(id);
  if (cached) return cached;
  const info = manifest.meshes[id];
  if (!info) return null;
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(new Float32Array(blob, info.position, info.vertices * 3), 3));
  if (info.normal !== undefined) {
    geometry.setAttribute("normal", new THREE.BufferAttribute(new Float32Array(blob, info.normal, info.vertices * 3), 3));
  }
  if (info.uv !== undefined) {
    geometry.setAttribute("uv", new THREE.BufferAttribute(new Float32Array(blob, info.uv, info.vertices * 2), 2));
  }
  geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(blob, info.index, info.indices), 1));
  if (info.normal === undefined) geometry.computeVertexNormals();
  geometry.computeBoundingSphere();
  cache.set(id, geometry);
  return geometry;
}

export function buildScene(manifest: Manifest, blob: ArrayBuffer, opts: { premium: boolean }): BuiltScene {
  const root = new THREE.Group();
  root.name = "bridge-scene";
  const materials = new MaterialCache(opts.premium);
  const primitiveCache = new Map<string, THREE.BufferGeometry>();
  const meshCache = new Map<number, THREE.BufferGeometry>();
  const bench = new BenchStyle();
  let benchUsed = false;
  const colliderMaterial = new THREE.MeshBasicMaterial({
    color: 0x35d0ff,
    wireframe: true,
    transparent: true,
    opacity: 0.55,
    depthTest: true,
  });

  const bodies: THREE.Group[] = manifest.bodies.map((b) => {
    const g = new THREE.Group();
    g.name = `body:${b.label}`;
    g.userData.body = b.i;
    g.matrixAutoUpdate = true;
    root.add(g);
    return g;
  });

  const bodyToArm = new Map<number, string>();
  for (const arm of manifest.arms) for (const b of arm.bodies) if (!bodyToArm.has(b)) bodyToArm.set(b, arm.name);

  const visuals: THREE.Mesh[] = [];
  const colliders: THREE.Mesh[] = [];
  const byOwner = new Map<string, THREE.Object3D[]>();
  const own = (key: string | null, object: THREE.Object3D) => {
    if (!key) return;
    const list = byOwner.get(key);
    if (list) list.push(object);
    else byOwner.set(key, [object]);
  };

  for (const shape of manifest.shapes) {
    if (shape.type === "unsupported" || shape.site) continue;
    if (shape.type === "plane" && (shape.size[0] === 0 || shape.size[1] === 0)) continue; // the ground: drawn by the stage
    let geometry: THREE.BufferGeometry | null = null;
    if (shape.type === "mesh" || shape.type === "convex") {
      if (shape.mesh === null || shape.mesh === undefined) continue;
      geometry = meshGeometry(manifest, blob, shape.mesh, meshCache);
    } else {
      geometry = primitive(shape, primitiveCache);
    }
    if (!geometry) continue;
    const parent = shape.body >= 0 ? bodies[shape.body] : root;
    if (!parent) continue;
    const [px, py, pz, qx, qy, qz, qw] = shape.xform;
    const armName = shape.body >= 0 ? bodyToArm.get(shape.body) : undefined;
    const owner = armName ? `arm:${armName}` : shape.owner;
    if (shape.visible) {
      const robot = Boolean(armName) || (shape.owner ?? "").startsWith("arm:");
      // The cell's own table is drawn as an engineered bench top: a grid finish over the exact collider box.
      const isBench = shape.owner === "static:table" && shape.type === "box" && !shape.texture;
      if (isBench) {
        bench.setRepeat((2 * shape.size[0]) / 0.25, (2 * shape.size[1]) / 0.25);
        benchUsed = true;
      }
      const material = isBench ? [bench.side, bench.side, bench.side, bench.side, bench.top, bench.side] : materials.get(shape, robot);
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.set(px, py, pz);
      mesh.quaternion.set(qx, qy, qz, qw);
      if (shape.type === "mesh" || shape.type === "convex") mesh.scale.set(shape.size[0], shape.size[1], shape.size[2]);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      mesh.userData = { shape: shape.i, owner, body: shape.body, label: shape.label };
      mesh.name = shape.label || `shape${shape.i}`;
      parent.add(mesh);
      visuals.push(mesh);
      own(owner, mesh);
    }
    if (shape.collide) {
      const wire = new THREE.Mesh(geometry, colliderMaterial);
      wire.position.set(px, py, pz);
      wire.quaternion.set(qx, qy, qz, qw);
      if (shape.type === "mesh" || shape.type === "convex") wire.scale.set(shape.size[0], shape.size[1], shape.size[2]);
      wire.visible = false;
      wire.renderOrder = 2;
      wire.userData = { shape: shape.i, owner, collider: true };
      parent.add(wire);
      colliders.push(wire);
    }
  }

  const deformables = manifest.deformables.map((d) => buildDeformable(d, blob));
  for (const d of deformables) {
    root.add(d.object);
    own(d.info.owner, d.object);
  }

  const bounds = new THREE.Box3();
  root.updateMatrixWorld(true);
  for (const mesh of visuals) bounds.expandByObject(mesh);

  return {
    root,
    bodies,
    visuals,
    colliders,
    deformables,
    byOwner,
    bodyToArm,
    bounds,
    setBenchTheme(look) {
      if (benchUsed) bench.apply(look);
    },
    dispose() {
      materials.dispose();
      bench.dispose();
      colliderMaterial.dispose();
      for (const g of primitiveCache.values()) g.dispose();
      for (const g of meshCache.values()) g.dispose();
      for (const d of deformables) {
        d.object.geometry.dispose();
        (d.object.material as THREE.Material).dispose();
      }
    },
  };
}

function buildDeformable(info: DeformableInfo, blob: ArrayBuffer): DeformableView {
  const color = srgb(info.color, [0.86, 0.85, 0.8]);
  if (info.kind === "surface" && info.index !== undefined && info.triangles) {
    const geometry = new THREE.BufferGeometry();
    const positions = new Float32Array(info.count * 3);
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3).setUsage(THREE.DynamicDrawUsage));
    geometry.setIndex(new THREE.BufferAttribute(new Uint32Array(blob, info.index, info.triangles), 1));
    const fabric = info.thickness > 0;
    const material = new THREE.MeshPhysicalMaterial({
      color,
      roughness: info.roughness ?? (fabric ? 0.92 : 0.6),
      metalness: info.metallic ?? 0,
      side: THREE.DoubleSide,
      transparent: info.opacity < 0.999,
      opacity: info.opacity,
      sheen: fabric ? 0.6 : 0,
      sheenRoughness: 0.8,
      sheenColor: new THREE.Color(0xffffff),
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    mesh.frustumCulled = false;
    mesh.userData = { owner: info.owner, deformable: info.name };
    mesh.name = `cloth:${info.name}`;
    return {
      info,
      object: mesh,
      update(particles) {
        const start = info.offset * 3;
        if (particles.length < start + info.count * 3) return;
        positions.set(particles.subarray(start, start + info.count * 3));
        geometry.attributes.position.needsUpdate = true;
        geometry.computeVertexNormals();
        geometry.computeBoundingSphere();
      },
    };
  }
  if (info.kind === "tube") {
    const sides = 12;
    const n = info.count;
    const radius = info.radius ?? 0.005;
    const positions = new Float32Array(n * sides * 3);
    const indices: number[] = [];
    for (let i = 0; i < n - 1; i++) {
      for (let k = 0; k < sides; k++) {
        const a = i * sides + k;
        const b = i * sides + ((k + 1) % sides);
        const c = (i + 1) * sides + k;
        const d = (i + 1) * sides + ((k + 1) % sides);
        indices.push(a, c, b, b, c, d);
      }
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3).setUsage(THREE.DynamicDrawUsage));
    geometry.setIndex(indices);
    const material = new THREE.MeshStandardMaterial({ color, roughness: info.roughness ?? 0.55, metalness: 0 });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = true;
    mesh.frustumCulled = false;
    mesh.userData = { owner: info.owner, deformable: info.name };
    const t = new THREE.Vector3();
    const nrm = new THREE.Vector3();
    const bin = new THREE.Vector3();
    const p = new THREE.Vector3();
    const prevN = new THREE.Vector3(0, 0, 1);
    return {
      info,
      object: mesh,
      update(particles) {
        const start = info.offset * 3;
        if (particles.length < start + n * 3) return;
        for (let i = 0; i < n; i++) {
          const j = start + i * 3;
          p.set(particles[j], particles[j + 1], particles[j + 2]);
          const i0 = Math.max(0, i - 1);
          const i1 = Math.min(n - 1, i + 1);
          t.set(
            particles[start + i1 * 3] - particles[start + i0 * 3],
            particles[start + i1 * 3 + 1] - particles[start + i0 * 3 + 1],
            particles[start + i1 * 3 + 2] - particles[start + i0 * 3 + 2],
          ).normalize();
          // Parallel transport of the ring frame keeps the tube from twisting.
          nrm.copy(prevN).sub(t.clone().multiplyScalar(prevN.dot(t)));
          if (nrm.lengthSq() < 1e-8) nrm.set(t.y, -t.x, 0);
          nrm.normalize();
          prevN.copy(nrm);
          bin.crossVectors(t, nrm);
          for (let k = 0; k < sides; k++) {
            const a = (k / sides) * Math.PI * 2;
            const cx = Math.cos(a) * radius;
            const sy = Math.sin(a) * radius;
            const o = (i * sides + k) * 3;
            positions[o] = p.x + nrm.x * cx + bin.x * sy;
            positions[o + 1] = p.y + nrm.y * cx + bin.y * sy;
            positions[o + 2] = p.z + nrm.z * cx + bin.z * sy;
          }
        }
        prevN.set(0, 0, 1);
        geometry.attributes.position.needsUpdate = true;
        geometry.computeVertexNormals();
      },
    };
  }
  // Particles with no surface of their own: drawn as the points they are.
  const geometry = new THREE.BufferGeometry();
  const positions = new Float32Array(info.count * 3);
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3).setUsage(THREE.DynamicDrawUsage));
  const points = new THREE.Points(geometry, new THREE.PointsMaterial({ color, size: 0.006, sizeAttenuation: true }));
  points.frustumCulled = false;
  points.userData = { owner: info.owner, deformable: info.name };
  return {
    info,
    object: points,
    update(particles) {
      const start = info.offset * 3;
      if (particles.length < start + info.count * 3) return;
      positions.set(particles.subarray(start, start + info.count * 3));
      geometry.attributes.position.needsUpdate = true;
    },
  };
}
