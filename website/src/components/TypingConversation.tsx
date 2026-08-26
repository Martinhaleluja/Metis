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
  const timerRef = useRef<ReturnType<typeof setTimeout>>();

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
            className="win95-window"
            style={{ transform: "rotate(-0.5deg)" }}
          >
            <div className="win95-titlebar">
              <span className="truncate">metis_chat.exe</span>
              <div className="flex gap-[2px]">
                <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
                  _
                </span>
                <span className="w-4 h-3.5 bg-[#c0c0c0] border border-white border-r-[#808080] border-b-[#808080] flex items-center justify-center text-[8px] text-black font-bold">
                  &times;
                </span>
              </div>
            </div>

            <div className="win95-field min-h-[160px] p-4 m-1 flex flex-col gap-3">
              {/* User message */}
              <div className="flex gap-2 items-start">
                <span
                  className="shrink-0 text-[11px] font-bold text-[#000080]"
                  style={{ fontFamily: "var(--font-system)" }}
                >
                  You&gt;
                </span>
                <span
                  className="text-[12px] text-black leading-relaxed"
                  style={{ fontFamily: "var(--font-system)" }}
                >
                  {phase === "user" ? (
                    <>
                      {displayed}
                      <span className="blink text-[#000080]">▌</span>
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
                    className="shrink-0 text-[11px] font-bold text-[#008080]"
                    style={{ fontFamily: "var(--font-system)" }}
                  >
                    Metis&gt;
                  </span>
                  <span
                    className="text-[12px] text-black leading-relaxed"
                    style={{ fontFamily: "var(--font-system)" }}
                  >
                    {phase === "metis" ? (
                      <>
                        {displayed}
                        <span className="blink text-[#008080]">▌</span>
                      </>
                    ) : (
                      convo.metis
                    )}
                  </span>
                </div>
              )}
            </div>

            {/* Status bar */}
            <div className="bg-[#c0c0c0] px-2 py-0.5 flex justify-between border-t-2 border-[#808080]">
              <span
                className="text-[10px] text-[#333]"
                style={{ fontFamily: "var(--font-system)" }}
              >
                {phase === "pause" ? "Ready" : "Typing..."}
              </span>
              <span
                className="text-[10px] text-[#333]"
                style={{ fontFamily: "var(--font-system)" }}
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
