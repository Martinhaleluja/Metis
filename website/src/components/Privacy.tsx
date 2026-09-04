import { Link } from "react-router-dom";
import { Reveal } from "./Reveal";

/**
 * Where your screen goes.
 *
 * This section has been rewritten twice, and the reason is worth recording.
 *
 * The first version said keys never leave your machine and there is no server in
 * the middle. That was true when Metis only ran on your own API key, and it
 * stopped being true the moment Metis started buying AI on your behalf — so it
 * had to go.
 *
 * What replaced it was accurate and unreadable: a four-route table in a window
 * titled data_flow.txt, using "gateway", "metering record", "token counts" and
 * "%LOCALAPPDATA%", and volunteering the phrase "Metis is in the middle of that
 * request". Every word of it was true. It was also written by an engineer for an
 * engineer, and the sentence a worried reader would screenshot and post was one
 * we had written about ourselves.
 *
 * This version says the same things in the order a person actually wants them,
 * and in words they already know. The precise version still exists, in the
 * privacy policy, for anyone who wants it.
 */

const answers = [
  {
    question: "When does Metis look at my screen?",
    answer:
      "Only when you ask it something. It takes one picture, answers your question, and that is the end of it. It is not watching in between, and it never records.",
  },
  {
    question: "Where does that picture go?",
    answer:
      "To the AI that answers you. If you are using the AI included with your plan, it passes through Metis on the way — we are the ones paying for the answer, so the request comes through us. We keep a note of what it cost and nothing else: not the picture, not your question, not the answer.",
  },
  {
    question: "Can I keep it off our servers entirely?",
    answer:
      "Yes, two ways. Connect your own AI account and your questions go straight there. Or run a model on your own computer, and nothing leaves the machine at all.",
  },
];

const facts = [
  {
    heading: "Private windows are hidden before anything is sent",
    body: "Banking apps, password managers and view-once photos in WhatsApp and Signal tell Windows not to record them, and Metis blacks them out of the picture. Password boxes are never read. You can add any other app to the list yourself.",
  },
  {
    heading: "Your conversations stay on your computer",
    body: "What you have talked about, and what Metis has learned about your work, are encrypted on your own machine and never uploaded. One button in settings deletes all of it.",
  },
  {
    heading: "Keys are never stored as readable text",
    body: "If you connect your own AI account, the key is encrypted and never sent back to your browser — not even to you. All you ever see again is the last four characters, so you can tell one from another.",
  },
  {
    heading: "It shows you. It cannot touch anything",
    body: "Metis draws arrows and highlights on your screen, but it cannot click, type, or move your mouse. That is built into how it works, not a rule it has been asked to follow.",
  },
];

export function Privacy() {
  return (
    <section id="privacy" className="scroll-mt-24 bg-surface-sunken py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="max-w-[22ch] type-title text-ink">
            It looks at your screen. Here is exactly what that means.
          </h2>
          <p className="mt-5 max-w-[60ch] type-body text-ink-muted">
            Three questions worth asking about anything that can see your desktop,
            answered plainly.
          </p>
        </Reveal>

        <Reveal delay={0.06}>
          <div className="mt-10 card">
            <div className="panel-title">
              <span>Your privacy</span>
            </div>

            <div className="bg-surface p-3">
              <div
                className="rounded-lg border border-line bg-surface px-3 py-2 p-5"
              >
                <ul className="divide-y divide-line">
                  {answers.map((entry) => (
                    <li key={entry.question} className="py-4 first:pt-0 last:pb-0">
                      <h3 className="text-[14px] font-bold text-ink">{entry.question}</h3>
                      <p className="mt-1.5 max-w-[80ch] text-[14px] leading-relaxed text-ink-muted">
                        {entry.answer}
                      </p>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          </div>
        </Reveal>

        <div className="mt-12 grid gap-x-14 gap-y-10 sm:grid-cols-2">
          {facts.map((fact, index) => (
            <Reveal key={fact.heading} delay={index * 0.05}>
              <div className="border-t border-line pt-5">
                <h3 className="type-heading text-ink">{fact.heading}</h3>
                <p className="mt-2 max-w-[48ch] type-caption text-ink-muted">{fact.body}</p>
              </div>
            </Reveal>
          ))}
        </div>

        <Reveal delay={0.2}>
          <p className="mt-10 type-caption text-ink-muted">
            The longer version, with everything spelled out, is in the{" "}
            <Link to="/legal/privacy" className="text-accent underline underline-offset-4">
              privacy policy
            </Link>
            .
          </p>
        </Reveal>
      </div>
    </section>
  );
}
