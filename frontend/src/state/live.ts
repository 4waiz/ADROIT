// SPDX-License-Identifier: Apache-2.0
// Data that changes every frame lives outside React: the viewport reads it on its own clock,
// and panels sample it through the throttled `telemetry` slice of the store.

import type { Frame, Manifest } from "../api/types";

type FrameListener = (frame: Frame) => void;

class LiveData {
  frame: Frame | null = null;
  manifest: Manifest | null = null;
  geometry: ArrayBuffer | null = null;
  private listeners = new Set<FrameListener>();

  setFrame(frame: Frame): void {
    this.frame = frame;
    for (const listener of this.listeners) listener(frame);
  }

  onFrame(listener: FrameListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
}

export const live = new LiveData();
