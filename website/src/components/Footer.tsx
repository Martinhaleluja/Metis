import { Link } from "react-router-dom";
import { useAuth } from "../lib/auth";
import { supportMailto } from "../lib/support";

/**
 * The page's foot.
 *
 * It used to mirror the XP taskbar at the bottom of the page — a second Start
 * button, taskbar links and a live clock — which meant the same inline
 * gradients existed twice with nothing shared between them. Rewritten alongside
 * the nav for the same reason: there was no class to restyle.
 *
 * The clock is gone deliberately. It was a joke about the taskbar, and without
 * the taskbar it is just a clock on a marketing site.
 */
export function Footer() {
  const auth = useAuth();
  const support = supportMailto(
    auth.status === "signed-in" ? auth.session.user.id : undefined,
  );

  const linkClass =
    "text-[13.5px] text-ink-muted no-underline transition-colors hover:text-ink";

  return (
    <footer className="mt-24 border-t border-line bg-surface-sunken">
      <div className="mx-auto flex max-w-[1180px] flex-col gap-6 px-5 py-10 sm:flex-row sm:items-center">
        <div className="flex items-center gap-2.5">
          <span
            aria-hidden="true"
            className="h-[22px] w-[22px] rounded-full bg-accent"
          />
          <span className="type-heading text-[15px] text-ink">Metis</span>
        </div>

        <nav
          aria-label="Footer"
          className="flex flex-wrap items-center gap-x-6 gap-y-3 sm:ml-auto"
        >
          <Link to="/pricing" className={linkClass}>
            Pricing
          </Link>
          <Link to="/legal/privacy" className={linkClass}>
            Privacy
          </Link>
          <Link to="/legal/terms" className={linkClass}>
            Terms
          </Link>
          <a href={support} className={linkClass}>
            Support
          </a>
          <a
            href="https://github.com/Martinhaleluja/Metis"
            className={linkClass}
            rel="noreferrer"
          >
            GitHub
          </a>
        </nav>
      </div>

      <div className="mx-auto max-w-[1180px] px-5 pb-10">
        <p className="type-caption text-ink-muted">
          Metis teaches you to use your computer. It never takes it over.
        </p>
      </div>
    </footer>
  );
}
