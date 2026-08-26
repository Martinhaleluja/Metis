import { EyeIcon as Eye } from "@phosphor-icons/react/dist/icons/Eye";
import { MicrophoneIcon as Microphone } from "@phosphor-icons/react/dist/icons/Microphone";
import { SpeakerHighIcon as SpeakerHigh } from "@phosphor-icons/react/dist/icons/SpeakerHigh";
import { HardDrivesIcon as HardDrives } from "@phosphor-icons/react/dist/icons/HardDrives";
import { PencilLine } from "@phosphor-icons/react/dist/icons/PencilLine";
import { UserCircle } from "@phosphor-icons/react/dist/icons/UserCircle";
import { Reveal } from "./Reveal";

const features = [
  {
    icon: Eye,
    winTitle: "screen_capture.exe",
    title: "It sees what you see",
    body: "Ask about anything on screen. Metis captures the full desktop for that one turn, then answers about what's actually there.",
    tilt: -1.2,
  },
  {
    icon: Microphone,
    winTitle: "push_to_talk.dll",
    title: "Push to talk — or just say its name",
    body: "Hold a key chord from anywhere in Windows, or say the wake word and leave your hands where they are. Metis listens either way.",
    shortcut: ["Ctrl", "Shift", "1"],
    tilt: 0.8,
  },
  {
    icon: SpeakerHigh,
    winTitle: "speech_engine.sys",
    title: "It answers out loud",
    body: "Metis speaks back through ElevenLabs or a local voice. You hear the answer while your eyes stay on the work.",
    tilt: 1.5,
  },
  {
    icon: PencilLine,
    winTitle: "guidance_overlay.dll",
    title: "It draws on your screen",
    body: "Arrows, highlights, and labels appear directly over the controls you need — then fade away. Metis teaches by pointing, not by taking over.",
    tilt: -0.7,
  },
  {
    icon: UserCircle,
    winTitle: "preferred_name.cfg",
    title: "It knows your name",
    body: "Set a preferred name and Metis calls you by it. The notch bar sits at the top of your screen — always one click or word away.",
    tilt: 0.5,
  },
  {
    icon: HardDrives,
    winTitle: "offline_mode.cfg",
    title: "No cloud needed",
    body: "Point it at Ollama running locally and the whole loop — voice, vision, speech — stays on your machine.",
    tilt: -1.0,
  },
];

export function Capabilities() {
  return (
    <section id="capabilities" className="scroll-mt-16 py-20 sm:py-28 bg-surface">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="type-title text-ink text-center max-w-[24ch] mx-auto">
            Built for the desktop, not a browser tab
          </h2>
          <p className="mt-4 type-body text-ink-muted text-center max-w-[56ch] mx-auto">
            Metis teaches while you work. It draws on your screen, speaks out
            loud, and never takes the controls &mdash; the skill it leaves
            behind is yours.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {features.map((feat, i) => (
            <Reveal key={feat.title} delay={i * 0.06}>
              <div
                className="win95-window h-full transition-transform hover:scale-[1.02]"
                style={{ transform: `rotate(${feat.tilt}deg)` }}
              >
                <div className="win95-titlebar">
                  <span className="truncate">{feat.winTitle}</span>
                  <div className="flex gap-[2px]">
                    <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
                      _
                    </span>
                    <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
                      &times;
                    </span>
                  </div>
                </div>
                <div className="p-5 bg-[#c0c0c0]">
                  <div className="flex items-start gap-4">
                    <span className="shrink-0 grid h-10 w-10 place-items-center rounded bg-white border border-[#808080] shadow-[inset_1px_1px_0_#dfdfdf]">
                      <feat.icon
                        size={20}
                        weight="bold"
                        className="text-[#000080]"
                      />
                    </span>
                    <div>
                      <h3
                        className="text-[14px] font-bold text-black"
                        style={{ fontFamily: "var(--font-system)" }}
                      >
                        {feat.title}
                      </h3>
                      <p
                        className="mt-1.5 text-[12px] text-[#333] leading-relaxed"
                        style={{ fontFamily: "var(--font-system)" }}
                      >
                        {feat.body}
                      </p>
                      {feat.shortcut && (
                        <div className="mt-3 flex items-center gap-1">
                          {feat.shortcut.map((key) => (
                            <kbd
                              key={key}
                              className="win95-button text-[10px] !py-0.5 !px-2 !cursor-default"
                            >
                              {key}
                            </kbd>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
