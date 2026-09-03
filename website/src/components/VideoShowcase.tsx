import { useEffect, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { Reveal } from "./Reveal";

const videos = [
  {
    src: "/metis-showcase-1.mp4",
    title: "Screen guidance",
    description:
      "Metis captures what's on your screen and tells you exactly where to look and what to do next.",
    era: "win95" as const,
    winTitle: "metis_guidance.exe",
  },
  {
    src: "/metis-showcase-2.mp4",
    title: "Voice interaction",
    description:
      "Hold one chord from anywhere in Windows and speak. Metis listens, then answers out loud.",
    era: "xp" as const,
    winTitle: "Metis Companion",
  },
  {
    src: "/metis-showcase-3.mp4",
    title: "Drawing overlays",
    description:
      "Metis draws arrows, highlights, and labels directly over the controls you need — then fades away.",
    era: "wmp11" as const,
    winTitle: "Metis — Live Overlay",
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

function Win95Player({ video }: { video: (typeof videos)[0] }) {
  const reduce = useReducedMotion();
  const { ref, playing, setPlaying, toggle } = useAutoplayOnScroll(reduce);

  return (
    <div className="win95-window">
      <div className="win95-titlebar">
        <span className="truncate">{video.winTitle}</span>
        <div className="flex gap-[2px]">
          <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
            _
          </span>
          <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
            &times;
          </span>
        </div>
      </div>
      <div className="p-1 bg-[#c0c0c0]">
        <div className="win95-field overflow-hidden">
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
        <div className="mt-1 flex items-center justify-between px-1 py-0.5">
          <button
            onClick={toggle}
            className="win95-button text-[10px] !py-0.5 cursor-pointer"
            aria-label={playing ? "Pause" : "Play"}
          >
            {playing ? "⏸ Pause" : "▶ Play"}
          </button>
          <span
            className="text-[10px] text-black"
            style={{ fontFamily: "var(--font-system)" }}
          >
            {video.title}
          </span>
        </div>
      </div>
    </div>
  );
}

function XpPlayer({ video }: { video: (typeof videos)[0] }) {
  const reduce = useReducedMotion();
  const { ref, playing, setPlaying, toggle } = useAutoplayOnScroll(reduce);

  return (
    <div className="xp-window">
      <div className="xp-titlebar">
        <span className="text-[12px] truncate">{video.winTitle}</span>
        <button className="xp-button-close" aria-hidden="true" tabIndex={-1}>
          &times;
        </button>
      </div>
      <div className="p-2 bg-[#ece9d8]">
        <div className="border border-[#7f9db9] overflow-hidden rounded">
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
        <div className="mt-2 flex items-center justify-between">
          <button
            onClick={toggle}
            className="xp-button-win text-[11px] !py-1 cursor-pointer"
            aria-label={playing ? "Pause" : "Play"}
          >
            {playing ? "⏸ Pause" : "▶ Play"}
          </button>
          <span
            className="text-[11px] text-[#003ca5] font-semibold"
            style={{ fontFamily: "Trebuchet MS, sans-serif" }}
          >
            {video.title}
          </span>
        </div>
      </div>
    </div>
  );
}

function Wmp11Player({ video }: { video: (typeof videos)[0] }) {
  const reduce = useReducedMotion();
  const { ref, playing, setPlaying, toggle } = useAutoplayOnScroll(reduce);

  return (
    <div className="wmp11-frame">
      <div className="wmp11-titlebar">
        <span className="font-semibold truncate">{video.winTitle}</span>
        <div className="flex gap-1.5">
          <span className="w-3 h-3 rounded-full bg-[#2c3444] border border-[#4a5568]" />
          <span className="w-3 h-3 rounded-full bg-[#e14e2c] border border-[#c42907]" />
        </div>
      </div>
      <div className="relative bg-black">
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
      <div className="wmp11-controls flex items-center gap-3">
        <button
          onClick={toggle}
          className="wmp11-play text-sm cursor-pointer"
          aria-label={playing ? "Pause" : "Play"}
        >
          {playing ? "⏸" : "▶"}
        </button>
        <div className="flex-1 h-[5px] bg-[#111419] border border-[#232935] rounded">
          <div className="h-full w-0 bg-gradient-to-r from-[#0070d6] to-[#56ccff] rounded" />
        </div>
        <span
          className="text-[10px] text-[#5a6a82]"
          style={{ fontFamily: "var(--font-mono)" }}
        >
          {video.title}
        </span>
      </div>
    </div>
  );
}

const Player = {
  win95: Win95Player,
  xp: XpPlayer,
  wmp11: Wmp11Player,
} as const;

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
          {videos.map((video, i) => {
            const C = Player[video.era];
            const tilts = [-1.5, 1, -0.8];
            return (
              <Reveal key={video.src} delay={i * 0.08}>
                <div
                  className="flex flex-col gap-4 transition-transform hover:scale-[1.02]"
                  style={{ transform: `rotate(${tilts[i]}deg)` }}
                >
                  <C video={video} />
                  <div>
                    <h3 className="type-heading text-ink">{video.title}</h3>
                    <p className="mt-1 type-caption text-ink-muted">
                      {video.description}
                    </p>
                  </div>
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}
