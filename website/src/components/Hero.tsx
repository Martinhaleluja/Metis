import { motion, useReducedMotion } from "motion/react";
import type { useWaitlist } from "../lib/waitlist";
import { FloatingCards } from "./FloatingCards";
import { MetisDemo } from "./MetisDemo";
import { WaitlistForm } from "./WaitlistForm";
import { springEnter } from "../lib/motion";

export function Hero({ waitlist }: { waitlist: ReturnType<typeof useWaitlist> }) {
  const reduce = useReducedMotion();

  const rise = (delay: number) => ({
    initial: reduce ? false : { opacity: 0, y: 22 },
    animate: { opacity: 1, y: 0 },
    transition: { ...springEnter, delay },
  });

  return (
    <section id="top" className="relative overflow-hidden pt-24 pb-10 sm:pb-14">
      {/* A single soft wash behind the fold, tinted with the mark's own blue. */}
      <div
        className="pointer-events-none absolute inset-x-0 top-0 h-[720px]"
        aria-hidden="true"
        style={{
          background:
            "radial-gradient(ellipse 70% 50% at 50% 0%, color-mix(in srgb, var(--sky) 22%, transparent), transparent 70%)",
        }}
      />

      <FloatingCards />

      <div className="relative mx-auto max-w-[1180px] px-5 text-center">
        <motion.div {...rise(0)} className="mb-7">
          <MetisDemo />
        </motion.div>

        <motion.h1
          {...rise(0.08)}
          className="mx-auto max-w-[15ch] type-display text-ink"
        >
          An AI companion
          <br />
          <span className="text-ink-muted">for your computer</span>
        </motion.h1>

        <motion.p
          {...rise(0.16)}
          className="mx-auto mt-5 max-w-[58ch] type-body text-ink-muted"
        >
          Metis sits in your Windows tray, sees what is on screen, and works alongside you
          instead of in another tab.
        </motion.p>

        <motion.div {...rise(0.24)} id="join" className="mt-8 scroll-mt-28">
          <WaitlistForm waitlist={waitlist} idPrefix="hero" />
        </motion.div>
      </div>
    </section>
  );
}
