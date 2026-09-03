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

const proPlan = plans[1];
const maxPlan = plans[2];

export function PrivacyPolicy() {
  return (
    <Document title="Privacy" file="Privacy">
      <Draft />

      <H>The short version</H>
      <ul>
        <li>
          Metis captures your screen <strong>only when you ask it something</strong>.
          It does not watch between requests and it does not record.
        </li>
        <li>
          Where that picture goes depends on which AI answers. With your own AI
          account, or a model on your own computer, it never touches a Metis
          server. With the AI included in your plan, it passes through Metis on
          the way, because we are the ones paying for the answer.
        </li>
        <li>
          Content an application marks as private &mdash; banking apps, password
          managers, view-once photos in WhatsApp and Signal &mdash; is blacked out
          before anything is sent, on every route.
        </li>
        <li>AI keys are encrypted and never shown again, to you or to us.</li>
        <li>Your conversations stay encrypted on your own computer, and you can delete all of it.</li>
      </ul>

      <H>Where your screen goes</H>
      <p>
        This is the part that changed when Metis started providing AI of its own,
        and the honest answer is different depending on which AI answered you —
        so it is written out for each.
      </p>

      <H3>The AI included with your plan</H3>
      <p>
        Your question and, when you ask about your screen, the picture are sent
        securely to Metis, and Metis passes them to an AI provider using its own
        account. We are in the middle of that request because we are the ones
        paying for the answer; there is no way to buy an answer on somebody
        else&rsquo;s behalf without the request coming through us.
      </p>
      <p>
        What we keep afterwards is a record of what it cost us: which model
        answered, how long it took, whether it worked, and the price. We do not
        keep your question, the picture, or the answer.
      </p>

      <H3>Your own AI account ({maxPlan.name}, {priceLabel(maxPlan)}/month)</H3>
      <p>
        You connect an OpenAI, Anthropic, Google Gemini, Mistral or OpenRouter
        account. The key is checked once, then encrypted, and it is never shown
        again to anyone — including you. Requests run on your account and are
        billed to you by that provider, separately from what you pay Metis. What
        they keep is covered by their privacy policy, not this one.
      </p>

      <H3>Your own key, entered in the app</H3>
      <p>
        A key entered into Metis on Windows is stored by Windows itself,
        encrypted to your user account, and used to call the provider straight
        from your machine. No Metis server is involved, and those requests are
        never counted against anything.
      </p>

      <H3>A local model</H3>
      <p>
        Run a model on your own computer and nothing leaves it at all.
      </p>

      <H>What is never captured</H>
      <ul>
        <li>
          <strong>Anything an app marks private.</strong> Programs can tell
          Windows &ldquo;do not record this&rdquo;, and they use it for exactly
          what you would expect &mdash; banking, password managers, disappearing
          photos. Metis blacks those areas out before the picture goes anywhere,
          so they never exist in anything that leaves your machine. The AI is told
          something was hidden from it, so it says it cannot see rather than
          guessing.
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

      <H>What we keep</H>
      <ul>
        <li>Your email address, and your password stored in a form nobody can read back.</li>
        <li>Which plan you are on, and whether your email is confirmed.</li>
        <li>
          For each answer we paid for: which model, how long, whether it worked,
          and what it cost. None of what was said.
        </li>
        <li>
          On {maxPlan.name}: your encrypted AI key, the last four characters of it,
          and a note of when you connected or disconnected it.
        </li>
        <li>Your waitlist entry, if you joined it.</li>
      </ul>

      <H>Who else touches it</H>
      <p>
        Metis is one person, not a data centre, so the parts it does not run
        itself are run by other companies. Each is named here with what it
        actually receives, because &ldquo;we may share data with third
        parties&rdquo; tells you nothing worth knowing.
      </p>
      <ul>
        <li>
          <strong>Supabase</strong> holds your account: email address, password
          in a form nobody can read back, which plan you are on, and the record
          of what each answer cost. Hosted in the EU.
        </li>
        <li>
          <strong>Render</strong> runs the server that passes your question to
          an AI provider. It handles the request; it stores none of it.
        </li>
        <li>
          <strong>Google</strong> answers the questions Metis pays for, through
          the Gemini API. It receives your question and, when you asked about
          your screen, the picture. It does not receive your email address or
          your name.
        </li>
        <li>
          <strong>Polar</strong> takes the payment and gets your billing details
          &mdash; name, email, country, and whatever the card requires. Metis
          never sees your card number.
        </li>
        <li>
          <strong>Sentry</strong> receives a crash report if the application
          fails, and only if crash reporting has been switched on for that
          build. A report carries the error and where in the code it happened.
          It never carries a screenshot, a conversation, an API key, or the
          contents of anything you were looking at.
        </li>
      </ul>
      <p>
        If you are in the UK or the EU, these are our processors under the GDPR
        and we remain the controller. If you are in California, none of this is
        a sale or share of personal information under the CCPA &mdash; Metis
        does not sell your data and has nothing to gain by doing so.
      </p>

      <H>Deleting it</H>
      <p>
        Your conversations, what Metis has learned, and any notes you wrote are on
        your own computer, and one button in settings deletes all of it. A
        connected AI key is deleted properly from your account page — the key
        itself, not just the record of it. For anything else, write to us and we
        will delete the account.
      </p>

      <H>Getting in touch</H>
      <p>
        Email{" "}
        <a className="text-accent underline underline-offset-4" href="mailto:privacy@metis.software">
          privacy@metis.software
        </a>{" "}
        about anything on this page, including asking for your account and
        everything in it to be deleted. A request about your own data should not
        have to be made in public, which is why this is an address and not an
        issue tracker.
      </p>
    </Document>
  );
}

