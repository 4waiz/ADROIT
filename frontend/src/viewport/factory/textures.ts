// SPDX-License-Identifier: Apache-2.0
// Textures the factory draws itself: safety stripes, cartons, signs, floor lettering, the HMI
// screen, contact shadows and dust. Photographic surfaces (floor, cladding, panels) are files; these are
// graphics, which a canvas draws crisply at any size.

import * as THREE from "three";

function canvas(w: number, h: number): [HTMLCanvasElement, CanvasRenderingContext2D] {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  return [c, c.getContext("2d")!];
}

function finish(c: HTMLCanvasElement, opts: { repeat?: boolean; srgb?: boolean; anisotropy?: number } = {}): THREE.CanvasTexture {
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = opts.srgb === false ? THREE.NoColorSpace : THREE.SRGBColorSpace;
  if (opts.repeat) tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.anisotropy = opts.anisotropy ?? 8;
  return tex;
}

/** Yellow and black diagonal bands, as on column guards, door frames and keep-clear zones. */
export function hazardStripes(): THREE.CanvasTexture {
  const [c, g] = canvas(256, 256);
  g.fillStyle = "#16171a";
  g.fillRect(0, 0, 256, 256);
  g.fillStyle = "#f2b705";
  for (let k = -2; k < 4; k++) {
    g.beginPath();
    g.moveTo(k * 128, 256);
    g.lineTo(k * 128 + 64, 256);
    g.lineTo(k * 128 + 64 + 256, 0);
    g.lineTo(k * 128 + 256, 0);
    g.closePath();
    g.fill();
  }
  // Worn paint: a scatter of small scuffs.
  for (let i = 0; i < 900; i++) {
    g.fillStyle = `rgba(${Math.random() < 0.5 ? "255,255,255" : "0,0,0"},${0.04 + Math.random() * 0.08})`;
    g.fillRect(Math.random() * 256, Math.random() * 256, 1 + Math.random() * 3, 1 + Math.random() * 2);
  }
  return finish(c, { repeat: true });
}

/** A corrugated carton's face: kraft board, a band of tape, a shipping label. One per face. */
export function cartonFace(): THREE.CanvasTexture {
  const [c, g] = canvas(256, 256);
  g.fillStyle = "#b8925d";
  g.fillRect(0, 0, 256, 256);
  for (let i = 0; i < 3000; i++) {
    g.fillStyle = Math.random() < 0.5 ? `rgba(90,60,25,${0.04 + Math.random() * 0.06})` : `rgba(255,235,200,${0.03 + Math.random() * 0.05})`;
    g.fillRect(Math.random() * 256, Math.random() * 256, 1 + Math.random() * 2, 1);
  }
  // Packing tape across the middle, slightly glossy-looking.
  g.fillStyle = "rgba(214,184,130,0.85)";
  g.fillRect(0, 108, 256, 40);
  g.fillStyle = "rgba(255,255,255,0.12)";
  g.fillRect(0, 112, 256, 6);
  // A label in one corner.
  g.fillStyle = "#f2f0ea";
  g.fillRect(150, 172, 84, 58);
  g.fillStyle = "#2a2a2a";
  for (let k = 0; k < 5; k++) g.fillRect(158, 182 + k * 9, 30 + ((k * 23) % 40), 3);
  g.strokeStyle = "rgba(60,40,20,0.35)";
  g.lineWidth = 3;
  g.strokeRect(1.5, 1.5, 253, 253);
  return finish(c);
}

/** A dust mote: a soft round falloff. */
export function softDot(): THREE.CanvasTexture {
  const [c, g] = canvas(64, 64);
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, "rgba(255,255,255,1)");
  grad.addColorStop(0.35, "rgba(255,255,255,0.45)");
  grad.addColorStop(1, "rgba(255,255,255,0)");
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  return finish(c);
}

/** A soft round contact shadow, for things the shadow map does not reach. */
export function blobShadow(): THREE.CanvasTexture {
  const [c, g] = canvas(128, 128);
  const grad = g.createRadialGradient(64, 64, 8, 64, 64, 64);
  grad.addColorStop(0, "rgba(0,0,0,0.55)");
  grad.addColorStop(0.6, "rgba(0,0,0,0.22)");
  grad.addColorStop(1, "rgba(0,0,0,0)");
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  return finish(c);
}

