import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getSupabase, isAuthConfigured, readableAuthError, useAuth } from "../lib/auth";

/**
 * Signing in, in a Windows 95 dialog.
 *
 * It is the same account as the desktop app: one email and password, one
 * `account_status` row, one plan. Someone who signed up in Metis on Windows
 * signs in here with what they already have, which is the whole reason this
 * page does not invent its own sign-up flow with different rules.
 */
export function Login() {
  const auth = useAuth();
  const navigate = useNavigate();

  const [mode, setMode] = useState<"in" | "up">("in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  useEffect(() => {
    if (auth.status === "signed-in") {
      navigate("/account", { replace: true });
    }
  }, [auth.status, navigate]);

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

      navigate("/account", { replace: true });
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
          <span className="text-[12px] font-bold text-black">Email address</span>
          <input
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="you@example.com"
            className="win95-field px-2 py-1.5 text-[13px] text-black outline-none"
            style={{ fontFamily: "var(--font-system)" }}
          />
        </label>

        <label className="flex flex-col gap-1.5">
          <span className="text-[12px] font-bold text-black">Password</span>
          <input
            type="password"
            required
            minLength={6}
            autoComplete={mode === "in" ? "current-password" : "new-password"}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            className="win95-field px-2 py-1.5 text-[13px] text-black outline-none"
            style={{ fontFamily: "var(--font-system)" }}
          />
        </label>

        <div role="alert" aria-live="polite" className="min-h-[18px]">
          {error && <p className="text-[12px] text-[#a80000]">{error}</p>}
        </div>

        <div className="flex items-center gap-2">
          <button type="submit" disabled={busy} className="win95-button press px-6 py-1.5">
            {busy ? "Working…" : mode === "in" ? "Sign in" : "Create account"}
          </button>
          <button
            type="button"
            onClick={() => {
              setMode(mode === "in" ? "up" : "in");
              setError(null);
            }}
            className="win95-button press px-4 py-1.5"
          >
            {mode === "in" ? "I need an account" : "I already have one"}
          </button>
        </div>
      </form>

      <p className="mt-5 text-[11px] leading-relaxed text-[#444]">
        An account is only needed for the AI Metis pays for. Metis works fully
        without one, on your own API key or a local model — and always will.
      </p>
    </Shell>
  );
}

/** The dialog frame both states share. */
function Shell({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="relative z-10 mx-auto flex min-h-[70vh] max-w-[1180px] items-center px-5 py-24">
      <div className="mx-auto w-full max-w-[460px]">
        <div className="win95-window">
          <div className="win95-titlebar">
            <span>{title}</span>
            <Link
              to="/"
              aria-label="Back to the home page"
              className="flex h-3.5 w-4 items-center justify-center border border-white border-r-[#808080] border-b-[#808080] bg-[#c0c0c0] text-[8px] font-bold text-black no-underline"
            >
              &times;
            </Link>
          </div>
          <div className="bg-[#c0c0c0] p-5" style={{ fontFamily: "var(--font-system)" }}>
            {children}
          </div>
        </div>
      </div>
    </main>
  );
}
