import { Reveal } from "./Reveal";

const facts = [
  {
    heading: "Keys live in Windows, not in a file",
    body: "Every API key is a separate entry in Windows Credential Manager. Secret values are never written into settings or logs.",
  },
  {
    heading: "The screen is read on request",
    body: "Metis captures the desktop for the turn you asked about. It is not watching between requests.",
  },
  {
    heading: "Private windows are blacked out before anything is sent",
    body: "Banking apps, password managers and view-once photos in WhatsApp and Signal mark themselves as not-for-capture, and Metis paints them out of the screenshot before it is encoded. Password boxes are never read, and you can name other apps to hide.",
  },
  {
    heading: "It can run with no cloud at all",
    body: "Point it at a vision-capable model running locally through Ollama and the whole loop stays on your machine.",
  },
  {
    heading: "It teaches — it never controls",
    body: "Metis draws arrows and highlights on your screen, but it cannot click, type, or move your mouse. The teaching policy is structural, not just a prompt instruction. Background agents are a separate, opt-in feature that does run tasks for you — confined to their own folder, and stopped for approval before anything destructive.",
  },
];

export function Privacy() {
  return (
    <section id="privacy" className="scroll-mt-24 bg-surface-sunken py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="max-w-[22ch] type-title text-ink">
            An assistant on your desktop should be answerable to you
          </h2>
          <p className="mt-5 max-w-[64ch] type-body text-ink-muted">
            Metis runs on your machine with your own provider keys. Here is what that means in
            practice.
          </p>
        </Reveal>

        <div className="mt-12 grid gap-x-14 gap-y-10 sm:grid-cols-2">
          {facts.map((fact, index) => (
            <Reveal key={fact.heading} delay={index * 0.05}>
              <div className="border-t border-line pt-5">
                <h3 className="type-heading text-ink">
                  {fact.heading}
                </h3>
                <p className="mt-2 max-w-[46ch] type-caption text-ink-muted">
                  {fact.body}
                </p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
