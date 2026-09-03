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

  /** Answers a month on Metis's own AI, or 0 for no cap. */
  max_turns_per_month: number;

  /** Minutes of dictation a month on Metis's own transcription, or 0 for no cap. */
  max_dictation_minutes_per_month: number;

  /** Agent messages a month. Always capped, on every plan. */
  max_agent_steps_per_month: number;
};

export type UsageThisPeriod = {
  spend_usd: number;

  /**
   * Talk messages. Deliberately excludes agent steps and dictation, which have
   * allowances of their own — counting them in here would quietly make the
   * number of answers smaller than the plan promises.
   */
  request_count: number;
  agent_steps: number;

  /** Seconds, because that is what the events record. Shown as minutes. */
  dictation_seconds: number;
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
 * Changes the plan on the account row, without paying for it.
 *
 * It goes through `set_my_test_plan` rather than updating `account_status`
 * directly, and the reason is that the direct update never worked. That table
 * has a select policy and no update policy, so the write matched zero rows and
 * came back with no error at all — the page reported success, re-read the
 * unchanged plan, and quietly reverted. A button that does nothing and says
 * nothing is worse than one that refuses, because there is no way to tell it
 * apart from a bug.
 *
 * The obvious fix — an update policy — would let anybody grant themselves Max,
 * so the function does the write instead and refuses anyone who is not staff.
 * That refusal is the normal case for a real customer and has to read like an
 * answer rather than a fault.
 *
 * There is deliberately no local override. A plan is a fact about the account,
 * held in one place, and a client-side copy of it is a second answer to a
 * question that must only have one.
 */
export async function changePlan(newPlan: PlanId): Promise<{ ok: boolean; error?: string }> {
  try {
    const supabase = await getSupabase();
    const { error } = await supabase.rpc("set_my_test_plan", { target_plan: newPlan });

    if (error) {
      return { ok: false, error: readablePlanError(error.code, error.message) };
    }

    return { ok: true };
  } catch (err) {
    return {
      ok: false,
      error: err instanceof Error ? err.message : "The plan could not be changed.",
    };
  }
}

/**
 * The two refusals `set_my_test_plan` raises, in words worth reading.
 *
 * They are matched on SQLSTATE rather than on the sentence, so rewording the
 * function's message cannot silently turn a recognised refusal into a raw
 * database error on screen. The prose is checked as well because PostgREST does
 * not always carry the code through. Anything else is passed along untouched:
 * a wrong-but-specific message beats a right-but-useless one, the same bargain
 * `readableAuthError` makes.
 */
function readablePlanError(code: string | undefined, message: string): string {
  const text = message.toLowerCase();

  // insufficient_privilege: signed in, but not a developer, founder or admin.
  if (code === "42501" || text.includes("only staff")) {
    return "Only staff accounts can switch plan without paying for it.";
  }

  // invalid_authorization_specification: the session went away mid-click.
  if (code === "28000" || text.includes("not signed in")) {
    return "You are signed out. Sign in again to change your plan.";
  }

  return message;
}

/**
 * Persists the user's chosen avatar and display name.
 */
export async function updateUserProfile(
  avatar: string,
  displayName: string,
): Promise<{ ok: boolean; error?: string }> {
  try {
    const supabase = await getSupabase();
    const { error } = await supabase.auth.updateUser({
      data: { avatar, display_name: displayName },
    });

    if (error) {
      return { ok: false, error: error.message };
    }

    return { ok: true };
  } catch (err) {
    return {
      ok: false,
      error: err instanceof Error ? err.message : "Your profile could not be saved.",
    };
  }
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
 * The providers a Max customer can connect. Matches the `ai_providers` rows the
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
