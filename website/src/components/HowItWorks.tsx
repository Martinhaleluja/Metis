import { useEffect, useRef } from "react";
import { X } from "@phosphor-icons/react/dist/icons/X";
import { MagnifyingGlassPlus } from "@phosphor-icons/react/dist/icons/MagnifyingGlassPlus";
import { MagnifyingGlassMinus } from "@phosphor-icons/react/dist/icons/MagnifyingGlassMinus";
import { Trash } from "@phosphor-icons/react/dist/icons/Trash";
import { FloppyDisk } from "@phosphor-icons/react/dist/icons/FloppyDisk";

/** The slice of gsap.matchMedia this component uses */
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

    void (async () => {
      const [{ gsap }, { ScrollTrigger }] = await Promise.all([
        import("gsap"),
        import("gsap/ScrollTrigger"),
      ]);

      if (cancelled || !wrapper.current || !track.current) return;
      gsap.registerPlugin(ScrollTrigger);

      media = gsap.matchMedia();

      media.add(
        "(min-width: 768px) and (prefers-reduced-motion: no-preference)",
        () => {
          const distance = () => track.current!.scrollWidth - window.innerWidth;
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
    <section id="how" className="scroll-mt-24 bg-[#c0c0c0]/10 py-10 md:py-0">
      <div ref={wrapper} className="relative overflow-hidden md:flex md:h-[100dvh] md:flex-col">
        <div className="mx-auto w-full max-w-[1180px] shrink-0 px-5 pt-20 md:pt-28">
          <h2 className="max-w-[20ch] type-title text-ink font-pixel">
            C:\&gt; One chord, and it is already looking
          </h2>
          <p className="mt-2 text-ink-muted type-caption">
            Here is the step-by-step breakdown of how Metis guides you through any desktop workflow.
          </p>
        </div>

        <div
          ref={track}
          className="mt-10 flex flex-col gap-6 px-5 pb-20 md:mt-0 md:min-h-0 md:flex-1 md:flex-row md:flex-nowrap md:items-center md:gap-8 md:pb-0 md:pl-[max(1.25rem,calc((100vw-1180px)/2))]"
        >
          
          {/* Step 1: Volume Control / Recording Panel */}
          <article className="win95-window shrink-0 flex flex-col justify-between shadow-[4px_4px_0_#000] text-black md:h-[450px] md:w-[480px] p-1">
            <div className="win95-titlebar">
              <span>Volume Control & Audio Input</span>
              <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><X /></button>
            </div>
            
            <div className="p-3 bg-[#c0c0c0] flex-1 flex flex-col justify-between">
              <div>
                <span className="font-display text-[32px] leading-none font-semibold text-blue-900 font-pixel">01</span>
                <h3 className="type-heading text-black font-pixel mt-1">Hold the chord and talk</h3>
                <p className="mt-2 text-[11px] text-zinc-700 leading-normal">
                  Press <strong>Ctrl+Shift+1</strong> from anywhere in Windows, or say the wake word. Metis captures voice while the key chord is active.
                </p>
              </div>

              {/* Volume Mixer / Slider UI */}
              <div className="win95-field bg-white my-3 p-2 flex gap-4 items-center flex-1">
                <div className="w-20 h-full flex flex-col items-center justify-between border-r border-zinc-200 pr-2">
                  <span className="text-[9px] font-bold">Mic Vol</span>
                  <div className="h-16 w-1 bg-zinc-400 relative">
                    <div className="absolute top-4 left-1/2 -translate-x-1/2 w-3 h-2 bg-[#dfdfdf] border border-white shadow-sm cursor-pointer" />
                  </div>
                  <span className="text-[9px]">Mute ▢</span>
                </div>
                <div className="flex-1 flex flex-col justify-center gap-1.5">
                  <span className="text-[10px] text-zinc-500 font-mono">Audio Buffer Stream:</span>
                  <div className="h-12 bg-black border border-zinc-500 p-0.5 relative overflow-hidden">
                    <img 
                      src="/image6.jpg" 
                      alt="Audio Visualizer Waveform" 
                      className="w-full h-full object-cover opacity-85"
                    />
                  </div>
                </div>
              </div>

              <div className="text-[10px] text-zinc-600 bg-zinc-100 p-1.5 border border-zinc-300">
                Hotkey: <code>Ctrl + Shift + 1</code> (System-wide global hook)
              </div>
            </div>
          </article>

          {/* Step 2: My Computer / Desktop Explorer */}
          <article className="win95-window shrink-0 flex flex-col justify-between shadow-[4px_4px_0_#000] text-black md:h-[450px] md:w-[480px] p-1">
            <div className="win95-titlebar">
              <span>My Computer - [Desktop Capture]</span>
              <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><X /></button>
            </div>

            {/* Folder Menus */}
            <div className="bg-[#c0c0c0] border-b border-[#808080] text-[10px] px-2 py-0.5 flex gap-2">
              <span><u>F</u>ile</span>
              <span><u>E</u>dit</span>
              <span><u>V</u>iew</span>
              <span><u>H</u>elp</span>
            </div>

            <div className="p-3 bg-[#c0c0c0] flex-1 flex flex-col justify-between">
              <div>
                <span className="font-display text-[32px] leading-none font-semibold text-blue-900 font-pixel">02</span>
                <h3 className="type-heading text-black font-pixel mt-1">It looks at the desktop</h3>
                <p className="mt-2 text-[11px] text-zinc-700 leading-normal">
                  Metis captures the complete virtual desktop (all screens and monitors, preserving scaling) and sends a compressed snapshot with your request.
                </p>
              </div>

              {/* Folder Content Grid showing image5 */}
              <div className="win95-field bg-white my-3 flex-1 flex flex-col overflow-hidden">
                <div className="bg-zinc-100 border-b border-zinc-300 p-1 flex justify-between items-center text-[9px] text-zinc-600">
                  <span>Directory: C:\Users\metis\Downloads</span>
                  <span>1 object(s) selected</span>
                </div>
                <div className="flex-1 p-2 flex gap-3 items-center bg-[#808080]/10">
                  <div className="w-[140px] flex flex-col items-center gap-1">
                    <span className="text-[28px]">📁</span>
                    <span className="font-bold text-[10px] text-center">captures</span>
                    <span className="text-[9px] text-zinc-500">desktop_active.jpg</span>
                  </div>
                  <div className="flex-1 border border-zinc-400 p-0.5 bg-white">
                    <img 
                      src="/image5.jpg" 
                      alt="Virtual Desktop Capture" 
                      className="w-full h-24 object-cover"
                    />
                  </div>
                </div>
              </div>

              <div className="text-[10px] text-zinc-600 bg-zinc-100 p-1.5 border border-zinc-300">
                Preserves original bounds across multi-monitor setups.
              </div>
            </div>
          </article>

          {/* Step 3: Windows Picture and Fax Viewer */}
          <article className="win95-window shrink-0 flex flex-col justify-between shadow-[4px_4px_0_#000] text-black md:h-[450px] md:w-[480px] p-1">
            <div className="win95-titlebar">
              <span>metis_guidance.jpg - Windows Picture and Fax Viewer</span>
              <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><X /></button>
            </div>

            <div className="p-3 bg-[#c0c0c0] flex-1 flex flex-col justify-between">
              <div>
                <span className="font-display text-[32px] leading-none font-semibold text-blue-900 font-pixel">03</span>
                <h3 className="type-heading text-black font-pixel mt-1">It shows you where to look</h3>
                <p className="mt-2 text-[11px] text-zinc-700 leading-normal">
                  Metis draws a temporary click-through overlay (arrows, highlights, numbering) directly over controls on your real screen, fading out automatically.
                </p>
              </div>

              {/* Fax Viewer Canvas showing image8 */}
              <div className="win95-field bg-white my-3 flex-1 flex flex-col overflow-hidden items-center justify-center p-1.5 relative">
                <img 
                  src="/image8.jpg" 
                  alt="Visual guidance overlay preview" 
                  className="w-full h-[120px] object-cover border border-zinc-200"
                />
                
                {/* Photo Viewer Toolbar Controls */}
                <div className="absolute bottom-1 bg-[#dfdfdf] border border-white shadow px-2 py-0.5 flex gap-2 rounded text-[10px] text-zinc-700">
                  <button className="hover:text-black"><MagnifyingGlassPlus size={11} weight="bold" /></button>
                  <button className="hover:text-black"><MagnifyingGlassMinus size={11} weight="bold" /></button>
                  <button className="hover:text-black">↺</button>
                  <button className="hover:text-black">🖨️</button>
                  <button className="hover:text-black"><FloppyDisk size={11} /></button>
                  <button className="hover:text-black"><Trash size={11} /></button>
                </div>
              </div>

              <div className="text-[10px] text-zinc-600 bg-zinc-100 p-1.5 border border-zinc-300">
                Safe Mode active: Metis never clicks or types. Pointer stays yours!
              </div>
            </div>
          </article>

          {/* Tail spacer so the last card can clear the right edge. */}
          <div className="hidden shrink-0 md:block md:w-[8vw]" aria-hidden="true" />
        </div>
      </div>
    </section>
  );
}
