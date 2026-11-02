// SPDX-License-Identifier: LicenseRef-BRIDGE
// The ADROIT wordmark, as supplied: light.png (white glyphs) on the dark theme, dark.png
// (graphite glyphs) on the light theme. Never recoloured, never stretched.

import darkLogo from "../../../dark.png";
import lightLogo from "../../../light.png";
import { useStore } from "../state/store";

export function Logo({ className = "brand-logo", height }: { className?: string; height?: number }) {
  const theme = useStore((s) => s.theme);
  return (
    <img
      className={className}
      src={theme === "dark" ? lightLogo : darkLogo}
      alt="ADROIT"
      style={height ? { height } : undefined}
      draggable={false}
    />
  );
}
