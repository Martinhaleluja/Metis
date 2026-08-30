import { useState } from "react";
import { plans, priceLabel } from "../lib/plans";
import { Reveal } from "./Reveal";

/**
 * The awkward questions, answered before anyone has to ask them.
 *
 * Built on <details>/<summary> so it still opens with JavaScript disabled and
 * so the browser handles the keyboard and screen-reader semantics; the state
 * hook only exists to swap the [+]/[-] glyph, which is the Win95 part.
 *
 * The prices are interpolated rather than typed, so this file cannot drift away
 * from the pricing cards.
 */

const plus = plans[1];
const pro = plans[2];

const faqs: { q: string; a: React.ReactNode }[] = [
  {
    q: "What am I actually paying for?",
    a: (
      <>
        The software, and the AI Metis buys on your behalf. Plus is{" "}
        {priceLabel(plus)} a month and includes a monthly allowance of managed AI,
        including screen vision. Pro is {priceLabel(pro)} a month and includes the
        same managed AI plus the ability to connect your own provider account.
        Nothing is charged during early access.
      </>
    ),
  },
  {
    q: "Do the plans limit what Metis can do on my own API key?",
    a: (
      <>
        No, and this is the part worth reading twice. The plans meter what Metis
        pays for. If you paste your own API key into the desktop app, the request
        goes from your machine straight to that provider and never touches a Metis
        server &mdash; so there is nothing for us to meter. You keep full screen
        vision and full automation on Free, signed out, indefinitely. A local model
        through Ollama works the same way.
      </>
    ),
  },
  {
    q: "What does Bring Your Own AI mean, exactly?",
    a: (
      <>
        On Pro you connect an account you already have with OpenAI, Anthropic,
        Google Gemini, Mistral or OpenRouter, and Metis drives it for you. You keep
        full control of which provider and which model answers each request.
      </>
    ),
  },
  {
    q: "So does my provider charge me on top of the Pro subscription?",
    a: (
      <>
        Yes, and that is by design. The {priceLabel(pro)} goes to Metis for the
        software platform. The model usage on your connected account is billed to
        you by that provider, separately, at their published rates. Metis does not
        mark it up, does not resell it, and does not see the invoice. Every request
        shows up in your provider&rsquo;s own dashboard.
      </>
    ),
  },
  {
    q: "Where does my screen actually go?",
    a: (
      <>
        It depends on which AI answered. On managed Free and Plus, the screenshot
        and your question go to Metis&rsquo;s gateway, which forwards them to the
        provider on Metis&rsquo;s key. On Pro with a connected account, and on any
        plan using your own API key in the desktop app, the request goes to the
        provider and Metis&rsquo;s servers are not in the path. Either way, windows
        that mark themselves protected &mdash; banking apps, password managers,
        view-once media &mdash; are blacked out before the image is encoded, and
        password fields are never read.
      </>
    ),
  },
  {
    q: "How is my API key stored?",
    a: (
      <>
        If you paste it into the desktop app, it goes into Windows Credential
        Manager and never leaves your machine. If you connect it through the
        account page on Pro, the browser posts it once over TLS to Metis&rsquo;s
        gateway, which stores it encrypted &mdash; never in plain text, never in a
        log. Nothing can read it back afterwards: the page only ever shows a
        four-character hint. It is never kept in the browser.
      </>
    ),
  },
  {
    q: "What happens when I hit a usage limit?",
    a: (
      <>
        Metis tells you, and managed AI stops answering until the next monthly
        period. Nothing is charged over the plan price, because the limit exists to
        stop that happening &mdash; there is no overage bill. Your own API key keeps
        working normally, since that allowance is between you and your provider.
      </>
    ),
  },
  {
    q: "Can I cancel?",
    a: (
      <>
        Whenever you like, from the account page, with no phone call and no
        retention offer. You keep the paid features until the end of the period you
        already paid for, then the account drops to Free. Your chats, memory and
        settings are on your own machine and are not touched by any of this.
      </>
    ),
  },
  {
    q: "Is Metis going to click things for me?",
    a: (
      <>
        The assistant will not. It draws arrows and highlights on your screen and
        explains what it sees, and it structurally cannot move your mouse or type
        for you. Background agents are a separate, opt-in feature that does run
        tasks &mdash; confined to their own folder, and stopped for approval before
        anything destructive.
      </>
    ),
  },
];

export function Faq() {
  const [open, setOpen] = useState<number | null>(0);

  return (
    <section id="faq" className="scroll-mt-16 py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="mx-auto max-w-[24ch] text-center type-title text-ink">
            Questions worth asking first
          </h2>
        </Reveal>

        <div className="mx-auto mt-12 max-w-[780px]">
          <Reveal delay={0.06}>
            <div className="win95-window">
              <div className="win95-titlebar">
                <span>help_topics.hlp</span>
                <span className="flex h-3.5 w-4 items-center justify-center border border-white border-r-[#808080] border-b-[#808080] bg-[#c0c0c0] text-[8px] font-bold text-black">
                  ?
                </span>
              </div>

              <div className="bg-[#c0c0c0] p-3" style={{ fontFamily: "var(--font-system)" }}>
                <div className="win95-field divide-y divide-[#d4d0c8] p-1">
                  {faqs.map((faq, index) => (
                    <details
                      key={faq.q}
                      open={open === index}
                      onToggle={(event) => {
                        const el = event.currentTarget;
                        setOpen((current) =>
                          el.open ? index : current === index ? null : current,
                        );
                      }}
                      className="group"
                    >
                      <summary className="flex cursor-pointer list-none items-start gap-2.5 px-3 py-2.5 text-[13px] font-bold text-black hover:bg-[#000080] hover:text-white [&::-webkit-details-marker]:hidden">
                        <span
                          aria-hidden="true"
                          className="mt-[1px] shrink-0 font-mono text-[12px] text-[#000080] group-hover:text-white group-open:text-[#000080]"
                        >
                          {open === index ? "[-]" : "[+]"}
                        </span>
                        <span>{faq.q}</span>
                      </summary>
                      <div className="px-3 pt-0.5 pb-4 pl-[38px] text-[12px] leading-relaxed text-[#222]">
                        {faq.a}
                      </div>
                    </details>
                  ))}
                </div>
              </div>
            </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
