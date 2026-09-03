import { useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../lib/auth";
import { startCheckout } from "../lib/billing";
import type { Plan } from "../lib/plans";

/**
 * The one button on the site that changes shape depending on whether Metis can
 * take money yet.
 *
 * While `billing_is_live` is false there is no checkout to send anyone to, so
 * the paid plans say so plainly and point at the waitlist. A button that opens
 * a dead checkout costs more trust than a button that admits the shop is not
 * open. Free never needs the waitlist — it is available now — so it always
 * links to sign-up.
 *
 * Once billing is live the paid plans need a session, because the gateway
 * creates the checkout for a particular account and will not do it for a
 * stranger. So there are two live states rather than one: signed in gets a
 * button that goes straight to the payment page, and signed out gets sent to
 * sign in first, with the way back attached. Sending somebody to a checkout
 * that then refuses them for being signed out is the same broken promise as the
 * dead button, one page later.
 */
export function UpgradeButton({
  plan,
  billingIsLive,
  className = "",
}: {
  plan: Plan;
  billingIsLive: boolean;
  className?: string;
}) {
  // Only look for a session when one could be needed. This hook downloads the
  // Supabase client, and a visitor reading the home page has no business paying
  // for that in bandwidth while there is nothing to buy.
  const auth = useAuth(billingIsLive && plan.id !== "free");
  const location = useLocation();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const base = `press w-full text-center no-underline block ${className}`;

  // Decided by the plan's id rather than its price, because the id is what the
  // gateway's checkout takes: "which plans have a checkout" and "which plans
  // cost something" are the same question today, and the first one is the one
  // actually being asked here.
  if (plan.id === "free") {
    return (
      <Link to="/login" className={`press rounded-full border border-line bg-surface px-4 py-2 font-semibold ${base}`}>
        {plan.ctaLabel}
      </Link>
    );
  }

  if (!billingIsLive) {
    return (
      <a href="/#join" className={`press rounded-full border border-line bg-surface px-4 py-2 font-semibold ${base}`}>
        Plans open soon &mdash; join the waitlist
      </a>
    );
  }

  if (auth.status !== "signed-in") {
    // Where to come back to, which is wherever this card is being read — the
    // pricing page, or the home page's pricing section.
    const next = `${location.pathname}${location.search}${location.hash}`;

    return (
      <Link
        to={`/login?next=${encodeURIComponent(next)}`}
        className={`press rounded-full border border-line bg-surface px-4 py-2 font-semibold ${base}`}
      >
        {plan.ctaLabel}
      </Link>
    );
  }

  // Narrowed by the `plan.id === "free"` return above, and captured here because
  // that narrowing does not follow a property access into the handler below.
  const paidPlan = plan.id;

  async function buy() {
    if (pending) return;

    setPending(true);
    setError(null);

    const result = await startCheckout(paidPlan);

    // On success the browser is already on its way to the payment page, so the
    // button stays disabled: re-enabling it during the navigation invites a
    // second click and a second checkout session.
    if (!result.ok) {
      setError(result.error);
      setPending(false);
    }
  }

  return (
    <>
      <button
        type="button"
        disabled={pending}
        onClick={() => void buy()}
        className={`press rounded-full border border-line bg-surface px-4 py-2 font-semibold ${base}`}
      >
        {pending ? "Opening checkout…" : plan.ctaLabel}
      </button>

      {error && (
        <p
          role="alert"
          className="mt-2 border border-[#c62828] bg-[#ffebee] px-2 py-1.5 text-[11px] leading-snug text-[#b71c1c]"
        >
          {error}
        </p>
      )}
    </>
  );
}
