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
    tone: "brand",
  },
  {
    icon: Microphone,
    title: "Talk to it, or type",
    body: "Hold a keyboard shortcut from anywhere in Windows and speak, or just say its name. Type instead if you would rather.",
    shortcut: ["Ctrl", "Shift", "1"],
    tone: "sun",
  },
  {
    icon: SpeakerHigh,
    title: "It answers out loud",
    body: "You hear the answer while your eyes stay on the work. Turn it off and read instead — some people prefer that.",
    tone: "blush",
  },
  {
    icon: PencilLine,
    title: "It draws on your screen",
    body: "Arrows and highlights appear over the buttons you need, then fade. It shows you where to click. It never clicks for you.",
    tone: "leaf",
  },
  {
    icon: UserCircle,
    title: "Always one word away",
    body: "Metis lives in a small bar at the top of your screen. It stays out of the way until you ask, and it remembers your name.",
    tone: "wave",
  },
  {
    icon: HardDrives,
    title: "It can run with no internet",
    body: "Point Metis at a model running on your own computer and nothing you say or show it ever leaves the machine.",
    tone: "grape",
  },
];

/**
 * The six colours are a wayfinding device, not decoration: six identical cards
 * read as one undifferentiated list, and these are six genuinely different
 * things. Colour is never the only signal — each card also carries its own icon
 * and its own heading — so the grouping survives greyscale, colour blindness
 * and a screen reader.
 */
const tones: Record<string, { tile: string; text: string; card: string }> = {
  brand: { tile: "bg-brand-soft", text: "text-brand", card: "bg-brand-soft/40" },
  sun: { tile: "bg-sun-soft", text: "text-sun", card: "bg-sun-soft/40" },
  blush: { tile: "bg-blush-soft", text: "text-blush", card: "bg-blush-soft/40" },
  leaf: { tile: "bg-leaf-soft", text: "text-leaf", card: "bg-leaf-soft/40" },
  wave: { tile: "bg-wave-soft", text: "text-wave", card: "bg-wave-soft/40" },
  grape: { tile: "bg-grape-soft", text: "text-grape", card: "bg-grape-soft/40" },
};

export function Capabilities() {
  return (
    <section id="capabilities" className="scroll-mt-16 py-20 sm:py-28 bg-page">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <p className="pill bg-accent-wash text-accent mx-auto w-fit">
            What it does
          </p>
          <h2 className="mt-4 type-title text-ink text-center max-w-[24ch] mx-auto">
            Built for the desktop, not a browser tab
          </h2>
          <p className="mt-4 type-body text-ink-muted text-center max-w-[56ch] mx-auto">
            Metis teaches while you work. It draws on your screen, speaks out
            loud, and never takes the controls &mdash; the skill it leaves
            behind is yours.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {features.map((feat, i) => {
            const tone = tones[feat.tone];
            return (
              <Reveal key={feat.title} delay={i * 0.06}>
                <div className={`tint tint-hover h-full p-6 ${tone.card}`}>
                  <span className={`tile ${tone.tile}`}>
                    <feat.icon size={24} weight="bold" className={tone.text} />
                  </span>
                  <h3 className="mt-5 type-heading text-ink">{feat.title}</h3>
                  <p className="mt-2 type-caption text-ink-muted">{feat.body}</p>
                  {feat.shortcut && (
                    <div className="mt-4 flex items-center gap-1.5">
                      {feat.shortcut.map((key) => (
                        <kbd
                          key={key}
                          className="rounded-lg border border-line bg-surface px-2 py-1 font-sans text-[14px] font-semibold text-ink shadow-sm"
                        >
                          {key}
                        </kbd>
                      ))}
                    </div>
                  )}
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}