/** Large-scale variation: soft blotches that break up the repeat of a tiled floor. */
export function macroNoise(seed = 7): THREE.CanvasTexture {
  const [c, g] = canvas(512, 512);
  let s = seed;
  const rand = () => {
    s = (s * 16807) % 2147483647;
    return s / 2147483647;
  };
  g.fillStyle = "rgb(128,128,128)";
  g.fillRect(0, 0, 512, 512);
  for (let i = 0; i < 220; i++) {
    const x = rand() * 512;
    const y = rand() * 512;
    const r = 20 + rand() * 120;
    const light = rand() < 0.5;
    const grad = g.createRadialGradient(x, y, 0, x, y, r);
    grad.addColorStop(0, light ? "rgba(255,255,255,0.10)" : "rgba(0,0,0,0.12)");
    grad.addColorStop(1, "rgba(128,128,128,0)");
    g.fillStyle = grad;
    // Draw wrapped, so the texture tiles without a seam.
    for (const dx of [-512, 0, 512]) for (const dy of [-512, 0, 512]) g.fillRect(x - r + dx, y - r + dy, 2 * r, 2 * r);
  }
  return finish(c, { repeat: true, srgb: false });
}

export interface SignSpec {
  kind: "warning" | "mandatory" | "exit" | "info" | "plate";
  title: string;
  lines?: string[];
}

