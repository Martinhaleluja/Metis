import { motion, useReducedMotion } from "motion/react";
import { CursorClickIcon as CursorClick } from "@phosphor-icons/react/dist/icons/CursorClick";
import { GraduationCapIcon as GraduationCap } from "@phosphor-icons/react/dist/icons/GraduationCap";
import { PencilSimpleIcon as PencilSimple } from "@phosphor-icons/react/dist/icons/PencilSimple";
import { Reveal } from "./Reveal";
import { springUI } from "../lib/motion";

/**
 * What Metis does, and the one thing it will not do.
 *
 * This section used to be a segmented control offering a choice between
 * teaching and an Autopilot that worked the machine for you. The choice is
 * gone: Metis is a learning instrument and cannot operate the computer at all.
 * Advertising a switch that no longer exists would be the worst kind of stale
 * copy, because someone would install it expecting the other half.
 *
 * The claim is stronger stated plainly than it was as an option.
 */
const traits = [
  {
    key: "teaches",
    icon: GraduationCap,
    title: "It teaches while you work",
    detail:
      "Metis explains what to do and why, in the application you are actually using, on the screen you are actually looking at.",
  },
  {
    key: "draws",
    icon: PencilSimple,
    title: "It draws on your screen",
    detail:
      "Arrows, outlines and a pointer that traces the path, laid over your own windows. The marks are click-through and fade on their own.",
  },
  {
    key: "never",
    icon: CursorClick,
    title: "It never takes the controls",
    detail:
      "No clicking, no typing, no moving your pointer. Not as a default, not as a setting you could switch on — the ability is simply not in the program.",
  },
] as const;

export function NeverTakesOver() {
  const reduce = useReducedMotion();

  return (
    <section className="py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <div className="rounded-[20px] border border-line bg-surface p-7 sm:p-12">
          <Reveal>
            <h2 className="max-w-[20ch] type-title text-ink">
              It shows you how. It never does it for you.
            </h2>
            <p className="mt-4 max-w-[62ch] type-body text-ink-muted">
              A tool that finishes the task for you cannot also be the thing that teaches you to do
              it. So Metis does not have that ability at all — the skill it leaves behind is yours.
            </p>
          </Reveal>

          <Reveal delay={0.08}>
            <div className="mt-10 grid gap-4 sm:grid-cols-3">
              {traits.map((trait, index) => {
                const Icon = trait.icon;
                return (
                  <motion.div
                    key={trait.key}
                    initial={reduce ? false : { opacity: 0, y: 14 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, margin: "-60px" }}
                    transition={reduce ? { duration: 0 } : { ...springUI, delay: index * 0.07 }}
                    className="rounded-[20px] bg-surface-sunken p-6"
                  >
                    <span className="mb-4 inline-grid h-10 w-10 place-items-center rounded-full bg-accent-wash text-accent">
                      <Icon size={19} weight="bold" />
                    </span>
                    <p className="type-heading text-ink">{trait.title}</p>
                    <p className="mt-2.5 type-caption text-ink-muted">{trait.detail}</p>
                  </motion.div>
                );
              })}
            </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
