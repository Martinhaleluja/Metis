import { Link } from "react-router-dom";
import { Reveal } from "./Reveal";

/**
 * Where screen content goes, per plan.
 *
 * This section used to say that keys never leave the machine and there is no
 * server in the middle. That was true when Metis only ever ran on the
 * customer's own API key. It is still true for your own key and for Pro's
 * connected account, and it is flatly untrue for managed Free and Plus, where
 * the screenshot and the prompt go to Metis's gateway and the gateway calls the
 * provider on Metis's key.
 *
 * A privacy claim that is true on one plan and false on another has to be
 * written per plan, so that is how it is written. The table is the section; the
 * reassuring lines come after it, not before.
 */

const routes = [
  {
    route: "Managed AI on Free and Plus",
    who: "Metis's gateway, then the provider",
    detail:
      "Your question and — on Plus — the screenshot are sent to Metis's own server, which forwards them to the AI provider using Metis's API key. Metis is in the middle of that request, by definition: it is the account being billed. The gateway keeps what it needs to meter and bill the request — the model, the token counts, the cost — and does not store the screenshot or the text of your question.",
    note: "Free has no screen vision on Metis's AI at all, so on Free this route carries text only.",
  },
  {
    route: "Your own provider account on Pro",
    who: "The provider you connected, directly",
    detail:
      "You connect an OpenAI, Anthropic, Gemini, Mistral or OpenRouter account and Metis calls it with your credentials. The key is held encrypted by the gateway and never in plain text, and the request is charged to you by that provider. What they keep is governed by their privacy policy, not ours.",
  },
  {
    route: "Your own API key in the desktop app",
    who: "The provider, straight from your machine",
    detail:
      "Paste a key into Metis on Windows and there is no Metis server in the path at all — not on Pro, not on Plus, and not on Free while signed out. The key lives in Windows Credential Manager, the request goes from your computer to the provider, and screen vision and automation are never metered, because there is nothing of ours for them to touch.",
  },
  {
    route: "A local model through Ollama",
    who: "Nobody",
    detail:
      "Point Metis at a vision-capable model running on your own machine and the whole loop — capture, question, answer, speech — stays there.",
  },
];

const facts = [
  {
    heading: "The screen is read on request",
    body: "Metis captures the desktop for the turn you asked about. It is not watching between requests, and screenshots are never written to disk — they exist for the length of the request and are then gone.",
  },
  {
    heading: "Private windows are blacked out before anything is sent",
    body: "Banking apps, password managers and view-once photos in WhatsApp and Signal mark themselves as not-for-capture, and Metis paints them out of the screenshot before it is encoded — on every route above. Password boxes are never read, and you can name other apps to hide.",
  },
  {
    heading: "Keys are never stored in plain text",
    body: "A key you paste into the desktop app is a separate entry in Windows Credential Manager. A key you connect on Pro is encrypted in the gateway's vault. Neither is ever written into a settings file, a log, or an audit record, and the browser only ever gets a four-character hint back.",
  },
  {
    heading: "Chats and memory stay on your computer",
    body: "Conversation history and what Metis has learned about your work are encrypted to your Windows account under %LOCALAPPDATA%\\Metis. None of it is uploaded, on any plan, and Settings → Privacy deletes all of it.",
  },
  {
    heading: "It teaches — it never controls",
    body: "Metis draws arrows and highlights on your screen, but it cannot click, type, or move your mouse. The teaching policy is structural, not a prompt instruction. Background agents are a separate, opt-in feature that does run tasks for you — confined to their own folder, and stopped for approval before anything destructive.",
  },
];

export function Privacy() {
  return (
    <section id="privacy" className="scroll-mt-24 bg-surface-sunken py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="max-w-[24ch] type-title text-ink">
            Where your screen goes depends on who is paying for the answer
          </h2>
          <p className="mt-5 max-w-[66ch] type-body text-ink-muted">
            Metis can answer through AI we buy for you or through AI you buy
            yourself, and those are genuinely different privacy stories. Here is
            each one, without the softening.
          </p>
        </Reveal>

        <Reveal delay={0.06}>
          <div className="mt-10 win95-window">
            <div className="win95-titlebar">
              <span>data_flow.txt &mdash; Notepad</span>
              <span className="flex h-3.5 w-4 items-center justify-center border border-white border-r-[#808080] border-b-[#808080] bg-[#c0c0c0] text-[8px] font-bold text-black">
                &times;
              </span>
            </div>

            <div className="bg-[#c0c0c0] p-3">
              <div
                className="win95-field p-4"
                style={{ fontFamily: "var(--font-system)" }}
              >
                <ul className="divide-y divide-[#d4d0c8]">
                  {routes.map((route) => (
                    <li key={route.route} className="py-4 first:pt-0 last:pb-0">
                      <div className="flex flex-col gap-1 sm:flex-row sm:items-baseline sm:justify-between sm:gap-6">
                        <h3 className="text-[13px] font-bold text-black">{route.route}</h3>
                        <span className="shrink-0 text-[11px] font-bold text-[#000080]">
                          &rarr; {route.who}
                        </span>
                      </div>
                      <p className="mt-1.5 max-w-[86ch] text-[12px] leading-relaxed text-[#333]">
                        {route.detail}
                      </p>
                      {route.note && (
                        <p className="mt-1.5 text-[11px] text-[#555] italic">{route.note}</p>
                      )}
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
            The full detail is in the{" "}
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
