// SPDX-License-Identifier: Apache-2.0
// The binary frame (bridge_studio/frames.py): "BRF1", uint32 header length, JSON header, float32 buffers.

import type { Frame, FrameHeader } from "./types";

const MAGIC = 0x31465242; // "BRF1" little-endian
const decoder = new TextDecoder();

interface BufferEntry {
  name: string;
  offset: number;
  count: number;
  width: number;
}

export function decodeFrame(data: ArrayBuffer): Frame | null {
  if (data.byteLength < 8) return null;
  const view = new DataView(data);
  if (view.getUint32(0, true) !== MAGIC) return null;
  const length = view.getUint32(4, true);
  const header = JSON.parse(decoder.decode(new Uint8Array(data, 8, length))) as FrameHeader & {
    buffers: BufferEntry[];
  };
  const base = 8 + length;
  let bodyQ = new Float32Array(0);
  let particles = new Float32Array(0);
  for (const b of header.buffers) {
    // The payload starts 4-byte aligned (the header is space-padded), so these are zero-copy views.
    const array = new Float32Array(data, base + b.offset, b.count * b.width);
    if (b.name === "body_q") bodyQ = array;
    else if (b.name === "particles") particles = array;
  }
  return { header, bodyQ, particles, receivedAt: performance.now() };
}
