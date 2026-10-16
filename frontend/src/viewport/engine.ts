// SPDX-License-Identifier: Apache-2.0
// The viewport: a three.js stage that draws what the simulator reports, and turns pointer
// gestures into commands. It never moves a simulated body itself: body groups take the poses
// in each frame, and the tool-centre gizmo sends cursor samples the host's IK tracks.

import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { TransformControls } from "three/addons/controls/TransformControls.js";
import { RoomEnvironment } from "three/addons/environments/RoomEnvironment.js";
import { EffectComposer } from "three/addons/postprocessing/EffectComposer.js";
import { GTAOPass } from "three/addons/postprocessing/GTAOPass.js";
import { OutlinePass } from "three/addons/postprocessing/OutlinePass.js";
import { OutputPass } from "three/addons/postprocessing/OutputPass.js";
import { RenderPass } from "three/addons/postprocessing/RenderPass.js";
import { ShaderPass } from "three/addons/postprocessing/ShaderPass.js";
import { SMAAPass } from "three/addons/postprocessing/SMAAPass.js";
import { UnrealBloomPass } from "three/addons/postprocessing/UnrealBloomPass.js";
import { VignetteShader } from "three/addons/shaders/VignetteShader.js";
import type { Frame, Manifest, Vec3, WorkcellDoc } from "../api/types";
import type { EnvironmentName, Overlays, Quality, Theme } from "../state/store";
import { type BuiltScene, buildScene } from "./builder";
import { DraftLayer } from "./drafts";
import { FactoryEnvironment, HALL, type MachineState, type ScreenState } from "./factory";
import { OverlayLayer } from "./overlays";

THREE.Object3D.DEFAULT_UP.set(0, 0, 1);

/** Ambient occlusion at half the drawing size, upsampled in its own blend: a quarter of the cost. */
class HalfGTAOPass extends GTAOPass {
  override setSize(width: number, height: number): void {
    super.setSize(Math.max(1, Math.floor(width / 2)), Math.max(1, Math.floor(height / 2)));
  }
}

export type PresetName = "iso" | "operator" | "front" | "side" | "top" | "tool";
export type FollowMode = "none" | "robot" | "tool";

export interface EngineEvents {
  onSelect(key: string | null): void;
  onCursor(arm: string, position: number[], quat: number[], phase: "start" | "update" | "end"): void;
  onDraftMove(id: string, at: Vec3, rpy: Vec3): void;
  onStats(stats: { fps: number; ms: number; calls: number; triangles: number; quality: Quality; shadows: boolean; ao: boolean }): void;
}

const THEMES = {
  dark: {
    background: 0x0b0d10,
    floor: 0x15181c,
    grid: 0x2a3038,
    gridMajor: 0x3a424c,
    fog: 0x11151a,
    hemiSky: 0xbfd2ff,
    hemiGround: 0x1a1612,
    skyTop: 0x07090b,
    skyHorizon: 0x1a2027,
    skyBottom: 0x0d1013,
    pool: 0.07,
  },
  light: {
    background: 0xdfe3e7,
    floor: 0xbfc5cb,
    grid: 0xa6aeb6,
    gridMajor: 0x8a939c,
    fog: 0xdde1e5,
    hemiSky: 0xffffff,
    hemiGround: 0x8f8a84,
    skyTop: 0xc6ccd3,
    skyHorizon: 0xe9ecef,
    skyBottom: 0xd3d8dd,
    pool: 0.1,
  },
} as const;

const QUALITY = {
  performance: { pixelRatio: 1, shadows: false, shadowSize: 1024, ao: false, post: false, outline: false },
  balanced: { pixelRatio: 1.5, shadows: true, shadowSize: 2048, ao: false, post: true, outline: true },
  ultra: { pixelRatio: 2, shadows: true, shadowSize: 4096, ao: true, post: true, outline: true },
} as const;

export class ViewportEngine {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  private perspective: THREE.PerspectiveCamera;
  private ortho: THREE.OrthographicCamera;
  private camera: THREE.PerspectiveCamera | THREE.OrthographicCamera;
  private controls: OrbitControls;
  private gizmo: TransformControls;
  private gizmoTarget = new THREE.Object3D();
  private gizmoArm: string | null = null;
  private dragging = false;
  private lastCursorSent = 0;
  private composer: EffectComposer | null = null;
  private outline: OutlinePass | null = null;
  private gtao: GTAOPass | null = null;
  private renderPass: RenderPass | null = null;
  private stage = new THREE.Group();
  private floor: THREE.Mesh;
  private grid: THREE.Group;
  private dome: THREE.Mesh;
  private pool: THREE.Mesh;
  private key: THREE.DirectionalLight;
  private fill: THREE.DirectionalLight;
  private rim: THREE.DirectionalLight;
  private cellLight: THREE.SpotLight;
  private hemi: THREE.HemisphereLight;
  private envTexture: THREE.Texture | null = null;
  private built: BuiltScene | null = null;
  private overlays: OverlayLayer | null = null;
  private manifest: Manifest | null = null;
  private frame: Frame | null = null;
  private appliedSeq = -1;
  private selected: string | null = null;
  private quality: Quality = "balanced";
  private follow: FollowMode = "none";
  private followArm: string | null = null;
  private tween: { from: [THREE.Vector3, THREE.Vector3]; to: [THREE.Vector3, THREE.Vector3]; t0: number; ms: number } | null = null;
  private raycaster = new THREE.Raycaster();
  private pointerDown: { x: number; y: number; t: number } | null = null;
  private resizeObserver: ResizeObserver;
  private raf = 0;
  private frameTimes: number[] = [];
  private lastTick = performance.now();
  private lastStats = 0;
  private reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  private disposed = false;
  private overlayState: Overlays | null = null;
  private needsFraming = false;
  private theme: Theme = "dark";
  private drafts: DraftLayer;
  private factory: FactoryEnvironment;
  private environmentName: EnvironmentName = "studio";
  private bloom: UnrealBloomPass | null = null;
  private needsFactoryFit = false;
  private onEnvironmentProgress: ((fraction: number, ready: boolean) => void) | null = null;

