// SPDX-License-Identifier: Apache-2.0
// The shapes the BRIDGE service sends. Mirrors bridge_studio/manifest.py, host.py and frames.py.

export type Vec3 = [number, number, number];
export type Quat = [number, number, number, number]; // x, y, z, w
export type Pose = [number, number, number, number, number, number, number];

export type ShapeKind =
  | "plane"
  | "sphere"
  | "capsule"
  | "ellipsoid"
  | "cylinder"
  | "box"
  | "mesh"
  | "cone"
  | "convex"
  | "unsupported";

export interface ShapeInfo {
  i: number;
  body: number;
  type: ShapeKind;
  size: number[];
  xform: Pose;
  visible: boolean;
  collide: boolean;
  site: boolean;
  label: string;
  owner: string | null;
  live: boolean;
  color?: number[];
  opacity?: number;
  roughness?: number;
  metallic?: number;
  texture?: string;
  uvScale?: number[];
  uvOffset?: number[];
  mesh?: number | null;
}

export interface MeshInfo {
  id: number;
  vertices: number;
  indices: number;
  position: number;
  normal?: number;
  uv?: number;
  index: number;
}

export interface BodyInfo {
  i: number;
  label: string;
  owner: string | null;
  mass: number;
  com: Vec3;
}

export interface SubjectInfo {
  key: string;
  kind: "object" | "cloth" | "arm" | "static" | "region";
  name: string;
  bodies: number[];
  static?: boolean;
  category?: string;
  specKind?: string;
  mass?: number;
  particles?: number;
  asset?: string;
}

export interface JointInfo {
  name: string;
  coord: number;
  dof: number;
  type: "revolute" | "prismatic" | "other";
  unit: "rad" | "m";
  lower: number | null;
  upper: number | null;
  home: number | null;
  effortLimit: number | null;
  velocityLimit: number | null;
}

export interface ArmInfo {
  name: string;
  role: string;
  joints: JointInfo[];
  eeBody: number;
  baseBody: number;
  bodies: number[];
  homePose: Pose;
  tcpOffset: Vec3;
  gripper: { kind: string; span: number; coords: number[]; openWidth: number } | null;
}

export interface DeformableInfo {
  name: string;
  owner: string;
  offset: number;
  count: number;
  color: number[];
  opacity: number;
  thickness: number;
  roughness?: number;
  metallic?: number;
  kind: "surface" | "tube" | "points";
  radius?: number;
  slab?: boolean;
  triangles?: number;
  index?: number;
}

export interface CameraInfo {
  name: string;
  eye: Vec3 | null;
  lookAt: Vec3 | null;
  up: Vec3;
  fovY: number;
  width: number;
  height: number;
  parentBody: number;
}

export interface RegionInfo {
  name: string;
  key: string;
  frame: string;
  frameBody: number;
  origin: Vec3;
  xy: [number, number, number, number];
  z: [number, number];
}

export interface IslandInfo {
  name: string;
  solver: string;
  solverClass: string;
  substeps: number;
  domains: string[];
  entities: string[];
  bodies: number;
  particles: number;
  hostEngine: boolean;
  params: Record<string, unknown>;
}

export interface PhysicsInfo {
  controlHz: number;
  substeps: number;
  simDt: number;
  gravity: number;
  cudaGraph: boolean;
  device: string;
  coupler: string;
  islands: IslandInfo[];
  collision: {
    softContactMargin: number | null;
    rigidGap: number | null;
    contactKe: number | null;
    contactKd: number | null;
    fullSurfaceContact: boolean;
  };
  summary: string;
  problems: string[];
  warnings: string[];
}

export interface ScenarioInfo {
  id: string;
  title: string;
  summary: string;
  robot: string;
  physics: string;
  category: string;
  requires?: string[];
  available?: boolean;
  unavailableReason?: string;
  media?: string;
  successLabel?: string;
  source?: string;
}

export interface CellInfo {
  name: string;
  surfaceZ: number;
  origin?: Vec3;
  groundZ?: number;
  half?: Vec3 | null;
  mounts?: { name: string; xform: Pose }[];
  provenance?: string;
}

export interface Manifest {
  schema: string;
  generation: number;
  scenario: ScenarioInfo;
  cell: CellInfo;
  physics: PhysicsInfo;
  counts: { bodies: number; shapes: number; joints: number; particles: number; frameParticles: number };
  bodies: BodyInfo[];
  shapes: ShapeInfo[];
  meshes: MeshInfo[];
  subjects: SubjectInfo[];
  arms: ArmInfo[];
  deformables: DeformableInfo[];
  cameras: CameraInfo[];
  regions: RegionInfo[];
  geometryBytes: number;
  notes: string[];
  routines: string[];
}

export interface ReachVerdict {
  reachable: boolean | null;
  positionError?: number;
  angleError?: number;
  atLimit?: number[];
  solver?: string;
  target?: number[];
  why?: string;
}

export interface ArmTelemetry {
  mode: "cartesian" | "joint";
  q: number[];
  qd: number[];
  target: number[];
  goal?: number[];
  effort: (number | null)[] | null;
  ee: Pose;
  cursor: Pose;
  gap: number;
  status: string;
  suspended: boolean;
  source: string;
  speed: { joint: number; cartesian: number };
  reach: ReachVerdict | null;
  grip?: { command: number; width: number; closure: number | null };
  error?: string;
}

