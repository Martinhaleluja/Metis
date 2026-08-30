import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
  connectableProviders,
  connectProvider,
  disconnectProvider,
  isGatewayConfigured,
  listConnections,
  type Connection,
  useAccountData,
} from "../lib/account";
import { getSupabase, useAuth } from "../lib/auth";
import { planById, priceLabel } from "../lib/plans";

/**
 * The account page: what you are on, what you have used, and what you have
 * connected.
 *
 * Everything about money is deliberately read-only until a payment processor is
 * chosen. Rather than hide the upgrade path behind a comment, the page says
 * plainly that plans are not open yet — read from `billing_state`, so the day
 * that row flips this page changes without being redeployed.
 */
export function Account() {
  const auth = useAuth();
  const navigate = useNavigate();
  const session = auth.status === "signed-in" ? auth.session : null;
  const { data, error, loading } = useAccountData(session);

  useEffect(() => {
    if (auth.status === "signed-out") {
      navigate("/login", { replace: true });
    }
  }, [auth.status, navigate]);

  if (auth.status === "loading" || (loading && !data)) {
    return <Frame title="Account"><p className="type-body text-ink-muted">Loading…</p></Frame>;
  }

  if (!session) return null;

  if (error) {
    return (
      <Frame title="Account">
        <p className="type-body text-ink">Your account could not be loaded: {error}</p>
      </Frame>
    );
  }

  const plan = planById[data?.status?.plan ?? "free"];
  const limits = data?.limits;
  const usage = data?.usage;
  const budget = limits?.monthly_budget_usd ?? 0;
  const spent = usage?.spend_usd ?? 0;
  const fraction = budget > 0 ? Math.min(1, spent / budget) : 0;

  return (
    <Frame title="Account">
      <div className="grid gap-6 lg:grid-cols-[1.1fr_1fr]">
        {/* ---------------------------- The plan ---------------------------- */}
        <Window title={`plan.cfg — ${plan.name}`}>
          <div className="flex items-baseline justify-between gap-4">
            <div>
              <p className="text-[20px] font-bold text-black">Metis {plan.name}</p>
              <p className="mt-0.5 text-[12px] text-[#444]">{plan.aiSummary}</p>
            </div>
            <p className="shrink-0 text-[18px] font-bold text-black">
              {priceLabel(plan)}
              {plan.priceUsd > 0 && <span className="text-[11px] font-normal">/mo</span>}
            </p>
          </div>

          <p className="mt-3 text-[11px] text-[#444]">
            Signed in as {session.user.email}
            {data?.status?.email_verified === false && " — email not confirmed yet"}
          </p>

          <div className="mt-4 border-t border-[#808080] pt-4">
            {data?.subscription ? (
              <p className="text-[12px] text-black">
                {data.subscription.status} via {data.subscription.provider}
                {data.subscription.current_period_end && (
                  <>
                    {" · "}
                    {data.subscription.cancel_at_period_end ? "ends" : "renews"}{" "}
                    {new Date(data.subscription.current_period_end).toLocaleDateString()}
                  </>
                )}
              </p>
            ) : data?.billingIsLive ? (
              <p className="text-[12px] text-[#444]">No paid subscription on this account.</p>
            ) : (
              <p className="text-[12px] leading-relaxed text-[#444]">
                Paid plans are not open yet — we are still settling on a payment
                provider. Everything Plus and Pro describe is free for everyone in
                the meantime, and nothing you are using today will be taken away
                when they do open.
              </p>
            )}

            <div className="mt-3 flex flex-wrap gap-2">
              {data?.billingIsLive ? (
                <Link to="/pricing" className="win95-button press px-4 py-1.5 no-underline">
                  Change plan
                </Link>
              ) : (
                <a href="/#join" className="win95-button press px-4 py-1.5 no-underline">
                  Join the waitlist
                </a>
              )}
              <button type="button" onClick={() => void signOut(navigate)} className="win95-button press px-4 py-1.5">
                Sign out
              </button>
            </div>
          </div>
        </Window>

        {/* ---------------------------- The meter --------------------------- */}
        <Window title="usage.log — this month">
          {budget > 0 ? (
            <>
              <p className="text-[12px] text-black">
                <strong>${spent.toFixed(4)}</strong> of ${budget.toFixed(2)} of
                included AI used
              </p>

              {/* A Windows 95 progress bar: discrete blocks, not a smooth fill. */}
              <div className="mt-2 win95-field flex h-5 items-center gap-[2px] p-[3px]">
                {Array.from({ length: 28 }, (_, index) => (
                  <span
                    key={index}
                    className="h-full flex-1"
                    style={{
                      background: index / 28 < fraction ? "#000080" : "transparent",
                    }}
                  />
                ))}
              </div>

              <p className="mt-2 text-[11px] text-[#444]">
                Resets on the 1st. {usage?.request_count ?? 0} requests,{" "}
                {usage?.agent_steps ?? 0} agent steps so far.
              </p>
            </>
          ) : (
            <p className="text-[12px] leading-relaxed text-[#444]">
              This plan has no included AI allowance yet. Requests you make on
              your own API key are never counted here — they do not reach Metis's
              servers at all.
            </p>
          )}

          {limits && (
            <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-1.5 border-t border-[#808080] pt-3 text-[11px]">
              <Row label="Requests / minute" value={String(limits.requests_per_minute)} />
              <Row
                label="Screen vision on our AI"
                value={
                  limits.max_screenshot_bytes > 0
                    ? `up to ${Math.round(limits.max_screenshot_bytes / 1024 / 1024)} MB`
                    : "not included"
                }
              />
              <Row label="Memory entries" value={limits.memory_entries_max.toLocaleString()} />
              <Row label="Models" value={limits.managed_models.length ? String(limits.managed_models.length) : "—"} />
            </dl>
          )}
        </Window>
      </div>

      {/* --------------------------- Your own AI --------------------------- */}
      <div className="mt-6">
        <Connections
          session={session}
          isPro={(data?.status?.plan ?? "free") === "pro"}
          billingIsLive={data?.billingIsLive ?? false}
        />
      </div>
    </Frame>
  );
}

/**
 * Bring your own AI.
 *
 * Only shown on Pro when billing is live. While it is not, everyone is
 * effectively entitled to everything, so hiding it would hide a working feature
 * — and the gateway is the thing that actually decides, so this is only ever a
 * question of what to draw.
 */
function Connections({
  session,
  isPro,
  billingIsLive,
}: {
  session: import("@supabase/supabase-js").Session;
  isPro: boolean;
  billingIsLive: boolean;
}) {
  const [connections, setConnections] = useState<Connection[]>([]);
  const [provider, setProvider] = useState<string>(connectableProviders[0].key);
  const [apiKey, setApiKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const available = isPro || !billingIsLive;

  useEffect(() => {
    if (!available || !isGatewayConfigured) return;
    void listConnections(session).then(setConnections);
  }, [session, available]);

  if (!available) {
    return (
      <Window title="providers.cfg">
        <p className="text-[12px] leading-relaxed text-[#444]">
          Connecting your own OpenAI, Anthropic, Gemini, Mistral or OpenRouter
          account is part of Metis Pro. Your provider bills you for what the
          models cost; the $29 is for the software.
        </p>
      </Window>
    );
  }

  if (!isGatewayConfigured) {
    return (
      <Window title="providers.cfg">
        <p className="text-[12px] text-[#444]">
          Metis's API is not connected in this build, so provider connections
          cannot be managed from the web yet. The desktop app's Setup window
          still works.
        </p>
      </Window>
    );
  }

  async function connect(event: React.FormEvent) {
    event.preventDefault();
    if (busy || !apiKey.trim()) return;

    setBusy(true);
    setMessage(null);

    const result = await connectProvider(session, provider, apiKey.trim());

    // Cleared whatever happened. A key left sitting in a form field is a key on
    // screen, in the DOM, and in whatever the browser decides to autofill next.
    setApiKey("");
    setBusy(false);

    if (result.ok) {
      setMessage(`Connected. Metis will use the key ending ${result.keyHint}.`);
      setConnections(await listConnections(session));
    } else {
      setMessage(result.error);
    }
  }

  async function disconnect(key: string) {
    setBusy(true);
    await disconnectProvider(session, key);
    setConnections(await listConnections(session));
    setBusy(false);
    setMessage("Disconnected. The stored key has been deleted.");
  }

  return (
    <Window title="providers.cfg — bring your own AI">
      <p className="text-[12px] leading-relaxed text-[#444]">
        Metis tests the key against the provider, then stores it encrypted on the
        server. It is never sent back to this page, and never written to a log.
        Your provider bills you directly for what the models cost.
      </p>

      {connections.length > 0 && (
        <ul className="mt-4 divide-y divide-[#808080] border-y border-[#808080]">
          {connections.map((connection) => (
            <li key={connection.provider} className="flex items-center justify-between gap-4 py-2">
              <span className="text-[12px] text-black">
                <strong>
                  {connectableProviders.find((entry) => entry.key === connection.provider)?.label
                    ?? connection.provider}
                </strong>
                {connection.key_hint && (
                  <span className="ml-2 text-[11px] text-[#555]">{connection.key_hint}</span>
                )}
              </span>
              <button
                type="button"
                disabled={busy}
                onClick={() => void disconnect(connection.provider)}
                className="win95-button press px-3 py-1 text-[11px]"
              >
                Disconnect
              </button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={connect} className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end">
        <label className="flex flex-col gap-1.5">
          <span className="text-[11px] font-bold text-black">Provider</span>
          <select
            value={provider}
            onChange={(event) => setProvider(event.target.value)}
            className="win95-field px-2 py-1.5 text-[12px] text-black outline-none"
            style={{ fontFamily: "var(--font-system)" }}
          >
            {connectableProviders.map((entry) => (
              <option key={entry.key} value={entry.key}>
                {entry.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-1 flex-col gap-1.5">
          <span className="text-[11px] font-bold text-black">API key</span>
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            placeholder={connectableProviders.find((entry) => entry.key === provider)?.placeholder}
            className="win95-field px-2 py-1.5 text-[12px] text-black outline-none"
            style={{ fontFamily: "var(--font-mono)" }}
          />
        </label>

        <button type="submit" disabled={busy} className="win95-button press px-5 py-1.5">
          {busy ? "Testing…" : "Connect"}
        </button>
      </form>

      <div role="status" aria-live="polite" className="min-h-[18px]">
        {message && <p className="mt-2 text-[11px] text-[#333]">{message}</p>}
      </div>
    </Window>
  );
}

async function signOut(navigate: ReturnType<typeof useNavigate>) {
  const supabase = await getSupabase();
  await supabase.auth.signOut();
  navigate("/", { replace: true });
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt className="text-[#555]">{label}</dt>
      <dd className="text-right font-bold text-black">{value}</dd>
    </>
  );
}

function Window({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="win95-window">
      <div className="win95-titlebar">
        <span>{title}</span>
      </div>
      <div className="bg-[#c0c0c0] p-5" style={{ fontFamily: "var(--font-system)" }}>
        {children}
      </div>
    </div>
  );
}

function Frame({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main id="main" className="relative z-10 mx-auto max-w-[1180px] px-5 pb-24 pt-24">
      <h1 className="type-title text-ink">{title}</h1>
      <div className="mt-8">{children}</div>
    </main>
  );
}
