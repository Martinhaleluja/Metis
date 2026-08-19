import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion, useSpring } from "motion/react";
import { Cursor as CursorIcon } from "@phosphor-icons/react/dist/icons/Cursor";
import { Crop as CropIcon } from "@phosphor-icons/react/dist/icons/Crop";
import { Eraser as EraserIcon } from "@phosphor-icons/react/dist/icons/Eraser";
import { Hand as HandIcon } from "@phosphor-icons/react/dist/icons/Hand";
import { MagicWand as MagicWandIcon } from "@phosphor-icons/react/dist/icons/MagicWand";
import { PaintBrush as PaintBrushIcon } from "@phosphor-icons/react/dist/icons/PaintBrush";
import { springUI } from "../lib/motion";

/**
 * The centrepiece of the hero: a short loop of Metis being asked a question
 * about what is on screen, pointing at the control that answers it, and
 * narrating while the work happens.
 *
 * The window is a stylised image editor rather than a copy of any particular
 * product, and carries no third-party branding. It illustrates the
 * interaction, which is why nothing in it pretends to be a screenshot.
 *
 * Beats advance on a timer, so React re-renders about once every three
 * seconds rather than every frame. The pointer and the companion are the only
 * things moving continuously and both ride motion values on the compositor.
 */

const TOOLS = [HandIcon, CropIcon, MagicWandIcon, PaintBrushIcon, EraserIcon] as const;
const WAND = 2;

type Beat = {
  key: string;
  /** Where the pointer rests, as a fraction of the window box. */
  cursor: { x: number; y: number };
  question: string | null;
  answer: string | null;
  activeTool: number | null;
  tracing: boolean;
  cutOut: boolean;
};

const beats: Beat[] = [
  {
    key: "ask",
    cursor: { x: 0.66, y: 0.76 },
    question: "How do I cut the background out of this?",
    answer: null,
    activeTool: null,
    tracing: false,
    cutOut: false,
  },
  {
    key: "point",
    cursor: { x: 0.08, y: 0.46 },
    question: "How do I cut the background out of this?",
    answer: "This one. Select Subject.",
    activeTool: WAND,
    tracing: false,
    cutOut: false,
  },
  {
    key: "trace",
    cursor: { x: 0.58, y: 0.4 },
    question: null,
    answer: "Now draw around what you are keeping.",
    activeTool: WAND,
    tracing: true,
    cutOut: false,
  },
  {
    key: "done",
    cursor: { x: 0.78, y: 0.72 },
    question: null,
    answer: "That is it. The background is gone.",
    activeTool: WAND,
    tracing: true,
    cutOut: true,
  },
];

const BEAT_MS = 3000;

/** The object being cut out, and the shape the selection traces around it. */
const SUBJECT =
  "M168 28 C199 28 217 52 217 86 C217 124 196 146 168 146 C140 146 118 124 118 86 C118 52 137 28 168 28 Z";

