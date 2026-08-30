import { lazy, Suspense } from "react";
import { Route, Routes } from "react-router-dom";

import { Footer } from "./components/Footer";
import { Nav } from "./components/Nav";
import { RetroBackground } from "./components/RetroBackground";
import { Home } from "./pages/Home";
import { useHashScroll } from "./lib/scroll";

/**
 * The site.
 *
 * Two things about the split. The marketing pages are bundled normally, because
 * they are what almost everyone came for. The signed-in pages are loaded on
 * demand, because they pull in `@supabase/supabase-js` and a visitor reading the
 * pricing page has no business downloading a session library to do it.
 */
const PricingPage = lazy(() =>
  import("./pages/PricingPage").then((module) => ({ default: module.PricingPage })),
);
const Login = lazy(() => import("./pages/Login").then((module) => ({ default: module.Login })));
const Account = lazy(() => import("./pages/Account").then((module) => ({ default: module.Account })));
const PrivacyPolicy = lazy(() =>
  import("./pages/Legal").then((module) => ({ default: module.PrivacyPolicy })),
);
const Terms = lazy(() => import("./pages/Legal").then((module) => ({ default: module.Terms })));

export default function App() {
  useHashScroll();

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-[70] focus:rounded-full focus:bg-accent focus:px-4 focus:py-2 focus:text-[14px] focus:text-accent-contrast"
      >
        Skip to content
      </a>

      <RetroBackground />
      <Nav />

      <Suspense fallback={<Loading />}>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/pricing" element={<PricingPage />} />
          <Route path="/login" element={<Login />} />
          <Route path="/account" element={<Account />} />
          <Route path="/legal/privacy" element={<PrivacyPolicy />} />
          <Route path="/legal/terms" element={<Terms />} />

          {/* Anything else is the home page rather than a dead end. There is
              nothing on this site worth a 404 screen. */}
          <Route path="*" element={<Home />} />
        </Routes>
      </Suspense>

      <Footer />
    </>
  );
}

/**
 * Deliberately almost nothing. These chunks are small and local; a spinner that
 * appears for eighty milliseconds is worse than a moment of quiet.
 */
function Loading() {
  return <div className="min-h-[60vh]" aria-hidden="true" />;
}
