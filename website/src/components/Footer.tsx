import { Link } from "react-router-dom";
import { useAuth } from "../lib/auth";
import { supportMailto } from "../lib/support";

export function Footer() {
  const year = new Date().getFullYear();

  // Asked for the same reason the taskbar asks: so a signed-in customer's
  // support email carries their account id and a signed-out visitor's carries
  // no line at all. `Nav` already opens this subscription on every page, so
  // there is no new download here — only a second listener on the same client.
  const auth = useAuth();
  const support = supportMailto(
    auth.status === "signed-in" ? auth.session.user.id : undefined,
  );

  return (
    <footer
      className="border-t border-[#5ba0f5]"
      style={{
        background:
          "linear-gradient(to bottom, #3168d5, #4889e4 30%, #2050bc)",
      }}
    >
      <div className="mx-auto max-w-[1180px] px-4 py-3 flex items-center justify-between">
        <div className="flex items-center gap-3">
          {/* Mini Start button */}
          <div
            className="flex items-center gap-2 rounded-r-full pl-2 pr-3 py-1 border border-[#0e7c0e]"
            style={{
              background: "linear-gradient(to bottom, #3c9b3c, #1d6e1d)",
            }}
          >
            <img src="/metis-mark.png" alt="" width={16} height={16} />
            <span
              className="text-white font-bold text-[11px] italic"
              style={{ fontFamily: "Trebuchet MS, sans-serif" }}
            >
              start
            </span>
          </div>
          <span
            className="text-white/60 text-[11px] hidden sm:inline"
            style={{ fontFamily: "var(--font-system)" }}
          >
            &copy; {year} Metis &middot; An AI companion for Windows
          </span>
        </div>

        <div className="flex items-center gap-3">
          <Link
            to="/pricing"
            className="text-white/60 hover:text-white text-[11px] transition-colors no-underline"
            style={{ fontFamily: "var(--font-system)" }}
          >
            Pricing
          </Link>
          <Link
            to="/legal/privacy"
            className="text-white/60 hover:text-white text-[11px] transition-colors no-underline"
            style={{ fontFamily: "var(--font-system)" }}
          >
            Privacy
          </Link>
          <Link
            to="/legal/terms"
            className="text-white/60 hover:text-white text-[11px] transition-colors no-underline hidden sm:inline"
            style={{ fontFamily: "var(--font-system)" }}
          >
            Terms
          </Link>
          <a
            href={support}
            className="text-white/60 hover:text-white text-[11px] transition-colors no-underline"
            style={{ fontFamily: "var(--font-system)" }}
          >
            Support
          </a>
          <a
            href="https://github.com/Martinhaleluja/Metis"
            className="text-white/60 hover:text-white text-[11px] transition-colors"
            style={{ fontFamily: "var(--font-system)" }}
          >
            Source
          </a>
          <div
            className="h-[26px] px-3 flex items-center text-[10px] text-white/50 rounded border-l border-[#1040a0] shadow-[inset_1px_0_0_rgba(255,255,255,0.1)]"
            style={{
              fontFamily: "var(--font-system)",
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
    </footer>
  );
}
