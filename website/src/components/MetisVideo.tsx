import { useEffect, useRef, useState } from "react";
import { motion, useReducedMotion } from "motion/react";
import { Pause as PauseIcon } from "@phosphor-icons/react/dist/icons/Pause";
import { Play as PlayIcon } from "@phosphor-icons/react/dist/icons/Play";
import { springUI } from "../lib/motion";

/**
 * The hero centrepiece: a real recording of Metis working inside a video
 * editor, drawing on the screen and naming the control you need.
 *
 * Loading is deliberately deferred. The clip is most of a megabyte and sits
 * above the fold, so it starts at preload="none" behind its poster and only
 * begins fetching once the page has finished loading. That keeps the largest
 * paint on a 59kB image instead of the video.
 *
 * Playback is pausable. Anything that moves on its own for more than five
 * seconds needs a control to stop it (WCAG 2.2.2), and a screen recording that
 * loops forever is exactly that. Under prefers-reduced-motion it does not
 * start at all and rests on the poster until asked.
 */
export function MetisVideo() {
  const reduce = useReducedMotion();
  const video = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const node = video.current;
    if (!node || reduce) return;

    // Wait for the page to settle before spending bandwidth on the clip.
    const start = () => {
      node.preload = "auto";
      node.load();
      void node.play().catch(() => {
        // Autoplay can still be refused. The poster and the play control
        // remain, so a refusal costs the visitor nothing.
      });
      setReady(true);
    };

    if (document.readyState === "complete") {
      const id = window.setTimeout(start, 200);
      return () => window.clearTimeout(id);
    }

    window.addEventListener("load", start, { once: true });
    return () => window.removeEventListener("load", start);
  }, [reduce]);

  function toggle() {
    const node = video.current;
    if (!node) return;

    if (node.paused) {
      if (!ready) {
        node.preload = "auto";
        node.load();
        setReady(true);
      }
      void node.play().catch(() => undefined);
    } else {
      node.pause();
    }
  }

  return (
    <div className="relative mx-auto w-full max-w-[624px]">
      <motion.div
        initial={false}
        className="relative overflow-hidden rounded-[18px] border border-line bg-surface-sunken shadow-[var(--shadow-float)]"
      >
        <video
          ref={video}
          className="block aspect-video w-full"
          poster="/metis-demo-poster.jpg"
          preload="none"
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

        <motion.button
          type="button"
          onClick={toggle}
          whileTap={reduce ? undefined : { scale: 0.94 }}
          transition={springUI}
          className="material-strong absolute right-3 bottom-3 grid h-11 w-11 cursor-pointer place-items-center rounded-full text-ink"
          aria-label={playing ? "Pause the demonstration" : "Play the demonstration"}
        >
          {playing ? (
            <PauseIcon size={15} weight="fill" />
          ) : (
            <PlayIcon size={15} weight="fill" />
          )}
        </motion.button>
      </motion.div>
    </div>
  );
}
