import type { useWaitlist } from "../lib/waitlist";
import { Reveal } from "./Reveal";
import { WaitlistForm } from "./WaitlistForm";

export function FinalCta({
  waitlist,
}: {
  waitlist: ReturnType<typeof useWaitlist>;
}) {
  return (
    <section className="py-24 sm:py-32">
      <div className="mx-auto max-w-[640px] px-5">
        <Reveal>
          <div className="card" style={{ transform: "rotate(-0.8deg)" }}>
            <div className="panel-title">
              <span className="text-[12px]">
                Metis Setup &mdash; Join the Waitlist
              </span>
              <button
                className="xp-button-close"
                aria-hidden="true"
                tabIndex={-1}
              >
                &times;
              </button>
            </div>

            <div className="flex bg-surface-sunken">
              {/* XP wizard left banner */}
              <div className="hidden sm:flex w-[140px] shrink-0 flex-col justify-between p-4 bg-gradient-to-b from-[#1085d2] to-[#002f96]">
                <div>
                  <img
                    src="/metis-mark.png"
                    alt=""
                    width={40}
                    height={40}
                    className="drop-shadow-lg mb-3"
                  />
                  <div className="text-white text-[14px] font-bold">Metis</div>
                  <div className="text-white/60 text-[10px]">
                    Desktop Companion
                  </div>
                </div>
                <div className="text-white/40 text-[9px]">v1.0.0</div>
              </div>

              {/* Wizard content */}
              <div className="flex-1 p-6">
                <h2
                  className="text-[16px] font-bold text-accent"
                >
                  Get it the day it opens
                </h2>
                <p
                  className="mt-2 text-[12px] text-[#333] leading-relaxed"
                >
                  One email when the download is ready. Nothing else, and no
                  forwarding your address anywhere.
                </p>

                <div className="mt-6">
                  <WaitlistForm waitlist={waitlist} idPrefix="footer" />
                </div>
              </div>
            </div>

            {/* Wizard footer */}
            <div className="bg-surface-sunken border-t border-line px-4 py-3 flex justify-end gap-2">
              <span
                className="btn press text-[11px] opacity-50 !cursor-default"
                aria-hidden="true"
              >
                &lt; Back
              </span>
              <a
                href="#join"
                className="btn press text-[11px] no-underline"
              >
                Next &gt;
              </a>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
