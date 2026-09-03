import { useEffect, useRef, useState } from "react";
import { motion, useReducedMotion } from "motion/react";
import type { useWaitlist } from "../lib/waitlist";
import { WaitlistForm } from "./WaitlistForm";
import { springEnter } from "../lib/motion";

export function Hero({
  waitlist,
}: {
  waitlist: ReturnType<typeof useWaitlist>;
}) {
  const reduce = useReducedMotion();
  const videoRef = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);

  useEffect(() => {
    const node = videoRef.current;
    if (!node || reduce) return;

    const start = () => {
      void node.play().catch(() => {});
    };

    if (document.readyState === "complete") {
      const id = window.setTimeout(start, 300);
      return () => window.clearTimeout(id);
    }

    window.addEventListener("load", start, { once: true });
    return () => window.removeEventListener("load", start);
  }, [reduce]);

  function toggle() {
    const node = videoRef.current;
    if (!node) return;
    if (node.paused) {
      void node.play().catch(() => undefined);
    } else {
      node.pause();
    }
  }

  const rise = (delay: number) => ({
    initial: reduce ? false : ({ opacity: 0, y: 22 } as const),
    animate: { opacity: 1, y: 0 },
    transition: { ...springEnter, delay },
  });

  return (
    <section id="top" className="relative overflow-hidden pt-[48px] pb-12 sm:pb-16">
      {/* Subtle teal desktop-wallpaper wash */}
      <div
        className="pointer-events-none absolute inset-0"
        aria-hidden="true"
        style={{
          background:
            "linear-gradient(180deg, #e0f0f0 0%, #ffffff 50%)",
        }}
      />

      <div className="relative mx-auto max-w-[1180px] px-5 pt-14 text-center">
        {/* Status badge */}
        <motion.div {...rise(0)} className="mb-5">
          <span
            className="inline-flex items-center gap-2 press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] !cursor-default !py-1.5 !px-5 text-[12px]"
          >
            <span className="w-2 h-2 rounded-full bg-[#00c800] shadow-[0_0_4px_#00c800]" />
            Now accepting waitlist signups
          </span>
        </motion.div>

        <motion.h1
          {...rise(0.06)}
          className="mx-auto max-w-[18ch] type-display text-ink"
        >
          An AI companion
          <br />
          <span className="text-ink-muted">for your desktop</span>
        </motion.h1>

        <motion.p
          {...rise(0.12)}
          className="mx-auto mt-4 max-w-[52ch] type-body text-ink-muted"
        >
          Metis sits in a small bar at the top of your screen, sees what&rsquo;s on it, and
          teaches you the software you never got shown. Say its name or hold a key
          &mdash; it draws, speaks, and never takes over.
        </motion.p>

        {/* The demonstration itself.
            It used to be dressed as Windows Media Player 11 — a fake title bar
            naming the player, three window-control dots, and a skip-back button
            wired to nothing. All of it was set decoration around the only thing
            anyone comes here to look at, so the frame is now a plain card and
            the video fills it. */}
        <motion.div {...rise(0.18)} className="mt-10 mx-auto max-w-[720px]">
          <div className="card overflow-hidden">
            <div className="relative bg-black">
              <video
                ref={videoRef}
                className="block w-full aspect-video"
                poster="/metis-demo-poster.jpg"
                muted
                loop
                playsInline
                disablePictureInPicture
                tabIndex={-1}
                aria-label="Metis pointing out a control inside a video editor and explaining the step"
                onPlay={() => setPlaying(true)}
                onPause={() => setPlaying(false)}
              >
                <source src="/metis-demo.mp4" type="video/mp4" />
              </video>
            </div>

            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <button
                  onClick={toggle}
                  className="grid h-9 w-9 place-items-center rounded-full bg-accent text-accent-contrast text-lg cursor-pointer"
                  aria-label={
                    playing
                      ? "Pause the demonstration"
                      : "Play the demonstration"
                  }
                >
                  {playing ? "⏸" : "▶"}
                </button>
              </div>
              <div className="flex-1 h-[6px] rounded bg-surface-sunken">
                <div className="h-full w-[65%] rounded bg-accent" />
              </div>
              <span className="type-caption text-ink-muted" aria-hidden="true">
                1:24
              </span>
            </div>
          </div>
        </motion.div>

        {/* Waitlist form */}
        <motion.div {...rise(0.24)} id="join" className="mt-10 scroll-mt-28">
          <WaitlistForm waitlist={waitlist} idPrefix="hero" />
        </motion.div>
      </div>
    </section>
  );
}
