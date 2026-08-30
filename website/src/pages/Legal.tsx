import { Link } from "react-router-dom";
import { plans, priceLabel } from "../lib/plans";

/**
 * The privacy policy and the terms.
 *
 * Both carry the same warning the repository's own PRIVACY.md carries, and for
 * the same reason: this describes what the software actually does, which is the
 * part worth getting right first, and it is not a substitute for a document a
 * lawyer has read. Metis processes information belonging to people who never
 * installed it — anyone whose message happens to be on the screen — and that is
 * the part that needs a professional.
 */

const plus = plans[1];
const pro = plans[2];

export function PrivacyPolicy() {
  return (
    <Document title="Privacy" file="privacy_policy.txt">
      <Draft />

      <H>The short version</H>
      <ul>
        <li>
          Metis captures your screen <strong>only when you ask it something</strong>.
          It does not watch between requests and it does not record.
        </li>
        <li>
          Where that capture goes depends on whose AI answers. On your own API key
          or a local model, it never touches a Metis server. On the AI Metis pays
          for, it passes through Metis&rsquo;s gateway on the way to the provider.
        </li>
        <li>
          Content an application marks as private &mdash; banking apps, password
          managers, view-once photos in WhatsApp and Signal &mdash; is blacked out
          before anything is sent, on every route.
        </li>
        <li>API keys are never stored in plain text and never sent to a browser.</li>
        <li>Chats and memory are encrypted on your own machine, and you can delete all of it.</li>
      </ul>

      <H>Where screen content goes, per route</H>
      <p>
        This is the part that changed when Metis started offering AI of its own,
        and it is written per route because the honest answer is different for
        each.
      </p>

      <H3>The AI Metis pays for (Free and Plus)</H3>
      <p>
        Your question, and on {plus.name} the screenshot, are sent over HTTPS to
        Metis&rsquo;s gateway. The gateway calls an AI provider using
        Metis&rsquo;s own API key and streams the answer back. Metis is in the
        middle of that request by definition &mdash; it is the account being
        billed for it.
      </p>
      <p>
        What the gateway keeps is the metering record: which model answered, how
        many tokens it used, how long it took, whether it succeeded, and the
        estimated cost. It does not store your question, the screenshot, or the
        answer. On Free there is no screen vision on Metis&rsquo;s AI at all, so
        that route carries text only.
      </p>

      <H3>Your own provider account ({pro.name}, {priceLabel(pro)}/month)</H3>
      <p>
        You connect an OpenAI, Anthropic, Google Gemini, Mistral or OpenRouter
        account. The key is tested once, then held encrypted in Supabase Vault and
        never returned to any browser, including yours. Requests run on your
        credentials and are billed to you by that provider, separately from what
        you pay Metis. What the provider keeps is governed by their privacy
        policy, not this one.
      </p>

      <H3>Your own API key in the desktop app</H3>
      <p>
        A key pasted into Metis on Windows is stored in Windows Credential
        Manager, encrypted to your Windows account, and used to call the provider
        directly from your machine. No Metis server is in that path on any plan,
        and those requests are never metered.
      </p>

      <H3>A local model</H3>
      <p>
        Point Metis at Ollama and nothing leaves the computer at all.
      </p>

      <H>What is never captured</H>
      <ul>
        <li>
          <strong>Windows that mark themselves as protected.</strong> Applications
          can tell Windows &ldquo;do not record this&rdquo;, and they use it for
          exactly what you would expect. Metis paints those regions black before
          the image is encoded, so the pixels never exist in anything that gets
          sent. The model is told a region was withheld, so it says it cannot see
          rather than guessing.
        </li>
        <li>
          <strong>Password fields.</strong> No password field&rsquo;s contents,
          name or identifier is ever read.
        </li>
        <li>
          <strong>Applications you exclude.</strong> Name any window in Settings
          and it is blacked out the same way.
        </li>
      </ul>

      <H>What is stored on Metis&rsquo;s servers</H>
      <ul>
        <li>Your email address and a password hash, held by Supabase Auth.</li>
        <li>Your role, plan, and whether your email is confirmed.</li>
        <li>
          One metering row per request Metis paid for: model, provider, token
          counts, latency, status, estimated cost. No content.
        </li>
        <li>
          For {pro.name}: an encrypted provider key and a four-character hint, plus
          an audit entry recording that you connected or disconnected it.
        </li>
        <li>Your waitlist entry, if you joined it.</li>
      </ul>

      <H>Deleting it</H>
      <p>
        Chats, memory and skills are on your own machine and are removed from
        Settings &rarr; Memory &amp; privacy. Connected provider keys are deleted
        from your account page, which removes the encrypted secret itself and not
        merely the row pointing at it. For anything else, write to us and we will
        delete the account.
      </p>

      <H>Getting in touch</H>
      <p>
        Open an issue at{" "}
        <a
          className="text-accent underline underline-offset-4"
          href="https://github.com/Martinhaleluja/Metis"
        >
          github.com/Martinhaleluja/Metis
        </a>
        .
      </p>
    </Document>
  );
}

