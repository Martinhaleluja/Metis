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
        // The desktop app signs in with email and password, and this is the
        // same account. Nothing here uses a magic link, so there is no fragment
        // to pick out of the URL.
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
 */
export function useAuth(): AuthState {
  const [state, setState] = useState<AuthState>({ status: "loading" });

  useEffect(() => {
    if (!isAuthConfigured) {
      setState({ status: "signed-out" });
      return;
    }

    let cancelled = false;
    let unsubscribe: (() => void) | undefined;

    void getSupabase().then(async (supabase) => {
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
  }, []);

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
