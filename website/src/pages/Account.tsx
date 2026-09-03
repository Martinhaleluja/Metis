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
    <Frame title="Your Account">
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
                  className="flex h-16 w-16 items-center justify-center rounded-full border-2 border-[currentColor] bg-[#e0e0ff] text-[32px] shadow-sm hover:scale-105 transition-transform overflow-hidden"
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
                  <div className="absolute top-20 left-0 z-50 flex flex-col gap-2 rounded border border-line bg-surface p-2.5 shadow-lg w-[220px]">
                    <button
                      type="button"
                      onClick={() => {
                        setShowAvatarPicker(false);
                        fileInputRef.current?.click();
                      }}
                      className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press w-full py-1.5 text-[11.5px] font-bold text-center flex items-center justify-center gap-1.5"
                    >
                      <span>📁</span> Upload Photo...
                    </button>
                    <div className="border-t border-line my-1" />
                    <span className="text-[10px] text-[#444] font-bold">Or pick an icon:</span>
                    <div className="flex flex-wrap gap-1.5">
                      {AVATARS.map((av) => (
                        <button
                          key={av}
                          type="button"
                          onClick={() => handleSelectAvatar(av)}
                          className="h-7 w-7 rounded text-[16px] hover:bg-white flex items-center justify-center border border-transparent hover:border-line"
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
                        className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-0.5 text-[14px] font-bold text-ink outline-none"
                      />
                      <button
                        type="button"
                        onClick={handleSaveName}
                        className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-2 py-0.5 text-[11px]"
                      >
                        Save
                      </button>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2">
                      <span className="text-[18px] font-bold text-ink">{displayName}</span>
                      <button
                        type="button"
                        onClick={() => setIsEditingName(true)}
                        className="text-[11px] text-accent underline hover:text-blue-800"
                      >
                        Edit
                      </button>
                    </div>
                  )}
                  <span className="rounded bg-[#d0ffd0] border border-[#30b158] px-2 py-0.5 text-[10px] font-bold text-[#107030]">
                    {plan.name.toUpperCase()}
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
                <div className="mt-1 flex flex-wrap items-center gap-2 text-[12px] text-[#444]">
                  <span>{session.user.email}</span>
                  {data?.status && (
                    data.status.email_verified ? (
                      <span className="rounded bg-[#e8f5e9] px-1.5 py-0.2 text-[10px] font-semibold text-[#2e7d32]">
                        ✓ Verified
                      </span>
                    ) : (
                      <span
                        title="Open the link in the confirmation email we sent you."
                        className="rounded border border-[#e65100] bg-[#fff8e1] px-1.5 py-0.2 text-[10px] font-semibold text-[#e65100]"
                      >
                        ! Not verified — check your email
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
                className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-4 py-1.5 text-[11.5px]"
              >
                Sign out
              </button>
            </div>
          </div>

          {profileError && (
            <p className="mt-3 border border-[#c62828] bg-[#ffebee] px-3 py-2 text-[11.5px] text-[#b71c1c]">
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
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded border border-[currentColor] bg-[#e8f0fe] p-3 text-[12px]">
              <div>
                <span className="font-bold text-accent">
                  Active Subscription: Metis {plan.name}
                </span>
                <span className="ml-2 text-[#444]">
                  ({data.subscription.status}, {data.subscription.cancel_at_period_end ? "ends" : "renews"}{" "}
                  {data.subscription.current_period_end
                    ? new Date(data.subscription.current_period_end).toLocaleDateString()
                    : "monthly"})
                </span>
              </div>
              <a
                href="https://sandbox.polar.sh/purchases"
                target="_blank"
                rel="noreferrer"
                className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-3 py-1 text-[11px] font-bold text-ink"
              >
                Manage Billing &amp; Invoices ↗
              </a>
            </div>
          )}

          {planError && (
            <p className="mb-3 border border-[#c62828] bg-[#ffebee] px-3 py-2 text-[11.5px] text-[#b71c1c]">
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
                  className={`flex h-full flex-col justify-between rounded border-2 p-4 ${
                    isCurrent ? "border-[currentColor] bg-[#f0f4ff]" : "border-line bg-white"
                  }`}
                >
                  <div>
                    <div className="flex items-start justify-between gap-2">
                      <span className="text-[13px] font-bold uppercase tracking-wide text-accent">
                        Metis {option.name}
                      </span>
                      {isCurrent && (
                        <span className="shrink-0 rounded bg-accent px-2 py-0.5 text-[9.5px] font-bold text-white">
                          CURRENT
                        </span>
                      )}
                    </div>

                    <p className="mt-2 text-[22px] font-bold text-ink">
                      {priceLabel(option)}
                      <span className="text-[12px] font-normal text-[#666]">
                        {option.priceUsd === 0 ? "" : "/mo"}
                      </span>
                    </p>

                    <p className="mt-1 text-[11px] leading-snug text-[#555]">{option.tagline}</p>

                    <ul className="mt-3 space-y-1 border-t border-[#ddd] pt-2 text-[11px] text-[#333]">
                      {option.features.map((feature) => (
                        <li key={feature} className="flex gap-1.5">
                          <span aria-hidden="true" className="text-[#107030]">
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
                      className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] mt-4 w-full py-1.5 text-[11.5px] font-bold opacity-60"
                    >
                      ✓ Your Current Plan
                    </button>
                  ) : option.id === "free" ? (
                    <button
                      type="button"
                      disabled
                      className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] mt-4 w-full py-1.5 text-[11.5px] font-bold opacity-50"
                    >
                      Included Tier
                    </button>
                  ) : (
                    <button
                      type="button"
                      disabled={isCheckingOut}
                      onClick={() => void handleStartCheckout(option.id as PaidPlanId)}
                      className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press mt-4 w-full py-1.5 text-[11.5px] font-bold text-accent"
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
              <details className="text-[11px] text-[#555]">
                <summary className="cursor-pointer font-bold text-accent">
                  Developer Utility: Force-switch test plan (bypasses checkout)
                </summary>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  <span className="text-[10px] text-[#666]">Staff only:</span>
                  {plans.map((opt) => (
                    <button
                      key={opt.id}
                      type="button"
                      disabled={opt.id === currentPlanId || switchingPlan}
                      onClick={() => void handleSelectPlan(opt.id)}
                      className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-2.5 py-1 text-[10.5px]"
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
              <p className="mt-0.5 text-[12px] text-[#444]">{plan.aiSummary}</p>
            </div>
            <p className="shrink-0 text-[16px] font-bold text-ink">
              {priceLabel(plan)}
              {plan.priceUsd > 0 && <span className="text-[11px] font-normal">/mo</span>}
            </p>
          </div>

          <div className="mt-4 border-t border-line pt-4">
            {data?.subscription ? (
              <p className="text-[12px] text-ink">
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
              <p className="text-[12px] leading-relaxed text-[#444]">
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
          <Meter
            label="Talk messages"
            used={usage?.request_count ?? 0}
            cap={limits?.max_turns_per_month ?? 0}
          />
          <Meter
            label="Dictation"
            used={Math.floor((usage?.dictation_seconds ?? 0) / 60)}
            cap={limits?.max_dictation_minutes_per_month ?? 0}
            unit=" min"
          />
          <Meter
            label="Agent messages"
            used={usage?.agent_steps ?? 0}
            cap={limits?.max_agent_steps_per_month ?? 0}
          />

          <p className="mt-4 border-t border-line pt-3 text-[11px] leading-relaxed text-[#444]">
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
}: {
  label: string;
  used: number;
  cap: number;
  unit?: string;
}) {
  const unlimited = cap <= 0;
  const fraction = unlimited ? 1 : Math.min(1, used / cap);
  const blocks = 24;

  // Amber past three quarters, red once it is gone. The colour is the only
  // warning before a refusal, so it has to arrive while there is still
  // something left to do about it.
  const fill = unlimited
    ? "#9e9e9e"
    : fraction >= 1
      ? "#b71c1c"
      : fraction >= 0.75
        ? "#e65100"
        : "currentColor";

  return (
    <div className="mb-4 last:mb-0">
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
        <span className="text-[12px] text-ink">{label}</span>
        <span className="text-[11.5px] text-[#444]">
          {unlimited ? "Unlimited" : `${used}${unit} of ${cap}${unit}`}
        </span>
      </div>

      <div
        className="rounded-lg border border-line bg-surface px-3 py-2 mt-1.5 flex h-5 items-center gap-[2px] p-[3px]"
        role="progressbar"
        aria-label={label}
        aria-valuenow={unlimited ? undefined : used}
        aria-valuemin={0}
        aria-valuemax={unlimited ? undefined : cap}
        aria-valuetext={unlimited ? "Unlimited" : `${used}${unit} of ${cap}${unit}`}
      >
        {Array.from({ length: blocks }, (_, index) => (
          <span
            key={index}
            className="h-full flex-1"
            style={{ background: index / blocks < fraction ? fill : "transparent" }}
          />
        ))}
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
        <p className="text-[12px] leading-relaxed text-[#444]">
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
    <Window title="Your own AI account">
      <p className="text-[12px] leading-relaxed text-[#444]">
        Metis checks the key works, then encrypts it. It is never shown again —
        not here, not in the app, not to us. Your provider charges you directly
        for what the models cost.
      </p>

      {connections.length > 0 && (
        <ul className="mt-4 divide-y divide-line border-y border-line">
          {connections.map((connection) => (
            <li key={connection.provider} className="flex items-center justify-between gap-4 py-2">
              <span className="text-[12px] text-ink">
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
                className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-3 py-1 text-[11px]"
              >
                Disconnect
              </button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={connect} className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end">
        <label className="flex flex-col gap-1.5">
          <span className="text-[11px] font-bold text-ink">Provider</span>
          <select
            value={provider}
            onChange={(event) => setProvider(event.target.value)}
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[12px] text-ink outline-none"
          >
            {connectableProviders.map((entry) => (
              <option key={entry.key} value={entry.key}>
                {entry.label}
              </option>
            ))}
          </select>
        </label>

        <label className="flex flex-1 flex-col gap-1.5">
          <span className="text-[11px] font-bold text-ink">API key</span>
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            placeholder={connectableProviders.find((entry) => entry.key === provider)?.placeholder}
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[12px] text-ink outline-none"
          />
        </label>

        <button type="submit" disabled={busy} className="press rounded-lg border border-line bg-surface px-3 py-1.5 text-[13px] press px-5 py-1.5">
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

function Window({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="card">
      <div className="panel-title">
        <span>{title}</span>
      </div>
      <div className="bg-surface p-5">
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