  constructor(
    private container: HTMLElement,
    private events: EngineEvents,
  ) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance", preserveDrawingBuffer: false });
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.0;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap; // r186: PCF filters by shadow.radius
    this.renderer.domElement.tabIndex = 0;
    this.renderer.domElement.setAttribute("aria-label", "3D workcell viewport");
    container.appendChild(this.renderer.domElement);

    this.perspective = new THREE.PerspectiveCamera(42, 1, 0.02, 90);
    this.perspective.up.set(0, 0, 1);
    this.ortho = new THREE.OrthographicCamera(-1, 1, 1, -1, -20, 60);
    this.ortho.up.set(0, 0, 1);
    this.camera = this.perspective;
    this.perspective.position.set(1.9, -1.4, 1.6);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.12;
    this.controls.target.set(0.55, 0, 0.8);
    this.controls.maxDistance = 14;
    this.controls.minDistance = 0.08;
    this.controls.screenSpacePanning = true;

    // Lights: a warm key with shadows, a cool fill, a rim to separate the robot from the room.
    this.hemi = new THREE.HemisphereLight(0xbfd2ff, 0x1a1612, 0.55);
    this.key = new THREE.DirectionalLight(0xfff2e5, 2.4);
    this.key.position.set(2.4, -1.6, 3.4);
    this.key.castShadow = true;
    this.key.shadow.bias = -0.00025;
    this.key.shadow.normalBias = 0.012;
    this.key.shadow.radius = 4;
    this.fill = new THREE.DirectionalLight(0xd8e6ff, 0.7);
    this.fill.position.set(-2.0, 2.2, 1.6);
    this.rim = new THREE.DirectionalLight(0xffffff, 0.9);
    this.rim.position.set(-1.6, -2.8, 2.2);
    this.scene.add(this.hemi, this.key, this.key.target, this.fill, this.rim);
    // The factory's cell light: a pool of light over the bench from the fixture above it.
    this.cellLight = new THREE.SpotLight(0xfff2e2, 0, 0, 0.75, 1.0, 2);
    this.cellLight.visible = false;
    this.scene.add(this.cellLight, this.cellLight.target);

    this.floor = new THREE.Mesh(
      new THREE.CircleGeometry(14, 96),
      new THREE.MeshStandardMaterial({ color: THEMES.dark.floor, roughness: 0.92, metalness: 0.0 }),
    );
    this.floor.receiveShadow = true;
    this.floor.name = "floor";
    this.grid = this.makeGrid();
    this.dome = this.makeDome();
    this.pool = this.makePool();
    this.stage.add(this.floor, this.pool);
    this.scene.add(this.grid);
    this.scene.add(this.dome);
    this.scene.add(this.stage);

    this.gizmo = new TransformControls(this.camera, this.renderer.domElement);
    this.gizmo.setSize(0.85);
    this.gizmo.setSpace("world");
    this.gizmo.attach(this.gizmoTarget);
    this.gizmo.enabled = false;
    this.gizmo.getHelper().visible = false;
    this.scene.add(this.gizmoTarget, this.gizmo.getHelper());
    this.gizmo.addEventListener("dragging-changed", (e) => {
      const active = Boolean((e as unknown as { value: boolean }).value);
      this.controls.enabled = !active;
      this.dragging = active;
      if (this.gizmoArm) this.sendCursor(active ? "start" : "end");
    });
    this.gizmo.addEventListener("objectChange", () => {
      if (!this.dragging || !this.gizmoArm) return;
      const now = performance.now();
      if (now - this.lastCursorSent > 33) this.sendCursor("update");
    });

    this.drafts = new DraftLayer(this.scene, this.camera, this.renderer.domElement, this.controls, (id, at, rpy) =>
      this.events.onDraftMove(id, at, rpy),
    );

    this.factory = new FactoryEnvironment(this.renderer);
    this.scene.add(this.factory.group);

    this.applyEnvironment();
    this.setTheme("dark");
    this.setQuality("balanced");

