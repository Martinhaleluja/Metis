import { Link } from "react-router-dom";
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

export function Nav() {
  const auth = useAuth();

  return (
    <header className="fixed inset-x-0 top-0 z-50">
      <div
        className="flex h-[48px] items-center px-2 shadow-lg border-t border-[#5ba0f5]"
        style={{
          background:
            "linear-gradient(to bottom, #3168d5, #4889e4 30%, #2050bc)",
        }}
      >
        {/* Start button */}
        <Link
          to="/"
          className="flex items-center gap-2 h-[34px] rounded-r-full pl-2 pr-4 border border-[#0e7c0e] transition-all hover:brightness-110 active:brightness-90"
          style={{
            background:
              "linear-gradient(to bottom, #3c9b3c, #1d6e1d)",
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

        {/* Separator */}
        <div className="w-px h-6 mx-2 bg-[#1a50b0] shadow-[1px_0_0_rgba(255,255,255,0.15)]" />

        {/* Nav links as taskbar buttons */}
        <nav
          aria-label="Sections"
          className="hidden items-center gap-1 flex-1 md:flex"
        >
          <Link
            to="/pricing"
            className="h-[30px] px-3 flex items-center text-[12px] text-white/90 font-semibold rounded border transition-colors bg-[#3b7ddd]/30 border-[#2860b5]/50 shadow-[inset_0_1px_0_rgba(255,255,255,0.1)] hover:bg-[#5a9af3]/40 no-underline"
            style={{ fontFamily: "Segoe UI, Tahoma, sans-serif" }}
          >
            Pricing
          </Link>
          {links.map((link) => (
            <a
              key={link.href}
              href={link.href}
              className="h-[30px] px-3 flex items-center text-[12px] text-white/90 font-semibold rounded border transition-colors bg-[#3b7ddd]/30 border-[#2860b5]/50 shadow-[inset_0_1px_0_rgba(255,255,255,0.1)] hover:bg-[#5a9af3]/40"
              style={{ fontFamily: "Segoe UI, Tahoma, sans-serif" }}
            >
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
              style={{ fontFamily: "Segoe UI, Tahoma, sans-serif" }}
            >
              Account
            </Link>
          ) : (
            <a
              href="/#join"
              className="h-[30px] px-4 flex items-center text-[12px] text-white font-bold rounded border transition-colors bg-[#3b7ddd]/40 border-[#2860b5]/50 shadow-[inset_0_1px_0_rgba(255,255,255,0.15)] hover:bg-[#5a9af3]/50"
              style={{ fontFamily: "Segoe UI, Tahoma, sans-serif" }}
            >
              Join Waitlist
            </a>
          )}
          <div
            className="hidden sm:flex h-[30px] px-3 items-center text-[11px] text-white/70 rounded border-l border-[#1040a0] shadow-[inset_1px_0_0_rgba(255,255,255,0.1)]"
            style={{
              fontFamily: "Segoe UI, Tahoma, sans-serif",
              background:
                "linear-gradient(to bottom, #1a55c0, #1545a5)",
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
