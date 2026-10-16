// SPDX-License-Identifier: LicenseRef-BRIDGE
// Which hosts count as the simulation's own machine: there the page connects straight away, as
// it always has; anywhere else it asks first and can show the offline state.

import { describe, expect, it } from "vitest";
import { servedLocally } from "./presence";

describe("servedLocally", () => {
  it("is true for this machine's own names", () => {
    for (const host of ["localhost", "127.0.0.1", "[::1]", "LOCALHOST", "localhost.", "studio.localhost"]) {
      expect(servedLocally(host), host).toBe(true);
    }
  });

  it("is false for a hosted copy or another machine", () => {
    for (const host of ["adroit.bridgeae.com", "bridgeae.com", "192.168.1.20", "localhost.example.com", "127.0.0.1.nip.io"]) {
      expect(servedLocally(host), host).toBe(false);
    }
  });
});
