// SPDX-License-Identifier: Apache-2.0
// The guided tour: a dimmed screen with one control lit at a time and a card that says what it
// does, in plain words. It opens by itself the first time, and from the graduation-cap button
// after that. Clicks pass through the lit control, so each step can be tried as it is read.

import { ArrowLeft, ArrowRight, GraduationCap, X } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { type Key, useT } from "../../i18n";
import { useStore } from "../../state/store";

interface Step {
  /** A `data-tour` value, or ".viewport" for the 3D view; none centres the card. */
  target?: string;
  title: Key;
  body: Key;
}

const STEPS: Step[] = [
  { title: "tour.welcomeTitle", body: "tour.welcomeBody" },
  { target: ".viewport", title: "tour.viewTitle", body: "tour.viewBody" },
  { target: "transport", title: "tour.playTitle", body: "tour.playBody" },
  { target: "pad", title: "tour.moveTitle", body: "tour.moveBody" },
  { target: "hand", title: "tour.handTitle", body: "tour.handBody" },
  { target: "ready-programs", title: "tour.programsTitle", body: "tour.programsBody" },
  { target: "teach", title: "tour.teachTitle", body: "tour.teachBody" },
  { target: "goal", title: "tour.goalTitle", body: "tour.goalBody" },
  { target: "change-scene", title: "tour.scenesTitle", body: "tour.scenesBody" },
  { target: "view-tools", title: "tour.toolsTitle", body: "tour.toolsBody" },
  { target: "mode", title: "tour.modeTitle", body: "tour.modeBody" },
  { title: "tour.doneTitle", body: "tour.doneBody" },
];

function find(target: string | undefined): Element | null {
  if (!target) return null;
  return target.startsWith(".") ? document.querySelector(target) : document.querySelector(`[data-tour="${target}"]`);
}

/** Opens the tour once, for a first visit, as soon as there is a scene to show it on. */
export function useFirstVisitTour(): void {
  const seen = useStore((s) => s.tutorialSeen);
  const ready = useStore((s) => Boolean(s.manifest) && s.connection === "open");
  useEffect(() => {
    if (seen || !ready) return;
    const id = window.setTimeout(() => useStore.setState({ tutorialOpen: true }), 1200);
    return () => window.clearTimeout(id);
  }, [seen, ready]);
}

