import { CheckIcon as Check } from "@phosphor-icons/react/dist/icons/Check";
import { Link } from "react-router-dom";
import { planById, plans, priceLabel } from "../lib/plans";
import { useBillingIsLive } from "../lib/billing";
import { Reveal } from "./Reveal";
import { UpgradeButton } from "./UpgradeButton";

/**
 * Three Win95 windows, one per plan.
 *
 * The line under the heading is the whole pricing argument and it has to stay
 * true. The plans meter Metis's own AI bill rather than the software: every
 * plan gets the whole app, and a model running on the user's own computer is
 * outside all of it. Bringing a provider key of your own is outside the
 * allowance too — Metis is not paying for those requests — but is itself part
 * of Max, so this section must never imply that a key gets a Free account the
 * paid behaviour.
 */
export function Pricing() {
  const billingIsLive = useBillingIsLive();

  return (
    <section id="pricing" className="scroll-mt-16 bg-surface py-20 sm:py-28">
      <div className="mx-auto max-w-[1180px] px-5">
        <Reveal>
          <h2 className="mx-auto max-w-[24ch] text-center type-title text-ink">
            Start free. Pay when you want more.
          </h2>
          <p className="mx-auto mt-4 max-w-[58ch] text-center type-body text-ink-muted">
            Every plan includes the whole app. What changes is how much AI comes
            with it &mdash; and on {planById.max.name}, whether you would rather
            bring your own.
          </p>
        </Reveal>

        <div className="mt-14 grid items-start gap-6 lg:grid-cols-3">
          {plans.map((plan, index) => (
            <Reveal key={plan.id} delay={index * 0.06}>
              {/* The featured plan inverts rather than merely gaining a ring.
                  A border weight is a difference you have to look for; a
                  filled card is one you cannot miss, which is the whole job
                  of marking one plan as the recommended one. */}
              <div
                className={`flex h-full flex-col rounded-[24px] p-7 ${
                  plan.featured
                    ? "bg-accent text-accent-contrast shadow-[var(--shadow-lift)] lg:-my-3 lg:py-10"
                    : "card"
                }`}
              >
                <div className="flex items-center justify-between gap-3">
                  <h3
                    className={`type-heading ${
                      plan.featured ? "text-accent-contrast" : "text-ink"
                    }`}
                  >
                    {plan.name}
                  </h3>
                  {plan.featured && (
                    <span className="pill bg-white/20 text-accent-contrast">
                      Most popular
                    </span>
                  )}
                </div>

                <p className="mt-5 flex items-baseline gap-1.5">
                  <span
                    className={`font-display text-[44px] font-bold leading-none ${
                      plan.featured ? "text-accent-contrast" : "text-ink"
                    }`}
                  >
                    {priceLabel(plan)}
                  </span>
                  <span
                    className={
                      plan.featured
                        ? "text-[14px] text-accent-contrast/75"
                        : "text-[14px] text-ink-muted"
                    }
                  >
                    {plan.cadence}
                  </span>
                </p>

                <p
                  className={`mt-3 type-caption ${
                    plan.featured ? "text-accent-contrast/85" : "text-ink-muted"
                  }`}
                >
                  {plan.tagline}
                </p>

                {/* What the plan actually buys, set apart so it reads as a
                    specification rather than another marketing line. */}
                <p
                  className={`mt-5 rounded-2xl px-4 py-3 text-[14px] leading-snug ${
                    plan.featured
                      ? "bg-white/15 text-accent-contrast"
                      : "bg-surface-sunken text-ink"
                  }`}
                >
                  <span className="font-semibold">AI:</span> {plan.aiSummary}
                </p>

                <ul className="mt-5 flex-1 space-y-2.5">
                  {plan.features.map((feature) => (
                    <li
                      key={feature}
                      className={`flex items-start gap-2.5 text-[14px] ${
                        plan.featured ? "text-accent-contrast/90" : "text-ink-muted"
                      }`}
                    >
                      <Check
                        size={15}
                        weight="bold"
                        className={`mt-[3px] shrink-0 ${
                          plan.featured ? "text-accent-contrast" : "text-leaf"
                        }`}
                      />
                      <span className="leading-snug">{feature}</span>
                    </li>
                  ))}
                </ul>

                <div className="mt-7">
                  <UpgradeButton
                    plan={plan}
                    billingIsLive={billingIsLive}
                    className="w-full"
                  />
                </div>
              </div>
            </Reveal>
          ))}
        </div>

        <Reveal delay={0.2}>
          <p className="mt-10 text-center type-caption text-ink-muted">
            <Link to="/pricing" className="text-accent underline underline-offset-4">
              Compare the plans line by line
            </Link>
            {billingIsLive ? null : " — nothing is charged yet; Metis is in early access."}
          </p>
        </Reveal>
      </div>
    </section>
  );
}
