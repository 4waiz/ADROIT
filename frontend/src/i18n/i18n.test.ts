// SPDX-License-Identifier: Apache-2.0
// The two dictionaries carry the same keys and the same placeholders, and translate() fills them.

import { describe, expect, it } from "vitest";
import { ar } from "./ar";
import { en } from "./en";
import { translate } from "./index";

const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe("dictionaries", () => {
  it("have the same keys", () => {
    expect(Object.keys(ar).sort()).toEqual(Object.keys(en).sort());
  });

  it("have the same placeholders in every string, and no empty Arabic string", () => {
    const table = ar as Record<string, string>;
    for (const [key, text] of Object.entries(en)) {
      expect(placeholders(table[key]), key).toEqual(placeholders(text));
      expect(table[key].trim().length, key).toBeGreaterThan(0);
    }
  });
});

describe("translate", () => {
  it("fills placeholders in either language", () => {
    expect(translate("en", "goal.metAt", { t: "8.4" })).toBe("Met at 8.4 s");
    expect(translate("ar", "goal.metAt", { t: "8.4" })).toContain("8.4");
  });
});
