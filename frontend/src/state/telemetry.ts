// SPDX-License-Identifier: LicenseRef-BRIDGE
// Ring buffers of what the simulator reported, for the plots. Every sample is a value the
// frame carried; nothing is interpolated or synthesised. A frame without a value (an effort
// the solver does not expose) records NaN, which the plots draw as a gap.

import type { Frame } from "../api/types";

export const HISTORY_SECONDS = 30;
const CAPACITY = 2400;

export class Series {
  readonly time = new Float64Array(CAPACITY);
  readonly values: Float64Array[];
  length = 0;
  private head = 0;

  constructor(readonly channels: number) {
    this.values = Array.from({ length: channels }, () => new Float64Array(CAPACITY));
  }

  push(t: number, sample: ArrayLike<number | null | undefined>): void {
    this.time[this.head] = t;
    for (let c = 0; c < this.channels; c++) {
      const v = sample[c];
      this.values[c][this.head] = v === null || v === undefined ? Number.NaN : v;
    }
    this.head = (this.head + 1) % CAPACITY;
    this.length = Math.min(this.length + 1, CAPACITY);
  }

  clear(): void {
    this.length = 0;
    this.head = 0;
  }

  /** The last `seconds` of samples, oldest first, as uPlot's column arrays. */
  window(seconds: number): number[][] {
    const n = this.length;
    const start = (this.head - n + CAPACITY) % CAPACITY;
    const latest = n ? this.time[(this.head - 1 + CAPACITY) % CAPACITY] : 0;
    const xs: number[] = [];
    const cols: number[][] = Array.from({ length: this.channels }, () => []);
    for (let k = 0; k < n; k++) {
      const i = (start + k) % CAPACITY;
      if (latest - this.time[i] > seconds) continue;
      xs.push(this.time[i]);
      for (let c = 0; c < this.channels; c++) cols[c].push(this.values[c][i]);
    }
    return [xs, ...cols];
  }

  last(channel: number): number {
    if (!this.length) return Number.NaN;
    return this.values[channel][(this.head - 1 + CAPACITY) % CAPACITY];
  }
}

export interface ArmSeries {
  q: Series;
  qd: Series;
  effort: Series;
  ee: Series;
  grip: Series;
}

class TelemetryStore {
  arms = new Map<string, ArmSeries>();
  contacts = new Series(2); // count, total force
  rate = new Series(2); // steps/s, real-time factor
  render = new Series(2); // fps, frame ms (the browser's own)
  private lastT = -1;
  private generation = -1;

  ingest(frame: Frame): void {
    const h = frame.header;
    if (h.gen !== this.generation) {
      this.reset();
      this.generation = h.gen;
    }
    if (h.t < this.lastT - 1e-9) {
      // A reset put simulated time back: start the plots over rather than drawing back in time.
      this.reset();
      this.generation = h.gen;
    }
    if (h.t === this.lastT) return; // a held scene republishes the same instant
    this.lastT = h.t;
    for (const [name, arm] of Object.entries(h.arms)) {
      if (arm.error) continue;
      let s = this.arms.get(name);
      const n = arm.q.length;
      if (!s || s.q.channels !== n) {
        s = {
          q: new Series(n),
          qd: new Series(n),
          effort: new Series(n),
          ee: new Series(3),
          grip: new Series(3),
        };
        this.arms.set(name, s);
      }
      s.q.push(h.t, arm.q);
      s.qd.push(h.t, arm.qd);
      s.effort.push(h.t, arm.effort ?? new Array(n).fill(null));
      s.ee.push(h.t, arm.ee.slice(0, 3));
      s.grip.push(h.t, [arm.grip?.command ?? null, arm.grip ? arm.grip.width * 1000 : null, arm.grip?.closure ?? null]);
    }
    this.contacts.push(h.t, [h.contacts.available ? h.contacts.count : null, h.contacts.totalForce ?? null]);
    this.rate.push(h.t, [h.rate.steps, h.rate.realtime]);
  }

  ingestRender(t: number, fps: number, ms: number): void {
    this.render.push(t, [fps, ms]);
  }

  reset(): void {
    this.arms.clear();
    this.contacts.clear();
    this.rate.clear();
    this.lastT = -1;
  }
}

export const telemetry = new TelemetryStore();
