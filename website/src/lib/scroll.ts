import { useEffect } from "react";
import { useLocation } from "react-router-dom";

/**
 * Keeps the marketing page's hash anchors working now that there is a router.
 *
 * A plain `<a href="#pricing">` on the page the anchor lives on is still handled
 * by the browser and needs nothing from us. What breaks is arriving from another
 * route — `/pricing` → `/#pricing` — because React Router changes the URL
 * without the browser ever performing a fragment navigation. This runs the jump
 * after the new route has painted.
 */
export function useHashScroll() {
  const { pathname, hash } = useLocation();

  useEffect(() => {
    if (!hash) {
      // A route change with no fragment should start at the top, the way
      // following a link between two pages normally does.
      window.scrollTo({ top: 0, behavior: "instant" as ScrollBehavior });
      return;
    }

    const target = document.getElementById(hash.slice(1));
    if (!target) return;

    // One frame of delay: the section has to exist before it can be measured.
    const id = window.requestAnimationFrame(() => {
      target.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    return () => window.cancelAnimationFrame(id);
  }, [pathname, hash]);
}