/** A safety sign, laid out as ISO 7010 signs are: pictogram field and a text band. */
export function sign(spec: SignSpec, w = 512, h = 384): THREE.CanvasTexture {
  const [c, g] = canvas(w, h);
  const font = '"Figtree Variable", "Segoe UI", Arial, sans-serif';
  const bg = { warning: "#f7f7f2", mandatory: "#f7f7f2", exit: "#0a8f3c", info: "#1d4f91", plate: "#f2b705" }[spec.kind];
  g.fillStyle = bg;
  g.fillRect(0, 0, w, h);
  const ink = spec.kind === "exit" || spec.kind === "info" ? "#ffffff" : "#111111";
  if (spec.kind === "warning") {
    // A yellow triangle with a black border and an exclamation mark.
    const cx = w / 2;
    const top = h * 0.08;
    const size = h * 0.5;
    g.fillStyle = "#111";
    g.beginPath();
    g.moveTo(cx, top);
    g.lineTo(cx + size * 0.62, top + size);
    g.lineTo(cx - size * 0.62, top + size);
    g.closePath();
    g.fill();
    g.fillStyle = "#f2b705";
    g.beginPath();
    g.moveTo(cx, top + size * 0.16);
    g.lineTo(cx + size * 0.48, top + size * 0.9);
    g.lineTo(cx - size * 0.48, top + size * 0.9);
    g.closePath();
    g.fill();
    g.fillStyle = "#111";
    g.fillRect(cx - size * 0.045, top + size * 0.36, size * 0.09, size * 0.3);
    g.beginPath();
    g.arc(cx, top + size * 0.77, size * 0.055, 0, Math.PI * 2);
    g.fill();
  } else if (spec.kind === "mandatory") {
    // A blue disc with a white pictogram (safety glasses).
    const cx = w / 2;
    const cy = h * 0.33;
    const r = h * 0.25;
    g.fillStyle = "#1d4f91";
    g.beginPath();
    g.arc(cx, cy, r, 0, Math.PI * 2);
    g.fill();
    g.strokeStyle = "#fff";
    g.lineWidth = r * 0.12;
    for (const sx of [-1, 1]) {
      g.beginPath();
      g.ellipse(cx + sx * r * 0.36, cy + r * 0.05, r * 0.28, r * 0.2, 0, 0, Math.PI * 2);
      g.stroke();
    }
    g.beginPath();
    g.moveTo(cx - r * 0.08, cy);
    g.quadraticCurveTo(cx, cy - r * 0.12, cx + r * 0.08, cy);
    g.stroke();
  } else if (spec.kind === "exit") {
    // A running figure towards a door, simplified.
    g.fillStyle = "#ffffff";
    g.fillRect(w * 0.08, h * 0.12, w * 0.22, h * 0.56);
    g.fillStyle = bg;
    g.fillRect(w * 0.11, h * 0.16, w * 0.16, h * 0.52);
    g.fillStyle = "#ffffff";
    g.beginPath();
    g.arc(w * 0.42, h * 0.2, h * 0.055, 0, Math.PI * 2);
    g.fill();
    g.lineWidth = h * 0.06;
    g.lineCap = "round";
    g.strokeStyle = "#ffffff";
    g.beginPath();
    g.moveTo(w * 0.42, h * 0.28);
    g.lineTo(w * 0.38, h * 0.46);
    g.lineTo(w * 0.3, h * 0.6);
    g.moveTo(w * 0.38, h * 0.46);
    g.lineTo(w * 0.47, h * 0.58);
    g.moveTo(w * 0.41, h * 0.33);
    g.lineTo(w * 0.5, h * 0.4);
    g.moveTo(w * 0.41, h * 0.33);
    g.lineTo(w * 0.33, h * 0.38);
    g.stroke();
    g.beginPath();
    g.moveTo(w * 0.58, h * 0.4);
    g.lineTo(w * 0.86, h * 0.4);
    g.stroke();
    g.beginPath();
    g.moveTo(w * 0.78, h * 0.32);
    g.lineTo(w * 0.88, h * 0.4);
    g.lineTo(w * 0.78, h * 0.48);
    g.stroke();
  }
  if (spec.kind !== "plate") {
    g.fillStyle = spec.kind === "exit" ? "rgba(0,0,0,0)" : ink;
  }
  // Text band.
  const bandTop = spec.kind === "plate" ? h * 0.12 : h * 0.68;
  g.fillStyle = ink;
  g.textAlign = "center";
  g.textBaseline = "top";
  g.font = `800 ${Math.round(h * (spec.kind === "plate" ? 0.17 : 0.1))}px ${font}`;
  g.fillText(spec.title, w / 2, bandTop, w * 0.9);
  if (spec.lines) {
    g.font = `600 ${Math.round(h * 0.075)}px ${font}`;
    spec.lines.forEach((line, k) => g.fillText(line, w / 2, bandTop + h * (spec.kind === "plate" ? 0.24 : 0.13) + k * h * 0.095, w * 0.9));
  }
  g.strokeStyle = spec.kind === "plate" ? "#111" : "rgba(0,0,0,0.25)";
  g.lineWidth = spec.kind === "plate" ? 10 : 4;
  g.strokeRect(2, 2, w - 4, h - 4);
  return finish(c);
}

/** Painted floor lettering (cell number, walkway arrows): white or yellow paint on a clear field. */
export function floorText(text: string, color: string): THREE.CanvasTexture {
  const [c, g] = canvas(1024, 256);
  g.clearRect(0, 0, 1024, 256);
  g.fillStyle = color;
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.font = `800 190px "Figtree Variable", "Segoe UI", Arial, sans-serif`;
  g.fillText(text, 512, 138, 980);
  // Scuffed paint.
  g.globalCompositeOperation = "destination-out";
  for (let i = 0; i < 2600; i++) {
    g.fillStyle = `rgba(0,0,0,${0.1 + Math.random() * 0.4})`;
    g.fillRect(Math.random() * 1024, Math.random() * 256, 1 + Math.random() * 4, 1 + Math.random() * 2);
  }
  return finish(c);
}

export function floorArrow(color: string): THREE.CanvasTexture {
  const [c, g] = canvas(256, 512);
  g.clearRect(0, 0, 256, 512);
  g.fillStyle = color;
  g.beginPath();
  g.moveTo(128, 20);
  g.lineTo(236, 190);
  g.lineTo(168, 190);
  g.lineTo(168, 492);
  g.lineTo(88, 492);
  g.lineTo(88, 190);
  g.lineTo(20, 190);
  g.closePath();
  g.fill();
  g.globalCompositeOperation = "destination-out";
  for (let i = 0; i < 1400; i++) {
    g.fillStyle = `rgba(0,0,0,${0.1 + Math.random() * 0.4})`;
    g.fillRect(Math.random() * 256, Math.random() * 512, 1 + Math.random() * 4, 1 + Math.random() * 2);
  }
  return finish(c);
}

