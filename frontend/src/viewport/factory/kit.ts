// SPDX-License-Identifier: Apache-2.0
// The factory's building blocks: its materials (painted steel in the colours real plant uses,
// the epoxy floor, the cladding) and a batcher that merges hundreds of static parts into a few
// draw calls, so a whole hall costs about what one machine would.

import * as THREE from "three";
import { RoundedBoxGeometry } from "three/addons/geometries/RoundedBoxGeometry.js";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { macroNoise } from "./textures";

/** Industrial paint and finish colours (RAL where there is one). */
export const PAINT = {
  safetyYellow: 0xf2b705, // RAL 1003
  lightGrey: 0xc5c7c4, // RAL 7035, enclosures and cabinets
  anthracite: 0x383e42, // RAL 7016
  signalBlue: 0x1e5aa8, // RAL 5005, rack uprights
  orange: 0xe25303, // RAL 2004, rack beams
  white: 0xeeeeea,
  black: 0x141518,
  green: 0x2f7d4b,
  steelGrey: 0x8d9399,
} as const;

export interface FactoryTextures {
  floorColor: THREE.Texture;
  floorNormal: THREE.Texture;
  floorRough: THREE.Texture;
  claddingNormal: THREE.Texture;
  claddingRough: THREE.Texture;
  panelColor: THREE.Texture;
  panelNormal: THREE.Texture;
  panelRough: THREE.Texture;
}

export class Materials {
  private cache = new Map<string, THREE.Material>();
  readonly macro = macroNoise();

  constructor(private tex: FactoryTextures | null) {}

  private memo<T extends THREE.Material>(key: string, make: () => T): T {
    let m = this.cache.get(key) as T | undefined;
    if (!m) {
      m = make();
      this.cache.set(key, m);
    }
    return m;
  }

  paint(color: number, roughness = 0.42, metalness = 0.15): THREE.MeshStandardMaterial {
    return this.memo(`paint:${color}:${roughness}:${metalness}`, () => new THREE.MeshStandardMaterial({ color, roughness, metalness }));
  }

  steel(color: number = PAINT.steelGrey, roughness = 0.38): THREE.MeshStandardMaterial {
    return this.memo(`steel:${color}:${roughness}`, () => new THREE.MeshStandardMaterial({ color, roughness, metalness: 0.85 }));
  }

  rubber(): THREE.MeshStandardMaterial {
    return this.memo("rubber", () => new THREE.MeshStandardMaterial({ color: 0x18191b, roughness: 0.85, metalness: 0 }));
  }

  glass(): THREE.MeshStandardMaterial {
    return this.memo(
      "glass",
      () =>
        new THREE.MeshStandardMaterial({
          color: 0x9fb4c4,
          roughness: 0.06,
          metalness: 0.0,
          transparent: true,
          opacity: 0.22,
          depthWrite: false,
          envMapIntensity: 1.4,
        }),
    );
  }

  /** Light emitted, not lit: values above 1 so the bloom pass picks them up. */
  glow(color: number, strength: number): THREE.MeshBasicMaterial {
    return this.memo(`glow:${color}:${strength}`, () => {
      const c = new THREE.Color(color).multiplyScalar(strength);
      return new THREE.MeshBasicMaterial({ color: c, toneMapped: true, fog: true });
    });
  }

  /** Painted steel with the machine-panel photograph's wear in it (roughness and dents). */
  panel(color: number, repeat = 1): THREE.MeshStandardMaterial {
    return this.memo(`panel:${color}:${repeat}`, () => {
      const m = new THREE.MeshStandardMaterial({ color, roughness: 1, metalness: 0.25 });
      if (this.tex) {
        m.roughnessMap = this.repeat(this.tex.panelRough, repeat);
        m.normalMap = this.repeat(this.tex.panelNormal, repeat);
        m.normalScale.set(0.6, 0.6);
        m.roughness = 0.9;
      } else {
        m.roughness = 0.45;
      }
      return m;
    });
  }

  /** Insulated wall cladding: a box profile, light grey, the photograph's surface detail. */
  cladding(width: number, height: number, color: number = 0xdfe3e6): THREE.MeshStandardMaterial {
    const m = new THREE.MeshStandardMaterial({ color, roughness: 1, metalness: 0.18 });
    if (this.tex) {
      const tile = 2.0; // metres per texture repeat
      m.normalMap = this.repeat(this.tex.claddingNormal, width / tile, height / tile, true);
      m.roughnessMap = this.repeat(this.tex.claddingRough, width / tile, height / tile, true);
      m.normalScale.set(1.2, 1.2);
    } else {
      m.roughness = 0.5;
    }
    this.cache.set(`cladding:${this.cache.size}`, m);
    return m;
  }

