import { useEffect, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { Reveal } from "./Reveal";

const videos = [
  {
    src: "/metis-showcase-1.mp4",
    title: "Screen guidance",
    description:
      "Metis captures what's on your screen and tells you exactly where to look and what to do next.",
  },
  {
    src: "/metis-showcase-2.mp4",
    title: "Voice interaction",
    description:
      "Hold one chord from anywhere in Windows and speak. Metis listens, then answers out loud.",
  },
  {
    src: "/metis-showcase-3.mp4",
    title: "Drawing overlays",
    description:
      "Metis draws arrows, highlights, and labels directly over the controls you need — then fades away.",
  },
];

function useAutoplayOnScroll(reduce: boolean | null) {
  const ref = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);

  useEffect(() => {
    const node = ref.current;
    if (!node || reduce) return;

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          void node.play().catch(() => {});
        } else {
          node.pause();
        }
      },
      { threshold: 0.4 },
    );

    observer.observe(node);
    return () => observer.disconnect();
  }, [reduce]);

  function toggle() {
    const node = ref.current;
    if (!node) return;
    if (node.paused) {
      void node.play().catch(() => undefined);
    } else {
      node.pause();
    }
  }

  return { ref, playing, setPlaying, toggle };
}

/**
 * One player, framed as a card.
 *
 * There were three — Win95Player, XpPlayer and Wmp11Player — identical in
 * behaviour and different only in which operating system's chrome they wore.
 * With the chrome gone they were three copies of the same component, so they
 * are one, and the `era` and `winTitle` fields that dressed them are gone.
 */
function Player({ video }: { video: (typeof videos)[0] }) {
  const reduce = useReducedMotion();
  const { ref, playing, setPlaying, toggle } = useAutoplayOnScroll(reduce);

  return (
    <div className="card overflow-hidden">
      <div className="relative bg-ink">
        <video
          ref={ref}
          className="block w-full aspect-video cursor-pointer"
          muted
          loop
          playsInline
          disablePictureInPicture
          tabIndex={-1}
          onClick={toggle}
          onPlay={() => setPlaying(true)}
          onPause={() => setPlaying(false)}
        >
          <source src={video.src} type="video/mp4" />
        </video>
      </div>
      <div className="flex items-center gap-3 px-4 py-3">
        <button
          onClick={toggle}
          className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-accent text-accent-contrast text-sm cursor-pointer"
          aria-label={playing ? "Pause" : "Play"}
        >
          {playing ? "⏸" : "▶"}
        </button>
        <span className="type-caption text-ink-muted truncate">
          Play
        </span>
      </div>
    </div>
  );
}

export function VideoShowcase() {
  return (
    <section id="showcase" className="scroll-mt-16 py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="type-title text-ink text-center">
            See what Metis can do
          </h2>
          <p className="mt-3 text-center type-body text-ink-muted max-w-[52ch] mx-auto">
            Three things that happen every time you ask for help.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-8 md:grid-cols-3">
          {videos.map((video, i) => (
              <Reveal key={video.src} delay={i * 0.08}>
                <div
                  className="flex flex-col gap-4 transition-transform hover:scale-[1.02]"
                >
                  <Player video={video} />
                  <div>
                    <h3 className="type-heading text-ink">{video.title}</h3>
                    <p className="mt-1 type-caption text-ink-muted">
                      {video.description}
                    </p>
                  </div>
                </div>
              </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