    const canvas = this.renderer.domElement;
    canvas.addEventListener("pointerdown", (e) => {
      this.pointerDown = { x: e.clientX, y: e.clientY, t: performance.now() };
    });
    canvas.addEventListener("pointerup", (e) => this.onPointerUp(e));

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(container);
    this.resize();
    this.loop();
  }

  // ------------------------------------------------------------------ stage
  private makeDome(): THREE.Mesh {
    const material = new THREE.ShaderMaterial({
      side: THREE.BackSide,
      depthWrite: false,
      fog: false,
      uniforms: { top: { value: new THREE.Color() }, horizon: { value: new THREE.Color() }, bottom: { value: new THREE.Color() } },
      vertexShader: `varying vec3 vDir; void main() { vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
      fragmentShader: [
        "uniform vec3 top;",
        "uniform vec3 horizon;",
        "uniform vec3 bottom;",
        "varying vec3 vDir;",
        "void main() {",
        "  float h = vDir.z;",
        "  vec3 c = h > 0.0 ? mix(horizon, top, pow(clamp(h, 0.0, 1.0), 0.55)) : mix(horizon, bottom, pow(clamp(-h * 4.0, 0.0, 1.0), 0.6));",
        "  gl_FragColor = vec4(c, 1.0);",
        "  #include <colorspace_fragment>",
        "}",
      ].join("\n"),
    });
    const dome = new THREE.Mesh(new THREE.SphereGeometry(30, 32, 16), material);
    dome.name = "sky";
    dome.renderOrder = -10;
    return dome;
  }

  private makePool(): THREE.Mesh {
    const size = 512;
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = size;
    const g = canvas.getContext("2d")!;
    const grad = g.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    grad.addColorStop(0, "rgba(255,255,255,1)");
    grad.addColorStop(0.45, "rgba(255,255,255,0.45)");
    grad.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = grad;
    g.fillRect(0, 0, size, size);
    const tex = new THREE.CanvasTexture(canvas);
    const pool = new THREE.Mesh(
      new THREE.PlaneGeometry(6, 6),
      new THREE.MeshBasicMaterial({ map: tex, transparent: true, opacity: 0.06, depthWrite: false, blending: THREE.AdditiveBlending }),
    );
    pool.position.set(0.5, 0, 0.0015);
    pool.name = "light-pool";
    return pool;
  }

  private makeGrid(): THREE.Group {
    const group = new THREE.Group();
    group.name = "grid";
    const minor = new THREE.GridHelper(12, 120, THEMES.dark.grid, THEMES.dark.grid);
    const major = new THREE.GridHelper(12, 12, THEMES.dark.gridMajor, THEMES.dark.gridMajor);
    for (const g of [minor, major]) {
      g.rotation.x = Math.PI / 2;
      const m = g.material as THREE.LineBasicMaterial;
      m.transparent = true;
      m.opacity = g === minor ? 0.35 : 0.6;
      m.depthWrite = false;
      g.position.z = 0.0008;
      group.add(g);
    }
    return group;
  }

  private applyEnvironment(): void {
    const pmrem = new THREE.PMREMGenerator(this.renderer);
    const env = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    pmrem.dispose();
    this.envTexture = env;
    this.scene.environment = env;
    this.scene.environmentIntensity = 0.55;
    this.scene.environmentRotation.set(Math.PI / 2, 0, 0); // the room is y-up; the world is z-up
  }

  setTheme(theme: Theme): void {
    this.theme = theme;
    this.applyLook();
  }

  /** Studio (a neutral stage, coloured by the interface theme) or factory (a lit industrial hall). */
  setEnvironment(name: EnvironmentName, onProgress?: (fraction: number, ready: boolean) => void): void {
    this.environmentName = name;
    if (onProgress) this.onEnvironmentProgress = onProgress;
    if (name === "factory" && !this.factory.ready) {
      void this.factory
        .load((f) => this.onEnvironmentProgress?.(f, false))
        .then(() => {
          this.onEnvironmentProgress?.(1, true);
          if (this.environmentName === "factory") {
            this.needsFactoryFit = true;
            this.applyLook();
            this.buildComposer();
          }
        });
    } else {
      this.onEnvironmentProgress?.(1, true);
    }
    this.applyLook();
    this.buildComposer();
    this.resize();
  }

  get environment(): EnvironmentName {
    return this.environmentName;
  }

  /** What the cell's stack light and operator panel show. */
  setMachineState(state: MachineState, screen: ScreenState): void {
    this.factory.setState(state, screen);
  }

  private applyLook(): void {
    const theme = this.theme;
    const factory = this.environmentName === "factory";
    this.stage.visible = !factory;
    this.dome.visible = !factory;
    this.factory.group.visible = factory;
    this.grid.visible = !factory && (this.overlayState?.grid ?? true);
    this.controls.maxPolarAngle = factory ? Math.PI * 0.495 : Math.PI;
    if (factory) {
      this.built?.setBenchTheme("factory");
      // Daylight from the roof and the hall's own light: the HDRI does the soft fill and the
      // reflections, one high key light gives crisp shadows on and around the cell.
      this.scene.background = new THREE.Color(0x7f8891);
      this.scene.fog = new THREE.FogExp2(0x8c949d, 0.028);
      this.scene.environment = this.factory.environment ?? this.envTexture;
      this.scene.environmentIntensity = this.factory.environment ? 0.62 : 0.5;
      this.cellLight.visible = true;
      this.cellLight.intensity = 55;
      this.scene.environmentRotation.set(Math.PI / 2, 0, 0);
      this.hemi.color.set(0xe8eef5);
      this.hemi.groundColor.set(0x6d6862);
      this.hemi.intensity = 0.28;
      this.key.color.set(0xfff3e4);
      this.key.intensity = 2.2;
      this.fill.intensity = 0.15;
      this.rim.intensity = 0.0;
      this.renderer.toneMappingExposure = 0.88;
      this.fitKeyLight();
      if (this.outline) this.styleOutline();
      return;
    }
    this.built?.setBenchTheme(theme);
    this.cellLight.visible = false;
    this.scene.environment = this.envTexture;
    this.scene.environmentRotation.set(Math.PI / 2, 0, 0);
    this.key.color.set(0xfff2e5);
    this.fill.intensity = 0.7;
    this.rim.intensity = 0.9;
    this.fitKeyLight();
    const t = THEMES[theme];
    this.scene.background = new THREE.Color(t.background);
    this.scene.fog = new THREE.Fog(t.fog, 6, 18);
    const sky = (this.dome.material as THREE.ShaderMaterial).uniforms;
    sky.top.value.set(t.skyTop);
    sky.horizon.value.set(t.skyHorizon);
    sky.bottom.value.set(t.skyBottom);
    (this.pool.material as THREE.MeshBasicMaterial).opacity = t.pool;
    (this.floor.material as THREE.MeshStandardMaterial).color.set(t.floor);
    const [minor, major] = this.grid.children as THREE.GridHelper[];
    (minor.material as THREE.LineBasicMaterial).color.set(t.grid);
    (major.material as THREE.LineBasicMaterial).color.set(t.gridMajor);
    this.hemi.color.set(t.hemiSky);
    this.hemi.groundColor.set(t.hemiGround);
    this.hemi.intensity = theme === "dark" ? 0.55 : 0.62;
    this.scene.environmentIntensity = theme === "dark" ? 0.55 : 0.42;
    this.key.intensity = theme === "dark" ? 2.4 : 2.1;
    this.renderer.toneMappingExposure = theme === "dark" ? 1.0 : 0.94;
    if (this.outline) this.styleOutline();
  }

  /**
   * The key light's position and shadow frustum, sized to the working volume. In the studio it is
   * a classic three-quarter key; in the factory it comes from high up, as skylight does, and its
   * shadow also covers the guarding, cabinet and panel around the cell.
   */
  private fitKeyLight(): void {
    const m = this.manifest;
    if (!m) return;
    const origin = m.cell.origin ?? [0.6, 0, m.cell.surfaceZ];
    const factory = this.environmentName === "factory";
    const half = m.cell.half ? Math.max(m.cell.half[0], m.cell.half[1]) : 0.7;
    const reach = factory ? Math.max(3.4, half + 2.6) : Math.max(1.1, half + 0.5);
    const cam = this.key.shadow.camera as THREE.OrthographicCamera;
    cam.left = -reach;
    cam.right = reach;
    cam.top = reach;
    cam.bottom = -reach;
    cam.near = 0.1;
    cam.far = factory ? 24 : 9;
    cam.updateProjectionMatrix();
    this.key.target.position.set(origin[0], origin[1], origin[2]);
    this.cellLight.position.set(origin[0] + 0.4, origin[1] - 0.6, origin[2] + 6.6);
    this.cellLight.target.position.set(origin[0], origin[1], origin[2]);
    if (factory) {
      this.key.position.set(origin[0] - 3.2, origin[1] - 4.6, origin[2] + 10.5);
      this.key.shadow.radius = 6;
      this.key.shadow.bias = -0.0002;
      this.key.shadow.normalBias = 0.02;
    } else {
      this.key.position.set(origin[0] + 1.9, origin[1] - 1.3, origin[2] + 3.0);
      this.key.shadow.radius = 4;
      this.key.shadow.bias = -0.00025;
      this.key.shadow.normalBias = 0.012;
    }
  }

  /** Lays the factory's guarding, markings, cabinet and panel around the cell as it stands now. */
  private fitFactory(): void {
    const built = this.built;
    if (!built) return;
    built.root.updateMatrixWorld(true);
    const box = new THREE.Box3();
    for (const objects of built.byOwner.values()) for (const o of objects) box.expandByObject(o);
    if (box.isEmpty()) box.setFromCenterAndSize(this.focusPoint(), new THREE.Vector3(1.2, 1.2, 1));
    const cx = (box.min.x + box.max.x) / 2;
    const cy = (box.min.y + box.max.y) / 2;
    const hx = Math.min(4, Math.max(0.6, (box.max.x - box.min.x) / 2 + 0.15));
    const hy = Math.min(4, Math.max(0.6, (box.max.y - box.min.y) / 2 + 0.15));
    this.factory.group.position.set(cx, cy, this.manifest?.cell.groundZ ?? 0);
    this.factory.fit({ x0: -hx, x1: hx, y0: -hy, y1: hy });
  }

  setQuality(quality: Quality): void {
    this.quality = quality;
    const q = QUALITY[quality];
    this.factory.setQuality({ atmosphere: quality !== "performance" });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, q.pixelRatio));
    this.renderer.shadowMap.enabled = q.shadows;
    this.key.castShadow = q.shadows;
    if (this.key.shadow.mapSize.x !== q.shadowSize) {
      this.key.shadow.mapSize.set(q.shadowSize, q.shadowSize);
      this.key.shadow.map?.dispose();
      this.key.shadow.map = null;
    }
    this.scene.traverse((o) => {
      const m = (o as THREE.Mesh).material as THREE.Material | undefined;
      if (m) m.needsUpdate = true;
    });
    this.buildComposer();
    this.resize();
  }

  private buildComposer(): void {
    this.composer?.dispose();
    this.composer = null;
    this.outline = null;
    this.gtao = null;
    this.bloom = null;
    const q = QUALITY[this.quality];
    const factory = this.environmentName === "factory";
    if (!q.post) return;
    const size = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    const target = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType, samples: 4 });
    const composer = new EffectComposer(this.renderer, target);
    this.renderPass = new RenderPass(this.scene, this.camera);
    composer.addPass(this.renderPass);
    if (q.ao || factory) {
      // Ultra keeps full-resolution occlusion; the factory at Balanced runs it at half resolution.
      this.gtao = q.ao ? new GTAOPass(this.scene, this.camera, size.x, size.y) : new HalfGTAOPass(this.scene, this.camera, size.x, size.y);
      this.gtao.blendIntensity = factory ? 1.0 : 0.85;
      composer.addPass(this.gtao);
    }
    if (factory) {
      this.bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), 0.42, 0.6, 3.2);
      composer.addPass(this.bloom);
    }
    if (q.outline) {
      this.outline = new OutlinePass(new THREE.Vector2(size.x, size.y), this.scene, this.camera);
      this.outline.edgeStrength = 3.2;
      this.outline.edgeGlow = 0.0;
      this.outline.edgeThickness = 1.0;
      this.styleOutline();
      composer.addPass(this.outline);
    }
    composer.addPass(new OutputPass());
    if (factory) {
      const vignette = new ShaderPass(VignetteShader);
      vignette.uniforms.offset.value = 1.05;
      vignette.uniforms.darkness.value = 1.05;
      composer.addPass(vignette);
    }
    if (this.quality === "ultra") composer.addPass(new SMAAPass());
    this.composer = composer;
    this.applySelection();
  }

  private styleOutline(): void {
    const outline = this.outline;
    if (!outline) return;
    const light = this.theme === "light";
    outline.visibleEdgeColor.set(light ? 0xe8461d : 0xff4713);
    outline.hiddenEdgeColor.set(light ? 0xf2a385 : 0x7a2a12);
    outline.edgeStrength = light ? 6 : 3.2;
    // Additive blending vanishes on a light background; draw the edge normally there.
    const overlay = (outline as unknown as { overlayMaterial?: THREE.Material }).overlayMaterial;
    if (overlay) {
      overlay.blending = light ? THREE.NormalBlending : THREE.AdditiveBlending;
      overlay.needsUpdate = true;
    }
  }

  // ------------------------------------------------------------------ scene
  setManifest(manifest: Manifest, geometry: ArrayBuffer): void {
    this.clearScene();
    this.manifest = manifest;
    this.built = buildScene(manifest, geometry, { premium: this.quality !== "performance" });
    this.scene.add(this.built.root);
    this.overlays = new OverlayLayer(manifest, this.built);
    this.scene.add(this.overlays.group);
    if (this.overlayState) this.setOverlays(this.overlayState);
    const surfaceZ = manifest.cell.surfaceZ;
    this.stage.position.z = manifest.cell.groundZ ?? 0;
    this.grid.position.z = manifest.cell.groundZ ?? 0;
    const origin = manifest.cell.origin ?? [0.6, 0, surfaceZ];
    this.pool.position.set(origin[0] - 0.2, origin[1], 0.0015);
    this.fitKeyLight();
    this.appliedSeq = -1;
    this.needsFactoryFit = true;
    this.built.setBenchTheme(this.environmentName === "factory" ? "factory" : this.theme);
    this.applySelection();
    this.needsFraming = true;
  }

  private clearScene(): void {
    if (this.built) {
      this.scene.remove(this.built.root);
      this.built.dispose();
      this.built = null;
    }
    if (this.overlays) {
      this.scene.remove(this.overlays.group);
      this.overlays.dispose();
      this.overlays = null;
    }
    this.frame = null;
    this.setGizmoArm(null);
  }

  setFrame(frame: Frame): void {
    this.frame = frame;
  }

  private applyFrame(): void {
    const frame = this.frame;
    const built = this.built;
    if (!frame || !built || frame.header.seq === this.appliedSeq) return;
    if (!this.manifest || frame.header.gen !== this.manifest.generation) return;
    this.appliedSeq = frame.header.seq;
    const q = frame.bodyQ;
    const n = Math.min(built.bodies.length, q.length / 7);
    for (let b = 0; b < n; b++) {
      const o = b * 7;
      const g = built.bodies[b];
      g.position.set(q[o], q[o + 1], q[o + 2]);
      g.quaternion.set(q[o + 3], q[o + 4], q[o + 5], q[o + 6]);
    }
    for (const d of built.deformables) d.update(frame.particles);
    if (this.needsFraming) {
      this.needsFraming = false;
      this.applyPreset("iso", false);
    }
    if (this.needsFactoryFit && this.factory.ready) {
      this.needsFactoryFit = false;
      this.fitFactory();
    }
    this.overlays?.update(frame, this.dragging || this.gizmo.getHelper().visible ? this.gizmoArm : null);
    if (this.gizmoArm && !this.dragging) {
      const arm = frame.header.arms[this.gizmoArm];
      if (arm && !arm.error) {
        const [x, y, z, qx, qy, qz, qw] = arm.mode === "cartesian" ? arm.cursor : arm.ee;
        this.gizmoTarget.position.set(x, y, z);
        this.gizmoTarget.quaternion.set(qx, qy, qz, qw);
      }
    }
  }

  // ----------------------------------------------------------- selection
  setSelection(key: string | null): void {
    this.selected = key;
    this.applySelection();
    const arm = key?.startsWith("arm:") ? key.slice(4) : null;
    this.setGizmoArm(arm);
  }

  private applySelection(): void {
    const objects = (this.selected && this.built?.byOwner.get(this.selected)) || [];
    if (this.outline) this.outline.selectedObjects = objects;
    if (!this.outline && this.built) {
      // Performance mode: an emissive tint instead of the outline pass.
      for (const mesh of this.built.visuals) {
        const m = mesh.material as THREE.MeshStandardMaterial;
        m.emissive?.set(0x000000);
      }
    }
  }

  setGizmoArm(arm: string | null): void {
    this.gizmoArm = arm;
    const helper = this.gizmo.getHelper();
    helper.visible = Boolean(arm);
    this.gizmo.enabled = Boolean(arm);
  }

  setGizmoMode(mode: "translate" | "rotate", space: "world" | "local"): void {
    this.gizmo.setMode(mode);
    this.gizmo.setSpace(space);
    this.drafts.setMode(mode);
  }

  /** The builder's draft: ghosts at document placements, the selected one on a gizmo. */
  setDraft(doc: WorkcellDoc | null, origin: Vec3 | null, visible: boolean, selected: string | null): void {
    this.drafts.update(doc, origin, visible, selected?.startsWith("draft:") ? selected.slice(6) : null);
  }

  private sendCursor(phase: "start" | "update" | "end"): void {
    if (!this.gizmoArm) return;
    this.lastCursorSent = performance.now();
    const p = this.gizmoTarget.position;
    const q = this.gizmoTarget.quaternion;
    this.events.onCursor(this.gizmoArm, [p.x, p.y, p.z], [q.x, q.y, q.z, q.w], phase);
  }

  private onPointerUp(e: PointerEvent): void {
    const down = this.pointerDown;
    this.pointerDown = null;
    if (!down || this.dragging || this.drafts.isDragging) return;
    if (Math.hypot(e.clientX - down.x, e.clientY - down.y) > 5 || performance.now() - down.t > 600) return;
    if (e.button !== 0) return;
    const hit = this.pick(e.clientX, e.clientY);
    this.events.onSelect(hit);
  }

  pick(clientX: number, clientY: number): string | null {
    if (!this.built) return null;
    const rect = this.renderer.domElement.getBoundingClientRect();
    const ndc = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
    this.raycaster.setFromCamera(ndc, this.camera);
    const ghosts = this.raycaster.intersectObjects(this.drafts.pickTargets(), false);
    if (ghosts.length) return (ghosts[0].object.userData.owner as string) ?? null;
    const targets: THREE.Object3D[] = [...this.built.visuals, ...this.built.deformables.map((d) => d.object)];
    const hits = this.raycaster.intersectObjects(targets, false);
    for (const hit of hits) {
      const owner = hit.object.userData.owner as string | null | undefined;
      if (owner) return owner;
    }
    return null;
  }

  // -------------------------------------------------------------- overlays
  setOverlays(overlays: Overlays): void {
    this.overlayState = overlays;
    this.overlays?.setVisibility(overlays);
    // The factory floor carries its own markings; the measuring grid belongs to the studio.
    this.grid.visible = overlays.grid && this.environmentName !== "factory";
    this.appliedSeq = -1;
  }

  // --------------------------------------------------------------- cameras
  setProjection(mode: "perspective" | "orthographic"): void {
    const from = this.camera;
    const next = mode === "orthographic" ? this.ortho : this.perspective;
    if (next === from) return;
    next.position.copy(from.position);
    next.quaternion.copy(from.quaternion);
    this.camera = next;
    this.controls.object = next;
    this.gizmo.camera = next;
    this.drafts.setCamera(next);
    if (this.renderPass) this.renderPass.camera = next;
    if (this.outline) this.outline.renderCamera = next;
    if (this.gtao) this.gtao.camera = next;
    this.resize();
  }

  setFollow(mode: FollowMode, arm: string | null): void {
    this.follow = mode;
    this.followArm = arm;
  }

  private focusPoint(): THREE.Vector3 {
    const m = this.manifest;
    const o = m?.cell.origin ?? [0.6, 0, 0.75];
    return new THREE.Vector3(o[0], o[1], (o[2] ?? 0.75) + 0.12);
  }

  /** The working volume: arms, objects, deformables, regions and the work surface; not the furniture under it. */
  private contentSphere(): THREE.Sphere {
    const box = new THREE.Box3();
    const built = this.built;
    if (built) {
      built.root.updateMatrixWorld(true);
      for (const [owner, objects] of built.byOwner) {
        if (owner.startsWith("static:")) continue;
        for (const o of objects) box.expandByObject(o);
      }
    }
    const cell = this.manifest?.cell;
    if (cell?.origin && cell.half) {
      const [ox, oy, oz] = cell.origin;
      box.expandByPoint(new THREE.Vector3(ox - cell.half[0], oy - cell.half[1], oz));
      box.expandByPoint(new THREE.Vector3(ox + cell.half[0], oy + cell.half[1], oz));
    }
    if (box.isEmpty()) box.setFromCenterAndSize(this.focusPoint(), new THREE.Vector3(1, 1, 1));
    const sphere = box.getBoundingSphere(new THREE.Sphere());
    sphere.radius = Math.max(0.25, sphere.radius);
    return sphere;
  }

  private presetPose(name: PresetName): [THREE.Vector3, THREE.Vector3] {
    const sphere = this.contentSphere();
    const c = sphere.center.clone();
    const fov = THREE.MathUtils.degToRad(this.perspective.fov);
    const aspect = Math.max(0.5, this.perspective.aspect || 1.6);
    // The distance at which the working volume fills the frame in its tighter direction.
    const fit = (margin: number) => (sphere.radius * margin) / Math.sin(Math.min(fov, 2 * Math.atan(Math.tan(fov / 2) * aspect)) / 2);
    const from = (dir: THREE.Vector3, margin = 1.0) => c.clone().addScaledVector(dir.normalize(), fit(margin));
    switch (name) {
      case "operator": {
        // Behind the robot base, looking out over the bench the way an operator stands.
        const b = this.robotBase() ?? new THREE.Vector3(c.x - 0.6, c.y, c.z);
        const dir = new THREE.Vector3(b.x - c.x, b.y - c.y, 0);
        if (dir.lengthSq() < 1e-6) dir.set(-1, 0, 0);
        dir.normalize();
        return [from(new THREE.Vector3(dir.x, dir.y, 0.62), 0.92), c.clone().add(new THREE.Vector3(0, 0, -0.05))];
      }
      case "front":
        return [from(new THREE.Vector3(1, 0, 0.36)), c];
      case "side":
        return [from(new THREE.Vector3(0, -1, 0.3)), c];
      case "top":
        return [from(new THREE.Vector3(0.0001, 0, 1), 0.95), c];
      case "tool": {
        const arm = this.followArm ?? this.manifest?.arms[0]?.name;
        const ee = arm ? this.frame?.header.arms[arm]?.ee : undefined;
        if (ee) {
          const p = new THREE.Vector3(ee[0], ee[1], ee[2]);
          return [p.clone().add(new THREE.Vector3(0.34, -0.3, 0.2)), p];
        }
        return this.presetPose("iso");
      }
      case "iso":
      default:
        // In the factory, a little wider: the cell's surroundings are part of the picture.
        return [from(new THREE.Vector3(1.0, -0.95, 0.66), this.environmentName === "factory" ? 1.12 : 0.84), c];
    }
  }

  private robotBase(): THREE.Vector3 | null {
    const m = this.manifest;
    const frame = this.frame;
    if (!m?.arms.length) return null;
    const bodies = m.arms.map((a) => a.baseBody).filter((b) => b >= 0);
    if (!bodies.length || !frame) {
      return null;
    }
    const v = new THREE.Vector3();
    for (const b of bodies) v.add(new THREE.Vector3(frame.bodyQ[b * 7], frame.bodyQ[b * 7 + 1], frame.bodyQ[b * 7 + 2]));
    return v.multiplyScalar(1 / bodies.length);
  }

  applyPreset(name: PresetName, animate = true): void {
    const [position, target] = this.presetPose(name);
    this.moveTo(position, target, animate);
  }

  focusSelected(): void {
    if (!this.built || !this.selected) return;
    const objects = this.built.byOwner.get(this.selected);
    if (!objects?.length) return;
    const box = new THREE.Box3();
    for (const o of objects) box.expandByObject(o);
    if (box.isEmpty()) return;
    const centre = box.getCenter(new THREE.Vector3());
    const radius = Math.max(0.06, box.getSize(new THREE.Vector3()).length() * 0.5);
    const dir = this.camera.position.clone().sub(this.controls.target).normalize();
    const distance = radius / Math.sin(THREE.MathUtils.degToRad(this.perspective.fov / 2)) * 1.15;
    this.moveTo(centre.clone().addScaledVector(dir, distance), centre, true);
  }

  private moveTo(position: THREE.Vector3, target: THREE.Vector3, animate: boolean): void {
    if (!animate || this.reducedMotion) {
      this.camera.position.copy(position);
      this.controls.target.copy(target);
      this.controls.update();
      return;
    }
    this.tween = {
      from: [this.camera.position.clone(), this.controls.target.clone()],
      to: [position, target],
      t0: performance.now(),
      ms: 650,
    };
  }

  // ------------------------------------------------------------- capture
  screenshot(): Promise<Blob | null> {
    this.renderOnce();
    return new Promise((resolve) => this.renderer.domElement.toBlob((b) => resolve(b), "image/png"));
  }

  /** Moves the camera to look from `eye` at `target`, over `ms` (0 jumps). */
  setView(eye: number[], target: number[], ms = 0): void {
    const position = new THREE.Vector3(eye[0], eye[1], eye[2]);
    const look = new THREE.Vector3(target[0], target[1], target[2]);
    if (ms <= 0 || this.reducedMotion) {
      this.moveTo(position, look, false);
      return;
    }
    this.tween = { from: [this.camera.position.clone(), this.controls.target.clone()], to: [position, look], t0: performance.now(), ms };
  }

  /** Where the factory stands (its floor origin under the cell's centre), or null in the studio. */
  factoryOrigin(): number[] | null {
    if (this.environmentName !== "factory") return null;
    const p = this.factory.group.position;
    return [p.x, p.y, p.z];
  }

  cameraState(): { eye: number[]; lookAt: number[]; fovY: number } {
    const p = this.camera.position;
    const t = this.controls.target;
    return { eye: [p.x, p.y, p.z], lookAt: [t.x, t.y, t.z], fovY: this.perspective.fov };
  }

  // ---------------------------------------------------------------- loop
  private resize(): void {
    const w = Math.max(1, this.container.clientWidth);
    const h = Math.max(1, this.container.clientHeight);
    this.renderer.setSize(w, h, false);
    this.perspective.aspect = w / h;
    this.perspective.updateProjectionMatrix();
    const distance = this.camera.position.distanceTo(this.controls.target);
    const halfH = Math.tan(THREE.MathUtils.degToRad(this.perspective.fov / 2)) * distance;
    this.ortho.left = -halfH * (w / h);
    this.ortho.right = halfH * (w / h);
    this.ortho.top = halfH;
    this.ortho.bottom = -halfH;
    this.ortho.updateProjectionMatrix();
    const size = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    this.composer?.setSize(w, h);
    this.outline?.setSize(size.x, size.y);
    this.bloom?.setSize(size.x, size.y);
    this.overlays?.setResolution(w, h);
  }

  private loop = (): void => {
    if (this.disposed) return;
    this.raf = requestAnimationFrame(this.loop);
    const now = performance.now();
    const dt = now - this.lastTick;
    this.lastTick = now;
    this.frameTimes.push(dt);
    if (this.frameTimes.length > 90) this.frameTimes.shift();

    if (this.tween) {
      const k = Math.min(1, (now - this.tween.t0) / this.tween.ms);
      const e = 1 - (1 - k) ** 3;
      this.camera.position.lerpVectors(this.tween.from[0], this.tween.to[0], e);
      this.controls.target.lerpVectors(this.tween.from[1], this.tween.to[1], e);
      if (k >= 1) this.tween = null;
    }
    this.applyFrame();
    this.applyFollow();
    this.controls.update();
    if (this.environmentName === "factory") {
      this.factory.update(dt / 1000);
      this.keepInsideHall();
    }
    if (this.camera === this.ortho) {
      // Keep the orthographic frame matched to the orbit distance, so zooming still zooms.
      const distance = this.camera.position.distanceTo(this.controls.target);
      const halfH = Math.tan(THREE.MathUtils.degToRad(this.perspective.fov / 2)) * distance;
      const aspect = this.perspective.aspect;
      this.ortho.left = -halfH * aspect;
      this.ortho.right = halfH * aspect;
      this.ortho.top = halfH;
      this.ortho.bottom = -halfH;
      this.ortho.updateProjectionMatrix();
    }
    const t0 = performance.now();
    this.renderOnce();
    const ms = performance.now() - t0;
    if (now - this.lastStats > 500) {
      this.lastStats = now;
      const mean = this.frameTimes.reduce((a, b) => a + b, 0) / Math.max(1, this.frameTimes.length);
      const info = this.renderer.info.render;
      const q = QUALITY[this.quality];
      this.events.onStats({
        fps: mean > 0 ? 1000 / mean : 0,
        ms,
        calls: info.calls,
        triangles: info.triangles,
        quality: this.quality,
        shadows: q.shadows,
        ao: q.ao,
      });
    }
  };

  /** In the factory the orbit stops at the walls, the roof and the floor instead of passing through. */
  private keepInsideHall(): void {
    const o = this.factory.group.position;
    const p = this.camera.position;
    const m = 0.6;
    const x = THREE.MathUtils.clamp(p.x, o.x + HALL.x0 + m, o.x + HALL.x1 - m);
    const y = THREE.MathUtils.clamp(p.y, o.y + HALL.y0 + m, o.y + HALL.y1 - m);
    const z = THREE.MathUtils.clamp(p.z, o.z + 0.15, o.z + HALL.eave - 0.4);
    if (x !== p.x || y !== p.y || z !== p.z) p.set(x, y, z);
  }

  private applyFollow(): void {
    if (this.follow === "none" || !this.frame) return;
    let point: THREE.Vector3 | null = null;
    if (this.follow === "tool") {
      const arm = this.followArm ?? this.manifest?.arms[0]?.name;
      const ee = arm ? this.frame.header.arms[arm]?.ee : undefined;
      if (ee) point = new THREE.Vector3(ee[0], ee[1], ee[2]);
    } else {
      point = this.robotBase();
      if (point) point.z += 0.35;
    }
    if (!point) return;
    const delta = point.clone().sub(this.controls.target);
    this.controls.target.add(delta);
    this.camera.position.add(delta);
  }

  private renderOnce(): void {
    if (this.composer) this.composer.render();
    else this.renderer.render(this.scene, this.camera);
  }

  dispose(): void {
    this.disposed = true;
    cancelAnimationFrame(this.raf);
    this.resizeObserver.disconnect();
    this.clearScene();
    this.gizmo.dispose();
    this.drafts.dispose();
    this.controls.dispose();
    this.composer?.dispose();
    this.envTexture?.dispose();
    this.factory.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}
