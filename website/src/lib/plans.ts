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
    tagline: "Try Metis with a small allowance of AI on us.",
    aiSummary: "Google Gemini, on Metis's key, text only",
    features: [
      "The full desktop app: notch bar, voice, on-screen drawing",
      "Ask Metis anything in text on our Gemini key",
      "No screen vision on Metis's AI — that is what Plus pays for",
      "Unlimited screen vision and automation on your own API key",
      "50 memory entries",
    ],
    ctaLabel: "Try Free",
    featured: false,
  },
  {
    id: "plus",
    name: "Plus",
    priceUsd: 14,
    cadence: "per month",
    tagline: "Metis looks at your screen, and we pay for the looking.",
    aiSummary: "Managed cost-efficient providers, chosen and paid for by Metis",
    features: [
      "Everything in Free",
      "Screen vision on Metis's AI — send a screenshot, get an answer",
      "Browser assistance and stronger automation",
      "Background agents, up to 30 steps per task",
      "500 memory entries",
      "Higher request rate and a larger monthly AI allowance",
    ],
    ctaLabel: "Upgrade to Plus",
    featured: true,
  },
  {
    id: "pro",
    name: "Pro",
    priceUsd: 29,
    cadence: "per month",
    tagline: "Bring your own AI account and drive it from Metis.",
    aiSummary: "Bring Your Own AI, plus everything Plus is managed for",
    features: [
      "Everything in Plus",
      "Connect your own OpenAI, Anthropic, Google Gemini, Mistral or OpenRouter account",
      "Your provider bills you for model usage, separately from this $29",
      "Full provider and model control on every request",
      "Advanced multi-agent workflows, up to 60 steps per task",
      "5,000 memory entries",
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
    label: "Desktop app, notch bar, voice, drawing",
    values: { free: true, plus: true, pro: true },
  },
  {
    label: "AI Metis pays for",
    values: {
      free: "Gemini, text only",
      plus: "Managed, cost-efficient",
      pro: "Managed + your own",
    },
  },
  {
    label: "Screen vision on Metis's AI",
    values: { free: false, plus: true, pro: true },
    note: "Free can still see your screen on your own API key.",
  },
  {
    label: "Screen vision on your own API key",
    values: { free: true, plus: true, pro: true },
    note: "Never metered. Those requests do not touch Metis's servers.",
  },
  {
    label: "Browser assistance",
    values: { free: false, plus: true, pro: true },
  },
  {
    label: "Background agents",
    values: { free: false, plus: "30 steps per task", pro: "60 steps per task" },
  },
  {
    label: "Memory entries",
    values: { free: "50", plus: "500", pro: "5,000" },
  },
  {
    label: "Connect your own provider account",
    values: { free: false, plus: false, pro: true },
    note: "OpenAI, Anthropic, Google Gemini, Mistral, OpenRouter.",
  },
  {
    label: "Choose the exact model per request",
    values: { free: false, plus: false, pro: true },
  },
  {
    label: "Run fully offline through Ollama",
    values: { free: true, plus: true, pro: true },
    note: "Local models are yours. Metis never sees those turns.",
  },
];
