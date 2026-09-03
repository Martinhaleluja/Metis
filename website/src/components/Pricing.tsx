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
              <div
                className={`h-full ${plan.featured ? "card-raised ring-1 ring-accent" : "card"}`}
              >
                <div className="panel-title">
                  <span className="truncate">{plan.name}</span></div>

                <div
                  className="flex h-full flex-col bg-surface p-5"
                >
                  <div className="flex items-baseline justify-between">
                    <h3 className="text-[18px] font-bold text-ink">{plan.name}</h3>
                    {plan.featured && (
                      <span className="bg-accent px-2 py-0.5 text-[10px] font-bold text-white">
                        MOST POPULAR
                      </span>
                    )}
                  </div>

                  <p className="mt-3 flex items-baseline gap-1.5">
                    <span className="text-[34px] leading-none font-bold text-ink">
                      {priceLabel(plan)}
                    </span>
                    <span className="text-[12px] text-[#444]">{plan.cadence}</span>
                  </p>

                  <p className="mt-2 text-[12px] leading-relaxed text-[#333]">{plan.tagline}</p>

                  {/* The AI itself, in a sunken field so it reads as a value the
                      system reported rather than a marketing line. */}
                  <p className="rounded-lg border border-line bg-surface px-3 py-2 mt-4 px-2.5 py-1.5 text-[11px] text-ink">
                    <span className="font-bold">AI:</span> {plan.aiSummary}
                  </p>

                  <ul className="mt-4 flex-1 space-y-2">
                    {plan.features.map((feature) => (
                      <li key={feature} className="flex items-start gap-2 text-[12px] text-[#222]">
                        <Check
                          size={13}
                          weight="bold"
                          className="mt-[3px] shrink-0 text-accent"
                        />
                        <span className="leading-snug">{feature}</span>
                      </li>
                    ))}
                  </ul>

                  <div className="mt-6">
                    <UpgradeButton
                      plan={plan}
                      billingIsLive={billingIsLive}
                      className="!py-2 text-[12px]"
                    />
                  </div>
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
