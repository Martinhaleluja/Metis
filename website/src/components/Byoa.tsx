import { Reveal } from "./Reveal";

/**
 * The BYOA billing explainer, as an XP wizard.
 *
 * Two invoices for one thing is the single most confusing part of this product,
 * so it gets a section of its own that says the number out loud instead of a
 * footnote under a price. The security paragraph is here for the same reason:
 * "connect your account" is a sentence people have learned to distrust, and the
 * answer to it is specifics.
 */

const steps = [
  {
    n: "1",
    title: "You subscribe to Pro",
    body: "$29 a month, paid to Metis. That buys the software: the desktop app, the notch bar, the drawing overlay, agents, memory, and the account that ties them together.",
  },
  {
    n: "2",
    title: "You connect a provider account",
    body: "OpenAI, Anthropic, Google Gemini, Mistral or OpenRouter. You paste a key you created on their site, and Metis verifies it with a single throwaway request before saving anything.",
  },
  {
    n: "3",
    title: "Your provider bills you for the models",
    body: "Separately, on their own invoice, at their own rates. Metis never marks that usage up and never resells it — it is your account, and you can see every request in your provider's own dashboard.",
  },
  {
    n: "4",
    title: "You pick the model, per request",
    body: "No managed allowance, no monthly AI budget from us, no model chosen on your behalf. Pro takes the ceiling off because the ceiling was only ever about our bill.",
  },
];

export function Byoa() {
  return (
    <section id="byoa" className="scroll-mt-16 bg-surface-sunken py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="mx-auto max-w-[26ch] text-center type-title text-ink">
            Bring Your Own AI, in plain arithmetic
          </h2>
          <p className="mx-auto mt-4 max-w-[58ch] text-center type-body text-ink-muted">
            Pro is two bills, not one, and it is worth being blunt about which is
            which before you subscribe.
          </p>
        </Reveal>

        <div className="mx-auto mt-12 max-w-[760px]">
          <Reveal delay={0.06}>
            <div className="xp-window" style={{ transform: "rotate(-0.5deg)" }}>
              <div className="xp-titlebar">
                <span className="text-[12px]">Bring Your Own AI &mdash; How billing works</span>
                <button className="xp-button-close" aria-hidden="true" tabIndex={-1}>
                  &times;
                </button>
              </div>

              <div className="bg-[#ece9d8] p-6" style={{ fontFamily: "var(--font-system)" }}>
                <ol className="space-y-5">
                  {steps.map((step) => (
                    <li key={step.n} className="flex gap-4">
                      <span className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-[#003ca5] text-[12px] font-bold text-white">
                        {step.n}
                      </span>
                      <div>
                        <h3
                          className="text-[14px] font-bold text-[#003ca5]"
                          style={{ fontFamily: "Trebuchet MS, sans-serif" }}
                        >
                          {step.title}
                        </h3>
                        <p className="mt-1 text-[12px] leading-relaxed text-[#333]">{step.body}</p>
                      </div>
                    </li>
                  ))}
                </ol>

                <div className="mt-6 border-t border-[#aca899] pt-5">
                  <h3
                    className="text-[14px] font-bold text-[#003ca5]"
                    style={{ fontFamily: "Trebuchet MS, sans-serif" }}
                  >
                    What happens to the key
                  </h3>
                  <ul className="mt-2 space-y-2 text-[12px] leading-relaxed text-[#333]">
                    <li>
                      &bull; The browser posts it once, over TLS, straight to Metis&rsquo;s gateway.
                      It is never written to local storage, never put in a URL, and never
                      echoed back into the page.
                    </li>
                    <li>
                      &bull; The gateway stores it encrypted in a vault your account alone can
                      unlock. It is never kept in plain text, and it is never written to a log
                      or an audit record.
                    </li>
                    <li>
                      &bull; Everything the account page and the desktop app can read back is a
                      four-character hint like <code className="font-mono">&hellip;aB3d</code>,
                      which is enough to tell two keys apart and useless to anyone else.
                    </li>
                    <li>
                      &bull; Disconnecting a provider deletes the stored secret. If you would
                      rather not trust any of that, revoke the key at the provider &mdash; or
                      skip BYOA entirely and paste your key into the desktop app, where it
                      lives in Windows Credential Manager and never leaves the machine.
                    </li>
                  </ul>
                </div>
              </div>

              <div className="flex justify-end gap-2 border-t border-[#aca899] bg-[#ece9d8] px-4 py-3">
                <a href="#pricing" className="xp-button-win text-[11px] no-underline">
                  Back to plans
                </a>
                <a href="#faq" className="xp-button-win text-[11px] no-underline">
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
