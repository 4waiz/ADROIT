// SPDX-License-Identifier: LicenseRef-BRIDGE
// Drag-to-resize for panels. `sign` is +1 when the size grows as the pointer moves right (or
// down), -1 when it grows as the pointer moves left (or up). Mirrored for right-to-left layouts.

import { useCallback, useRef, useState } from "react";

export function useResize(axis: "x" | "y", size: number, onChange: (v: number) => void, sign: 1 | -1) {
  const [dragging, setDragging] = useState(false);
  const start = useRef({ pointer: 0, size: 0 });

  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      const rtl = document.documentElement.dir === "rtl" && axis === "x";
      start.current = { pointer: axis === "x" ? e.clientX : e.clientY, size };
      setDragging(true);
      const move = (ev: PointerEvent) => {
        const delta = (axis === "x" ? ev.clientX : ev.clientY) - start.current.pointer;
        onChange(start.current.size + delta * sign * (rtl ? -1 : 1));
      };
      const up = () => {
        setDragging(false);
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        document.body.style.cursor = "";
      };
      document.body.style.cursor = axis === "x" ? "col-resize" : "row-resize";
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up);
    },
    [axis, size, onChange, sign],
  );

  return { dragging, onPointerDown };
}
