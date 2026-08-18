import type { ReactNode } from "react";
import { motion, useReducedMotion } from "motion/react";
import { springEnter } from "../lib/motion";

/**
 * Enter-on-scroll for content that just needs to arrive in order. Anything
 * that has to pin or scrub uses GSAP instead; this stays on Motion so it does
 * not pull ScrollTrigger into sections that do not need it.
 */
export function Reveal({
  children,
  delay = 0,
  className = "",
}: {
  children: ReactNode;
  delay?: number;
  className?: string;
}) {
  const reduce = useReducedMotion();

  return (
    <motion.div
      className={className}
      initial={reduce ? false : { opacity: 0, y: 24 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.25 }}
      transition={{ ...springEnter, delay }}
    >
      {children}
    </motion.div>
  );
}
