import { useEffect, useState } from "react";
import { getSupabase } from "./auth";
import type { PlanId } from "./plans";
import { rpc } from "./supabase";

/**
 * Whether Metis can actually take money yet, and what happens when it can.
 *
 * The answer comes from the single row of `billing_state` in Supabase — the same
 * row the desktop app and the gateway read — rather than from a constant here,
 * so turning billing on is an UPDATE and not a redeploy.
 *
 * It is asked through the `billing_is_live()` function rather than by selecting
 * the row. The row's select policy is granted to `authenticated`, and the whole
 * point of this question is that the public pricing page needs it while nobody
 * is signed in, as `anon`. Reading the table directly always came back empty and
 * so always concluded "not live", permanently, whatever the row said — the buy
 * button could not appear even after the shop opened. The function returns the
 * one boolean the public is entitled to know and is granted to `anon`, which is
 * narrower than widening the policy would have been: `billing_state` also
 * carries the cost-protection note, and a policy cannot withhold a column.
 *
 * Failing closed is deliberate and unchanged. The worst outcome of guessing low
 * is a waitlist link; the worst outcome of guessing high is a checkout button
 * that goes nowhere.
 */

export async function fetchBillingIsLive(): Promise<boolean> {
  try {
    // Anything other than a literal true — a null, an error, a build with no
    // Supabase credentials at all — is "no".
    return (await rpc<boolean>("billing_is_live")) === true;
  } catch {
    return false;
  }
}

/** Reads the switch once on mount. It changes about once in the product's life. */
export function useBillingIsLive(): boolean {
  // Starts closed. Until the answer comes back nobody has looked, and the
  // honest rendering of "we do not know yet" is the waitlist link rather than a
  // buy button that might be about to disappear.
  const [live, setLive] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void fetchBillingIsLive().then((value) => {
      if (!cancelled) setLive(value);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  return live;
}

const gatewayUrl = (import.meta.env.VITE_METIS_API_URL as string | undefined)?.replace(/\/+$/, "");

/** Free has nothing to charge for, so it never has a checkout. */
export type PaidPlanId = Exclude<PlanId, "free">;

export type CheckoutResult = { ok: true } | { ok: false; error: string };

/**
 * Starts a subscription, and is the only place on the site that knows a payment
 * processor exists.
 *
 * The gateway creates the session, because that is where the processor's secret
 * key lives; all this does is ask for a URL and go there. Which processor it is
 * never reaches the browser, so changing it later is a server deployment rather
 * than a website release.
 *
 * The session is read here rather than passed in. A token held in React state
 * can have expired while the pricing page sat open in a tab, and asking the
 * client for it at the moment of the click gets the refreshed one instead of the
 * stale one — a checkout that fails with "not signed in" for somebody who plainly
 * is would be a miserable way to lose a sale.
 *
 * Every failure returns a sentence rather than throwing or logging. The caller
 * is a button, the person is looking at it, and "nothing happened" is the one
 * outcome that must not be possible.
 */
export async function startCheckout(planId: PaidPlanId): Promise<CheckoutResult> {
  if (!gatewayUrl) {
    return { ok: false, error: "Metis's API is not connected in this build, so checkout cannot open." };
  }

  let accessToken: string;
  try {
    const supabase = await getSupabase();
    const { data } = await supabase.auth.getSession();

    if (!data.session) {
      return { ok: false, error: "Your session has expired. Sign in again and the checkout will open." };
    }

    accessToken = data.session.access_token;
  } catch (cause) {
    return {
      ok: false,
      error: cause instanceof Error ? cause.message : "Your account could not be read.",
    };
  }

  try {
    const response = await fetch(`${gatewayUrl}/v1/checkout`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${accessToken}`,
      },
      body: JSON.stringify({ plan: planId }),
    });

    // The gateway refuses with `{ error, kind }`, and the one refusal that comes
    // from ASP.NET itself — an authenticated user with no account row — uses
    // `detail`. Both are written to be read by a person.
    const body = (await response.json().catch(() => ({}))) as {
      url?: string;
      error?: string;
      detail?: string;
    };

    if (!response.ok) {
      return {
        ok: false,
        error:
          body.error ??
          body.detail ??
          `Checkout could not be started (${response.status}). Try again in a moment.`,
      };
    }

    if (!body.url) {
      return { ok: false, error: "Checkout could not be started: no payment page came back." };
    }

    // Leaving the site is the success case. Nothing after this runs.
    window.location.href = body.url;
    return { ok: true };
  } catch {
    return { ok: false, error: "Metis could not be reached. Check your connection and try again." };
  }
}