export function MetisDemo() {
  const reduce = useReducedMotion();
  const frame = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState({ w: 0, h: 0 });

  // Under reduced motion the loop never starts. It rests on the beat that
  // carries the most meaning rather than sitting on an empty window.
  const [index, setIndex] = useState(reduce ? 2 : 0);
  const beat = beats[index];

  useLayoutEffect(() => {
    const node = frame.current;
    if (!node) return;
    const observer = new ResizeObserver(([entry]) => {
      const r = entry.contentRect;
      setBox({ w: r.width, h: r.height });
    });
    observer.observe(node);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (reduce) return;
    const id = window.setInterval(() => {
      // Pausing on a hidden tab keeps a background page off the CPU.
      if (document.visibilityState === "visible") {
        setIndex((i) => (i + 1) % beats.length);
      }
    }, BEAT_MS);
    return () => window.clearInterval(id);
  }, [reduce]);

  // Pixels rather than percentages, so both followers stay on transforms
  // instead of animating layout properties.
  const targetX = beat.cursor.x * box.w;
  const targetY = beat.cursor.y * box.h;

  const cursorX = useSpring(0, { bounce: 0, duration: 0.9 });
  const cursorY = useSpring(0, { bounce: 0, duration: 0.9 });
  // The companion trails the pointer rather than being welded to it.
  const orbX = useSpring(0, { bounce: 0, duration: 1.3 });
  const orbY = useSpring(0, { bounce: 0, duration: 1.3 });

  useEffect(() => {
    if (!box.w) return;
    if (reduce) {
      cursorX.jump(targetX);
      cursorY.jump(targetY);
      orbX.jump(targetX);
      orbY.jump(targetY);
      return;
    }
    cursorX.set(targetX);
    cursorY.set(targetY);
    orbX.set(targetX);
    orbY.set(targetY);
  }, [targetX, targetY, box.w, reduce, cursorX, cursorY, orbX, orbY]);

  // Put the answer on whichever side of the companion has room, and cap it to
  // that space. On a phone the frame is barely 340px wide, so a fixed side and
  // a fixed max-width run straight off an edge. The mark is 28px and centred on
  // the pointer, and the bubble clears it by 8px, so both have to come out of
  // the measurement or flipping sides just moves the overflow.
  const HALF_MARK = 14;
  const BUBBLE_GAP = 8;
  const FRAME_EDGE = 10;
  const orbPx = beat.cursor.x * box.w;
  const roomRight = box.w - (orbPx + HALF_MARK + BUBBLE_GAP) - FRAME_EDGE;
  const roomLeft = orbPx - HALF_MARK - BUBBLE_GAP - FRAME_EDGE;
  const bubbleLeft = roomRight < 150 && roomLeft > roomRight;
  const bubbleMax = Math.round(Math.max(96, Math.min(190, bubbleLeft ? roomLeft : roomRight)));

  return (
    <div className="relative mx-auto w-full max-w-[480px]">
      <div
        ref={frame}
        className="relative aspect-[16/9] w-full overflow-hidden rounded-[18px] border border-line bg-surface shadow-[var(--shadow-float)]"
      >
        {/* What the person asked, laid over the window they are looking at. */}
        <div className="pointer-events-none absolute inset-x-0 top-11 z-30 flex justify-center px-6">
          <AnimatePresence mode="wait">
            {beat.question && (
              <motion.p
                key={beat.question}
                initial={reduce ? false : { opacity: 0, y: -6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={reduce ? undefined : { opacity: 0, y: -6 }}
                transition={springUI}
                className="material-strong rounded-full px-3.5 py-1.5 text-center text-[12px] text-ink"
              >
                &ldquo;{beat.question}&rdquo;
              </motion.p>
            )}
          </AnimatePresence>
        </div>
        <div className="flex h-8 items-center gap-1.5 border-b border-line bg-surface-sunken px-3">
          {["#ff5f57", "#febc2e", "#28c840"].map((dot) => (
            <span key={dot} className="h-2 w-2 rounded-full" style={{ background: dot }} />
          ))}
          <span className="ml-2 text-[10.5px] text-ink-muted">Untitled-1 @ 100%</span>
        </div>

        <div className="flex h-[calc(100%-2rem)]">
          <div className="flex w-9 shrink-0 flex-col items-center gap-1 border-r border-line bg-surface-sunken py-2">
            {TOOLS.map((Tool, i) => {
              const active = beat.activeTool === i;
              return (
                <span key={i} className="relative grid h-6 w-6 place-items-center">
                  {active && (
                    <motion.span
                      layoutId="tool-highlight"
                      transition={springUI}
                      className="absolute inset-0 rounded-md bg-accent"
                    />
                  )}
                  <Tool
                    size={13}
                    weight="bold"
                    className={`relative z-10 ${active ? "text-accent-contrast" : "text-ink-muted"}`}
                  />
                </span>
              );
            })}
          </div>

          <div className="relative flex-1">
            {/* The photograph, standing in as a soft field of colour. */}
            <div
              className="absolute inset-0"
              style={{
                background: "linear-gradient(150deg, #cfe4ff 0%, #e7d9ff 46%, #ffe2cc 100%)",
              }}
            />

            {/* Once the background is cut, what is behind it is nothing. */}
            <motion.div
              className="absolute inset-0"
              initial={false}
              animate={{ opacity: beat.cutOut ? 1 : 0 }}
              transition={springUI}
              style={{
                background:
                  "repeating-conic-gradient(#e6e6ea 0% 25%, #ffffff 0% 50%) 50% / 15px 15px",
              }}
            />

            <svg
              viewBox="0 0 320 172"
              preserveAspectRatio="none"
              className="absolute inset-0 h-full w-full"
              aria-hidden="true"
            >
              <defs>
                <linearGradient id="metis-demo-subject" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stopColor="#8ab6f0" />
                  <stop offset="100%" stopColor="#41639b" />
                </linearGradient>
              </defs>

              <path d={SUBJECT} fill="url(#metis-demo-subject)" />

              {/* Metis draws the outline on, the way it annotates a real screen. */}
              <motion.path
                d={SUBJECT}
                fill="none"
                stroke="#ffffff"
                strokeWidth={2}
                vectorEffect="non-scaling-stroke"
                transform="translate(168 87) scale(1.09) translate(-168 -87)"
                initial={false}
                animate={{
                  pathLength: beat.tracing ? 1 : 0,
                  opacity: beat.tracing ? 1 : 0,
                }}
                transition={{ duration: reduce ? 0 : 1.1, ease: [0.16, 1, 0.3, 1] }}
              />
            </svg>
          </div>
        </div>

        {/* Pointer and companion ride above the whole window. */}
        <motion.div
          className="pointer-events-none absolute top-0 left-0 z-20"
          style={{ x: cursorX, y: cursorY }}
        >
          <CursorIcon
            size={20}
            weight="fill"
            className="text-ink drop-shadow-[0_2px_4px_rgba(0,0,0,0.35)]"
          />
        </motion.div>

        <motion.div
          className="pointer-events-none absolute top-0 left-0 z-10"
          style={{ x: orbX, y: orbY }}
        >
          <div className="relative translate-y-5 -translate-x-1/2">
            <img
              src="/metis-mark.png"
              alt=""
              width={64}
              height={64}
              className="h-7 w-7 drop-shadow-[0_4px_10px_rgba(10,107,224,0.4)]"
            />

            <AnimatePresence mode="wait">
              {beat.answer && (
                <motion.p
                  key={beat.answer}
                  initial={reduce ? false : { opacity: 0, scale: 0.94 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={reduce ? undefined : { opacity: 0, scale: 0.97 }}
                  transition={springUI}
                  className="material-strong absolute top-1/2 w-max -translate-y-1/2 rounded-2xl px-3 py-2 text-[12px] leading-snug text-ink"
                  style={
                    bubbleLeft
                      ? { right: "calc(100% + 8px)", maxWidth: bubbleMax, transformOrigin: "right center" }
                      : { left: "calc(100% + 8px)", maxWidth: bubbleMax, transformOrigin: "left center" }
                  }
                >
                  {beat.answer}
                </motion.p>
              )}
            </AnimatePresence>
          </div>
        </motion.div>
      </div>
    </div>
  );
}
