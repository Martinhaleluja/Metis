import { EyeIcon as Eye } from "@phosphor-icons/react/dist/icons/Eye";
import { MicrophoneIcon as Microphone } from "@phosphor-icons/react/dist/icons/Microphone";
import { SpeakerHighIcon as SpeakerHigh } from "@phosphor-icons/react/dist/icons/SpeakerHigh";
import { HardDrivesIcon as HardDrives } from "@phosphor-icons/react/dist/icons/HardDrives";
import { PencilLine } from "@phosphor-icons/react/dist/icons/PencilLine";
import { UserCircle } from "@phosphor-icons/react/dist/icons/UserCircle";
import { Reveal } from "./Reveal";

/**
 * The six things Metis does, said the way someone would say them out loud.
 *
 * The window titles used to be .exe, .dll and .sys. On a product whose whole
 * pitch is "it looks at your screen", a card labelled screen_capture.exe beside
 * two files named like drivers reads as malware rather than as a joke about
 * Windows 95. The retro chrome stays — it is the brand — but the filenames are
 * ordinary documents now.
 *
 * The bodies lost their jargon for the same reason. "Key chord" is a phrase from
 * editor documentation. "That one turn" is what the codebase calls a request and
 * means nothing to anybody else. Naming ElevenLabs and Ollama in a feature card
 * hands the reader two more companies to evaluate before they have decided
 * whether they want this one.
 */
const features = [
  {
    icon: Eye,
    title: "It sees what you see",
    body: "Ask about anything on your screen. Metis looks once, when you ask, and answers about what is actually in front of you.",
    tilt: -1.2,
  },
  {
    icon: Microphone,
    title: "Talk to it, or type",
    body: "Hold a keyboard shortcut from anywhere in Windows and speak, or just say its name. Type instead if you would rather.",
    shortcut: ["Ctrl", "Shift", "1"],
    tilt: 0.8,
  },
  {
    icon: SpeakerHigh,
    title: "It answers out loud",
    body: "You hear the answer while your eyes stay on the work. Turn it off and read instead — some people prefer that.",
    tilt: 1.5,
  },
  {
    icon: PencilLine,
    title: "It draws on your screen",
    body: "Arrows and highlights appear over the buttons you need, then fade. It shows you where to click. It never clicks for you.",
    tilt: -0.7,
  },
  {
    icon: UserCircle,
    title: "Always one word away",
    body: "Metis lives in a small bar at the top of your screen. It stays out of the way until you ask, and it remembers your name.",
    tilt: 0.5,
  },
  {
    icon: HardDrives,
    title: "It can run with no internet",
    body: "Point Metis at a model running on your own computer and nothing you say or show it ever leaves the machine.",
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
                className="card h-full transition-transform hover:scale-[1.02]"
              >
                <div className="p-5 bg-surface">
                  <div className="flex items-start gap-4">
                    <span className="shrink-0 grid h-10 w-10 place-items-center rounded-xl bg-accent-wash border border-line">
                      <feat.icon
                        size={20}
                        weight="bold"
                        className="text-accent"
                      />
                    </span>
                    <div>
                      <h3
                        className="text-[14px] font-bold text-ink"
                      >
                        {feat.title}
                      </h3>
                      <p
                        className="mt-1.5 text-[12px] text-[#333] leading-relaxed"
                      >
                        {feat.body}
                      </p>
                      {feat.shortcut && (
                        <div className="mt-3 flex items-center gap-1">
                          {feat.shortcut.map((key) => (
                            <kbd
                              key={key}
                              className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] text-[10px] !py-0.5 !px-2 !cursor-default"
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
