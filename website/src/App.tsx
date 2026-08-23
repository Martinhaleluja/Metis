import { AppMarquee } from "./components/AppMarquee";
import { Capabilities } from "./components/Capabilities";
import { FinalCta } from "./components/FinalCta";
import { Footer } from "./components/Footer";
import { Hero } from "./components/Hero";
import { HowItWorks } from "./components/HowItWorks";
import { NeverTakesOver } from "./components/NeverTakesOver";
import { Nav } from "./components/Nav";
import { Privacy } from "./components/Privacy";
import { useWaitlist } from "./lib/waitlist";

export default function App() {
  // One instance for the whole page, so joining from either form updates the
  // count and the confirmation in both places at once.
  const waitlist = useWaitlist();

  return (
    <>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-4 focus:left-4 focus:z-[70] focus:rounded-full focus:bg-accent focus:px-4 focus:py-2 focus:text-[14px] focus:text-accent-contrast"
      >
        Skip to content
      </a>

      <div className="scroll-edge" aria-hidden="true" />
      <div className="grain-plate" aria-hidden="true" />

      <Nav />

      <main id="main">
        <Hero waitlist={waitlist} />
        <AppMarquee />
        <Capabilities />
        <NeverTakesOver />
        <HowItWorks />
        <Privacy />
        <FinalCta waitlist={waitlist} />
      </main>

      <Footer />
    </>
  );
}
