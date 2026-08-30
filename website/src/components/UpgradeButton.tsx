import { Link } from "react-router-dom";
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
  const base = `press w-full text-center no-underline block ${className}`;

  if (plan.priceUsd === 0) {
    return (
      <Link to="/login" className={`win95-button ${base}`}>
        {plan.ctaLabel}
      </Link>
    );
  }

  if (!billingIsLive) {
    return (
      <a href="/#join" className={`win95-button ${base}`}>
        Plans open soon &mdash; join the waitlist
      </a>
    );
  }

  return (
    <Link to="/account" className={`win95-button ${base}`}>
      {plan.ctaLabel}
    </Link>
  );
}
