import { useEffect } from "react";
import { motion, useMotionValue, useReducedMotion, useSpring, useTransform } from "motion/react";
import { X } from "@phosphor-icons/react/dist/icons/X";
import { springEnter } from "../lib/motion";

const cards = [
  {
    key: "hotkey",
    depth: 26,
    className: "left-[4%] top-[152px]",
    rotate: -4,
    winTitle: "Hotkeys.sys",
    winIcon: "⌨️",
    body: (
      <>
        <p className="text-[12px] font-bold font-pixel text-black">Hold to talk</p>
        <div className="mt-2.5 flex items-center gap-1">
          {["Ctrl", "Shift", "1"].map((key) => (
            <kbd
              key={key}
              className="win95-button text-[9px] !px-1.5 !py-0.5"
            >
              {key}
            </kbd>
          ))}
        </div>
      </>
    ),
  },
  {
    key: "wake",
    depth: 40,
    className: "right-[4%] top-[112px]",
    rotate: 3,
    winTitle: "Listening.exe",
    winIcon: "🎙️",
    body: (
      <>
        <p className="text-[12px] font-bold font-pixel text-black">Wake Word</p>
        <div className="mt-2.5 flex h-4 items-end gap-[2px]" aria-hidden="true">
          {[9, 16, 6, 20, 12, 18, 8].map((height, index) => (
            <span
              key={index}
              className="w-[2px] rounded bg-[#0054e3] motion-safe:animate-[metis-halo_1.4s_ease-in-out_infinite]"
              style={{ height: `${height * 0.7}px`, animationDelay: `${index * 0.11}s` }}
            />
          ))}
        </div>
      </>
    ),
  },
  {
    key: "screen",
    depth: 32,
    className: "left-[7%] bottom-[11%]",
    rotate: 3,
    winTitle: "screen_capture.dll",
    winIcon: "🖼️",
    body: (
      <>
        <p className="text-[12px] font-bold font-pixel text-black">Sees your screen</p>
        <p className="mt-1 text-[11px] leading-snug text-zinc-700 font-pixel">
          Only when you ask.
        </p>
      </>
    ),
  },
  {
    key: "modes",
    depth: 22,
    className: "right-[6%] bottom-[16%]",
    rotate: -3,
    winTitle: "Safety_Rules.txt",
    winIcon: "🛡️",
    body: (
      <>
        <p className="text-[12px] font-bold font-pixel text-black">Teaches you</p>
        <div className="mt-2 flex gap-1" aria-hidden="true">
          <span className="bg-[#002f96] text-white px-1.5 py-0.5 text-[9px] font-bold rounded-sm">
            Draws
          </span>
          <span className="win95-button !px-1.5 !py-0.5 text-[9px] font-bold pointer-events-none">
            User Clicks
          </span>
        </div>
      </>
    ),
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

  return (
    <motion.div
      className={`absolute w-[184px] ${card.className}`}
      style={reduce ? undefined : { x, y }}
      initial={reduce ? false : { opacity: 0, y: 28, scale: 0.94 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ ...springEnter, delay: 0.35 + index * 0.09 }}
    >
      <div
        className="win95-window shadow-[3px_3px_0_#000] p-1 text-black"
        style={{ rotate: `${card.rotate}deg` }}
      >
        {/* Title Bar */}
        <div className="win95-titlebar text-[9px] py-0.5 px-1.5 mb-1.5 flex justify-between items-center">
          <span className="font-bold flex items-center gap-1 font-pixel select-none">
            <span>{card.winIcon}</span>
            {card.winTitle}
          </span>
          <button className="win95-button !p-0 h-3.5 w-3.5 flex items-center justify-center text-[7px]">
            <X size={6} weight="bold" />
          </button>
        </div>
        
        {/* Content */}
        <div className="p-1">
          {card.body}
        </div>
      </div>
    </motion.div>
  );
}
