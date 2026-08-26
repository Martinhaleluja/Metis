import { useEffect, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { Pause } from "@phosphor-icons/react/dist/icons/Pause";
import { Play } from "@phosphor-icons/react/dist/icons/Play";
import { CaretLeft } from "@phosphor-icons/react/dist/icons/CaretLeft";
import { CaretRight } from "@phosphor-icons/react/dist/icons/CaretRight";
import { SpeakerHigh } from "@phosphor-icons/react/dist/icons/SpeakerHigh";
import { SpeakerSimpleX } from "@phosphor-icons/react/dist/icons/SpeakerSimpleX";
import { CornersOut } from "@phosphor-icons/react/dist/icons/CornersOut";
import { X } from "@phosphor-icons/react/dist/icons/X";
import { Minus } from "@phosphor-icons/react/dist/icons/Minus";
import { Square } from "@phosphor-icons/react/dist/icons/Square";
import { MagnifyingGlass } from "@phosphor-icons/react/dist/icons/MagnifyingGlass";

function formatTime(seconds: number) {
  if (isNaN(seconds)) return "00:00";
  const m = Math.floor(seconds / 60).toString().padStart(2, "0");
  const s = Math.floor(seconds % 60).toString().padStart(2, "0");
  return `${m}:${s}`;
}

export function MetisVideo() {
  const reduce = useReducedMotion();
  const video = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);
  const [ready, setReady] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);

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

  function handleSeek(e: React.MouseEvent<HTMLDivElement>) {
    const node = video.current;
    if (!node || !duration) return;

    const rect = e.currentTarget.getBoundingClientRect();
    const clickX = e.clientX - rect.left;
    const width = rect.width;
    const ratio = Math.max(0, Math.min(1, clickX / width));
    node.currentTime = ratio * duration;
  }

  function handleVolumeChange(e: React.ChangeEvent<HTMLInputElement>) {
    const node = video.current;
    if (!node) return;

    const newVol = parseFloat(e.target.value);
    node.volume = newVol;
    node.muted = newVol === 0;
  }

  function toggleMute() {
    const node = video.current;
    if (!node) return;
    node.muted = !node.muted;
  }

  function handleFullscreen() {
    const node = video.current;
    if (!node) return;
    if (node.requestFullscreen) {
      void node.requestFullscreen();
    }
  }

  // Calculate seek percentage for the scrubber width
  const progressPercent = duration ? (currentTime / duration) * 100 : 0;

  return (
    <div className="relative mx-auto w-full max-w-[640px] p-1 select-none">
      {/* 2006 WMP 11 Chrome Container */}
      <div className="wmp-container text-[12px] text-[#b4c5d8]">
        {/* Title Bar */}
        <div className="wmp-titlebar select-none">
          <div className="flex items-center gap-2">
            {/* WMP 11 Orange/Blue Logo */}
            <div className="flex h-4 w-4 items-center justify-center rounded-full bg-[#ff7300] p-[2px] shadow-sm">
              <div className="h-0 w-0 border-t-[4px] border-b-[4px] border-l-[6px] border-t-transparent border-b-transparent border-l-white ml-[1px]" />
            </div>
            <span className="font-semibold tracking-wide text-white drop-shadow">
              Windows Media Player
            </span>
          </div>
          {/* Windows Window Controls */}
          <div className="flex items-center gap-[3px]">
            <button className="flex h-[18px] w-6 items-center justify-center rounded bg-[#2e3745] hover:bg-[#3d485a] hover:text-white">
              <Minus size={11} weight="bold" />
            </button>
            <button className="flex h-[18px] w-6 items-center justify-center rounded bg-[#2e3745] hover:bg-[#3d485a] hover:text-white">
              <Square size={9} weight="bold" />
            </button>
            <button className="flex h-[18px] w-6 items-center justify-center rounded bg-[#cc3333] text-white hover:bg-[#ff3333]">
              <X size={10} weight="bold" />
            </button>
          </div>
        </div>

        {/* Tab/Menu Bar */}
        <div className="flex flex-wrap items-center justify-between border-b border-[#1b212a] bg-[#171b22] px-3 py-1.5">
          {/* Back/Forward Controls */}
          <div className="flex items-center gap-1.5">
            <button className="flex h-5 w-5 items-center justify-center rounded-full bg-[#2a323e] hover:bg-[#394454] hover:text-white">
              <CaretLeft size={12} weight="bold" />
            </button>
            <button className="flex h-5 w-5 items-center justify-center rounded-full bg-[#2a323e] hover:bg-[#394454] hover:text-white">
              <CaretRight size={12} weight="bold" />
            </button>
            <div className="h-4 w-[1px] bg-[#232935] mx-1" />
            {/* Tabs */}
            <div className="flex items-center gap-1">
              {[
                { name: "Now Playing", active: true },
                { name: "Library", active: false },
                { name: "Rip", active: false },
                { name: "Burn", active: false },
                { name: "Sync", active: false },
              ].map((tab) => (
                <span
                  key={tab.name}
                  className={`cursor-pointer rounded px-2 py-0.5 font-medium transition-all ${
                    tab.active
                      ? "bg-gradient-to-b from-[#2d6fa5] to-[#123f66] text-white shadow-[0_1px_3px_rgba(0,0,0,0.3)] border border-[#3e86c0]"
                      : "hover:bg-[#252c38] hover:text-white"
                  }`}
                >
                  {tab.name}
                </span>
              ))}
            </div>
          </div>

          {/* Search box on right */}
          <div className="relative mt-1 sm:mt-0 flex items-center rounded border border-[#2b3547] bg-[#0c0d10] px-2 py-0.5">
            <input
              type="text"
              placeholder="Search..."
              className="bg-transparent text-[11px] text-white outline-none placeholder-[#4b586e] w-20 focus:w-28 transition-all"
              readOnly
            />
            <MagnifyingGlass size={11} className="text-[#5c6d86]" />
          </div>
        </div>

        {/* Video Screen Panel */}
        <div className="relative bg-black">
          <video
            ref={video}
            className="mx-auto block aspect-video w-full"
            poster="/metis-demo-poster.jpg"
            preload="none"
            muted
            loop
            playsInline
            disablePictureInPicture
            tabIndex={-1}
            aria-label="Metis demonstrating on-screen action"
            onPlay={() => setPlaying(true)}
            onPause={() => setPlaying(false)}
            onTimeUpdate={(e) => setCurrentTime(e.currentTarget.currentTime)}
            onDurationChange={(e) => setDuration(e.currentTarget.duration)}
            onVolumeChange={(e) => {
              setVolume(e.currentTarget.volume);
              setMuted(e.currentTarget.muted);
            }}
          >
            <source src="/metis-demo.mp4" type="video/mp4" />
          </video>

          {/* Center Play Overlay - only shown when paused */}
          {!playing && (
            <div
              onClick={toggle}
              className="absolute inset-0 flex cursor-pointer items-center justify-center bg-black/30 hover:bg-black/20 transition-all group"
            >
              <div className="flex h-16 w-16 items-center justify-center rounded-full bg-gradient-to-b from-[#0070d6] to-[#003870] border-2 border-[#8ccaff] shadow-[0_0_20px_rgba(86,204,255,0.7)] group-hover:scale-105 transition-all">
                <Play size={24} weight="fill" className="text-white ml-1" />
              </div>
            </div>
          )}
        </div>

        {/* Bottom Panel */}
        <div className="wmp-control-panel">
          {/* 1. Scrubber / Timeline Slider */}
          <div className="mb-2">
            <div
              onClick={handleSeek}
              className="wmp-seek-bar relative"
              title="Seek"
            >
              <div
                className="wmp-seek-progress"
                style={{ width: `${progressPercent}%` }}
              />
              <div
                className="wmp-seek-handle"
                style={{ left: `${progressPercent}%` }}
              />
            </div>
          </div>

          {/* 2. Media Controls Panel */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            {/* Left section: Playing Status and Visualizer Wave */}
            <div className="flex items-center gap-3 min-w-[140px]">
              <div className="flex flex-col">
                <span className="font-semibold text-white truncate max-w-[150px]">
                  metis-demo.mp4
                </span>
                <span className="text-[10px] text-[#7186a0]">
                  {playing ? "Playing" : "Paused"}
                </span>
              </div>
              {/* Fake Equalizer/Waveform when playing */}
              <div className="flex h-6 items-end gap-[2px]" aria-hidden="true">
                {[8, 14, 10, 18, 12, 16, 9].map((height, i) => (
                  <span
                    key={i}
                    className="w-[2px] bg-[#008cff] transition-all duration-300"
                    style={{
                      height: playing ? `${height}px` : "2px",
                      opacity: playing ? 0.8 : 0.3,
                      animation: playing
                        ? `metis-halo 1s ease-in-out infinite alternate`
                        : "none",
                      animationDelay: `${i * 0.1}s`,
                    }}
                  />
                ))}
              </div>
            </div>

            {/* Center section: Transport buttons */}
            <div className="flex items-center justify-center gap-2">
              <button
                className="wmp-nav-button"
                onClick={() => {
                  if (video.current) video.current.currentTime = 0;
                }}
              >
                <CaretLeft size={16} weight="fill" />
              </button>

              <button
                type="button"
                onClick={toggle}
                className="wmp-play-button"
                aria-label={playing ? "Pause" : "Play"}
              >
                {playing ? (
                  <Pause size={20} weight="fill" />
                ) : (
                  <Play size={20} weight="fill" className="ml-0.5" />
                )}
              </button>

              <button
                className="wmp-nav-button"
                onClick={() => {
                  if (video.current) video.current.currentTime = duration;
                }}
              >
                <CaretRight size={16} weight="fill" />
              </button>
            </div>

            {/* Right section: Vol, Duration, and Fullscreen */}
            <div className="flex items-center justify-center sm:justify-end gap-3 min-w-[140px]">
              {/* Mute and volume slider */}
              <div className="flex items-center gap-1.5">
                <button
                  onClick={toggleMute}
                  className="text-[#b4c5d8] hover:text-white transition-colors"
                >
                  {muted || volume === 0 ? (
                    <SpeakerSimpleX size={15} weight="bold" />
                  ) : (
                    <SpeakerHigh size={15} weight="bold" />
                  )}
                </button>
                <input
                  type="range"
                  min="0"
                  max="1"
                  step="0.05"
                  value={muted ? 0 : volume}
                  onChange={handleVolumeChange}
                  className="h-1 w-14 cursor-pointer appearance-none rounded-lg bg-[#252c38] accent-[#0070d6] outline-none"
                />
              </div>

              {/* Time display */}
              <span className="text-[11px] font-mono text-[#adc0d8] tracking-wider select-none">
                {formatTime(currentTime)} / {formatTime(duration)}
              </span>

              {/* Fullscreen */}
              <button
                onClick={handleFullscreen}
                className="text-[#b4c5d8] hover:text-white transition-colors"
                title="Fullscreen"
              >
                <CornersOut size={16} weight="bold" />
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
