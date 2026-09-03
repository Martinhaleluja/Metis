import type { Session, SupabaseClient } from "@supabase/supabase-js";
import { useEffect, useState } from "react";

/**
 * Signing in, kept away from the marketing page.
 *
 * The waitlist deliberately does not use `@supabase/supabase-js` — it needs two
 * unauthenticated RPC calls and a hand-rolled `fetch` does that in twenty lines
 * (see `supabase.ts`). Sessions are a different problem: refresh tokens,
 * storage, expiry, and the tab-visibility rules around all three are exactly the
 * kind of thing worth taking a library for.
 *
 * So the library is loaded here, and only here, behind a dynamic import. A
 * visitor who reads the pricing page and leaves never downloads it.
 */

const url = import.meta.env.VITE_SUPABASE_URL as string | undefined;
const publishableKey = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY as string | undefined;

export const isAuthConfigured = Boolean(url && publishableKey);

let clientPromise: Promise<SupabaseClient> | null = null;

/** One client per tab, created the first time somebody needs a session. */
export function getSupabase(): Promise<SupabaseClient> {
  if (!url || !publishableKey) {
    return Promise.reject(new Error("Supabase is not configured for this build."));
  }

  clientPromise ??= import("@supabase/supabase-js").then(({ createClient }) =>
    createClient(url, publishableKey, {
      auth: {
        persistSession: true,
        autoRefreshToken: true,
        // Still false, and still deliberately. The handoff below reads its own
        // fragment and calls verifyOtp directly, rather than letting the client
        // consume anything it finds in the URL — so a link someone was sent
        // cannot sign them in as somebody else just by being opened.
        detectSessionInUrl: false,
      },
    }),
  );

  return clientPromise;
}

export type AuthState =
  | { status: "loading" }
  | { status: "signed-out" }
  | { status: "signed-in"; session: Session };

/**
 * The current session, and every change to it.
 *
 * `loading` is a real third state rather than a stand-in for signed-out. The
 * difference matters on `/account`, where treating "we have not looked yet" as
 * "not signed in" would bounce a signed-in customer to the login page for a
 * fraction of a second on every reload.
 *
 * `enabled` exists because asking the question is not free. Looking for a
 * session downloads `@supabase/supabase-js`, which is the whole reason the
 * signed-in pages are split into their own chunk, and the marketing pages must
 * not pull it in just by being read. The pricing buttons need to know whether
 * somebody is signed in only once there is something to buy, so they ask only
 * then. While it is false nothing is fetched and the state stays `loading`,
 * which is the truth: nobody has looked.
 */
export function useAuth(enabled = true): AuthState {
  const [state, setState] = useState<AuthState>({ status: "loading" });

  useEffect(() => {
    if (!enabled) return;

    if (!isAuthConfigured) {
      setState({ status: "signed-out" });
      return;
    }

    let cancelled = false;
    let unsubscribe: (() => void) | undefined;

    void getSupabase().then(async (supabase) => {
      // Before the session is read, not after. A handoff from the desktop app
      // is an instruction about *which* account this page is for, so reading
      // the existing session first would show the wrong one and then swap it
      // underneath the reader.
      await redeemDesktopHandoff();
      if (cancelled) return;

      const { data } = await supabase.auth.getSession();
      if (cancelled) return;

      setState(
        data.session
          ? { status: "signed-in", session: data.session }
          : { status: "signed-out" },
      );

      const { data: listener } = supabase.auth.onAuthStateChange((_event, session) => {
        setState(session ? { status: "signed-in", session } : { status: "signed-out" });
      });

      unsubscribe = () => listener.subscription.unsubscribe();
    });

    return () => {
      cancelled = true;
      unsubscribe?.();
    };
  }, [enabled]);

  return state;
}

/**
 * Turns Supabase's error text into something worth reading.
 *
 * Its messages are written for developers and some of them are actively
 * confusing to a customer — "Invalid login credentials" for a typo'd password
 * is fine, but "Email not confirmed" reads as a fault rather than as an
 * instruction. Anything unrecognised is passed through: a wrong-but-specific
 * message beats a right-but-useless one.
 */
export function readableAuthError(message: string): string {
  const text = message.toLowerCase();

  if (text.includes("invalid login credentials")) {
    return "That email and password do not match an account.";
  }
  if (text.includes("email not confirmed")) {
    return "Check your inbox and confirm your email address first.";
  }
  if (text.includes("already registered")) {
    return "There is already an account with that email. Try signing in.";
  }
  if (text.includes("password should be at least")) {
    return "Passwords need to be at least six characters.";
  }
  if (text.includes("rate limit") || text.includes("too many")) {
    return "Too many attempts just now. Wait a minute and try again.";
  }

  return message;
}


/**
 * Redeems a one-time sign-in handed over by the desktop app.
 *
 * Metis and this site keep entirely separate Supabase sessions. "Manage on Web"
 * used to be a plain link, so it opened whichever account this browser was last
 * signed in as — and on a shared or re-used machine that is routinely not the
 * account Metis is signed in to. People saw a different plan and a different
 * address than the app had just shown them, and concluded the two disagreed
 * about who they were. They did.
 *
 * The token arrives in the fragment, which browsers do not send to the server:
 * it stays out of access logs and out of `Referer`. It is spent immediately and
 * stripped from the address bar either way, so a reload or a shared URL cannot
 * replay it — and Supabase treats these as single-use regardless.
 *
 * Returns true when a session was established, so the caller can re-read it.
 */
export async function redeemDesktopHandoff(): Promise<boolean> {
  const fragment = window.location.hash;
  if (!fragment.includes("handoff=")) {
    return false;
  }

  const token = new URLSearchParams(fragment.slice(1)).get("handoff");

  // Cleared before the await, not after. Whatever happens next, the token must
  // not survive in the address bar to be copied, shared or reloaded.
  history.replaceState(null, "", window.location.pathname + window.location.search);

  if (!token) {
    return false;
  }

  try {
    const supabase = await getSupabase();
    const { error } = await supabase.auth.verifyOtp({
      token_hash: token,
      type: "magiclink",
    });
    return !error;
  } catch {
    // An expired or already-spent token is the ordinary case here, not an
    // exceptional one: the page simply carries on showing the session it
    // already had, or the signed-out state.
    return false;
  }
}
