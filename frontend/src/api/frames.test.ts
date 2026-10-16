// SPDX-License-Identifier: LicenseRef-BRIDGE
// The client's frame decoder against frames packed exactly as bridge_studio/frames.py packs them.

import { describe, expect, it } from "vitest";
import { decodeFrame } from "./frames";

/** bridge_studio.frames.encode_frame, in TypeScript: "BRF1", uint32 length, padded JSON, float32 rows. */
function encode(header: Record<string, unknown>, buffers: [string, number[][]][]): ArrayBuffer {
  const described: { name: string; offset: number; count: number; width: number }[] = [];
  const rows: number[] = [];
  let offset = 0;
  for (const [name, data] of buffers) {
    const width = data.length ? data[0].length : 1;
    described.push({ name, offset, count: data.length, width });
    for (const row of data) rows.push(...row);
    offset += data.length * width * 4;
  }
  let text = JSON.stringify({ ...header, buffers: described });
  text += " ".repeat((4 - (new TextEncoder().encode(text).length % 4)) % 4);
  const json = new TextEncoder().encode(text);
  const out = new ArrayBuffer(8 + json.length + rows.length * 4);
  const view = new DataView(out);
  new Uint8Array(out, 0, 4).set(new TextEncoder().encode("BRF1"));
  view.setUint32(4, json.length, true);
  new Uint8Array(out, 8, json.length).set(json);
  new Float32Array(out, 8 + json.length, rows.length).set(rows);
  return out;
}

describe("decodeFrame", () => {
  it("reads the header and both buffers", () => {
    const bodies = [
      [0.1, 0.2, 0.3, 0, 0, 0, 1],
      [-0.5, 0.25, 0.9, 0, 0.7071, 0, 0.7071],
    ];
    const particles = [
      [1, 2, 3],
      [4, 5, 6],
      [7, 8, 9],
    ];
    const frame = decodeFrame(encode({ type: "frame", step: 42, t: 0.84 }, [["body_q", bodies], ["particles", particles]]));
    expect(frame).not.toBeNull();
    expect(frame!.header.step).toBe(42);
    expect(frame!.header.t).toBeCloseTo(0.84);
    expect(frame!.bodyQ.length).toBe(14);
    expect(frame!.bodyQ[7]).toBeCloseTo(-0.5);
    expect(frame!.bodyQ[13]).toBeCloseTo(0.7071, 4);
    expect(Array.from(frame!.particles)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9]);
  });

  it("keeps a header whose length needs padding aligned", () => {
    for (const name of ["a", "ab", "abc", "abcd"]) {
      const frame = decodeFrame(encode({ type: "frame", name }, [["body_q", [[1, 2, 3, 4, 5, 6, 7]]]]));
      expect(Array.from(frame!.bodyQ)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    }
  });

  it("refuses what is not a frame", () => {
    expect(decodeFrame(new ArrayBuffer(4))).toBeNull();
    expect(decodeFrame(new TextEncoder().encode("JSON{}  ").buffer as ArrayBuffer)).toBeNull();
  });

  it("gives empty arrays when a scene has no particles", () => {
    const frame = decodeFrame(encode({ type: "frame" }, [["body_q", [[0, 0, 0, 0, 0, 0, 1]]], ["particles", []]]));
    expect(frame!.particles.length).toBe(0);
  });
});
