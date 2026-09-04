import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  changePlan,
  connectableProviders,
  connectProvider,
  disconnectProvider,
  isGatewayConfigured,
  listConnections,
  type Connection,
  updateUserProfile,
  useAccountData,
} from "../lib/account";
import { getSupabase, useAuth } from "../lib/auth";
import { startCheckout, type PaidPlanId } from "../lib/billing";
import { planById, plans, priceLabel, type PlanId } from "../lib/plans";

const AVATARS = ["🦊", "🦉", "⚡", "🔮", "🚀", "🤖", "🎨", "🐼", "👑"];

export function Account() {
  const auth = useAuth();
  const navigate = useNavigate();
  const session = auth.status === "signed-in" ? auth.session : null;
  const { data, error, loading, reload } = useAccountData(session);

  // Seeded from the account itself rather than from localStorage, and never
  // from a name typed into the source. The default used to be "Martin", which
  // greeted every visitor by the author's first name.
  const metadata = (session?.user.user_metadata ?? {}) as {
    avatar?: string;
    display_name?: string;
  };
  const [avatar, setAvatar] = useState(metadata.avatar ?? "\u{1F98A}");
  const [displayName, setDisplayName] = useState(
    metadata.display_name ?? session?.user.email?.split("@")[0] ?? "You",
  );
  const [profileError, setProfileError] = useState<string | null>(null);
  const [planError, setPlanError] = useState<string | null>(null);
  const [isEditingName, setIsEditingName] = useState(false);
  const [showAvatarPicker, setShowAvatarPicker] = useState(false);
  const [switchingPlan, setSwitchingPlan] = useState(false);
  const [startingCheckout, setStartingCheckout] = useState<PaidPlanId | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

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

  const isStaff =
    data?.status?.role === "developer" ||
    data?.status?.role === "founder" ||
    data?.status?.role === "staff";

  // The account row is the only authority on which plan this is.
  const currentPlanId: PlanId = (data?.status?.plan as PlanId | undefined) ?? "free";
  const plan = planById[currentPlanId] ?? planById.free;
  const limits = data?.limits;
  const usage = data?.usage;

  async function handleStartCheckout(planId: PaidPlanId) {
    setStartingCheckout(planId);
    setPlanError(null);
    const result = await startCheckout(planId);
    if (!result.ok) {
      setPlanError(result.error);
      setStartingCheckout(null);
    }
  }

  async function handleSelectPlan(targetPlan: PlanId) {
    if (switchingPlan || !session || targetPlan === currentPlanId) return;

    setSwitchingPlan(true);
    setPlanError(null);

    const result = await changePlan(targetPlan);
    if (!result.ok) {
      setPlanError(result.error ?? "The plan could not be changed.");
    }

    await reload();
    setSwitchingPlan(false);
  }

  async function handleSaveName() {
    setIsEditingName(false);
    setProfileError(null);
    const result = await updateUserProfile(avatar, displayName);
    if (!result.ok) setProfileError(result.error ?? "Your name could not be saved.");
  }

  async function handleSelectAvatar(newAvatar: string) {
    setAvatar(newAvatar);
    setShowAvatarPicker(false);
    setProfileError(null);
    const result = await updateUserProfile(newAvatar, displayName);
    if (!result.ok) setProfileError(result.error ?? "Your picture could not be saved.");
  }

  function handleFileUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    if (file.size > 8 * 1024 * 1024) {
      setProfileError("Image must be under 8 MB.");
      return;
    }
    const reader = new FileReader();
    reader.onload = (event) => {
      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement("canvas");
        const maxDim = 256;
        let width = img.width;
        let height = img.height;
        if (width > height) {
          if (width > maxDim) {
            height = Math.round((height * maxDim) / width);
            width = maxDim;
          }
        } else {
          if (height > maxDim) {
            width = Math.round((width * maxDim) / height);
            height = maxDim;
          }
        }
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext("2d");
        ctx?.drawImage(img, 0, 0, width, height);
        const dataUrl = canvas.toDataURL("image/jpeg", 0.85);
        void handleSelectAvatar(dataUrl);
      };
      img.src = event.target?.result as string;
    };
    reader.readAsDataURL(file);
  }

  return (
    <Frame
      title="Your account"
      subtitle="Your plan, what you have used this month, and the AI account Metis answers on."
    >
      {/* --------------------------- User Profile Card --------------------------- */}
      <div className="mb-6">
        <Window title="Profile &amp; Identity">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div className="flex items-center gap-4">
              <div className="relative">
                <button
                  type="button"
                  title="Click to change avatar or upload photo"
                  onClick={() => setShowAvatarPicker((prev) => !prev)}
                  className="flex h-20 w-20 cursor-pointer items-center justify-center overflow-hidden rounded-2xl bg-gradient-to-br from-brand-soft to-grape-soft text-[36px] shadow-sm ring-1 ring-line transition-transform hover:scale-105"
                >
                  {avatar.startsWith("data:") || avatar.startsWith("http") ? (
                    <img src={avatar} alt="Profile" className="h-full w-full object-cover" />
                  ) : (
                    avatar
                  )}
                </button>

                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/*"
                  onChange={handleFileUpload}
                  className="hidden"
                />

                {showAvatarPicker && (
                  <div className="card absolute top-24 left-0 z-50 flex w-[240px] flex-col gap-2 p-3">
                    <button
                      type="button"
                      onClick={() => {
                        setShowAvatarPicker(false);
                        fileInputRef.current?.click();
                      }}
                      className="btn press w-full"
                    >
                      Upload a photo
                    </button>
                    <div className="border-t border-line my-1" />
                    <span className="text-[14px] font-semibold text-ink-muted">Or pick an icon:</span>
                    <div className="flex flex-wrap gap-1.5">
                      {AVATARS.map((av) => (
                        <button
                          key={av}
                          type="button"
                          onClick={() => handleSelectAvatar(av)}
                          className="flex h-9 w-9 cursor-pointer items-center justify-center rounded-xl border border-transparent text-[18px] transition-colors hover:border-line hover:bg-surface-sunken"
                        >
                          {av}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>

              <div>
                <div className="flex items-center gap-2">
                  {isEditingName ? (
                    <div className="flex items-center gap-1.5">
                      <input
                        type="text"
                        value={displayName}
                        onChange={(e) => setDisplayName(e.target.value)}
                        className="rounded-xl border border-line bg-surface px-3 py-2 text-[16px] font-semibold text-ink outline-none focus-visible:border-accent"
                      />
                      <button
                        type="button"
                        onClick={handleSaveName}
                        className="btn press"
                      >
                        Save
                      </button>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2">
                      <span className="type-heading text-[22px] text-ink">{displayName}</span>
                      <button
                        type="button"
                        onClick={() => setIsEditingName(true)}
                        className="cursor-pointer text-[14px] font-medium text-accent underline underline-offset-2"
                      >
                        Edit
                      </button>
                    </div>
                  )}
                  <span className="pill bg-accent-wash text-accent">
                    {plan.name}
                  </span>
                </div>

                {/* The badge used to say "✓ Verified" for everyone, which made
                    it a decoration rather than a fact — and the fact it was
                    covering for matters: an account with an unverified address
                    earns no plan capability at all, on the server, whatever it
                    has paid. Somebody being refused things needs to be able to
                    see why. It is drawn only when there is an account row to
                    read it from; between signing up and the trigger seeding
                    that row, "not verified" would be a guess rather than an
                    answer. */}
                <div className="mt-2 flex flex-wrap items-center gap-2 text-[14px] text-ink-muted">
                  <span>{session.user.email}</span>
                  {data?.status && (
                    data.status.email_verified ? (
                      <span className="pill bg-leaf-soft text-leaf">
                        Verified
                      </span>
                    ) : (
                      <span
                        title="Open the link in the confirmation email we sent you."
                        className="pill bg-sun-soft text-sun"
                      >
                        Not verified — check your email
                      </span>
                    )
                  )}
                </div>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => void signOut(navigate)}
                className="btn press"
              >
                Sign out
              </button>
            </div>
          </div>

          {profileError && (
            <p className="mt-4 rounded-xl bg-blush-soft px-4 py-3 text-[14px] text-blush">
              {profileError}
            </p>
          )}
        </Window>
      </div>

      {/* ---------------------- Subscription Plan Switcher ----------------------
           Rendered from `plans` rather than written out three times.

           The hand-written version had each plan's price, name and feature list
           typed into the markup, which is how it came to be offering "Metis
           Plus, $12/mo, 2,000 questions" — three numbers that had stopped being
           true and that nobody would notice were wrong, because there was
           nothing for them to disagree with. Now there is exactly one place a
           plan is described, and this reads it. */}
      <div className="mb-6">
        <Window title="Your plan">
          {data?.subscription && (
            <div className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-accent-wash p-4 text-[14px]">
              <div>
                <span className="font-bold text-accent">
                  Active Subscription: Metis {plan.name}
                </span>
                <span className="ml-2 text-ink-muted">
                  ({data.subscription.status}, {data.subscription.cancel_at_period_end ? "ends" : "renews"}{" "}
                  {data.subscription.current_period_end
                    ? new Date(data.subscription.current_period_end).toLocaleDateString()
                    : "monthly"})
                </span>
              </div>
              <a
                href="https://polar.sh/purchases"
                target="_blank"
                rel="noreferrer"
                className="btn press px-3 py-1 text-[14px] font-bold text-ink"
              >
                Manage Billing &amp; Invoices ↗
              </a>
            </div>
          )}

          {planError && (
            <p className="mb-4 rounded-xl bg-blush-soft px-4 py-3 text-[14px] text-blush">
              {planError}
            </p>
          )}

          <div className="grid items-stretch gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {plans.map((option) => {
              const isCurrent = option.id === currentPlanId;
              const isCheckingOut = startingCheckout === option.id;
              return (
                <div
                  key={option.id}
                  className={`flex h-full flex-col justify-between rounded-[20px] border p-5 transition-shadow ${
                    isCurrent
                      ? "border-accent bg-accent-wash shadow-[var(--shadow-lift)]"
                      : "border-line bg-surface hover:shadow-[var(--shadow-card)]"
                  }`}
                >
                  <div>
                    <div className="flex items-start justify-between gap-2">
                      <span className="type-heading text-ink">
                        {option.name}
                      </span>
                      {isCurrent && (
                        <span className="pill shrink-0 bg-accent text-accent-contrast">
                          Current
                        </span>
                      )}
                    </div>

                    <p className="mt-3 font-display text-[30px] font-bold leading-none text-ink">
                      {priceLabel(option)}
                      <span className="text-[14px] font-normal text-ink-muted">
                        {option.priceUsd === 0 ? "" : "/mo"}
                      </span>
                    </p>

                    <p className="mt-2 type-caption text-ink-muted">{option.tagline}</p>

                    <ul className="mt-4 space-y-2 border-t border-line pt-4 text-[14px] text-ink-muted">
                      {option.features.map((feature) => (
                        <li key={feature} className="flex gap-2">
                          <span aria-hidden="true" className="font-semibold text-leaf">
                            &#10003;
                          </span>
                          <span>{feature}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  {isCurrent ? (
                    <button
                      type="button"
                      disabled
                      className="btn press mt-4 w-full py-1.5 text-[13.5px] font-bold opacity-60"
                    >
                      ✓ Your Current Plan
                    </button>
                  ) : option.id === "free" ? (
                    <button
                      type="button"
                      disabled
                      className="btn press mt-4 w-full py-1.5 text-[13.5px] font-bold opacity-50"
                    >
                      Included Tier
                    </button>
                  ) : (
                    <button
                      type="button"
                      disabled={isCheckingOut}
                      onClick={() => void handleStartCheckout(option.id as PaidPlanId)}
                      className="btn press mt-4 w-full py-1.5 text-[13.5px] font-bold text-accent"
                    >
                      {isCheckingOut ? "Opening checkout…" : `Upgrade to ${option.name}`}
                    </button>
                  )}
                </div>
              );
            })}
          </div>

          {isStaff && (
            <div className="mt-5 border-t border-line pt-3">
              <details className="text-[14px] text-ink-muted">
                <summary className="cursor-pointer font-bold text-accent">
                  Developer Utility: Force-switch test plan (bypasses checkout)
                </summary>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <span className="text-[14px] text-ink-muted">Staff only:</span>
                  {plans.map((opt) => (
                    <button
                      key={opt.id}
                      type="button"
                      disabled={opt.id === currentPlanId || switchingPlan}
                      onClick={() => void handleSelectPlan(opt.id)}
                      className="btn press px-2.5 py-1 text-[14px]"
                    >
                      {switchingPlan ? "…" : `Set ${opt.name}`}
                    </button>
                  ))}
                </div>
              </details>
            </div>
          )}
        </Window>
      </div>

      <div className="grid gap-6 lg:grid-cols-[1.1fr_1fr]">
        {/* ---------------------------- Plan Summary & Details ---------------------------- */}
        <Window title={`Plan Details — Metis ${plan.name}`}>
          <div className="flex items-baseline justify-between gap-4">
            <div>
              <p className="text-[18px] font-bold text-ink">Metis {plan.name}</p>
              <p className="mt-0.5 text-[14px] text-ink-muted">{plan.aiSummary}</p>
            </div>
            <p className="shrink-0 text-[16px] font-bold text-ink">
              {priceLabel(plan)}
              {plan.priceUsd > 0 && <span className="text-[14px] font-normal">/mo</span>}
            </p>
          </div>

          <div className="mt-4 border-t border-line pt-4">
            {data?.subscription ? (
              <p className="text-[14px] text-ink">
                {data.subscription.status} via {data.subscription.provider}
                {data.subscription.current_period_end && (
                  <>
                    {" · "}
                    {data.subscription.cancel_at_period_end ? "ends" : "renews"}{" "}
                    {new Date(data.subscription.current_period_end).toLocaleDateString()}
                  </>
                )}
              </p>
            ) : (
              <p className="text-[14px] leading-relaxed text-ink-muted">
                Active subscription managed directly on your Metis profile. You can change plans anytime above.
              </p>
            )}
          </div>
        </Window>

        {/* ---------------------------- The meters ---------------------------
             One per thing the plan is actually sold in.

             It used to be a single bar against a dollar budget, which is the
             one number a customer cannot act on: nobody knows whether $0.42 of
             Gemini is a lot, and there is no way to work backwards from it to
             how many more questions are left. These three are countable by the
             person spending them and are the same words the pricing page uses. */}
        <Window title="This month">
          <div className="space-y-5">
            <Meter
              label="Talk messages"
              used={usage?.request_count ?? 0}
              cap={limits?.max_turns_per_month ?? 0}
              tone="brand"
            />
            <Meter
              label="Dictation"
              used={Math.floor((usage?.dictation_seconds ?? 0) / 60)}
              cap={limits?.max_dictation_minutes_per_month ?? 0}
              unit=" min"
              tone="wave"
            />
            <Meter
              label="Agent messages"
              used={usage?.agent_steps ?? 0}
              cap={limits?.max_agent_steps_per_month ?? 0}
              tone="grape"
            />
          </div>

          <p className="mt-6 border-t border-line pt-4 type-caption text-ink-muted">
            Resets on the 1st. Answers on a model running on your own computer
            are never counted, on any plan.
          </p>
        </Window>
      </div>

      {/* --------------------------- Your own AI --------------------------- */}
      <div className="mt-6">
        <Connections session={session} isMax={currentPlanId === "max"} />
      </div>
    </Frame>
  );
}

