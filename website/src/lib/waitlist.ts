import { useCallback, useEffect, useRef, useState } from "react";
import { isSupabaseConfigured, rpc } from "./supabase";

export type JoinFailure = "invalid_email" | "rate_limited" | "unreachable" | "unconfigured";

export type WaitlistEntry = {
  alreadyJoined: boolean;
  position: number;
  referralCode: string;
  referrals: number;
  total: number;
};

type RpcResponse =
  | {
      ok: true;
      already_joined: boolean;
      position: number;
      referral_code: string;
      referrals: number;
      total: number;
    }
  | { ok: false; error: Exclude<JoinFailure, "unreachable" | "unconfigured"> };

export const failureMessages: Record<JoinFailure, string> = {
  invalid_email: "That address does not look right. Check it and try again.",
  rate_limited: "That is a lot of signups from one connection. Try again in an hour.",
  unreachable: "We could not reach the waitlist just now. Try again in a moment.",
  unconfigured: "The waitlist is not connected yet. Set the Supabase environment variables.",
};

/** Reads a referral code out of the share link a friend sent. */
function referralFromUrl(): string | null {
  if (typeof window === "undefined") return null;
  const code = new URLSearchParams(window.location.search).get("ref");
  return code ? code.trim().toUpperCase().slice(0, 16) : null;
}

const COUNT_POLL_MS = 25_000;

export function useWaitlist() {
  const [count, setCount] = useState<number | null>(null);
  const [state, setState] = useState<"idle" | "submitting" | "joined">("idle");
  const [entry, setEntry] = useState<WaitlistEntry | null>(null);
  const [failure, setFailure] = useState<JoinFailure | null>(null);
  const referral = useRef<string | null>(referralFromUrl());

  const refreshCount = useCallback(async () => {
    if (!isSupabaseConfigured) return;
    try {
      const total = await rpc<number>("waitlist_count");
      if (typeof total === "number") setCount(total);
    } catch {
      // A failed count is not worth showing anyone; the last good figure stays.
    }
  }, []);

  // Poll rather than subscribe: the table deliberately has no read policy, so
  // realtime cannot broadcast it to an anonymous visitor. A slow poll keeps the
  // number honest without hammering the database.
  useEffect(() => {
    void refreshCount();
    if (typeof document === "undefined") return;

    const id = window.setInterval(() => {
      if (document.visibilityState === "visible") void refreshCount();
    }, COUNT_POLL_MS);

    return () => window.clearInterval(id);
  }, [refreshCount]);

  const join = useCallback(async (email: string) => {
    setFailure(null);

    if (!isSupabaseConfigured) {
      setFailure("unconfigured");
      return;
    }

    setState("submitting");

    let response: RpcResponse;
    try {
      response = await rpc<RpcResponse>("join_waitlist", {
        p_email: email,
        p_referral_code: referral.current,
        p_source: window.location.host,
      });
    } catch {
      setState("idle");
      setFailure("unreachable");
      return;
    }

    if (!response.ok) {
      setState("idle");
      setFailure(response.error);
      return;
    }

    setEntry({
      alreadyJoined: response.already_joined,
      position: response.position,
      referralCode: response.referral_code,
      referrals: response.referrals,
      total: response.total,
    });
    setCount(response.total);
    setState("joined");
  }, []);

  return { count, state, entry, failure, join } as const;
}
