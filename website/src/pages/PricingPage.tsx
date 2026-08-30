import { Byoa } from "../components/Byoa";
import { Faq } from "../components/Faq";
import { Pricing } from "../components/Pricing";
import { Providers } from "../components/Providers";
import { Reveal } from "../components/Reveal";
import { useBillingIsLive } from "../lib/billing";
import { comparison, plans, type PlanId } from "../lib/plans";

/**
 * The pricing page: the same three cards as the home page, plus the row-by-row
 * comparison there is no room for there.
 *
 * The cards are reused rather than restyled. A pricing table that disagrees with
 * the pricing cards is the classic way a site ends up quoting two prices, and
 * reusing the component makes that impossible rather than merely unlikely.
 */
export function PricingPage() {
  useBillingIsLive();

  return (
    <main id="main" className="relative z-10 pt-16">
      <Pricing />

      <section className="bg-page py-20 sm:py-28">
        <div className="mx-auto max-w-[1180px] px-5">
          <Reveal>
            <h2 className="type-title text-ink">Everything, side by side</h2>
            <p className="mt-4 max-w-[62ch] type-body text-ink-muted">
              A dash means the plan does not include it on Metis&rsquo;s AI. It
              almost never means Metis cannot do it &mdash; check the note.
            </p>
          </Reveal>

          <Reveal delay={0.06}>
            <div className="mt-10 win95-window">
              <div className="win95-titlebar">
                <span>compare.xls</span>
              </div>

              <div className="overflow-x-auto bg-[#c0c0c0] p-3">
                <table
                  className="w-full min-w-[640px] border-collapse text-left"
                  style={{ fontFamily: "var(--font-system)" }}
                >
                  <thead>
                    <tr>
                      <th className="win95-field px-3 py-2 text-[12px] font-bold text-black">
                        Feature
                      </th>
                      {plans.map((plan) => (
                        <th
                          key={plan.id}
                          className="win95-field px-3 py-2 text-center text-[12px] font-bold text-black"
                        >
                          {plan.name}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {comparison.map((row) => (
                      <tr key={row.label} className="align-top">
                        <th
                          scope="row"
                          className="border-b border-[#a0a0a0] px-3 py-2.5 text-[12px] font-normal text-black"
                        >
                          {row.label}
                          {row.note && (
                            <span className="mt-0.5 block text-[10.5px] leading-snug text-[#555]">
                              {row.note}
                            </span>
                          )}
                        </th>
                        {plans.map((plan) => (
                          <td
                            key={plan.id}
                            className="border-b border-[#a0a0a0] px-3 py-2.5 text-center text-[12px] text-black"
                          >
                            <Cell value={row.values[plan.id as PlanId]} />
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </Reveal>
        </div>
      </section>

      <Providers />
      <Byoa />
      <Faq />
    </main>
  );
}

/**
 * A tick, a dash, or a phrase.
 *
 * The dash is a real character rather than an empty cell: a blank reads as
 * "we forgot to fill this in", which is a worse answer than "no".
 */
function Cell({ value }: { value: string | boolean }) {
  if (value === true) {
    return (
      <span className="font-bold text-[#006400]" aria-label="Included">
        &#10003;
      </span>
    );
  }

  if (value === false) {
    return (
      <span className="text-[#888]" aria-label="Not included">
        &mdash;
      </span>
    );
  }

  return <span>{value}</span>;
}
