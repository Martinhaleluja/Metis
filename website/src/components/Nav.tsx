import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../lib/auth";

/**
 * Taskbar buttons. The anchors are absolute rather than bare fragments so they
 * work from /pricing and /account too, where there is no #privacy to scroll to
 * on the current page.
 */
const links = [
  { href: "/#showcase", label: "See it work" },
  { href: "/#capabilities", label: "What it does" },
  { href: "/#privacy", label: "Privacy" },
];

const taskbarButton =
  "h-[30px] px-3 flex items-center text-[12px] text-white/90 font-semibold rounded border " +
  "transition-colors bg-[#3b7ddd]/30 border-[#2860b5]/50 " +
  "shadow-[inset_0_1px_0_rgba(255,255,255,0.1)] hover:bg-[#5a9af3]/40 no-underline";

const systemFont = { fontFamily: "Segoe UI, Tahoma, sans-serif" };

export function Nav() {
  const auth = useAuth();
  const location = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // Closed on navigation. Most of these are hash links to the page already
  // underneath the menu, so without this the menu stays open covering the very
  // thing it just scrolled to.
  useEffect(() => setMenuOpen(false), [location.pathname, location.hash]);

  useEffect(() => {
    if (!menuOpen) return;

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setMenuOpen(false);
    }

    function onPointerDown(event: MouseEvent) {
      if (!menuRef.current?.contains(event.target as Node)) setMenuOpen(false);
    }

    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("mousedown", onPointerDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("mousedown", onPointerDown);
    };
  }, [menuOpen]);

  return (
    <header className="fixed inset-x-0 top-0 z-50">
      <div
        className="flex h-[48px] items-center px-2 shadow-lg border-t border-[#5ba0f5]"
        style={{
          background: "linear-gradient(to bottom, #3168d5, #4889e4 30%, #2050bc)",
        }}
      >
        {/* ------------------------- Start ------------------------------
            On a wide screen this is simply the way home, and the links sit
            beside it. On a narrow one it opens a Start menu holding the same
            links — which is where a taskbar keeps them, and the reason this
            site is drawn as a taskbar at all.

            The alternative, and what was here before, was `hidden md:flex` on
            the nav and nothing in its place: below 768px the site had no
            navigation whatsoever. Pricing, privacy and everything else were
            reachable only by knowing the URL. */}
        <div className="relative" ref={menuRef}>
          <Link
            to="/"
            onClick={(event) => {
              // Only below md, where this button is doing the menu's job.
              if (window.matchMedia("(min-width: 768px)").matches) return;
              event.preventDefault();
              setMenuOpen((open) => !open);
            }}
            aria-haspopup="true"
            aria-expanded={menuOpen}
            className="flex items-center gap-2 h-[34px] rounded-r-full pl-2 pr-4 border border-[#0e7c0e] transition-all hover:brightness-110 active:brightness-90"
            style={{
              background: "linear-gradient(to bottom, #3c9b3c, #1d6e1d)",
              boxShadow: "inset 0 1px 0 rgba(255,255,255,0.35)",
            }}
          >
            <img
              src="/metis-mark.png"
              alt=""
              width={22}
              height={22}
              className="drop-shadow-sm"
            />
            <span
              className="text-white font-bold text-[14px] italic drop-shadow-[0_1px_1px_rgba(0,0,0,0.3)]"
              style={{ fontFamily: "Trebuchet MS, sans-serif" }}
            >
              start
            </span>
          </Link>

          {menuOpen && (
            <nav
              aria-label="Menu"
              className="absolute left-0 top-[42px] flex w-[248px] overflow-hidden border-2 border-[#dfdfdf] bg-[#c0c0c0] shadow-[4px_4px_0_rgba(0,0,0,0.35)] md:hidden"
              style={{ borderRightColor: "#404040", borderBottomColor: "#404040" }}
            >
              {/* The vertical banner down the left of a real Start menu. */}
              <div
                className="flex w-[26px] shrink-0 items-end justify-center pb-3"
                style={{ background: "linear-gradient(to bottom, #1a55c0, #0a2a70)" }}
                aria-hidden="true"
              >
                {/* Anchored to the bottom rather than padded from the top, so
                    the word cannot be clipped when the menu is short. */}
                <span className="whitespace-nowrap text-[12px] font-bold italic text-white/80 [writing-mode:vertical-rl] [transform:rotate(180deg)]">
                  Metis
                </span>
              </div>

              <ul className="flex-1 py-1.5">
                <StartItem to="/">Home</StartItem>
                <StartItem to="/pricing">Pricing</StartItem>
                {links.map((link) => (
                  <StartItem key={link.href} href={link.href}>
                    {link.label}
                  </StartItem>
                ))}

                <li className="my-1.5 border-t border-[#808080] border-b border-b-white" />

                {auth.status === "signed-in" ? (
                  <StartItem to="/account">Your account</StartItem>
                ) : (
                  <StartItem href="/#join">Join the waitlist</StartItem>
                )}
              </ul>
            </nav>
          )}
        </div>

        {/* Separator */}
        <div className="w-px h-6 mx-2 bg-[#1a50b0] shadow-[1px_0_0_rgba(255,255,255,0.15)]" />

        {/* Nav links as taskbar buttons */}
        <nav aria-label="Sections" className="hidden items-center gap-1 flex-1 md:flex">
          <Link to="/pricing" className={taskbarButton} style={systemFont}>
            Pricing
          </Link>
          {links.map((link) => (
            <a key={link.href} href={link.href} className={taskbarButton} style={systemFont}>
              {link.label}
            </a>
          ))}
        </nav>

        {/* System tray area */}
        <div className="ml-auto flex items-center gap-3">
          {auth.status === "signed-in" ? (
            <Link
              to="/account"
              className="h-[30px] px-4 flex items-center text-[12px] text-white font-bold rounded border transition-colors bg-[#3b7ddd]/40 border-[#2860b5]/50 shadow-[inset_0_1px_0_rgba(255,255,255,0.15)] hover:bg-[#5a9af3]/50 no-underline"
              style={systemFont}
            >
              Account
            </Link>
          ) : (
            <a
              href="/#join"
              className="h-[30px] px-4 flex items-center text-[12px] text-white font-bold rounded border transition-colors bg-[#3b7ddd]/40 border-[#2860b5]/50 shadow-[inset_0_1px_0_rgba(255,255,255,0.15)] hover:bg-[#5a9af3]/50"
              style={systemFont}
            >
              Join Waitlist
            </a>
          )}
          <div
            className="hidden sm:flex h-[30px] px-3 items-center text-[11px] text-white/70 rounded border-l border-[#1040a0] shadow-[inset_1px_0_0_rgba(255,255,255,0.1)]"
            style={{
              ...systemFont,
              background: "linear-gradient(to bottom, #1a55c0, #1545a5)",
            }}
            aria-hidden="true"
          >
            {new Date().toLocaleTimeString([], {
              hour: "2-digit",
              minute: "2-digit",
            })}
          </div>
        </div>
      </div>
    </header>
  );
}

/**
 * One row of the Start menu.
 *
 * 44px tall rather than the 20-odd pixels a real Windows menu used, because
 * this one is being tapped with a thumb: below the standard minimum touch
 * target the retro accuracy stops being charming and starts being a menu you
 * cannot hit.
 */
function StartItem({
  to,
  href,
  children,
}: {
  to?: string;
  href?: string;
  children: React.ReactNode;
}) {
  const className =
    "flex h-[44px] items-center px-4 text-[13px] font-semibold text-black no-underline " +
    "hover:bg-[#000080] hover:text-white focus-visible:bg-[#000080] focus-visible:text-white";

  return (
    <li>
      {to ? (
        <Link to={to} className={className} style={systemFont}>
          {children}
        </Link>
      ) : (
        <a href={href} className={className} style={systemFont}>
          {children}
        </a>
      )}
    </li>
  );
}
