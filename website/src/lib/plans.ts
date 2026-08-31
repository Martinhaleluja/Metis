/**
 * The three plans, written down once.
 *
 * A price that appears in two files is a price that will one day disagree with
 * itself, and the version a customer sees is whichever one you forgot. Every
 * card, table, FAQ answer and legal page reads its numbers from here.
 *
 * The important thing these plans do NOT do is limit the software. They limit
 * what Metis is willing to pay a provider for on your behalf. A person running
 * Metis on their own API key gets screen vision and every automation on the
 * free plan, signed out, forever, because those requests never reach Metis at
 * all. Copy anywhere on the site has to keep saying that.
 */

export type PlanId = "free" | "plus" | "pro";

export type Plan = {
  id: PlanId;
  name: string;
  /** Whole US dollars per month. Formatted by `priceLabel`, never by hand. */
  priceUsd: number;
  cadence: string;
  tagline: string;
  /** What the AI on this plan is, in the customer's words rather than ours. */
  aiSummary: string;
  features: string[];
  ctaLabel: string;
  /** Drawn with the accent border on the pricing grid. Exactly one is true. */
  featured: boolean;
};

export const plans: Plan[] = [
  {
    id: "free",
    name: "Free",
    priceUsd: 0,
    cadence: "forever",
    tagline: "Everything Metis does, with a small monthly allowance on us.",
    aiSummary: "AI included — nothing to sign up for",
    features: [
      "The whole app: voice, on-screen drawing, and the bar at the top of your screen",
      "120 questions a month, on us",
      "Metis can look at your screen",
      "Remembers what you are working on",
    ],
    ctaLabel: "Try Free",
    featured: false,
  },
  {
    id: "plus",
    name: "Plus",
    priceUsd: 14,
    cadence: "per month",
    tagline: "For using it every day, without counting.",
    aiSummary: "A much larger allowance, and better models",
    features: [
      "Everything in Free",
      "No monthly question limit",
      "Sees your screen in full detail, not a scaled-down copy",
      "Background agents that get on with a job while you work",
      "Help with what is in your browser",
      "Remembers far more of what you are working on",
    ],
    ctaLabel: "Upgrade to Plus",
    featured: true,
  },
  {
    id: "pro",
    name: "Pro",
    priceUsd: 29,
    cadence: "per month",
    tagline: "Use your own AI account, and choose the model yourself.",
    aiSummary: "Everything in Plus, plus your own AI account",
    features: [
      "Everything in Plus",
      "Connect your own OpenAI, Anthropic, Gemini, Mistral or OpenRouter account",
      "Your provider charges you for the models, separately from this $29",
      "Pick the exact model, question by question",
      "Agents that can run for much longer, and hand work to each other",
    ],
    ctaLabel: "Go Pro",
    featured: false,
  },
];

export const planById: Record<PlanId, Plan> = {
  free: plans[0],
  plus: plans[1],
  pro: plans[2],
};

/** "$0" and "$14" rather than "$0.00" — none of the plans have cents yet. */
export function priceLabel(plan: Plan): string {
  return `$${plan.priceUsd}`;
}

/**
 * The comparison grid on /pricing. Kept beside the plans because a row that
 * says something different from the feature list above it is the same bug as
 * two different prices.
 */
export type ComparisonRow = {
  label: string;
  /** A string renders as text; a boolean renders as a tick or a dash. */
  values: Record<PlanId, string | boolean>;
  note?: string;
};

export const comparison: ComparisonRow[] = [
  {
    label: "Price",
    values: { free: "$0", plus: "$14/mo", pro: "$29/mo" },
  },
  {
    label: "The whole app — voice, drawing, everything",
    values: { free: true, plus: true, pro: true },
  },
  {
    label: "Questions included each month",
    values: { free: "120", plus: "No limit", pro: "No limit" },
  },
  {
    label: "Metis can look at your screen",
    values: { free: "Yes", plus: "In full detail", pro: "In full detail" },
  },
  {
    label: "Help with what is in your browser",
    values: { free: false, plus: true, pro: true },
  },
  {
    label: "Background agents",
    values: { free: false, plus: true, pro: "Longer, and in teams" },
  },
  {
    label: "How much it remembers",
    values: { free: "A little", plus: "A lot", pro: "The most" },
  },
  {
    label: "Use your own AI account",
    values: { free: false, plus: false, pro: true },
    note: "OpenAI, Anthropic, Gemini, Mistral or OpenRouter.",
  },
  {
    label: "Choose the model yourself",
    values: { free: false, plus: false, pro: true },
  },
  {
    label: "Run it with no internet at all",
    values: { free: true, plus: true, pro: true },
    note: "With a model on your own computer, nothing leaves the machine.",
  },
];