/**
 * One allowance, and how much of it is gone.
 *
 * A cap of zero means the plan does not count this at all, drawn as a full
 * quiet bar rather than an empty one: an empty bar reads as "none included",
 * which is the opposite of what it means.
 *
 * The bar is a Windows 95 progress control — discrete blocks rather than a
 * smooth fill — because that is the chrome the rest of the site is built in.
 * The blocks are flex children of a fixed-height row, so it is correct at any
 * width without a media query.
 */
function Meter({
  label,
  used,
  cap,
  unit = "",
  tone = "brand",
}: {
  label: string;
  used: number;
  cap: number;
  unit?: string;
  tone?: "brand" | "sun" | "leaf" | "wave" | "grape";
}) {
  const unlimited = cap <= 0;
  const fraction = unlimited ? 1 : Math.min(1, used / cap);

  // Amber past three quarters, red once it is gone. The colour is the only
  // warning before a refusal, so it has to arrive while there is still
  // something left to do about it.
  //
  // It is never the *only* signal: the figure beside the label always says
  // "N of M" in words, so a reader who cannot separate these hues still knows
  // exactly where they stand. That is the line between colour-coding and
  // colour-only meaning.
  // Written out rather than interpolated. Tailwind scans source text for
  // complete class names, so `bg-${tone}` produces nothing at all -- the class
  // is never generated and the bar silently renders transparent.
  const toneBar = {
    brand: "bg-brand",
    sun: "bg-sun",
    leaf: "bg-leaf",
    wave: "bg-wave",
    grape: "bg-grape",
  }[tone];

  const bar = unlimited
    ? "bg-ink-muted/40"
    : fraction >= 1
      ? "bg-blush"
      : fraction >= 0.75
        ? "bg-sun"
        : toneBar;

  return (
    <div>
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
        <span className="text-[14px] font-medium text-ink">{label}</span>
        <span className="text-[14px] tabular-nums text-ink-muted">
          {unlimited ? "Unlimited" : `${used}${unit} of ${cap}${unit}`}
        </span>
      </div>

      <div
        className="mt-2 h-2.5 overflow-hidden rounded-full bg-surface-sunken"
        role="progressbar"
        aria-label={label}
        aria-valuenow={unlimited ? undefined : used}
        aria-valuemin={0}
        aria-valuemax={unlimited ? undefined : cap}
        aria-valuetext={unlimited ? "Unlimited" : `${used}${unit} of ${cap}${unit}`}
      >
        <div
          className={`h-full rounded-full transition-[width] duration-500 ${bar}`}
          style={{ width: `${Math.max(fraction * 100, unlimited ? 100 : 3)}%` }}
        />
      </div>
    </div>
  );
}

