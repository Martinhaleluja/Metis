import type { Session } from "@supabase/supabase-js";
import { useCallback, useEffect, useState } from "react";
import { getSupabase } from "./auth";
import type { PlanId } from "./plans";

/**
 * Everything the account page shows, read from the two places that are allowed
 * to say it.
 *
 * The split is worth understanding. Supabase answers questions about *this*
 * account — the plan, the usage, the subscription — under row level security,
 * so the browser can ask directly and read only its own rows. The gateway
 * answers questions that involve a secret, which today means the bring-your-own
 * provider connections: adding one needs Metis's server-side vault key, and no
 * amount of row level security would make it safe to do that from a browser.
 */

const gatewayUrl = (import.meta.env.VITE_METIS_API_URL as string | undefined)?.replace(/\/+$/, "");

export const isGatewayConfigured = Boolean(gatewayUrl);

export type AccountStatus = {
  role: string;
  plan: PlanId;
  email_verified: boolean;
};

export type PlanLimits = {
  plan: PlanId;
  monthly_budget_usd: number;
  max_screenshot_bytes: number;
  requests_per_minute: number;
  memory_entries_max: number;
  managed_models: string[];
  max_turns_per_month: number;
};

export type UsageThisPeriod = {
  spend_usd: number;
  request_count: number;
  agent_steps: number;
  period_start: string;
};

export type Subscription = {
  provider: string;
  status: string;
  plan_key: PlanId | null;
  current_period_end: string | null;
  cancel_at_period_end: boolean;
};

export type Connection = {
  provider: string;
  model: string | null;
  key_hint: string | null;
  last_tested_at: string | null;
  last_test_ok: boolean | null;
};

export type AccountData = {
  status: AccountStatus | null;
  limits: PlanLimits | null;
  usage: UsageThisPeriod | null;
  subscription: Subscription | null;
  billingIsLive: boolean;
};

/**
 * Loads the whole account page in one pass.
 *
 * Every query is allowed to come back empty without that being an error. A brand
 * new account has no subscription, has used nothing, and — very briefly, before
 * the sign-up trigger runs — may not even have a status row. None of those are
 * worth an error message; they are just a quieter version of the same page.
 */
export function useAccountData(session: Session | null) {
  const [data, setData] = useState<AccountData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(async () => {
    if (!session) return;

    setLoading(true);
    try {
      const supabase = await getSupabase();

      const [status, limits, usage, subscription, billing] = await Promise.all([
        supabase.from("account_status").select("role, plan, email_verified").maybeSingle(),
        supabase.from("plan_limits").select("*"),
        supabase.rpc("my_usage_this_period"),
        supabase.rpc("my_subscription"),
        supabase.from("billing_state").select("billing_is_live").maybeSingle(),
      ]);

      const plan = (status.data?.plan ?? "free") as PlanId;
      const limitsForPlan =
        (limits.data as PlanLimits[] | null)?.find((row) => row.plan === plan) ?? null;

      setData({
        status: (status.data as AccountStatus | null) ?? null,
        limits: limitsForPlan,
        usage: firstRow<UsageThisPeriod>(usage.data),
        subscription: firstRow<Subscription>(subscription.data),
        billingIsLive: Boolean(billing.data?.billing_is_live),
      });
      setError(null);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Your account could not be loaded.");
    } finally {
      setLoading(false);
    }
  }, [session]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { data, error, loading, reload };
}

/**
 * A Postgres function returning a table comes back as an array of rows; the
 * same function returning nothing comes back as an empty one. Both mean "there
 * isn't one" here.
 */
function firstRow<T>(value: unknown): T | null {
  if (Array.isArray(value)) return (value[0] as T) ?? null;
  return (value as T) ?? null;
}

// ------------------------- Bring your own provider -------------------------

/**
 * The providers a Pro customer can connect. Matches the `ai_providers` rows the
 * gateway validates against; listed here so the form can be rendered before the
 * network answers.
 */
export const connectableProviders = [
  { key: "openai", label: "OpenAI", placeholder: "sk-…" },
  { key: "anthropic", label: "Anthropic", placeholder: "sk-ant-…" },
  { key: "google", label: "Google Gemini", placeholder: "AIza…" },
  { key: "openrouter", label: "OpenRouter", placeholder: "sk-or-…" },
] as const;

async function gateway(
  path: string,
  session: Session,
  init: RequestInit = {},
): Promise<Response> {
  if (!gatewayUrl) {
    throw new Error("Metis's API is not configured for this build.");
  }

  return fetch(`${gatewayUrl}${path}`, {
    ...init,
    headers: {
      ...(init.headers ?? {}),
      "Content-Type": "application/json",
      Authorization: `Bearer ${session.access_token}`,
    },
  });
}

export async function listConnections(session: Session): Promise<Connection[]> {
  const response = await gateway("/v1/connections", session);
  if (!response.ok) return [];

  const body = (await response.json()) as { connections?: Connection[] };
  return body.connections ?? [];
}

/**
 * Sends a key to the gateway, which tests it against the provider before
 * storing it encrypted.
 *
 * The key goes in a POST body over HTTPS and never into a URL, a query string,
 * or anything this page keeps. What comes back is a four-character hint and
 * nothing else — there is no endpoint anywhere that reads a stored key back to
 * a browser, deliberately, including to the browser that sent it.
 */
export async function connectProvider(
  session: Session,
  provider: string,
  apiKey: string,
  model?: string,
): Promise<{ ok: true; keyHint: string } | { ok: false; error: string }> {
  const response = await gateway("/v1/connections", session, {
    method: "POST",
    body: JSON.stringify({ provider, apiKey, model: model || null }),
  });

  const body = (await response.json().catch(() => ({}))) as {
    keyHint?: string;
    error?: string;
  };

  return response.ok
    ? { ok: true, keyHint: body.keyHint ?? "…" }
    : { ok: false, error: body.error ?? "That connection could not be saved." };
}

export async function disconnectProvider(session: Session, provider: string): Promise<boolean> {
  const response = await gateway(`/v1/connections/${encodeURIComponent(provider)}`, session, {
    method: "DELETE",
  });

  return response.ok;
}