export function Terms() {
  return (
    <Document title="Terms" file="terms.txt">
      <Draft />

      <H>What Metis is</H>
      <p>
        Metis is a Windows application that reads your screen when you ask it to,
        explains what it sees, and draws on the screen to show you where to look.
        It teaches; it does not operate your computer for you. Background agents
        are a separate, opt-in feature that does perform tasks, within a folder
        you nominate and stopping for your approval before anything destructive.
      </p>

      <H>The licence</H>
      <p>
        Metis is proprietary software. The source is published so it can be read
        and audited, and all rights are reserved: publishing source is not the
        same as granting a licence to copy, modify or redistribute it. The full
        terms ship with the application and are in the repository.
      </p>

      <H>Plans and payment</H>
      <p>
        {plans[0].name} is {priceLabel(plans[0])}. {plus.name} is{" "}
        {priceLabel(plus)} per month and {pro.name} is {priceLabel(pro)} per
        month. Those prices cover the software and the AI Metis buys on your
        behalf, within the monthly allowance shown on your account page.
      </p>
      <p>
        On {pro.name}, model usage on a provider account you connect is billed by
        that provider, directly to you, and is entirely separate from what you pay
        Metis. Metis does not mark it up and does not see the invoice.
      </p>
      <p>
        <strong>No plan is on sale yet.</strong> Until a payment provider is
        settled, every paid capability is available to everyone at no charge. When
        that changes it will be announced first, and nothing you rely on today
        that runs on your own API key will be taken away.
      </p>

      <H>Cancelling</H>
      <p>
        A subscription can be cancelled at any time from your account page. Access
        continues to the end of the period already paid for, and there is no
        cancellation fee. Metis keeps working afterwards on the free plan, and on
        your own API key exactly as before.
      </p>

      <H>Allowances and fair use</H>
      <p>
        Paid plans include a monthly allowance of AI rather than an unlimited
        amount, because the underlying providers charge per request and an
        unlimited promise would be one Metis could not keep. The current numbers
        are on your account page and can change; a reduction would be announced
        before it took effect. Requests on your own key are not counted.
      </p>
      <p>
        Metis may temporarily reduce access to its own AI if costs run beyond what
        it can carry. Local models and your own connected provider are unaffected
        by that, structurally &mdash; those requests never reach Metis&rsquo;s
        servers.
      </p>

      <H>What we ask of you</H>
      <ul>
        <li>Do not use Metis to break the law or someone else&rsquo;s rights.</li>
        <li>
          Do not resell access to the AI included with your plan, or point
          automated traffic at it.
        </li>
        <li>Keep the credentials for your account and any connected provider to yourself.</li>
      </ul>

      <H>No warranty</H>
      <p>
        Metis is provided as it is. It reads a screen and asks a language model
        about it, and language models are confidently wrong sometimes. Check
        anything that matters before acting on it, particularly where a step is
        irreversible.
      </p>
    </Document>
  );
}

// ------------------------------- Chrome -------------------------------

function Draft() {
  return (
    <div className="mb-6 border-l-4 border-[#000080] bg-[#ffffe1] px-4 py-3">
      <p className="text-[12px] leading-relaxed text-black">
        <strong>Draft &mdash; not yet reviewed by a lawyer.</strong> This
        describes what the software actually does, which is the part worth
        getting right first. It is not a substitute for a policy checked against
        the data protection law of everywhere Metis is distributed.
      </p>
    </div>
  );
}

function Document({
  title,
  file,
  children,
}: {
  title: string;
  file: string;
  children: React.ReactNode;
}) {
  return (
    <main id="main" className="relative z-10 mx-auto max-w-[1180px] px-5 pb-24 pt-24">
      <div className="mx-auto max-w-[820px]">
        <h1 className="type-title text-ink">{title}</h1>

        <div className="mt-8 win95-window">
          <div className="win95-titlebar">
            <span>{file} &mdash; Notepad</span>
            <Link
              to="/"
              aria-label="Back to the home page"
              className="flex h-3.5 w-4 items-center justify-center border border-white border-r-[#808080] border-b-[#808080] bg-[#c0c0c0] text-[8px] font-bold text-black no-underline"
            >
              &times;
            </Link>
          </div>

          <div className="bg-[#c0c0c0] p-3">
            <div
              className="win95-field space-y-3 p-6 text-[13px] leading-relaxed text-black [&_li]:ml-5 [&_li]:list-disc [&_p]:max-w-[74ch] [&_ul]:space-y-1.5"
              style={{ fontFamily: "var(--font-system)" }}
            >
              {children}
            </div>
          </div>
        </div>

        <p className="mt-6 type-caption text-ink-muted">
          <Link to="/legal/privacy" className="text-accent underline underline-offset-4">
            Privacy
          </Link>
          {" · "}
          <Link to="/legal/terms" className="text-accent underline underline-offset-4">
            Terms
          </Link>
        </p>
      </div>
    </main>
  );
}

function H({ children }: { children: React.ReactNode }) {
  return <h2 className="!mt-7 text-[15px] font-bold text-black first:!mt-0">{children}</h2>;
}

function H3({ children }: { children: React.ReactNode }) {
  return <h3 className="!mt-5 text-[13px] font-bold text-[#000080]">{children}</h3>;
}
