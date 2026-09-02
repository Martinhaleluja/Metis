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

const pro = plans[1];
const max = plans[2];

const faqs: { q: string; a: React.ReactNode }[] = [
  {
    q: "What am I paying for?",
    a: (
      <>
        The app, and the AI that answers you. Free includes 50 talk messages a
        month, plenty of dictation and 10 agent messages. {pro.name} is{" "}
        {priceLabel(pro)} a month and takes the limit off talking and dictating
        entirely. {max.name} is {priceLabel(max)} a month and also lets you
        connect an AI account of your own. Nothing is being charged yet — Metis
        is still in early access.
      </>
    ),
  },
  {
    q: "What is the free plan actually like?",
    a: (
      <>
        The whole app, with a hundred and twenty questions a month on us. Metis
        can look at your screen, draw on it, speak, and remember what you are
        working on. The paid plans lift the limits rather than unlocking the
        features.
      </>
    ),
  },
  {
    q: "Does my own AI account cost extra on Pro?",
    a: (
      <>
        Yes, and it is worth being clear about it. The {priceLabel(pro)} goes to
        Metis for the software. What the models cost is billed to you by your own
        provider, on their own invoice, at their own rates. Metis adds nothing on
        top and never sees that bill — every request shows up on their website,
        under your account.
      </>
    ),
  },
  {
    q: "Where does my screen go?",
    a: (
      <>
        To whichever AI answers you. On the AI included with your plan that
        passes through Metis on the way, because we are the ones paying for the
        answer; we keep a note of what it cost and nothing else. With your own AI account, or a model running
        on your own computer, it goes straight there and our servers are not
        involved. Either way, windows that mark themselves private — banking apps,
        password managers, view-once photos — are blacked out first, and password
        boxes are never read.
      </>
    ),
  },
  {
    q: "What happens to my AI key if I connect one?",
    a: (
      <>
        It is encrypted the moment it arrives and never shown again, to you or to
        us. All you see afterwards is the last four characters. Disconnecting
        deletes it, and you can always cancel the key at your provider instead —
        it is your account, and that ends it whatever we do.
      </>
    ),
  },
  {
    q: "What happens when I run out for the month?",
    a: (
      <>
        Metis tells you, and waits until the next month. Nothing is ever charged
        above the plan price — the limit exists so that cannot happen, and there
        is no overage bill.
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
                <span>questions.txt &mdash; Notepad</span>
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
