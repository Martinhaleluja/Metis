import { EyeIcon as Eye } from "@phosphor-icons/react/dist/icons/Eye";
import { HardDrivesIcon as HardDrives } from "@phosphor-icons/react/dist/icons/HardDrives";
import { MicrophoneIcon as Microphone } from "@phosphor-icons/react/dist/icons/Microphone";
import { SpeakerHighIcon as SpeakerHigh } from "@phosphor-icons/react/dist/icons/SpeakerHigh";
import { WaveformIcon as Waveform } from "@phosphor-icons/react/dist/icons/Waveform";
import { Reveal } from "./Reveal";

export function Capabilities() {
  return (
    <section id="capabilities" className="scroll-mt-24 py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="max-w-[18ch] type-title text-ink">
            Built for the desktop, not a browser tab
          </h2>
        </Reveal>

        <div className="mt-12 grid gap-4 lg:grid-cols-6">
          {/* Wide cell, carries the visual weight for the whole grid. */}
          <Reveal className="lg:col-span-4">
            <article
              className="relative flex h-full min-h-[280px] flex-col justify-between overflow-hidden rounded-[20px] border border-line p-7"
              style={{
                background:
                  "linear-gradient(140deg, color-mix(in srgb, var(--sky) 26%, var(--surface)) 0%, var(--surface) 58%)",
              }}
            >
              <div className="relative max-w-[34ch]">
                <span className="mb-4 inline-grid h-9 w-9 place-items-center rounded-full bg-surface text-accent shadow-[var(--shadow-card)]">
                  <Eye size={17} weight="bold" />
                </span>
                <h3 className="type-heading text-ink">
                  It can see what you are looking at
                </h3>
                <p className="mt-2 type-caption text-ink-muted">
                  Ask about the thing on your screen and Metis captures the whole desktop for
                  that one turn, then answers about what is actually there.
                </p>
              </div>

              <img
                src="/metis-mark.png"
                alt=""
                width={256}
                height={256}
                loading="lazy"
                decoding="async"
                className="pointer-events-none absolute -right-10 -bottom-12 w-[210px] opacity-25 motion-safe:animate-drift"
              />
            </article>
          </Reveal>

          <Reveal delay={0.06} className="lg:col-span-2">
            <article className="flex h-full min-h-[280px] flex-col justify-between rounded-[20px] border border-line bg-surface p-7">
              <div>
                <span className="mb-4 inline-grid h-9 w-9 place-items-center rounded-full bg-accent-wash text-accent">
                  <Microphone size={17} weight="bold" />
                </span>
                <h3 className="type-heading text-ink">Push to talk</h3>
                <p className="mt-2 type-caption text-ink-muted">
                  Hold one chord from anywhere in Windows and speak.
                </p>
              </div>

              <div className="mt-6 flex items-center gap-1.5">
                {["Ctrl", "Shift", "1"].map((key) => (
                  <kbd
                    key={key}
                    className="rounded-lg border border-line bg-surface-sunken px-2.5 py-1.5 font-sans text-[12px] font-semibold text-ink-muted"
                  >
                    {key}
                  </kbd>
                ))}
              </div>
            </article>
          </Reveal>

          <Reveal delay={0.04} className="lg:col-span-2">
            <article className="flex h-full min-h-[220px] flex-col justify-between rounded-[20px] border border-line bg-accent-wash p-7">
              <div>
                <span className="mb-4 inline-grid h-9 w-9 place-items-center rounded-full bg-surface text-accent">
                  <Waveform size={17} weight="bold" />
                </span>
                <h3 className="type-heading text-ink">A wake word</h3>
                <p className="mt-2 type-caption text-ink-muted">
                  Leave your hands where they are and just say the word.
                </p>
              </div>

              <div className="mt-6 flex h-6 items-end gap-[3px]" aria-hidden="true">
                {[10, 18, 8, 22, 14, 20, 9, 16].map((height, index) => (
                  <span
                    key={index}
                    className="w-[3px] rounded-full bg-accent/70 motion-safe:animate-[metis-halo_1.5s_ease-in-out_infinite]"
                    style={{ height, animationDelay: `${index * 0.1}s` }}
                  />
                ))}
              </div>
            </article>
          </Reveal>

          <Reveal delay={0.08} className="lg:col-span-2">
            <article className="h-full min-h-[220px] rounded-[20px] border border-line bg-surface p-7">
              <span className="mb-4 inline-grid h-9 w-9 place-items-center rounded-full bg-accent-wash text-accent">
                <HardDrives size={17} weight="bold" />
              </span>
              <h3 className="type-heading text-ink">
                Or no cloud at all
              </h3>
              <p className="mt-2 type-caption text-ink-muted">
                Point Metis at a model running on your own machine through Ollama and nothing
                leaves the desk.
              </p>
            </article>
          </Reveal>

          <Reveal delay={0.12} className="lg:col-span-2">
            <article className="h-full min-h-[220px] rounded-[20px] border border-line bg-surface p-7">
              <span className="mb-4 inline-grid h-9 w-9 place-items-center rounded-full bg-accent-wash text-accent">
                <SpeakerHigh size={17} weight="bold" />
              </span>
              <h3 className="type-heading text-ink">
                It answers out loud
              </h3>
              <p className="mt-2 type-caption text-ink-muted">
                A spoken reply you can listen to while you keep working, or silence if you
                would rather read.
              </p>
            </article>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