export function Tutorial() {
  const t = useT();
  const [index, setIndex] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);
  const card = useRef<HTMLDivElement>(null);
  const [cardSize, setCardSize] = useState({ w: 360, h: 200 });
  const dir = useStore((s) => (s.lang === "ar" ? -1 : 1));

  // The tour explains Simple mode, and ends by pointing at the switch to Expert.
  useEffect(() => {
    if (useStore.getState().uiMode !== "simple") useStore.getState().setPref("uiMode", "simple");
  }, []);

  const close = useCallback(() => {
    useStore.setState({ tutorialOpen: false });
    useStore.getState().setPref("tutorialSeen", true);
  }, []);

  // Steps whose control is not on screen (a hand-less robot, a scene without programs) are passed.
  const step = STEPS[index];
  const go = useCallback((delta: number) => {
    setIndex((k) => {
      let next = k + delta;
      while (next > 0 && next < STEPS.length - 1 && STEPS[next].target && !find(STEPS[next].target)) next += delta;
      return Math.max(0, Math.min(STEPS.length - 1, next));
    });
  }, []);

  // Follow the target as the layout moves (panels scroll, the window resizes).
  useLayoutEffect(() => {
    let raf = 0;
    const tick = () => {
      const el = find(step.target);
      if (el) {
        el.scrollIntoView({ block: "nearest", inline: "nearest" });
        const r = el.getBoundingClientRect();
        setRect((prev) => (prev && Math.abs(prev.x - r.x) < 0.5 && Math.abs(prev.y - r.y) < 0.5 && Math.abs(prev.width - r.width) < 0.5 && Math.abs(prev.height - r.height) < 0.5 ? prev : r));
      } else {
        setRect(null);
      }
      raf = requestAnimationFrame(tick);
    };
    tick();
    return () => cancelAnimationFrame(raf);
  }, [step.target]);

  useLayoutEffect(() => {
    if (card.current) setCardSize({ w: card.current.offsetWidth, h: card.current.offsetHeight });
  }, [index, rect]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // A focused button already answers Enter with its own click.
      if (e.key === "Enter" && (e.target as HTMLElement | null)?.tagName === "BUTTON") return;
      if (e.key === "Escape") close();
      else if (e.key === "Enter" || e.key === (dir > 0 ? "ArrowRight" : "ArrowLeft")) {
        if (index === STEPS.length - 1) close();
        else go(1);
      } else if (e.key === (dir > 0 ? "ArrowLeft" : "ArrowRight")) go(-1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [close, go, index, dir]);

  const W = window.innerWidth;
  const H = window.innerHeight;
  const pad = 8;
  const hole = rect ? { x: rect.x - pad, y: rect.y - pad, w: rect.width + 2 * pad, h: rect.height + 2 * pad } : null;

  // The card sits beside the lit control: below it, else above, else to a side; always on screen.
  const position = useMemo(() => {
    const m = 14;
    if (!hole) return { left: (W - cardSize.w) / 2, top: (H - cardSize.h) / 2 };
    const fits = {
      below: hole.y + hole.h + m + cardSize.h < H - m,
      above: hole.y - m - cardSize.h > m,
      right: hole.x + hole.w + m + cardSize.w < W - m,
      left: hole.x - m - cardSize.w > m,
    };
    const clampX = (x: number) => Math.max(m, Math.min(W - cardSize.w - m, x));
    const clampY = (y: number) => Math.max(m, Math.min(H - cardSize.h - m, y));
    // A target that fills most of the screen (the 3D view) gets the card inside it, at the top.
    if (hole.w * hole.h > W * H * 0.35) return { left: clampX(hole.x + hole.w / 2 - cardSize.w / 2), top: clampY(hole.y + 70) };
    if (fits.below) return { left: clampX(hole.x + hole.w / 2 - cardSize.w / 2), top: hole.y + hole.h + m };
    if (fits.above) return { left: clampX(hole.x + hole.w / 2 - cardSize.w / 2), top: hole.y - m - cardSize.h };
    if (fits.left) return { left: hole.x - m - cardSize.w, top: clampY(hole.y + hole.h / 2 - cardSize.h / 2) };
    if (fits.right) return { left: hole.x + hole.w + m, top: clampY(hole.y + hole.h / 2 - cardSize.h / 2) };
    return { left: clampX(hole.x + hole.w / 2 - cardSize.w / 2), top: clampY(hole.y + hole.h / 2 - cardSize.h / 2) };
  }, [hole?.x, hole?.y, hole?.w, hole?.h, W, H, cardSize.w, cardSize.h]);

  const last = index === STEPS.length - 1;
  const path = hole
    ? `M0 0H${W}V${H}H0Z M${hole.x + 6} ${hole.y}H${hole.x + hole.w - 6}Q${hole.x + hole.w} ${hole.y} ${hole.x + hole.w} ${hole.y + 6}V${hole.y + hole.h - 6}Q${hole.x + hole.w} ${hole.y + hole.h} ${hole.x + hole.w - 6} ${hole.y + hole.h}H${hole.x + 6}Q${hole.x} ${hole.y + hole.h} ${hole.x} ${hole.y + hole.h - 6}V${hole.y + 6}Q${hole.x} ${hole.y} ${hole.x + 6} ${hole.y}Z`
    : `M0 0H${W}V${H}H0Z`;

  return (
    <div className="tour" role="dialog" aria-modal="true" aria-labelledby="tour-title">
      <svg className="tour-scrim" width={W} height={H} aria-hidden>
        <path d={path} fillRule="evenodd" />
      </svg>
      {hole && <div className="tour-ring" style={{ left: hole.x, top: hole.y, width: hole.w, height: hole.h }} />}
      <div ref={card} className="tour-card" style={{ left: position.left, top: position.top }}>
        <div className="tour-head">
          <span className="tour-icon"><GraduationCap size={16} /></span>
          <span className="tour-count">{t("tour.count", { n: index + 1, total: STEPS.length })}</span>
          <span className="grow" />
          <button type="button" className="icon-btn sm" aria-label={t("tour.skip")} onClick={close}>
            <X size={14} />
          </button>
        </div>
        <h2 id="tour-title">{t(step.title)}</h2>
        <p>{t(step.body)}</p>
        <div className="tour-dots" aria-hidden>
          {STEPS.map((_, k) => (
            <span key={k} className={k === index ? "on" : k < index ? "done" : ""} />
          ))}
        </div>
        <div className="tour-foot">
          {index === 0 ? (
            <button type="button" className="btn" onClick={close}>{t("tour.skip")}</button>
          ) : (
            <button type="button" className="btn" onClick={() => go(-1)}>
              <ArrowLeft size={14} className="flip-rtl" /> {t("tour.back")}
            </button>
          )}
          <span className="grow" />
          <button type="button" className="btn primary" onClick={() => (last ? close() : go(1))} autoFocus>
            {index === 0 ? t("tour.start") : last ? t("tour.finish") : t("tour.next")} {!last && <ArrowRight size={14} className="flip-rtl" />}
          </button>
        </div>
      </div>
    </div>
  );
}
