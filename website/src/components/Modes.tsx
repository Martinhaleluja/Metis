import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { CursorClickIcon as CursorClick } from "@phosphor-icons/react/dist/icons/CursorClick";
import { GraduationCapIcon as GraduationCap } from "@phosphor-icons/react/dist/icons/GraduationCap";
import { Reveal } from "./Reveal";
import { springMomentum, springUI } from "../lib/motion";

/**
 * The two modes, described the way the application itself describes them in
 * Metis.Core.Models.AssistanceMode. Learn is the default because a mode that
 * cannot act cannot act wrongly.
 */
const modes = [
  {
    key: "learn",
    name: "Learn",
    icon: GraduationCap,
    tagline: "Metis shows you how and never touches the computer itself.",
    detail:
      "It explains, draws on the screen, and points at the exact control. Tell it to just do the thing and it declines, then offers to show you instead. This is where every install starts.",
    points: ["Annotates the screen", "Never moves your pointer", "The default"],
  },
  {
    key: "autopilot",
    name: "Autopilot",
    icon: CursorClick,
    tagline: "Metis can work the computer for you.",
    detail:
      "Mouse, keyboard, launching programs, system settings. It still reads each request to decide whether acting is what you actually wanted, and it still stops before anything destructive, irreversible, financial, or security-sensitive.",
    points: ["Works the machine", "Reads intent per request", "Stops at risky steps"],
  },
] as const;

export function Modes() {
  const [active, setActive] = useState(0);
  const reduce = useReducedMotion();
  const mode = modes[active];
  const Icon = mode.icon;

  return (
    <section className="py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <div className="rounded-[20px] border border-line bg-surface p-7 sm:p-12">
          <Reveal>
            <h2 className="max-w-[20ch] type-title text-ink">
              You decide how much of the machine it gets
            </h2>
            <p className="mt-4 max-w-[62ch] type-body text-ink-muted">
              One switch, and it is a ceiling rather than a behaviour. Metis still reads every
              request on its own terms underneath.
            </p>
          </Reveal>

          <Reveal delay={0.08}>
            {/* Segmented control. The sliding pill is a shared layout element so
                the selection physically travels between the two options. */}
            <div
              role="tablist"
              aria-label="Assistance mode"
              className="mt-9 inline-flex gap-1 rounded-full border border-line bg-surface-sunken p-1"
            >
              {modes.map((item, index) => (
                <button
                  key={item.key}
                  role="tab"
                  id={`mode-tab-${item.key}`}
                  aria-selected={active === index}
                  aria-controls={`mode-panel-${item.key}`}
                  onClick={() => setActive(index)}
                  className="press relative cursor-pointer rounded-full px-5 py-2.5 text-[14px] font-medium"
                >
                  {active === index && (
                    <motion.span
                      layoutId="mode-pill"
                      transition={reduce ? { duration: 0 } : springMomentum}
                      className="absolute inset-0 rounded-full bg-accent"
                    />
                  )}
                  <span
                    className={`relative z-10 ${
                      active === index ? "text-accent-contrast" : "text-ink-muted"
                    }`}
                  >
                    {item.name}
                  </span>
                </button>
              ))}
            </div>

            <div className="mt-8">
              <AnimatePresence mode="wait">
                <motion.div
                  key={mode.key}
                  role="tabpanel"
                  id={`mode-panel-${mode.key}`}
                  aria-labelledby={`mode-tab-${mode.key}`}
                  initial={reduce ? false : { opacity: 0, y: 14 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={reduce ? undefined : { opacity: 0, y: -10 }}
                  transition={springUI}
                  className="grid gap-8 lg:grid-cols-[1.4fr_1fr]"
                >
                  <div>
                    <span className="mb-4 inline-grid h-10 w-10 place-items-center rounded-full bg-accent-wash text-accent">
                      <Icon size={19} weight="bold" />
                    </span>
                    <p className="type-heading text-ink">
                      {mode.tagline}
                    </p>
                    <p className="mt-3 max-w-[58ch] type-caption text-ink-muted">
                      {mode.detail}
                    </p>
                  </div>

                  <ul className="flex flex-col justify-center gap-3 rounded-[20px] bg-surface-sunken p-6">
                    {mode.points.map((point) => (
                      <li key={point} className="flex items-center gap-2.5 text-[14px] text-ink">
                        <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-accent" />
                        {point}
                      </li>
                    ))}
                  </ul>
                </motion.div>
              </AnimatePresence>
            </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
