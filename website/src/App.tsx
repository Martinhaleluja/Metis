import { AppMarquee } from "./components/AppMarquee";
import { Capabilities } from "./components/Capabilities";
import { FinalCta } from "./components/FinalCta";
import { Footer } from "./components/Footer";
import { Hero } from "./components/Hero";
import { Nav } from "./components/Nav";
import { Privacy } from "./components/Privacy";
import { RetroBackground } from "./components/RetroBackground";
import { TypingConversation } from "./components/TypingConversation";
import { VideoShowcase } from "./components/VideoShowcase";
import { useWaitlist } from "./lib/waitlist";

export default function App() {
  const waitlist = useWaitlist();

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

      <main id="main" className="relative z-10">
        <Hero waitlist={waitlist} />
        <AppMarquee />
        <VideoShowcase />
        <TypingConversation />
        <Capabilities />
        <Privacy />
        <FinalCta waitlist={waitlist} />
      </main>

      <Footer />
    </>
  );
}
