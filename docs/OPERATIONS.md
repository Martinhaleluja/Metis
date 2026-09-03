# Running Metis in production

Everything Metis runs on sits inside a permanent free tier. This file is the
operator's manual for that arrangement: what each service does, where it breaks,
and what to do about it. It is written for one person with no budget, because
that is who runs it.

| Piece | Service | Free tier | What it costs when outgrown |
| --- | --- | --- | --- |
| Website | Vercel Hobby | Unlimited static, global CDN | $20/mo for a commercial licence |
| Gateway | Render Free | 750 instance-hours/mo, sleeps when idle | $7/mo Starter removes the sleep |
| Database, auth | Supabase Free | 500 MB, 50k monthly active users | $25/mo Pro |
| Payments | Polar | No monthly fee | ~5% + 50c per sale |
| Downloads | GitHub Releases | Unlimited bandwidth | Nothing. This one never bites |
| Crash reports | Sentry Free | 5,000 errors/mo | $26/mo |
| Email | Resend Free | 3,000/mo, 100/day | $20/mo |
| Uptime pings | cron-job.org | Unlimited jobs, 1-minute resolution | Nothing |

---

## 1. The cold start, and how to avoid it

Render's free tier stops a service after **15 minutes** without a request. The
next request wakes it, and waking takes **50–60 seconds** — a .NET container
starting from cold.

That is long enough to look broken. The app now handles it in two ways: gateway
timeouts are set above the wake time rather than below it, and the notch shows a
"waking up" state instead of appearing to hang. But the better fix is to stop it
sleeping during the hours anyone is awake.

### Keep it warm with cron-job.org

Free, unlimited jobs, no card. **https://cron-job.org**

1. Create an account and click **Create cronjob**.
2. Fill it in exactly:

   | Field | Value |
   | --- | --- |
   | Title | `Metis gateway keep-alive` |
   | URL | `https://metis-gateway.onrender.com/health` |
   | Schedule | Every **10 minutes** |
   | Request method | `GET` |

3. Under **Advanced**, restrict the hours it runs — see the budget note below.
4. Save, then hit **Test run**. It must return **200** with `{"status":"ok"}`.

### The budget arithmetic that matters

Render gives **750 instance-hours a month**, and a month is about 730 hours. So
a single service pinged around the clock uses **all** of it and there is nothing
spare. That is survivable with one service and nothing else, but it leaves no
margin, and a second service on the same account would exhaust the allowance
partway through the month.

Restrict the pings to the hours real people use Metis — say 06:00 to 24:00 in
your timezone. That is 18 hours a day, roughly **540 hours a month**, which
leaves headroom and still means nobody meets a cold start during the day. The
first visitor after 06:00 pays the wake cost; nobody else does.

`/health` is deliberately the target. It touches no database and returns a
constant, so a ping costs essentially nothing.

---

## 2. Email

Two separate systems send mail, and only one of them is yours to configure.

### Receipts and invoices — nothing to do

Polar is the merchant of record. It sends the payment receipt and the VAT
invoice itself, from its own domain, and it is the party legally required to.
You do not send these and should not try to.

### Sign-in, verification and password resets — configure this

Supabase's built-in mailer is rate-limited to a handful of messages an hour and
is explicitly not for production. Past that, sign-ups silently stop arriving.
Point it at Resend instead.

**Step 1 — get an SMTP credential from Resend** (free: 3,000/month, 100/day)

1. Sign up at **https://resend.com**.
2. **Domains → Add Domain.** Without a verified domain, Resend only lets you
   send to your own address, which is useless for real users. Add the DNS
   records it gives you at your registrar and wait for verification.
3. **API Keys → Create API Key**, with *Sending access*. Copy it once.

**Step 2 — point Supabase at it**

Supabase Dashboard → **Project Settings → Authentication → SMTP Settings** →
enable *Custom SMTP*:

| Field | Value |
| --- | --- |
| Host | `smtp.resend.com` |
| Port | `587` |
| Username | `resend` |
| Password | your Resend API key |
| Sender email | `noreply@yourdomain` — must be on the verified domain |
| Sender name | `Metis` |

**Step 3 — prove it works.** Sign up with a real address you control and
confirm the verification mail arrives. Do not assume; the failure mode here is
silent, and you only find out when a user tells you they never got the email.

> Until a domain is verified, leave Supabase's default mailer in place. It is
> rate-limited but it works for the handful of accounts that exist today.

---

## 3. Crash reporting

Both the app and the website can report crashes to Sentry, and both are **off
until a DSN is set**. No account exists yet, so nothing is being sent.

To switch it on:

1. Sign up at **https://sentry.io** — free tier is 5,000 errors/month.
2. Create two projects: one **.NET** (the desktop app) and one **React** (the site).
3. Copy each project's DSN.
4. Desktop: set `METIS_SENTRY_DSN` in the environment.
5. Website: set `VITE_SENTRY_DSN` in Vercel → Settings → Environment Variables,
   then **redeploy** — Vite bakes the value in at build time, so saving the
   setting alone changes nothing.

Both are configured to send no personal data: no screenshots, no chat content,
no API keys, no access tokens.

---

## 4. Watching it

| Question | Where |
| --- | --- |
| Is the gateway up? | cron-job.org shows a failure history and can email you |
| Did a payment land? | `select * from billing_events order by received_at desc limit 5;` |
| Is anyone actually using it? | `select count(*), date_trunc('day', created_at) from usage_events group by 2 order by 2 desc;` |
| What is the AI costing? | `select sum(estimated_cost_usd) from usage_events where created_at >= date_trunc('month', now());` |
| Is a user stuck on the wrong plan? | `select * from account_status where user_id = '…';` |

**The number to watch weekly** is AI spend per free user. It is the largest cost
in this business and the one that grows quietly with success rather than
announcing itself.

---

## 5. When something breaks

Two switches exist, both a single SQL statement, neither needing a deploy.

```sql
-- The included AI refuses, in your words. Local models and users' own keys
-- are untouched, because those requests never reach the gateway.
update billing_state
   set cost_protection_mode = 'refuse',
       cost_protection_note = 'Metis''s included AI is paused for an hour. Your own API key still works.';

-- Softer: cheapest model, no screenshots, capped output.
update billing_state set cost_protection_mode = 'degrade';

-- Back to normal.
update billing_state set cost_protection_mode = 'off', cost_protection_note = null;

-- And the big one: take billing off entirely. Nobody is locked out.
update billing_state set billing_is_live = false;
```

Keep these where you can reach them from a phone.

**If the gateway will not start**, it is almost always a missing environment
variable. `SUPABASE_URL` and `SUPABASE_SERVICE_KEY` are the two that stop the
process dead; everything else degrades rather than crashes. Render → the
service → **Logs** names it on the first line.

**If answers 503 but the service is up**, `GOOGLE_API_KEY` is missing or the
Gemini quota is spent.

**If the website's account page cannot reach the gateway**, it is CORS:
`ALLOWED_ORIGINS` must list the Vercel origin exactly — scheme included, no
trailing slash. This one fails only in a browser, so `curl` will tell you
everything is fine while every real user is broken.