export interface ScreenState {
  title: string;
  status: string;
  tone: "ok" | "warn" | "err" | "busy" | "idle";
  detail: string;
}

/** The operator panel's screen: what the cell is doing, in the studio's own colours. */
export class ScreenTexture {
  readonly texture: THREE.CanvasTexture;
  private c: HTMLCanvasElement;
  private g: CanvasRenderingContext2D;
  private logo: HTMLImageElement | null = null;
  private last = "";

  constructor(logoUrl: string) {
    [this.c, this.g] = canvas(640, 400);
    this.texture = finish(this.c);
    const img = new Image();
    img.onload = () => {
      this.logo = img;
      this.last = "";
    };
    img.src = logoUrl;
  }

  draw(state: ScreenState): void {
    const key = JSON.stringify(state) + (this.logo ? "1" : "0");
    if (key === this.last) return;
    this.last = key;
    const { g } = this;
    const w = this.c.width;
    const h = this.c.height;
    const font = '"Figtree Variable", "Segoe UI", Arial, sans-serif';
    const tone = { ok: "#3ddc84", warn: "#f2b705", err: "#ff4d4f", busy: "#4b9dff", idle: "#9aa3ad" }[state.tone];
    g.fillStyle = "#0d1014";
    g.fillRect(0, 0, w, h);
    g.fillStyle = "#151a20";
    g.fillRect(0, 0, w, 64);
    if (this.logo) {
      const lh = 26;
      const lw = (this.logo.width / this.logo.height) * lh;
      g.drawImage(this.logo, 22, 19, lw, lh);
    }
    g.fillStyle = "#ff4713";
    g.fillRect(0, 62, w, 3);
    g.fillStyle = "#e9edf1";
    g.font = `700 22px ${font}`;
    g.textAlign = "right";
    g.textBaseline = "middle";
    g.fillText(state.title, w - 22, 33);
    g.textAlign = "left";
    g.fillStyle = tone;
    g.beginPath();
    g.arc(44, 140, 16, 0, Math.PI * 2);
    g.fill();
    g.fillStyle = "#ffffff";
    g.font = `800 52px ${font}`;
    g.fillText(state.status, 76, 142);
    g.fillStyle = "#9aa3ad";
    g.font = `500 24px ${font}`;
    g.fillText(state.detail, 30, 210, w - 60);
    // A row of soft tiles, as the panel's buttons.
    const labels = ["START", "PAUSE", "HOME"];
    labels.forEach((label, k) => {
      const x = 30 + k * 200;
      g.fillStyle = k === 0 ? "#1f6f43" : "#1c232b";
      g.fillRect(x, 280, 180, 82);
      g.fillStyle = "#e9edf1";
      g.font = `700 24px ${font}`;
      g.textAlign = "center";
      g.fillText(label, x + 90, 322);
      g.textAlign = "left";
    });
    this.texture.needsUpdate = true;
  }

  dispose(): void {
    this.texture.dispose();
  }
}

/** A wall graphic: the wordmark on the cladding, with a little paint wear. */
export function wallGraphic(logoUrl: string, onReady: (tex: THREE.CanvasTexture, aspect: number) => void): void {
  const img = new Image();
  img.onload = () => {
    const pad = 40;
    const [c, g] = canvas(img.width + 2 * pad, img.height + 2 * pad);
    g.clearRect(0, 0, c.width, c.height);
    g.drawImage(img, pad, pad);
    g.globalCompositeOperation = "destination-out";
    for (let i = 0; i < 1600; i++) {
      g.fillStyle = `rgba(0,0,0,${0.08 + Math.random() * 0.25})`;
      g.fillRect(Math.random() * c.width, Math.random() * c.height, 1 + Math.random() * 3, 1 + Math.random() * 2);
    }
    onReady(finish(c), c.width / c.height);
  };
  img.src = logoUrl;
}
