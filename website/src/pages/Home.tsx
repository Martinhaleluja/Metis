import { AppMarquee } from "../components/AppMarquee";
import { Byoa } from "../components/Byoa";
import { Capabilities } from "../components/Capabilities";
import { Faq } from "../components/Faq";
import { FinalCta } from "../components/FinalCta";
import { Hero } from "../components/Hero";
import { Pricing } from "../components/Pricing";
import { Privacy } from "../components/Privacy";
import { Providers } from "../components/Providers";
import { TypingConversation } from "../components/TypingConversation";
import { VideoShowcase } from "../components/VideoShowcase";
import { useWaitlist } from "../lib/waitlist";

/**
 * The marketing page, in the order the argument is made.
 *
 * Show it working, say what it does, then say where the screen goes, then say
 * what it costs. Pricing after privacy on purpose: the plans are about which AI
 * pays for the answer, so the privacy section is what makes them make sense
 * rather than a disclaimer bolted on afterwards.
 */
export function Home() {
  const waitlist = useWaitlist();

  return (
    <main id="main" className="relative z-10">
      <Hero waitlist={waitlist} />
      <AppMarquee />
      <VideoShowcase />
      <TypingConversation />
      <Capabilities />
      <Privacy />
      <Pricing />
      <Providers />
      <Byoa />
      <Faq />
      <FinalCta waitlist={waitlist} />
    </main>
  );
}
