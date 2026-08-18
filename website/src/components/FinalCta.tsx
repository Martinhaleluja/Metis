import type { useWaitlist } from "../lib/waitlist";
import { MetisOrb } from "./MetisOrb";
import { Reveal } from "./Reveal";
import { WaitlistForm } from "./WaitlistForm";

export function FinalCta({ waitlist }: { waitlist: ReturnType<typeof useWaitlist> }) {
  return (
    <section className="relative overflow-hidden py-24 sm:py-32">
      <div
        className="pointer-events-none absolute inset-x-0 bottom-0 h-[560px]"
        aria-hidden="true"
        style={{
          background:
            "radial-gradient(ellipse 60% 60% at 50% 100%, color-mix(in srgb, var(--sky) 24%, transparent), transparent 70%)",
        }}
      />

      <div className="relative mx-auto max-w-[1180px] px-5 text-center">
        <Reveal>
          <div className="mx-auto mb-8 w-[104px]">
            <MetisOrb />
          </div>

          <h2 className="mx-auto max-w-[16ch] type-title text-ink">
            Get it the day it opens
          </h2>
          <p className="mx-auto mt-5 max-w-[52ch] type-body text-ink-muted">
            One email when the download is ready. Nothing else, and no forwarding your address
            anywhere.
          </p>
        </Reveal>

        <Reveal delay={0.1} className="mt-9">
          <WaitlistForm waitlist={waitlist} idPrefix="footer" />
        </Reveal>
      </div>
    </section>
  );
}
