import { useEffect, useRef, useState } from "react";
import { Reveal } from "./Reveal";

const conversations = [
  {
    user: "Hey Metis, how do I add a drop shadow in Photoshop?",
    metis:
      "Look at the Layers panel on the right — I've highlighted the fx button at the bottom. Click it and choose Drop Shadow. I'll walk you through the settings.",
  },
  {
    user: "What does this error in VS Code mean?",
    metis:
      "That's a missing import. See the red squiggle on line 12? Right-click it, pick Quick Fix, and choose the auto-import. Done — I drew an arrow so you can find it.",
  },
  {
    user: "Can you show me how to make a pivot table?",
    metis:
      "Select your data range first — I've outlined it in blue on your screen. Now go to Insert → PivotTable. I'll guide you through each field.",
  },
];

const CHAR_DELAY = 32;
const PAUSE_AFTER_LINE = 1200;
const PAUSE_BETWEEN_CONVOS = 3000;

export function TypingConversation() {
  const [convoIdx, setConvoIdx] = useState(0);
  const [phase, setPhase] = useState<"user" | "metis" | "pause">("user");
  const [displayed, setDisplayed] = useState("");
  const charIdx = useRef(0);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const convo = conversations[convoIdx];

  useEffect(() => {
    const text = phase === "user" ? convo.user : phase === "metis" ? convo.metis : "";
    if (phase === "pause") {
      timerRef.current = setTimeout(() => {
        const next = (convoIdx + 1) % conversations.length;
        setConvoIdx(next);
        setPhase("user");
        charIdx.current = 0;
        setDisplayed("");
      }, PAUSE_BETWEEN_CONVOS);
      return () => clearTimeout(timerRef.current);
    }

    if (charIdx.current >= text.length) {
      timerRef.current = setTimeout(() => {
        if (phase === "user") {
          setPhase("metis");
          charIdx.current = 0;
          setDisplayed("");
        } else {
          setPhase("pause");
        }
      }, PAUSE_AFTER_LINE);
      return () => clearTimeout(timerRef.current);
    }

    timerRef.current = setTimeout(() => {
      charIdx.current += 1;
      setDisplayed(text.slice(0, charIdx.current));
    }, CHAR_DELAY);

    return () => clearTimeout(timerRef.current);
  }, [displayed, phase, convoIdx, convo]);

  return (
    <section className="py-16 sm:py-24">
      <div className="mx-auto max-w-[680px] px-5">
        <Reveal>
          <h2 className="type-title text-ink text-center mb-3">
            Talk to Metis like a friend
          </h2>
          <p className="text-center type-body text-ink-muted mb-10 max-w-[48ch] mx-auto">
            Ask anything about what&rsquo;s on screen. Metis sees it, explains it,
            and draws right where you need to look.
          </p>
        </Reveal>

        <Reveal delay={0.1}>
          <div
            className="card"
          >
            <div className="panel-title">
              <span className="truncate">A conversation with Metis</span></div>

            <div className="rounded-lg border border-line bg-surface px-3 py-2 min-h-[160px] p-4 m-1 flex flex-col gap-3">
              {/* User message */}
              <div className="flex gap-2 items-start">
                <span
                  className="shrink-0 text-[14px] font-bold text-accent"
                >
                  You&gt;
                </span>
                <span
                  className="text-[14px] text-ink leading-relaxed"
                >
                  {phase === "user" ? (
                    <>
                      {displayed}
                      <span className="blink text-accent">▌</span>
                    </>
                  ) : (
                    convo.user
                  )}
                </span>
              </div>

              {/* Metis response */}
              {(phase === "metis" || phase === "pause") && (
                <div className="flex gap-2 items-start">
                  <span
                    className="shrink-0 text-[14px] font-bold text-accent"
                  >
                    Metis&gt;
                  </span>
                  <span
                    className="text-[14px] text-ink leading-relaxed"
                  >
                    {phase === "metis" ? (
                      <>
                        {displayed}
                        <span className="blink text-accent">▌</span>
                      </>
                    ) : (
                      convo.metis
                    )}
                  </span>
                </div>
              )}
            </div>

            {/* Status bar */}
            <div className="bg-surface px-2 py-0.5 flex justify-between border-t-2 border-line">
              <span
                className="text-[14px] text-ink-muted"
              >
                {phase === "pause" ? "Ready" : "Typing..."}
              </span>
              <span
                className="text-[14px] text-ink-muted"
              >
                {convoIdx + 1}/{conversations.length}
              </span>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
