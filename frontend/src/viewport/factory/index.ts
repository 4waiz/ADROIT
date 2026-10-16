// SPDX-License-Identifier: Apache-2.0
// The digital factory around the simulated cell: a lit hall, machines and racking, and the cell's
// own guarding, cabinet and operator panel. Photographic assets (an industrial HDRI for light and
// reflections, scanned floor and steel textures, props) come from Poly Haven under CC0; see
// public/factory/ASSETS.md. Nothing here is simulated: it is the room the simulation stands in,
// and its stack light and panel screen only report what the simulation is doing. The lab's
// robots around the cell (lab.ts) are animated scenery as well.

import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { HDRLoader } from "three/addons/loaders/HDRLoader.js";
import darkLogo from "../../../../dark.png";
import lightLogo from "../../../../light.png";
import { buildSurround, type CellSurround, type Footprint, type MachineState } from "./cell";
import { buildHall, HALL, type Props } from "./hall";
import { type FactoryTextures, Materials } from "./kit";
import { buildLab, type Lab } from "./lab";
import { type ScreenState, softDot } from "./textures";

export type { Footprint, MachineState, ScreenState };
export { HALL };

const BASE = "/factory/";

export interface FactoryQuality {
  atmosphere: boolean;
}

export class FactoryEnvironment {
  readonly group = new THREE.Group();
  private mats: Materials | null = null;
  private props: Props = {};
  private hall: THREE.Group | null = null;
  private surround: CellSurround | null = null;
  private lab: Lab | null = null;
  private motion = true;
  private atmosphere = new THREE.Group();
  private dust: THREE.Points | null = null;
  private dustSeed: Float32Array | null = null;
  private env: THREE.Texture | null = null;
  private loading: Promise<void> | null = null;
  private footprint: Footprint | null = null;
  private state: MachineState = "offline";
  private screenState: ScreenState = { title: "CELL 01", status: "OFFLINE", tone: "idle", detail: "Waiting for the simulation" };
  private time = 0;
  private disposed = false;

  constructor(private renderer: THREE.WebGLRenderer) {
    this.group.name = "factory";
    this.group.visible = false;
    this.atmosphere.name = "factory-atmosphere";
    this.group.add(this.atmosphere);
  }

  /** Loads the assets and builds the hall once; later calls share the same promise. */
  load(onProgress?: (fraction: number) => void): Promise<void> {
    if (!this.loading) this.loading = this.doLoad(onProgress);
    return this.loading;
  }

  get ready(): boolean {
    return this.hall !== null;
  }

  /** The prefiltered HDRI for image-based light and reflections, once loaded. */
  get environment(): THREE.Texture | null {
    return this.env;
  }

  private async doLoad(onProgress?: (fraction: number) => void): Promise<void> {
    const manager = new THREE.LoadingManager();
    manager.onProgress = (_url, loaded, total) => onProgress?.(total ? loaded / total : 1);
    const textures = new THREE.TextureLoader(manager);
    const tex = (name: string, color: boolean) =>
      new Promise<THREE.Texture>((resolve, reject) => {
        textures.load(
          `${BASE}textures/${name}.webp`,
          (t) => {
            t.colorSpace = color ? THREE.SRGBColorSpace : THREE.NoColorSpace;
            t.wrapS = t.wrapT = THREE.RepeatWrapping;
            t.anisotropy = Math.min(8, this.renderer.capabilities.getMaxAnisotropy());
            resolve(t);
          },
          undefined,
          reject,
        );
      });
    const gltf = new GLTFLoader(manager);
    const prop = (name: string) =>
      gltf.loadAsync(`${BASE}models/${name}.glb`).then((g) => {
        // glTF is y-up; this world is z-up.
        g.scene.rotation.x = Math.PI / 2;
        g.scene.updateMatrixWorld(true);
        g.scene.traverse((o) => {
          const mesh = o as THREE.Mesh;
          if (mesh.isMesh) {
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            const mat = mesh.material as THREE.MeshStandardMaterial;
            if (mat?.isMaterial) mat.envMapIntensity = 0.9;
          }
        });
        return g.scene as THREE.Object3D;
      });
    const hdr = new HDRLoader(manager).loadAsync(`${BASE}factory_1k.hdr`);

    let surfaces: FactoryTextures | null = null;
    try {
      const [floorColor, floorNormal, floorRough, claddingNormal, claddingRough, panelColor, panelNormal, panelRough] = await Promise.all([
        tex("floor_color", true),
        tex("floor_normal", false),
        tex("floor_rough", false),
        tex("cladding_normal", false),
        tex("cladding_rough", false),
        tex("panel_color", true),
        tex("panel_normal", false),
        tex("panel_rough", false),
      ]);
      surfaces = { floorColor, floorNormal, floorRough, claddingNormal, claddingRough, panelColor, panelNormal, panelRough };
    } catch {
      surfaces = null; // the hall still builds, in plain paint
    }
    const names = { box: "cardboard_box", crate: "plastic_crate", drum: "drum", chest: "tool_chest", cart: "storage_cart", extinguisher: "fire_extinguisher", floorSign: "floor_sign" } as const;
    const loaded = await Promise.all(Object.entries(names).map(([key, file]) => prop(file).then((o) => [key, o] as const).catch(() => [key, undefined] as const)));
    for (const [key, o] of loaded) if (o) this.props[key as keyof Props] = o;
    try {
      const equirect = await hdr;
      equirect.mapping = THREE.EquirectangularReflectionMapping;
      const pmrem = new THREE.PMREMGenerator(this.renderer);
      this.env = pmrem.fromEquirectangular(equirect).texture;
      pmrem.dispose();
      equirect.dispose();
    } catch {
      this.env = null;
    }
    if (this.disposed) return;
    this.mats = new Materials(surfaces);
    this.hall = buildHall(this.mats, this.props, darkLogo);
    this.group.add(this.hall);
    this.lab = buildLab();
    this.lab.setMotion(this.motion);
    this.group.add(this.lab.group);
    this.buildAtmosphere();
    if (this.footprint) this.fit(this.footprint);
  }

