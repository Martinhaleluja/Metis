import { useEffect } from "react";
import { motion, useMotionValue, useReducedMotion, useSpring, useTransform } from "motion/react";
import { EyeIcon as Eye } from "@phosphor-icons/react/dist/icons/Eye";
import { GraduationCapIcon as GraduationCap } from "@phosphor-icons/react/dist/icons/GraduationCap";
import { LightningIcon as Lightning } from "@phosphor-icons/react/dist/icons/Lightning";
import { WaveformIcon as Waveform } from "@phosphor-icons/react/dist/icons/Waveform";
import { springEnter } from "../lib/motion";

/**
 * The vignettes that hang around the hero. Each one is a live miniature of a
 * real Metis surface, not a picture of a fake screenshot.
 *
 * Pointer parallax runs entirely on motion values. Putting the cursor position
 * into React state would re-render this subtree on every mouse event.
 */

const cards = [
  {
    key: "hotkey",
    depth: 26,
    className: "left-[4%] top-[152px]",
    rotate: -5,
    body: (
      <>
        <p className="text-[13px] font-medium text-ink">Hold to talk</p>
        <div className="mt-2.5 flex items-center gap-1">
          {["Ctrl", "Shift", "1"].map((key) => (
            <kbd
              key={key}
              className="rounded-md border border-line bg-surface-sunken px-1.5 py-1 font-sans text-[11px] font-semibold text-ink-muted"
            >
              {key}
            </kbd>
          ))}
        </div>
      </>
    ),
    icon: Lightning,
  },
  {
    key: "wake",
    depth: 40,
    className: "right-[4%] top-[112px]",
    rotate: 4,
    body: (
      <>
        <p className="text-[13px] font-medium text-ink">Listening</p>
        <div className="mt-3 flex h-5 items-end gap-[3px]" aria-hidden="true">
          {[9, 16, 6, 20, 12, 18, 8].map((height, index) => (
            <span
              key={index}
              className="w-[3px] rounded-full bg-accent/70 motion-safe:animate-[metis-halo_1.4s_ease-in-out_infinite]"
              style={{ height, animationDelay: `${index * 0.11}s` }}
            />
          ))}
        </div>
      </>
    ),
    icon: Waveform,
  },
  {
    key: "screen",
    depth: 32,
    className: "left-[7%] bottom-[11%]",
    rotate: 3,
    body: (
      <>
        <p className="text-[13px] font-medium text-ink">Sees your screen</p>
        <p className="mt-1.5 text-[12px] leading-snug text-ink-muted">
          Only when you ask.
        </p>
      </>
    ),
    icon: Eye,
  },
  {
    key: "modes",
    depth: 22,
    className: "right-[6%] bottom-[16%]",
    rotate: -4,
    body: (
      <>
        <p className="text-[13px] font-medium text-ink">Teaches, never takes over</p>
        <div className="mt-2.5 flex gap-1" aria-hidden="true">
          <span className="rounded-full bg-accent px-2 py-[3px] text-[11px] font-medium text-accent-contrast">
            Shows you
          </span>
          <span className="rounded-full bg-surface-sunken px-2 py-[3px] text-[11px] font-medium text-ink-muted">
            Your pointer
          </span>
        </div>
      </>
    ),
    icon: GraduationCap,
  },
] as const;

export function FloatingCards() {
  const reduce = useReducedMotion();
  const pointerX = useMotionValue(0);
  const pointerY = useMotionValue(0);

  const springX = useSpring(pointerX, { bounce: 0, duration: 0.9 });
  const springY = useSpring(pointerY, { bounce: 0, duration: 0.9 });

  useEffect(() => {
    if (reduce) return;

    const onMove = (event: PointerEvent) => {
      pointerX.set(event.clientX / window.innerWidth - 0.5);
      pointerY.set(event.clientY / window.innerHeight - 0.5);
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    return () => window.removeEventListener("pointermove", onMove);
  }, [pointerX, pointerY, reduce]);

  return (
    <div className="pointer-events-none absolute inset-0 hidden xl:block" aria-hidden="true">
      {cards.map((card, index) => (
        <Card
          key={card.key}
          card={card}
          index={index}
          springX={springX}
          springY={springY}
          reduce={Boolean(reduce)}
        />
      ))}
    </div>
  );
}

type CardProps = {
  card: (typeof cards)[number];
  index: number;
  springX: ReturnType<typeof useSpring>;
  springY: ReturnType<typeof useSpring>;
  reduce: boolean;
};

function Card({ card, index, springX, springY, reduce }: CardProps) {
  const x = useTransform(springX, (value) => value * card.depth);
  const y = useTransform(springY, (value) => value * card.depth);
  const Icon = card.icon;

  return (
    <motion.div
      className={`absolute w-[178px] ${card.className}`}
      style={reduce ? undefined : { x, y }}
      initial={reduce ? false : { opacity: 0, y: 28, scale: 0.94 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ ...springEnter, delay: 0.35 + index * 0.09 }}
    >
      <div
        className="rounded-[20px] border border-line bg-surface p-3.5 shadow-[var(--shadow-float)]"
        style={{ rotate: `${card.rotate}deg` }}
      >
        <span className="mb-2.5 inline-grid h-7 w-7 place-items-center rounded-full bg-accent-wash text-accent">
          <Icon size={15} weight="bold" />
        </span>
        {card.body}
      </div>
    </motion.div>
  );
}
