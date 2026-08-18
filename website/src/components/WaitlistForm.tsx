import { useState, type FormEvent } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { ArrowRightIcon as ArrowRight } from "@phosphor-icons/react/dist/icons/ArrowRight";
import { CheckIcon as Check } from "@phosphor-icons/react/dist/icons/Check";
import { CopyIcon as Copy } from "@phosphor-icons/react/dist/icons/Copy";
import { SpinnerGapIcon as SpinnerGap } from "@phosphor-icons/react/dist/icons/SpinnerGap";
import { failureMessages } from "../lib/waitlist";
import type { useWaitlist } from "../lib/waitlist";
import { CountUp } from "./CountUp";
import { springEnter, springUI } from "../lib/motion";

type Waitlist = ReturnType<typeof useWaitlist>;

export function WaitlistForm({
  waitlist,
  idPrefix,
}: {
  waitlist: Waitlist;
  idPrefix: string;
}) {
  const { count, state, entry, failure, join } = waitlist;
  const [email, setEmail] = useState("");
  const [copied, setCopied] = useState(false);
  const reduce = useReducedMotion();

  const fieldId = `${idPrefix}-email`;
  const errorId = `${idPrefix}-email-error`;
  const busy = state === "submitting";

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    await join(email);
  }

  async function copyShareLink() {
    if (!entry) return;
    const link = `${window.location.origin}/?ref=${entry.referralCode}`;
    try {
      await navigator.clipboard.writeText(link);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2200);
    } catch {
      setCopied(false);
    }
  }

  if (state === "joined" && entry) {
    return (
      <motion.div
        initial={reduce ? false : { opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={springEnter}
        className="mx-auto w-full max-w-[520px] rounded-[20px] border border-line bg-surface p-6 text-left shadow-[var(--shadow-card)]"
      >
        <div className="flex items-center gap-2.5">
          <span className="grid h-8 w-8 place-items-center rounded-full bg-accent text-accent-contrast">
            <Check size={16} weight="bold" />
          </span>
          <p className="font-display text-lg font-semibold text-ink">
            {entry.alreadyJoined ? "You were already in" : "You are in"}
          </p>
        </div>

        <p className="mt-3 type-caption text-ink-muted">
          You are number{" "}
          <span className="font-semibold tabular-nums text-ink">
            {entry.position.toLocaleString()}
          </span>{" "}
          in the queue. We will email you a download link when Metis opens up.
        </p>

        <div className="mt-5 border-t border-line pt-5">
          <p className="text-[13px] font-medium text-ink">Move up the queue</p>
          <p className="mt-1 text-[13px] leading-relaxed text-ink-muted">
            Every person who joins with your link moves you closer to the front.
          </p>

          <div className="mt-3 flex flex-col gap-2 sm:flex-row">
            <code className="flex-1 truncate rounded-full border border-line bg-surface-sunken px-4 py-2.5 font-sans text-[13px] text-ink-muted">
              {window.location.host}/?ref={entry.referralCode}
            </code>
            <button
              type="button"
              onClick={copyShareLink}
              className="press inline-flex shrink-0 cursor-pointer items-center justify-center gap-1.5 rounded-full border border-line bg-surface px-4 py-2.5 text-[13px] font-medium text-ink hover:border-accent hover:text-accent"
            >
              {copied ? <Check size={14} weight="bold" /> : <Copy size={14} weight="bold" />}
              {copied ? "Copied" : "Copy link"}
            </button>
          </div>
        </div>
      </motion.div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-[520px]">
      <form onSubmit={onSubmit} noValidate>
        <label
          htmlFor={fieldId}
          className="mb-2 block text-left text-[13px] font-medium text-ink-muted"
        >
          Email address
        </label>

        <div className="flex flex-col gap-2 sm:flex-row">
          <input
            id={fieldId}
            name="email"
            type="email"
            required
            autoComplete="email"
            value={email}
            disabled={busy}
            onChange={(event) => setEmail(event.target.value)}
            aria-invalid={failure ? true : undefined}
            aria-describedby={failure ? errorId : undefined}
            className="min-w-0 flex-1 rounded-full border border-line bg-surface px-5 py-3.5 text-[15px] text-ink transition-colors duration-200 placeholder:text-ink-muted/70 hover:border-ink-muted/50 focus:border-accent focus:outline-none disabled:opacity-60"
            placeholder="you@example.com"
          />

          <button
            type="submit"
            disabled={busy}
            className="press inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-full bg-accent px-6 py-3.5 text-[15px] font-medium whitespace-nowrap text-accent-contrast hover:bg-accent-hover disabled:cursor-wait disabled:opacity-70"
          >
            {busy ? (
              <>
                <SpinnerGap size={16} weight="bold" className="motion-safe:animate-spin" />
                Joining
              </>
            ) : (
              <>
                Join the waitlist
                <ArrowRight size={16} weight="bold" />
              </>
            )}
          </button>
        </div>

        <AnimatePresence>
          {failure && (
            <motion.p
              id={errorId}
              role="alert"
              initial={reduce ? false : { opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: "auto" }}
              exit={{ opacity: 0, height: 0 }}
              transition={springUI}
              className="mt-2.5 text-left text-[13px] text-danger"
            >
              {failureMessages[failure]}
            </motion.p>
          )}
        </AnimatePresence>
      </form>

      <p className="mt-4 text-[14px] text-ink-muted" aria-live="polite">
        {count === null ? (
          <span className="opacity-70">Counting the queue</span>
        ) : count === 0 ? (
          "Nobody has joined yet. Be the first."
        ) : (
          <>
            <span className="font-semibold text-ink">
              <CountUp value={count} />
            </span>{" "}
            {count === 1 ? "person is" : "people are"} already waiting
          </>
        )}
      </p>
    </div>
  );
}