/**
 * Bring your own AI. Max only, and only Max.
 *
 * It used to be drawn for anybody while billing was off, on the theory that
 * nothing is being charged yet so nothing should be withheld yet. The gateway
 * does not agree: `POST /v1/connections` checks the Max entitlement whatever
 * `billing_is_live` says, so a Free account was shown the form, given a key to
 * paste, and refused with a 403 once it had. Being told no is survivable. Being
 * invited, made to fetch a key from another website, and then told no is not,
 * and it is the client that was wrong here rather than the server.
 *
 * So this asks the same question the server asks. Staff also qualify, but the
 * refusal comes back as a sentence rather than a silence, and a staff account
 * that lands on the explanation can move itself to Max in the panel above.
 */
function Connections({
  session,
  isMax,
}: {
  session: import("@supabase/supabase-js").Session;
  isMax: boolean;
}) {
  const [connections, setConnections] = useState<Connection[]>([]);
  const [provider, setProvider] = useState<string>(connectableProviders[0].key);
  const [apiKey, setApiKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!isMax || !isGatewayConfigured) return;
    void listConnections(session).then(setConnections);
  }, [session, isMax]);

  if (!isMax) {
    return (
      <Window title="Your own AI account">
        <p className="text-[14px] leading-relaxed text-ink-muted">
          Using your own OpenAI, Anthropic, Gemini or OpenRouter account is part
          of Metis {planById.max.name}, at {priceLabel(planById.max)} a month.
          That is what the plan buys; your provider charges you separately for
          what the models themselves cost.
        </p>
      </Window>
    );
  }

  if (!isGatewayConfigured) {
    return (
      <Window title="providers.cfg">
        <p className="text-[14px] text-ink-muted">
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
    <Window title="Your own AI account">
      <p className="text-[14px] leading-relaxed text-ink-muted">
        Metis checks the key works, then encrypts it. It is never shown again —
        not here, not in the app, not to us. Your provider charges you directly
        for what the models cost.
      </p>

      {connections.length > 0 && (
        <ul className="mt-4 divide-y divide-line border-y border-line">
          {connections.map((connection) => (
            <li key={connection.provider} className="flex items-center justify-between gap-4 py-2">
              <span className="text-[14px] text-ink">
                <strong>
                  {connectableProviders.find((entry) => entry.key === connection.provider)?.label
                    ?? connection.provider}
                </strong>
                {connection.key_hint && (
                  <span className="ml-2 text-[14px] text-ink-muted">{connection.key_hint}</span>
                )}
              </span>
              <button
                type="button"
                disabled={busy}
                onClick={() => void disconnect(connection.provider)}
                className="btn press px-3 py-1 text-[14px]"
              >
                Disconnect
              </button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={connect} className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end">
        <label className="flex flex-col gap-1.5">
          <span className="text-[14px] font-bold text-ink">Provider</span>
          <select
            value={provider}
            onChange={(event) => setProvider(event.target.value)}
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[14px] text-ink outline-none"
          >
            {connectableProviders.map((entry) => (
              <option key={entry.key} value={entry.key}>
                {entry.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-1 flex-col gap-1.5">
          <span className="text-[14px] font-bold text-ink">API key</span>
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            placeholder={connectableProviders.find((entry) => entry.key === provider)?.placeholder}
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[14px] text-ink outline-none"
          />
        </label>

        <button type="submit" disabled={busy} className="btn press px-5 py-1.5">
          {busy ? "Testing…" : "Connect"}
        </button>
      </form>

      <div role="status" aria-live="polite" className="min-h-[18px]">
        {message && <p className="mt-2 text-[14px] text-ink-muted">{message}</p>}
      </div>
    </Window>
  );
}

async function signOut(navigate: ReturnType<typeof useNavigate>) {
  const supabase = await getSupabase();
  await supabase.auth.signOut();
  navigate("/", { replace: true });
}

function Window({
  title,
  children,
  action,
}: {
  title: string;
  children: React.ReactNode;
  action?: React.ReactNode;
}) {
  return (
    <section className="card overflow-hidden">
      <header className="flex items-center justify-between gap-3 border-b border-line px-6 py-4">
        <h2 className="type-heading text-ink">{title}</h2>
        {action}
      </header>
      <div className="p-6">{children}</div>
    </section>
  );
}

/**
 * The page shell.
 *
 * The heading sits on the same aurora wash the marketing hero uses, so that
 * signing in reads as going further into one product rather than arriving at a
 * different, plainer one. An account page is where people go when something is
 * wrong; it should not also feel like the lights went out.
 */
function Frame({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}) {
  return (
    <main id="main" className="relative z-10 pb-24">
      <div
        className="pt-28 pb-16"
        style={{
          background:
            "radial-gradient(50% 60% at 15% 0%, var(--brand-soft) 0%, transparent 62%)," +
            "radial-gradient(45% 55% at 88% 10%, var(--grape-soft) 0%, transparent 58%)," +
            "linear-gradient(180deg, var(--surface-sunken) 0%, var(--page) 100%)",
        }}
      >
        <div className="mx-auto max-w-[1180px] px-5">
          <h1 className="type-display text-ink">{title}</h1>
          {subtitle && (
            <p className="mt-3 max-w-[52ch] type-body text-ink-muted">{subtitle}</p>
          )}
        </div>
      </div>
      <div className="mx-auto -mt-8 max-w-[1180px] px-5">{children}</div>
    </main>
  );
}
