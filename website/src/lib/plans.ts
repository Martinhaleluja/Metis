/**
 * The three plans, written down once.
 *
 * A price that appears in two files is a price that will one day disagree with
 * itself, and the version a customer sees is whichever one you forgot. Every
 * card, table, FAQ answer and legal page reads its numbers from here.
 *
 * Keep this in step with `src/Metis.Core/Services/PlanCatalogue.cs` and with
 * the `plan_limits` rows in Postgres. Those three exist separately because each
 * has to work without the other two — the site renders before anyone signs in,
 * the app runs offline, and the gateway is the only one that can actually
 * enforce anything — but they describe the same ladder and must agree about it.
 *
 * The plans were once Free, Plus and Pro. The middle one is now Pro and the top
 * one Max, so the word "pro" means different things either side of that change.
 * Anything reading a stored plan value has to go through the app's ParsePlan
 * rather than comparing strings here.
 */

export type PlanId = "free" | "pro" | "max";

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
    tagline: "Enough to find out whether Metis is for you.",
    aiSummary: "AI included — nothing to set up",
    features: [
      "50 talk messages a month",
      "Plenty of dictation — 300 minutes a month",
      "10 agent messages a month",
      "The whole app: voice, drawing on screen, and the bar at the top",
      "Metis can look at your screen when you ask",
    ],
    ctaLabel: "Start free",
    featured: false,
  },
  {
    id: "pro",
    name: "Pro",
    priceUsd: 20,
    cadence: "per month",
    tagline: "For using it every day, without counting.",
    aiSummary: "Talk and dictate as much as you like",
    features: [
      "Everything in Free",
      "Unlimited talk messages",
      "Unlimited dictation",
      "400 agent messages a month",
      "Sees your screen in full detail, not a scaled-down copy",
      "Help with what is in your browser",
      "Remembers far more of what you are working on",
    ],
    ctaLabel: "Go Pro",
    featured: true,
  },
  {
    id: "max",
    name: "Max",
    priceUsd: 50,
    cadence: "per month",
    tagline: "Everything, and your own AI account when you want it.",
    aiSummary: "Everything in Pro, plus your own AI account",
    features: [
      "Everything in Pro",
      "2,000 agent messages a month",
      "Connect your own OpenAI, Anthropic, Gemini or OpenRouter account",
      "Pick the exact model, question by question",
      "Agents that run for longer and hand work to each other",
    ],
    ctaLabel: "Go Max",
    featured: false,
  },
];

export const planById: Record<PlanId, Plan> = {
  free: plans[0],
  pro: plans[1],
  max: plans[2],
};

/** "$0" and "$20" rather than "$0.00" — none of the plans have cents. */
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
    values: { free: "$0", pro: "$20/mo", max: "$50/mo" },
  },
  {
    label: "Talk messages a month",
    values: { free: "50", pro: "Unlimited", max: "Unlimited" },
    note: "A talk message is one answer from Metis.",
  },
  {
    label: "Dictation",
    values: { free: "300 minutes", pro: "Unlimited", max: "Unlimited" },
    note: "Speaking instead of typing. Dictation on your own computer is never counted.",
  },
  {
    label: "Agent messages a month",
    values: { free: "10", pro: "400", max: "2,000" },
    note: "An agent gets on with a job while you work. Each step it takes is one message.",
  },
  {
    label: "The whole app — voice, drawing, everything",
    values: { free: true, pro: true, max: true },
  },
  {
    label: "Metis can look at your screen",
    values: { free: "Yes", pro: "In full detail", max: "In full detail" },
  },
  {
    label: "Help with what is in your browser",
    values: { free: false, pro: true, max: true },
  },
  {
    label: "How much it remembers",
    values: { free: "A little", pro: "A lot", max: "The most" },
  },
  {
    label: "Use your own AI account",
    values: { free: false, pro: false, max: true },
    note: "OpenAI, Anthropic, Gemini or OpenRouter. Your provider bills you for the models, separately from the $50.",
  },
  {
    label: "Choose the model yourself",
    values: { free: false, pro: false, max: true },
  },
  {
    label: "Run it with no internet at all",
    values: { free: true, pro: true, max: true },
    note: "With a model on your own computer, nothing leaves the machine and nothing is counted.",
  },
];
