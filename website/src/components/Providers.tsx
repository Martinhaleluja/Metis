import { providerIcons } from "../lib/providerIcons";
import { Reveal } from "./Reveal";

/**
 * Who is actually answering, split by who pays for the answer.
 *
 * The split is the point of the section. On Free and Plus, Metis picks the
 * model and pays the bill, so the list is short and it is ours to change. On
 * Pro you connect your own account and the bill is yours, so the list is long
 * and the choice is yours. Presenting them as one undifferentiated logo wall
 * would hide the only thing a reader needs to understand.
 */

type Mark = { slug: string | null; name: string; note: string };

const managed: Mark[] = [
  {
    slug: "googlegemini",
    name: "Google Gemini",
    note: "The managed model today. Free gets Flash-Lite in text; Plus gets vision-capable models.",
  },
];

const bringYourOwn: Mark[] = [
  { slug: null, name: "OpenAI", note: "GPT models on your own OpenAI account." },
  { slug: "anthropic", name: "Anthropic", note: "Claude models on your own Anthropic account." },
  { slug: "googlegemini", name: "Google Gemini", note: "Your own Google AI Studio key." },
  { slug: "mistralai", name: "Mistral", note: "Your own Mistral account." },
  { slug: "openrouter", name: "OpenRouter", note: "One key, routed to whichever model you pick." },
];

function ProviderMark({ mark }: { mark: Mark }) {
  const icon = mark.slug ? providerIcons[mark.slug] : undefined;

  return (
    <li
      className="win95-field flex items-start gap-3 p-3"
      style={{ fontFamily: "var(--font-system)" }}
    >
      <span className="grid h-9 w-9 shrink-0 place-items-center border border-[#808080] bg-[#c0c0c0]">
        {icon ? (
          <svg viewBox="0 0 24 24" width={18} height={18} aria-hidden="true" fill="#000080">
            <path d={icon.d} />
          </svg>
        ) : (
          // No trademark-safe glyph exists for this one, so the name carries it.
          <span className="text-[10px] font-bold text-[#000080]">AI</span>
        )}
      </span>
      <span>
        <span className="block text-[13px] font-bold text-black">{mark.name}</span>
        <span className="mt-0.5 block text-[11px] leading-snug text-[#444]">{mark.note}</span>
      </span>
    </li>
  );
}

function ProviderWindow({
  title,
  heading,
  blurb,
  marks,
  footer,
  tilt,
}: {
  title: string;
  heading: string;
  blurb: string;
  marks: Mark[];
  footer: string;
  tilt: number;
}) {
  return (
    <div className="win95-window h-full" style={{ transform: `rotate(${tilt}deg)` }}>
      <div className="win95-titlebar">
        <span className="truncate">{title}</span>
        <span className="flex h-3.5 w-4 items-center justify-center border border-white border-r-[#808080] border-b-[#808080] bg-[#c0c0c0] text-[8px] font-bold text-black">
          &times;
        </span>
      </div>

      <div className="bg-[#c0c0c0] p-5" style={{ fontFamily: "var(--font-system)" }}>
        <h3 className="text-[15px] font-bold text-black">{heading}</h3>
        <p className="mt-1.5 text-[12px] leading-relaxed text-[#333]">{blurb}</p>

        <ul className="mt-4 space-y-2">
          {marks.map((mark) => (
            <ProviderMark key={`${mark.name}-${mark.note}`} mark={mark} />
          ))}
        </ul>

        <p className="mt-4 border-t border-[#808080] pt-3 text-[11px] leading-snug text-[#333]">
          {footer}
        </p>
      </div>
    </div>
  );
}

export function Providers() {
  return (
    <section id="providers" className="scroll-mt-16 py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="mx-auto max-w-[26ch] text-center type-title text-ink">
            Two ways to get an answer
          </h2>
          <p className="mx-auto mt-4 max-w-[60ch] text-center type-body text-ink-muted">
            Either Metis buys the AI for you, or you bring an account you already
            have. The difference decides who gets the bill and whose servers your
            question passes through.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-8 lg:grid-cols-2">
          <Reveal>
            <ProviderWindow
              title="managed_ai.cfg"
              heading="Metis-managed AI"
              blurb="Free and Plus. You do not sign up for anything else and you never see a token bill — the request goes to Metis's gateway, which calls the provider on Metis's key."
              marks={managed}
              footer="One provider today, chosen for cost. Which model runs behind a plan can change without notice; what the plan is allowed to do cannot."
              tilt={-0.8}
            />
          </Reveal>

          <Reveal delay={0.08}>
            <ProviderWindow
              title="bring_your_own.cfg"
              heading="Bring your own — Pro"
              blurb="Connect an account you already pay for. Metis stops being the buyer and becomes the interface: you choose the provider and the exact model, per request."
              marks={bringYourOwn}
              footer="Your provider bills you for model usage, separately from the $29. Metis charges for the software, not the tokens."
              tilt={0.7}
            />
          </Reveal>
        </div>

        <Reveal delay={0.16}>
          <p className="mx-auto mt-10 max-w-[70ch] text-center type-caption text-ink-muted">
            There is a third way that costs nothing: paste your own API key into the
            desktop app on any plan, including Free while signed out. Metis calls the
            provider directly from your machine, keeps full screen vision and full
            automation, and no request ever reaches a Metis server. A local model
            through Ollama works the same way, without leaving the machine at all.
          </p>
        </Reveal>
      </div>
    </section>
  );
}
