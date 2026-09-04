import { ListIcon as List } from "@phosphor-icons/react/dist/icons/List";
import { XIcon as X } from "@phosphor-icons/react/dist/icons/X";
import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../lib/auth";
import { supportMailto } from "../lib/support";

/**
 * The site's navigation.
 *
 * This was a Windows XP taskbar — a blue gradient bar pinned to the top, a
 * green Start button, taskbar-style section buttons and a system tray with a
 * live clock. It was the single loudest piece of the retro costume, and none of
 * it was reusable: there was no class to restyle, only inline gradients, so it
 * is rewritten rather than adjusted.
 *
 * What replaced it is a floating glass bar using the same material as the
 * desktop app's notch, so the site and the product read as the same thing.
 *
 * The anchors stay absolute rather than bare fragments so they work from
 * /pricing and /account too, where there is no #privacy on the current page to
 * scroll to.
 */
const links = [
  { href: "/#capabilities", label: "What it does" },
  { href: "/#showcase", label: "See it work" },
  { href: "/#privacy", label: "Privacy" },
];

const linkClass =
  "px-3 py-2 text-[13.5px] font-medium text-ink-muted rounded-lg no-underline " +
  "transition-colors hover:text-ink hover:bg-surface-sunken";

export function Nav() {
  const auth = useAuth();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // The account id goes into the support email only when there is one. `useAuth`
  // is already being asked here for the account button, so this costs nothing
  // extra, and the id is read from the session rather than the token beside it.
  const support = supportMailto(
    auth.status === "signed-in" ? auth.session.user.id : undefined,
  );

  // Closed on navigation. Most of these are hash links to the page already
  // underneath the menu, so without this the menu stays open covering the very
  // thing it just scrolled to.
  useEffect(() => setMenuOpen(false), [location.pathname, location.hash]);

  useEffect(() => {
    if (!menuOpen) return;

    const onPointerDown = (event: PointerEvent) => {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMenuOpen(false);
    };

    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [menuOpen]);

  return (
    <header className="fixed inset-x-0 top-0 z-50 px-4 pt-4 sm:px-5">
      <div className="glass mx-auto flex h-[60px] max-w-[1180px] items-center gap-2 rounded-[18px] px-3 sm:px-4">
        <Link
          to="/"
          className="flex items-center gap-2.5 rounded-xl px-1 py-2 no-underline"
          aria-label="Metis home"
        >
          <span
            aria-hidden="true"
            className="h-[28px] w-[28px] rounded-[10px] bg-gradient-to-br from-accent to-grape"
          />
          <span className="type-heading text-[16px] text-ink">Metis</span>
        </Link>

        <nav
          aria-label="Sections"
          className="ml-6 hidden flex-1 items-center gap-1 md:flex"
        >
          <Link to="/pricing" className={linkClass}>
            Pricing
          </Link>
          {links.map((link) => (
            <a key={link.href} href={link.href} className={linkClass}>
              {link.label}
            </a>
          ))}
          <a href={support} className={linkClass}>
            Support
          </a>
        </nav>

        <div className="ml-auto flex items-center gap-2">
          {auth.status === "signed-in" ? (
            <Link
              to="/account"
              className="btn-cta press !px-5 !py-2.5 text-[14px]"
            >
              Your account
            </Link>
          ) : (
            <a
              href="/#join"
              className="btn-cta press !px-5 !py-2.5 text-[14px]"
            >
              Get Metis
            </a>
          )}

          {/* Below md the sections collapse into a menu. It is a real button
              with aria-expanded rather than the old Start button, which
              announced itself as nothing at all. */}
          <div className="relative md:hidden" ref={menuRef}>
            <button
              type="button"
              aria-label="Menu"
              aria-expanded={menuOpen}
              onClick={() => setMenuOpen((open) => !open)}
              className="flex h-11 w-11 cursor-pointer items-center justify-center rounded-xl border border-line bg-surface text-ink transition-colors hover:bg-surface-sunken"
            >
              {menuOpen ? (
                <X size={18} weight="bold" aria-hidden="true" />
              ) : (
                <List size={18} weight="bold" aria-hidden="true" />
              )}
            </button>

            {menuOpen && (
              <div
                className="card absolute right-0 top-11 w-56 overflow-hidden p-1"
                role="menu"
              >
                <MenuItem to="/">Home</MenuItem>
                <MenuItem to="/pricing">Pricing</MenuItem>
                {links.map((link) => (
                  <MenuItem key={link.href} href={link.href}>
                    {link.label}
                  </MenuItem>
                ))}
                <MenuItem href={support}>Support</MenuItem>
                {auth.status === "signed-in" ? (
                  <MenuItem to="/account">Your account</MenuItem>
                ) : (
                  <MenuItem href="/#join">Join the waitlist</MenuItem>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </header>
  );
}

/**
 * A row in the small-screen menu. Deliberately 44px tall: it is a touch target,
 * and the rest of the bar's sizing is for a pointer.
 */
function MenuItem({
  to,
  href,
  children,
}: {
  to?: string;
  href?: string;
  children: React.ReactNode;
}) {
  const className =
    "flex h-11 items-center rounded-lg px-3 text-[14px] text-ink no-underline hover:bg-surface-sunken";

  if (to) {
    return (
      <Link to={to} className={className} role="menuitem">
        {children}
      </Link>
    );
  }

  return (
    <a href={href} className={className} role="menuitem">
      {children}
    </a>
  );
}