export function Terms() {
  return (
    <Document title="Terms" file="Terms">
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
        {plans[0].name} is {priceLabel(plans[0])}. {proPlan.name} is{" "}
        {priceLabel(proPlan)} per month and {maxPlan.name} is {priceLabel(maxPlan)} per
        month. Those prices cover the software and the AI Metis buys on your
        behalf, within the monthly allowance shown on your account page.
      </p>
      <p>
        On {maxPlan.name}, model usage on a provider account you connect is billed by
        that provider, directly to you, and is entirely separate from what you pay
        Metis. Metis does not mark it up and does not see the invoice.
      </p>
      <p>
        <strong>Connecting your own AI account is part of {maxPlan.name}.</strong>{' '}
        Running a model on your own computer stays free on every plan, because
        it costs Metis nothing.
      </p>

      <H>Who you are buying from</H>
      <p>
        <strong>Polar is the merchant of record.</strong> When you subscribe,
        the contract of sale is between you and Polar, not between you and
        Metis. Polar takes the payment, is the seller named on your statement
        and on the invoice, and is the party responsible for charging and
        remitting VAT or sales tax wherever you happen to live.
      </p>
      <p>
        In practice that means your receipt and your invoice come from Polar,
        refunds and chargebacks are handled under Polar&rsquo;s terms, and your
        card details go to them rather than to us &mdash; Metis never sees a
        card number. Billing questions can go to either of us; anything that
        needs the payment itself touched has to be done by Polar.
      </p>

      <H>Cancelling</H>
      <p>
        A subscription can be cancelled at any time from your account page. Access
        continues to the end of the period already paid for, and there is no
        cancellation fee. Metis keeps working afterwards on the free plan, with
        its included AI and its own monthly allowance. Cancelling {maxPlan.name} ends
        the ability to answer on a provider key of your own; a model running on
        your own computer keeps working on every plan.
      </p>

      <H>Allowances and fair use</H>
      <p>
        Paid plans include a monthly allowance of AI rather than an unlimited
        amount, because the underlying providers charge per request and an
        unlimited promise would be one Metis could not keep. The current numbers
        are on your account page and can change; a reduction would be announced
        before it took effect. Requests answered on a {maxPlan.name} account&rsquo;s
        own provider key are not counted against any of it, because Metis is not
        paying for them.
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

      <H>What Metis tells you is not advice</H>
      <p>
        Metis reads a screen and asks a language model about it, and language
        models are confidently wrong sometimes. Everything it says is
        assistance, not instruction: <strong>you are the one deciding whether
        to act on it</strong>, and you remain responsible for what happens on
        your computer when you do.
      </p>
      <p>
        Check anything that matters before acting on it, particularly where a
        step is irreversible, involves money, or touches somebody else&rsquo;s
        data. Metis is not a substitute for professional advice &mdash; not
        legal, medical, financial, or anything else a qualified person is
        supposed to give you. It is a teacher that can be mistaken, and it will
        sometimes describe a button that is not there.
      </p>

      <H>No warranty, and what we are liable for</H>
      <p>
        Metis is provided as it is, without warranty of any kind, express or
        implied &mdash; including any implied warranty of merchantability,
        fitness for a particular purpose, or non-infringement. It is not
        promised to be uninterrupted, error-free, or available at any
        particular moment.
      </p>
      <p>
        <strong>
          Where the law allows a limit, total liability for any claim is capped
          at whatever you actually paid Metis in the twelve months before it
          arose
        </strong>{' '}
        &mdash; and at nothing, if you were on the free plan. Metis is not
        liable for indirect or consequential loss: lost profit, lost data, lost
        time, or work you had to redo.
      </p>
      <p>
        Some countries do not permit excluding certain warranties or limiting
        certain liabilities. Where that is true where you live, those rules win
        and this section applies only as far as it is allowed to. Nothing here
        limits liability for death, personal injury, or fraud.
      </p>
    </Document>
  );
}

// ------------------------------- Chrome -------------------------------

function Draft() {
  return (
    <div className="mb-6 border-l-4 border-[currentColor] bg-[#ffffe1] px-4 py-3">
      <p className="text-[12px] leading-relaxed text-ink">
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

        <div className="mt-8 card">
          <div className="panel-title">
            <span>{file}</span>
            <Link
              to="/"
              aria-label="Back to the home page"
              className="ml-auto grid h-7 w-7 place-items-center rounded-full text-[15px] leading-none text-ink-muted transition-colors hover:bg-surface-sunken hover:text-ink no-underline"
            >
              &times;
            </Link>
          </div>

          <div className="bg-surface p-3">
            <div
              className="rounded-lg border border-line bg-surface px-3 py-2 space-y-3 p-6 text-[13px] leading-relaxed text-ink [&_li]:ml-5 [&_li]:list-disc [&_p]:max-w-[74ch] [&_ul]:space-y-1.5"
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
  return <h2 className="!mt-7 text-[15px] font-bold text-ink first:!mt-0">{children}</h2>;
}

function H3({ children }: { children: React.ReactNode }) {
  return <h3 className="!mt-5 text-[13px] font-bold text-accent">{children}</h3>;
}
