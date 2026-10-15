// SPDX-License-Identifier: Apache-2.0
// Two languages, one key set. `npm test` fails if Arabic lacks a key English has, or the reverse.
// What the simulator itself says (event messages, solver summaries, scenario sources) stays in
// the engine's English: it is data, quoted, not interface.

import { useMemo } from "react";
import { useStore } from "../state/store";
import { ar } from "./ar";
import { en } from "./en";

export type Key = keyof typeof en;
export const dictionaries = { en, ar } as const;

export function translate(lang: "en" | "ar", key: Key, vars?: Record<string, string | number>): string {
  const table = dictionaries[lang] as Record<string, string>;
  let text = table[key] ?? (en as Record<string, string>)[key] ?? key;
  if (vars) for (const [k, v] of Object.entries(vars)) text = text.replaceAll(`{${k}}`, String(v));
  return text;
}

export function useT(): (key: Key, vars?: Record<string, string | number>) => string {
  const lang = useStore((s) => s.lang);
  return useMemo(() => (key: Key, vars?: Record<string, string | number>) => translate(lang, key, vars), [lang]);
}

/** Numbers in the interface stay Western Arabic digits in both languages: engineering readouts. */
export function fmt(value: number | null | undefined, digits = 3): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return "—";
  return value.toFixed(digits);
}
