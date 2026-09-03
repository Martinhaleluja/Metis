import { planById, priceLabel } from "../lib/plans";
import { providerIcons } from "../lib/providerIcons";
import { Reveal } from "./Reveal";

/**
 * Who is actually answering, split by who pays for the answer.
 *
 * The split is the point of the section. On the included AI, Metis picks the
 * model and pays the bill, so the list is short and it is ours to change. On
 * Max you connect your own account and the bill is yours, so the list is long
 * and the choice is yours. Presenting them as one undifferentiated logo wall
 * would hide the only thing a reader needs to understand.
 */

type Mark = { slug: string | null; name: string; note: string };

const managed: Mark[] = [
  {
    slug: "googlegemini",
    name: "Google Gemini",
    note: "The AI included with every plan. Nothing to sign up for and nothing to configure.",
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
      className="rounded-lg border border-line bg-surface px-3 py-2 flex items-start gap-3 p-3"
    >
      <span className="grid h-9 w-9 shrink-0 place-items-center border border-line bg-surface">
        {icon ? (
          <svg viewBox="0 0 24 24" width={18} height={18} aria-hidden="true" fill="currentColor">
            <path d={icon.d} />
          </svg>
        ) : (
          // No trademark-safe glyph exists for this one, so the name carries it.
          <span className="text-[10px] font-bold text-accent">AI</span>
        )}
      </span>
      <span>
        <span className="block text-[13px] font-bold text-ink">{mark.name}</span>
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
}: {
  title: string;
  heading: string;
  blurb: string;
  marks: Mark[];
  footer: string;
}) {
  return (
    <div className="card h-full">
      <div className="panel-title">
        <span className="truncate">{title}</span>
      </div>

      <div className="bg-surface p-5">
        <h3 className="text-[15px] font-bold text-ink">{heading}</h3>
        <p className="mt-1.5 text-[12px] leading-relaxed text-[#333]">{blurb}</p>

        <ul className="mt-4 space-y-2">
          {marks.map((mark) => (
            <ProviderMark key={`${mark.name}-${mark.note}`} mark={mark} />
          ))}
        </ul>

        <p className="mt-4 border-t border-line pt-3 text-[11px] leading-snug text-[#333]">
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
            have. That is the whole difference between the plans.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-8 lg:grid-cols-2">
          <Reveal>
            <ProviderWindow
              title="The AI included with Metis"
              heading="AI included with your plan"
              blurb="Included with every plan. There is nothing else to sign up for and no second bill — Metis pays for the answers, within the monthly allowance your plan includes."
              marks={managed}
              footer="We may change which model sits behind a plan as better ones arrive. What the plan includes is what we have promised you."
            />
          </Reveal>

          <Reveal delay={0.08}>
            <ProviderWindow
              title="Your own AI account"
              heading={`Your own account — ${planById.max.name}`}
              blurb="Connect an account you already pay for, and choose the exact model yourself. Metis stops buying the AI and simply drives the one you picked."
              marks={bringYourOwn}
              footer={`Your provider charges you for what the models cost, separately from the ${priceLabel(planById.max)}. That ${priceLabel(planById.max)} is for Metis.`}
            />
          </Reveal>
        </div>

        <Reveal delay={0.16}>
          <p className="mx-auto mt-10 max-w-[64ch] text-center type-caption text-ink-muted">
            There is a third option that involves no company at all: run a model on
            your own computer, and every question, picture and answer stays on the
            machine. Metis works that way on any plan.
          </p>
        </Reveal>
      </div>
    </section>
  );
}
