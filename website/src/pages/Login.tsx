import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { getSupabase, isAuthConfigured, readableAuthError, useAuth } from "../lib/auth";

/**
 * Signing in, in a Windows 95 dialog.
 *
 * It is the same account as the desktop app: one email and password, one
 * `account_status` row, one plan. Someone who signed up in Metis on Windows
 * signs in here with what they already have, which is the whole reason this
 * page does not invent its own sign-up flow with different rules.
 *
 * Somebody who arrived here from a pricing button was in the middle of buying
 * something, so `?next=` carries where they were and they are put back there
 * rather than on the account page wondering what happened to the checkout.
 */
export function Login() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const next = safeNext(params.get("next"));

  const [mode, setMode] = useState<"in" | "up">("in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  useEffect(() => {
    if (auth.status === "signed-in") {
      navigate(next, { replace: true });
    }
  }, [auth.status, navigate, next]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (busy) return;

    setBusy(true);
    setError(null);

    try {
      const supabase = await getSupabase();
      const result =
        mode === "in"
          ? await supabase.auth.signInWithPassword({ email: email.trim(), password })
          : await supabase.auth.signUp({ email: email.trim(), password });

      if (result.error) {
        setError(readableAuthError(result.error.message));
        return;
      }

      // Signing up with email confirmation on returns a user and no session.
      // That is not a failure and must not read like one.
      if (mode === "up" && !result.data.session) {
        setSent(true);
        return;
      }

      navigate(next, { replace: true });
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Something went wrong. Try again.");
    } finally {
      setBusy(false);
      // The password has done its one job.
      setPassword("");
    }
  }

  if (!isAuthConfigured) {
    return (
      <Shell title="Sign in">
        <p className="type-body text-ink-muted">
          Accounts are not connected in this build yet.
        </p>
      </Shell>
    );
  }

  if (sent) {
    return (
      <Shell title="Check your email">
        <p className="type-body text-ink">
          We sent a confirmation link to <strong>{email}</strong>. Open it, then
          come back and sign in.
        </p>
      </Shell>
    );
  }

  return (
    <Shell title={mode === "in" ? "Sign in to Metis" : "Create a Metis account"}>
      <form onSubmit={submit} className="flex flex-col gap-4">
        <label className="flex flex-col gap-1.5">
          <span className="text-[12px] font-bold text-ink">Email address</span>
          <input
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="you@example.com"
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[13px] text-ink outline-none"
          />
        </label>

        <label className="flex flex-col gap-1.5">
          <span className="text-[12px] font-bold text-ink">Password</span>
          <input
            type="password"
            required
            minLength={6}
            autoComplete={mode === "in" ? "current-password" : "new-password"}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className="rounded-lg border border-line bg-surface px-3 py-2 px-2 py-1.5 text-[13px] text-ink outline-none"
          />
        </label>

        <div role="alert" aria-live="polite" className="min-h-[18px]">
          {error && <p className="text-[12px] text-[#a80000]">{error}</p>}
        </div>

        <div className="flex items-center gap-2">
          <button type="submit" disabled={busy} className="btn press px-6 py-1.5">
            {busy ? "Working…" : mode === "in" ? "Sign in" : "Create account"}
          </button>
          <button
            type="button"
            onClick={() => {
              setMode(mode === "in" ? "up" : "in");
              setError(null);
            }}
            className="btn press px-4 py-1.5"
          >
            {mode === "in" ? "I need an account" : "I already have one"}
          </button>
        </div>
      </form>

      <p className="mt-5 text-[11px] leading-relaxed text-[#444]">
        An account is only needed for the AI Metis pays for. Metis works fully
        without one, on a model running on your own computer.
      </p>
    </Shell>
  );
}

/**
 * Where to go once the password is accepted.
 *
 * `next` is read out of the address bar, which anyone can write, so it is only
 * honoured when it is a path on this site. Two slashes or a slash-backslash
 * start a protocol-relative URL — `//example.com` is somebody else's origin
 * wearing a path's clothes — and that is the standard way a login page is
 * turned into a link that launders a phishing site through a domain people
 * trust. Anything else falls back to the account page.
 */
function safeNext(value: string | null): string {
  if (!value || !value.startsWith("/")) return "/account";
  if (value.startsWith("//") || value.startsWith("/\\")) return "/account";
  return value;
}

/** The dialog frame both states share. */
function Shell({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="relative z-10 mx-auto flex min-h-[70vh] max-w-[1180px] items-center px-5 py-24">
      <div className="mx-auto w-full max-w-[460px]">
        <div className="card">
          <div className="panel-title">
            <span>{title}</span>
            <Link
              to="/"
              aria-label="Back to the home page"
              className="ml-auto grid h-7 w-7 place-items-center rounded-full text-[15px] leading-none text-ink-muted transition-colors hover:bg-surface-sunken hover:text-ink no-underline"
            >
              &times;
            </Link>
          </div>
          <div className="bg-surface p-5">
            {children}
          </div>
        </div>
      </div>
    </main>
  );
}