export interface ContactSet {
  available: boolean;
  count: number;
  items: number[][];
  totalForce?: number;
  layout?: string[];
  why?: string;
}

export interface RunnerPlanStep {
  op: string;
  arm?: string;
  label: string;
  status: "pending" | "running" | "done" | "failed" | "stopped";
  index: number;
  seconds?: number;
  error?: string;
  warning?: string;
}

export interface RunnerInfo {
  kind: "routine" | "task";
  name: string;
  arms: string[];
  steps: number;
  seconds: number;
  phase: string;
  plan: RunnerPlanStep[];
  current: number;
  log: string[];
  status?: string;
  message?: string;
}

export interface FrameHeader {
  type: "frame";
  gen: number;
  seq: number;
  step: number;
  t: number;
  wall: number;
  playing: boolean;
  speed: number;
  finite: boolean;
  rate: {
    steps: number;
    realtime: number;
    stepMs: number | null;
    controlMs: number | null;
    controlHz: number;
  };
  arms: Record<string, ArmTelemetry>;
  contacts: ContactSet;
  effortSource: string | null;
  runner: RunnerInfo | null;
}

export interface Frame {
  header: FrameHeader;
  bodyQ: Float32Array; // n * 7
  particles: Float32Array; // m * 3
  receivedAt: number;
}

export interface SuccessVerdict {
  done: boolean;
  detail: string;
  metrics?: Record<string, unknown>;
}

export interface HostStatus {
  phase: "starting" | "idle" | "building" | "ready" | "error";
  phaseDetail: string;
  buildError: string;
  generation: number;
  scenario: ScenarioInfo | Record<string, never>;
  document: WorkcellDoc | null;
  playing: boolean;
  speed: number;
  speeds: number[];
  step: number;
  simTime: number;
  controlHz: number;
  stepsPerSecond: number;
  realtimeFactor: number;
  runner: RunnerInfo | null;
  lastRunner: RunnerInfo | null;
  success: SuccessVerdict | null;
  /** When the success measure first read done since the last build or reset (simulated time). */
  goalMet: { simTime: number; step: number; detail: string } | null;
  routines: string[];
  arms: Record<string, { mode: string; source: string; suspended: boolean; owner: string }>;
  notes: string[];
  target: string;
}

export type AckStatus = "accepted" | "rejected" | "completed" | "failed" | "interrupted" | "pending";

export interface Ack {
  id: string;
  name: string;
  status: AckStatus;
  message: string;
  result: Record<string, unknown>;
  duplicate?: boolean;
}

export interface CommandRecord {
  id: string;
  name: string;
  client: string;
  target: string;
  issuedAt: number;
  finishedAt: number | null;
  args: Record<string, unknown>;
  status: AckStatus;
  message: string;
}

export interface EventRecord {
  seq: number;
  level: "info" | "warning" | "error" | "success";
  source: string;
  message: string;
  wall: number;
  simTime: number | null;
}

// ------------------------------------------------------------ workcells
export type ObjectKind = "box" | "sphere" | "cylinder" | "capsule" | "slab" | "post" | "sheet" | "sponge" | "rope";

export interface ObjectDoc {
  id: string;
  kind: ObjectKind;
  name: string;
  at: Vec3;
  rpy: Vec3;
  half?: Vec3 | null;
  radius?: number | null;
  halfHeight?: number | null;
  size?: number[] | null;
  cells?: number[] | null;
  length?: number | null;
  segments?: number | null;
  mass?: number | null;
  friction?: number | null;
  color?: string | null;
  material?: string | null;
}

export interface WorkcellDoc {
  schema: "bridge.workcell/1";
  name: string;
  description: string;
  cell: string;
  physics: "rigid_grasp" | "deformable" | "cloth_grasp";
  robot: { model: string; at?: Vec3 | null } | null;
  objects: ObjectDoc[];
  regions: { name: string; xy: [number, number, number, number]; z: [number, number] }[];
  cameras: { name: string; eye: Vec3; lookAt: Vec3; fovY: number; width: number; height: number }[];
  savedAt?: number;
}

export interface WorkcellCatalog {
  schema: string;
  kinds: Record<string, { label: string; group: string; dynamic: boolean; fields: string[] }>;
  presets: Record<string, { label: string; summary: string; kinds: string[] }>;
  robots: string[];
  cells: string[];
  cellFrames: Record<string, { origin: Vec3; half: Vec3 | null; groundZ: number }>;
  materials: Record<string, string[]>;
}

export interface SavedWorkcell {
  slug: string;
  name: string;
  description: string;
  robot: string;
  objects: number;
  physics: string;
  savedAt: number;
}

export interface DoctorCheck {
  name: string;
  status: "ok" | "warn" | "fail";
  detail: string;
  fix: string;
}

export interface ServiceInfo {
  product: string;
  subtitle: string;
  version: string;
  builtBy: { name: string; url: string };
  basedOn: { name: string; license: string; url: string; note: string };
  startedAt: number;
  machine: {
    os: string;
    python: string;
    gpu: { name: string; arch: string; memoryGB: number }[];
    cuda: boolean;
    warp: string | null;
    renderer: { backend: string; why: string };
  };
  commandTarget: string;
  hardware: { state: string; detail: string };
}
