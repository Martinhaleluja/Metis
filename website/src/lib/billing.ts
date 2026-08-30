import { useEffect, useState } from "react";

/**
 * Whether Metis can actually take money yet.
 *
 * No payment processor has been chosen, so every upgrade button on the site is
 * a promise we cannot keep. Rather than hard-code that fact and then forget to
 * delete the constant on the day it stops being true, the answer comes from the
 * single row of `billing_state` in Supabase — the same row the desktop app and
 * the gateway read. Turning billing on is then an UPDATE, not a redeploy.
 *
 * `billing_state` is only readable by a signed-in user, so an anonymous visitor
 * on the marketing page gets an empty array back. That is deliberately treated
 * as "not live": the worst outcome of guessing low is a waitlist link, and the
 * worst outcome of guessing high is a checkout button that goes nowhere.
 */

const url = import.meta.env.VITE_SUPABASE_URL as string | undefined;
const publishableKey = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY as string | undefined;

type BillingRow = { billing_is_live: boolean };

export async function fetchBillingIsLive(accessToken?: string): Promise<boolean> {
  if (!url || !publishableKey) return false;

  try {
    const response = await fetch(`${url}/rest/v1/billing_state?select=billing_is_live&limit=1`, {
      headers: {
        apikey: publishableKey,
        Authorization: `Bearer ${accessToken ?? publishableKey}`,
      },
    });

    if (!response.ok) return false;

    const rows = (await response.json()) as BillingRow[];
    return rows.length > 0 && rows[0].billing_is_live === true;
  } catch {
    return false;
  }
}

/** Reads the switch once on mount. It changes about once in the product's life. */
export function useBillingIsLive(): boolean {
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

/**
 * Where the checkout redirect goes once a processor is chosen.
 *
 * This is the only place that should ever learn about the payment provider. It
 * is called from the upgrade buttons, and today it cannot be reached: every
 * button renders the waitlist state while `billing_is_live` is false. When the
 * processor is settled, create the session on the gateway and assign the URL it
 * returns to `window.location.href` here — nothing else on the site changes.
 */
export async function startCheckout(planId: string): Promise<void> {
  // eslint-disable-next-line no-console -- a loud no-op is better than a silent one
  console.warn(`Checkout for "${planId}" is not wired up yet; no processor has been chosen.`);
}
