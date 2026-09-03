import { planById, priceLabel } from "../lib/plans";
import { Reveal } from "./Reveal";

/**
 * Using your own AI account, as an XP wizard.
 *
 * Two invoices for one thing is the most confusing part of this product, so it
 * gets a section of its own that says the number out loud rather than a footnote
 * under a price.
 *
 * The plan being described is Max. The step below said "Pro" while quoting Max's
 * price from `planById.max`, which is the worst of both: the name a reader
 * shops by and the number they would be charged, disagreeing in the same
 * sentence. Both come from the plan now, so they cannot part company again.
 *
 * The security list used to be a security review: TLS, local storage, URLs,
 * plain text, logs, audit records, and a rendered sample of a key hint. All
 * true, all the wrong altitude — a reader who needs reassurance is not reassured
 * by a checklist they cannot evaluate, and a reader who does not need it has
 * just been handed four new things to worry about. It says what happens to the
 * key in the terms someone would ask the question in.
 */

const steps = [
  {
    n: "1",
    title: `You subscribe to ${planById.max.name}`,
    body: `${priceLabel(planById.max)} a month, paid to Metis. That buys the software — the app, the drawing, the background agents, the memory, and the account that ties them together.`,
  },
  {
    n: "2",
    title: "You connect a provider account",
    body: "OpenAI, Anthropic, Google Gemini, Mistral or OpenRouter. You paste a key from their website, and Metis checks it works before saving it.",
  },
  {
    n: "3",
    title: "Your provider bills you for the models",
    body: "Separately, on their own invoice, at their own rates. Metis never adds anything on top — it is your account, and you can see every request on their own website.",
  },
  {
    n: "4",
    title: "You pick the model, per request",
    body: "No monthly allowance from us and no model picked on your behalf. The limits on the other plans exist because we are paying; on your own account, they do not apply.",
  },
];

export function Byoa() {
  return (
    <section id="byoa" className="scroll-mt-16 bg-surface-sunken py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="mx-auto max-w-[26ch] text-center type-title text-ink">
            Use your own AI account
          </h2>
          <p className="mx-auto mt-4 max-w-[58ch] text-center type-body text-ink-muted">
            {planById.max.name} is two bills rather than one. Better to know
            which is which now than to find out on a statement.
          </p>
        </Reveal>

        <div className="mx-auto mt-12 max-w-[760px]">
          <Reveal delay={0.06}>
            <div className="card" style={{ transform: "rotate(-0.5deg)" }}>
              <div className="panel-title">
                <span className="text-[12px]">Using your own AI account</span>
                <button className="xp-button-close" aria-hidden="true" tabIndex={-1}>
                  &times;
                </button>
              </div>

              <div className="bg-surface-sunken p-6">
                <ol className="space-y-5">
                  {steps.map((step) => (
                    <li key={step.n} className="flex gap-4">
                      <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-[#003ca5] text-[12px] font-bold text-white">
                        {step.n}
                      </span>
                      <div>
                        <h3
                          className="text-[14px] font-bold text-accent"
                        >
                          {step.title}
                        </h3>
                        <p className="mt-1 text-[12px] leading-relaxed text-[#333]">{step.body}</p>
                      </div>
                    </li>
                  ))}
                </ol>

                <div className="mt-6 border-t border-line pt-5">
                  <h3
                    className="text-[14px] font-bold text-accent"
                  >
                    What happens to your key
                  </h3>
                  <ul className="mt-2 space-y-2 text-[12px] leading-relaxed text-[#333]">
                    <li>
                      &bull; It is encrypted the moment it arrives, and it is never
                      shown again &mdash; not on your account page, not in the app,
                      not to us.
                    </li>
                    <li>
                      &bull; All you ever see afterwards is the last four
                      characters, which is enough to tell one key from another.
                    </li>
                    <li>
                      &bull; Disconnecting deletes it properly. And you can always
                      cancel the key at your provider instead &mdash; it is your
                      account, and that ends it whatever we do.
                    </li>
                  </ul>
                </div>
              </div>

              <div className="flex justify-end gap-2 border-t border-line bg-surface-sunken px-4 py-3">
                <a href="#pricing" className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] text-[11px] no-underline">
                  Back to plans
                </a>
                <a href="#faq" className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] text-[11px] no-underline">
                  Read the FAQ &gt;
                </a>
              </div>
            </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
