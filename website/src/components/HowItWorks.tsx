import { useEffect, useRef } from "react";

/**
 * Three steps that happen in order, so the section reads left to right as you
 * scroll down. The pan is the point: it is the one place on the page where the
 * motion carries the meaning rather than decorating it.
 *
 * Setup goes through gsap.matchMedia so the pin builds and tears itself down
 * as the viewport crosses the breakpoint. A plain effect with a width check
 * only reads the width once, which leaves the pan dead for anyone who rotates
 * a tablet or resizes a window after load.
 */
const steps = [
  {
    number: "01",
    title: "Hold the chord and talk",
    body: "Ctrl+Shift+1 from anywhere in Windows, or the wake word if your hands are busy. Metis starts recording while you hold it.",
  },
  {
    number: "02",
    title: "It looks at the desktop",
    body: "On the turn you ask for, Metis captures the whole virtual desktop, every monitor, and sends it along with what you said.",
  },
  {
    number: "03",
    title: "It shows you, or it does it",
    body: "Learn draws on the screen and points at the control you need. Autopilot moves the pointer and works it for you.",
  },
];

/** The slice of gsap.matchMedia this component uses, so the handle can be held
 *  outside the async import without pulling GSAP's types into the bundle. */
type MatchMedia = {
  add: (query: string, callback: () => void) => void;
  revert: () => void;
};

export function HowItWorks() {
  const wrapper = useRef<HTMLDivElement>(null);
  const track = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let media: MatchMedia | undefined;
    let cancelled = false;

    // GSAP is only needed for this one section, so it is fetched as its own
    // chunk rather than sitting in the bundle that renders the hero.
    void (async () => {
      const [{ gsap }, { ScrollTrigger }] = await Promise.all([
        import("gsap"),
        import("gsap/ScrollTrigger"),
      ]);

      if (cancelled || !wrapper.current || !track.current) return;
      gsap.registerPlugin(ScrollTrigger);

      media = gsap.matchMedia();

      // Below the tablet breakpoint the cards stack, so pinning would trap the
      // visitor in a section with nowhere to pan.
      media.add(
        "(min-width: 768px) and (prefers-reduced-motion: no-preference)",
        () => {
          const distance = () => track.current!.scrollWidth - window.innerWidth;

          // On a very wide monitor the three cards already fit side by side.
          // There is nothing to pan, and pinning to a negative distance would
          // scrub backwards.
          if (distance() <= 0) return;

          gsap.to(track.current, {
            x: () => -distance(),
            ease: "none",
            scrollTrigger: {
              trigger: wrapper.current,
              start: "top top",
              end: () => `+=${distance()}`,
              pin: true,
              scrub: 1,
              invalidateOnRefresh: true,
            },
          });
        },
      );
    })();

    return () => {
      cancelled = true;
      media?.revert();
    };
  }, []);

  return (
    <section id="how" className="scroll-mt-24">
      <div ref={wrapper} className="relative overflow-hidden md:flex md:h-[100dvh] md:flex-col">
        <div className="mx-auto w-full max-w-[1180px] shrink-0 px-5 pt-20 md:pt-28">
          <h2 className="max-w-[16ch] type-title text-ink">
            One chord, and it is already looking
          </h2>
        </div>

        <div
          ref={track}
          className="mt-10 flex flex-col gap-5 px-5 pb-20 md:mt-0 md:min-h-0 md:flex-1 md:flex-row md:flex-nowrap md:items-center md:gap-8 md:pb-0 md:pl-[max(1.25rem,calc((100vw-1180px)/2))]"
        >
          {steps.map((step) => (
            <article
              key={step.number}
              className="flex shrink-0 flex-col justify-between rounded-[20px] border border-line bg-surface p-8 md:h-[320px] md:w-[46vw] md:max-w-[720px] md:p-10"
            >
              <span
                className="font-display text-[52px] leading-none font-semibold tracking-tight md:text-[64px]"
                style={{ color: "color-mix(in srgb, var(--accent) 22%, transparent)" }}
              >
                {step.number}
              </span>

              <div className="mt-8 md:mt-0">
                <h3 className="type-heading text-ink md:text-[1.5rem]">
                  {step.title}
                </h3>
                <p className="mt-3 max-w-[42ch] type-caption text-ink-muted">
                  {step.body}
                </p>
              </div>
            </article>
          ))}

          {/* Tail spacer so the last card can clear the right edge. */}
          <div className="hidden shrink-0 md:block md:w-[8vw]" aria-hidden="true" />
        </div>
      </div>
    </section>
  );
}