  /** Painted concrete: the floor photograph, tinted, for plinths and the dado wall. */
  concrete(width: number, height: number, color: number): THREE.MeshStandardMaterial {
    const m = new THREE.MeshStandardMaterial({ color, roughness: 1, metalness: 0 });
    if (this.tex) {
      const tile = 4.0;
      m.map = this.repeat(this.tex.floorColor, width / tile, height / tile, true);
      m.roughnessMap = this.repeat(this.tex.floorRough, width / tile, height / tile, true);
      m.normalMap = this.repeat(this.tex.floorNormal, width / tile, height / tile, true);
    }
    this.cache.set(`concrete:${this.cache.size}`, m);
    return m;
  }

  /** Copies a texture with its own repeat, sharing the image (clones share the source). */
  private repeat(t: THREE.Texture, x: number, y = x, own = false): THREE.Texture {
    if (!own && x === 1 && y === 1) return t;
    const c = t.clone();
    c.wrapS = c.wrapT = THREE.RepeatWrapping;
    c.repeat.set(x, y);
    c.needsUpdate = true;
    return c;
  }

  /**
   * The epoxy floor. The photograph's colour, roughness and normal maps are sampled at two scales
   * and blended through a large soft noise, so a 4 m tile does not repeat visibly across 40 m.
   */
  floor(width: number, depth: number, tint: number): THREE.MeshStandardMaterial {
    const m = new THREE.MeshStandardMaterial({ color: tint, roughness: 1, metalness: 0.0, envMapIntensity: 1.05 });
    if (!this.tex) {
      m.roughness = 0.5;
      return m;
    }
    const tile = 4.0;
    m.map = this.repeat(this.tex.floorColor, width / tile, depth / tile, true);
    m.roughnessMap = this.repeat(this.tex.floorRough, width / tile, depth / tile, true);
    m.normalMap = this.repeat(this.tex.floorNormal, width / tile, depth / tile, true);
    m.normalScale.set(0.35, 0.35);
    const macro = this.macro;
    m.onBeforeCompile = (shader) => {
      shader.uniforms.macroMap = { value: macro };
      shader.fragmentShader = shader.fragmentShader
        .replace("#include <common>", "#include <common>\nuniform sampler2D macroMap;")
        .replace(
          "#include <map_fragment>",
          [
            "#ifdef USE_MAP",
            "  // One map repeat is 4 m: the second sample repeats every 10.8 m, the blend every 23 m.",
            "  vec2 uvA = vMapUv;",
            "  vec2 uvB = vMapUv * 0.37 + vec2(0.31, 0.73);",
            "  float macro = texture2D(macroMap, vMapUv * 0.1739).r;",
            "  vec4 colA = texture2D(map, uvA);",
            "  vec4 colB = texture2D(map, uvB);",
            "  vec4 sampledDiffuseColor = mix(colA, colB, smoothstep(0.35, 0.65, macro));",
            "  sampledDiffuseColor.rgb *= 0.86 + 0.28 * macro;",
            "  diffuseColor *= sampledDiffuseColor;",
            "#endif",
          ].join("\n"),
        )
        .replace(
          "#include <roughnessmap_fragment>",
          [
            "float roughnessFactor = roughness;",
            "#ifdef USE_ROUGHNESSMAP",
            "  vec4 texelRoughness = texture2D(roughnessMap, vRoughnessMapUv);",
            "  // Epoxy: glossy where the photograph is smooth, matte where it is worn.",
            "  roughnessFactor *= mix(0.1, 0.5, texelRoughness.g);",
            "#endif",
          ].join("\n"),
        );
    };
    m.customProgramCacheKey = () => "bridge-epoxy-floor";
    return m;
  }

  dispose(): void {
    for (const m of this.cache.values()) {
      const s = m as THREE.MeshStandardMaterial;
      for (const t of [s.map, s.normalMap, s.roughnessMap]) if (t && t !== this.tex?.floorColor) t.dispose?.();
      m.dispose();
    }
    this.macro.dispose();
    this.cache.clear();
  }
}

// ------------------------------------------------------------------ geometry
const unitBox = new THREE.BoxGeometry(1, 1, 1).toNonIndexed();
const roundedCache = new Map<string, THREE.BufferGeometry>();
const cylinderCache = new Map<string, THREE.BufferGeometry>();