  /** Lays the cell's guarding, markings, cabinet and panel around its footprint (local to the factory). */
  fit(footprint: Footprint): void {
    this.footprint = footprint;
    if (!this.mats) return;
    if (this.surround) {
      this.group.remove(this.surround.group);
      this.surround.dispose();
    }
    this.surround = buildSurround(this.mats, footprint, this.props, lightLogo);
    this.group.add(this.surround.group);
    this.surround.stack.set(this.state);
    this.surround.screen.draw(this.screenState);
    this.lab?.fit(footprint);
    this.placeDust();
  }

  /** The operator's standing view of the cell, in factory-local coordinates. */
  operatorView(): { eye: THREE.Vector3; target: THREE.Vector3 } | null {
    return this.surround?.operator ?? null;
  }

  setState(state: MachineState, screen: ScreenState): void {
    this.state = state;
    this.screenState = screen;
    this.surround?.stack.set(state);
    this.surround?.screen.draw(screen);
  }

  setQuality(q: FactoryQuality): void {
    this.atmosphere.visible = q.atmosphere;
  }

  /** The lab's robots move unless the person asked for reduced motion. */
  setMotion(on: boolean): void {
    this.motion = on;
    this.lab?.setMotion(on);
  }

  update(dt: number): void {
    if (!this.group.visible) return;
    this.time += dt;
    this.surround?.stack.update(dt);
    this.lab?.update(dt);
    const dust = this.dust;
    const seed = this.dustSeed;
    if (dust && seed && this.atmosphere.visible) {
      const pos = dust.geometry.attributes.position as THREE.BufferAttribute;
      const t = this.time;
      for (let i = 0; i < pos.count; i++) {
        const s = seed[i * 4 + 3];
        // A slow rise with a lazy sway, wrapped inside the box above the cell.
        const z = ((seed[i * 4 + 2] + t * (0.02 + 0.03 * s)) % 3.6) + 0.25;
        pos.setXYZ(i, seed[i * 4] + Math.sin(t * 0.21 + s * 9) * 0.25, seed[i * 4 + 1] + Math.cos(t * 0.17 + s * 7) * 0.25, z);
      }
      pos.needsUpdate = true;
    }
  }

  private buildAtmosphere(): void {
    const count = 260;
    const positions = new Float32Array(count * 3);
    const dustGeometry = new THREE.BufferGeometry();
    dustGeometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
    this.dust = new THREE.Points(
      dustGeometry,
      new THREE.PointsMaterial({
        map: softDot(),
        size: 0.009,
        sizeAttenuation: true,
        color: new THREE.Color(1.0, 0.96, 0.88),
        transparent: true,
        opacity: 0.4,
        depthWrite: false,
        blending: THREE.AdditiveBlending,
      }),
    );
    this.dust.frustumCulled = false;
    this.atmosphere.add(this.dust);
    this.placeDust();
  }

  private placeDust(): void {
    if (!this.dust) return;
    const fp = this.footprint ?? { x0: -1, x1: 1, y0: -1, y1: 1 };
    const n = (this.dust.geometry.attributes.position as THREE.BufferAttribute).count;
    const seed = new Float32Array(n * 4);
    for (let i = 0; i < n; i++) {
      seed[i * 4] = fp.x0 - 0.8 + Math.random() * (fp.x1 - fp.x0 + 1.6);
      seed[i * 4 + 1] = fp.y0 - 0.8 + Math.random() * (fp.y1 - fp.y0 + 1.6);
      seed[i * 4 + 2] = Math.random() * 3.6;
      seed[i * 4 + 3] = Math.random();
    }
    this.dustSeed = seed;
  }

  dispose(): void {
    this.disposed = true;
    this.surround?.dispose();
    this.lab?.dispose();
    this.group.traverse((o) => {
      const mesh = o as THREE.Mesh;
      if (mesh.isMesh || (o as THREE.Points).isPoints) {
        mesh.geometry?.dispose();
      }
    });
    this.mats?.dispose();
    this.env?.dispose();
  }
}