/** A box of the given size, its corners rounded by `radius` (catches highlights as real metal does). */
export function rounded(x: number, y: number, z: number, radius: number): THREE.BufferGeometry {
  const key = [x, y, z, radius].map((v) => v.toFixed(3)).join(",");
  let g = roundedCache.get(key);
  if (!g) {
    g = new RoundedBoxGeometry(x, y, z, 2, Math.min(radius, x / 2, y / 2, z / 2)).toNonIndexed();
    roundedCache.set(key, g);
  }
  return g;
}

/** A cylinder along +z. */
export function cylinder(radius: number, height: number, segments = 24, radiusTop = radius): THREE.BufferGeometry {
  const key = [radius, height, segments, radiusTop].map((v) => v.toFixed(4)).join(",");
  let g = cylinderCache.get(key);
  if (!g) {
    g = new THREE.CylinderGeometry(radiusTop, radius, height, segments, 1).rotateX(Math.PI / 2).toNonIndexed();
    cylinderCache.set(key, g);
  }
  return g;
}

export function boxGeometry(): THREE.BufferGeometry {
  return unitBox;
}

const tmpM = new THREE.Matrix4();
const tmpQ = new THREE.Quaternion();
const tmpE = new THREE.Euler();
const tmpS = new THREE.Vector3();
const tmpP = new THREE.Vector3();

/** Static parts collected by material and merged into one mesh each. */
export class Batcher {
  private parts = new Map<THREE.Material, { geometries: THREE.BufferGeometry[]; cast: boolean }>();

  add(geometry: THREE.BufferGeometry, material: THREE.Material, matrix: THREE.Matrix4, cast = false): void {
    const g = geometry.clone().applyMatrix4(matrix);
    for (const name of Object.keys(g.attributes)) if (!["position", "normal", "uv"].includes(name)) g.deleteAttribute(name);
    if (!g.attributes.uv) g.setAttribute("uv", new THREE.BufferAttribute(new Float32Array((g.attributes.position.count) * 2), 2));
    const entry = this.parts.get(material);
    if (entry) {
      entry.geometries.push(g);
      entry.cast ||= cast;
    } else {
      this.parts.set(material, { geometries: [g], cast });
    }
  }

  /** A box of size (x, y, z) centred at (px, py, pz), turned by yaw about +z then pitch/roll. */
  box(material: THREE.Material, size: [number, number, number], at: [number, number, number], rot: [number, number, number] = [0, 0, 0], cast = false): void {
    this.add(unitBox, material, compose(at, rot, size), cast);
  }

  shape(geometry: THREE.BufferGeometry, material: THREE.Material, at: [number, number, number], rot: [number, number, number] = [0, 0, 0], scale: [number, number, number] = [1, 1, 1], cast = false): void {
    this.add(geometry, material, compose(at, rot, scale), cast);
  }

  build(name: string, receiveShadow = true): THREE.Group {
    const group = new THREE.Group();
    group.name = name;
    for (const [material, { geometries, cast }] of this.parts) {
      const merged = mergeGeometries(geometries, false);
      for (const g of geometries) g.dispose();
      if (!merged) continue;
      merged.computeBoundingSphere();
      const mesh = new THREE.Mesh(merged, material);
      mesh.castShadow = cast;
      mesh.receiveShadow = receiveShadow;
      mesh.matrixAutoUpdate = false;
      mesh.updateMatrix();
      group.add(mesh);
    }
    this.parts.clear();
    return group;
  }
}

export function compose(at: [number, number, number], rot: [number, number, number], scale: [number, number, number]): THREE.Matrix4 {
  tmpE.set(rot[0], rot[1], rot[2], "ZYX");
  tmpQ.setFromEuler(tmpE);
  tmpP.set(at[0], at[1], at[2]);
  tmpS.set(scale[0], scale[1], scale[2]);
  return tmpM.compose(tmpP, tmpQ, tmpS).clone();
}

/** A flat decal quad lying on the floor (z up), centred at (x, y), turned by yaw. */
export function floorQuad(material: THREE.Material, w: number, h: number, x: number, y: number, yaw = 0, z = 0.002): THREE.Mesh {
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, h), material);
  mesh.position.set(x, y, z);
  mesh.rotation.set(0, 0, yaw);
  mesh.receiveShadow = true;
  return mesh;
}
