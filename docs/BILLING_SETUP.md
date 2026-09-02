# Turning billing on

Everything below is built and tested, and none of it is switched on. This is the
list of things to do when it should be, and the point of the design is that
**none of them are code changes** — they are products created on a processor,
secrets set on the service, and one row updated in Postgres.

The gateway ships a signature verifier for Polar and one for Stripe. Both are
inert until their secret is set, both land in the same normalised shape, and
everything downstream of that shape — idempotency, subscription sync, plan
derivation, the audit trail — is written once and does not care which processor
sent the event.

Starting a checkout is the one part that is not processor-agnostic: it talks to
Polar's API, and is inert until its own token is set. Section 6 says what it
needs.

---

## 1. Pick one

| | Polar | Stripe |
|---|---|---|
| Merchant of record | Yes — Polar is the seller, and handles VAT and sales tax for you | No — you are the seller, and tax is your problem |
| Fees | Higher, because the tax handling is included | Lower headline rate |
| Setup | An organisation, three products, one webhook | An account, three prices, one webhook, plus tax configuration |
| Payout | Through Stripe Connect | Direct |

For a one-person product selling internationally, merchant-of-record is usually
worth the extra percent: registering for VAT in every jurisdiction you sell into
is not a smaller problem than it sounds.

Check the current fees and the supported-country list before deciding — both
change, and both are on the processor's own pages rather than in this file for
exactly that reason:

- <https://polar.sh/resources/pricing>
- <https://polar.sh/docs/merchant-of-record/supported-countries>
- <https://stripe.com/pricing>

> A note on connecting Stripe to Polar: these are alternatives, not layers.
> Polar *pays you out* through Stripe Connect, so you will link a Stripe account
> either way — but that is Polar sending you money, not Polar processing
> payments through your Stripe. You do not need to build against both.

## 2. Create three products

Whichever processor, create three and put this metadata on each. The gateway
reads `plan` (or `plan_id`) to decide which tier the subscription grants, and
accepts both the bare and the prefixed form.

The ladder is Free, Pro, Max. It was once Free, Plus, Pro — the middle plan took
the name Pro and the top became Max — so the word "pro" means different things
either side of that change, and a product created under the old naming needs its
metadata rewritten rather than left to be interpreted. `Entitlements.ParsePlan`
still reads `plus` as Pro so no existing subscriber is demoted, but new products
should say what they mean.

**Free** — $0
```
plan=free   plan_id=metis_free   ai_mode=metis_managed   ai_providers=gemini
```

**Pro** — $20/month
```
plan=pro    plan_id=metis_pro    price_usd=20   ai_mode=metis_managed
ai_providers=gemini   byoa=false
```

**Max** — $50/month
```
plan=max    plan_id=metis_max    price_usd=50   ai_mode=byoa   byoa=true
providers=openai,anthropic,gemini,mistral,openrouter
```

The prices here have to agree with three other places, none of which read this
file: `website/src/lib/plans.ts`, `Metis.Core.Services.PlanCatalogue`, and the
`plan_limits` rows in Postgres. A product priced differently from the pricing
page is the version the customer's card sees.

A Free product is worth creating even though nobody pays for it: it gives
cancellations somewhere to land, so a cancelled subscriber becomes an explicit
Free customer rather than a row with no product.

Copy each product's id — Polar shows it on the product page — into
`POLAR_PRODUCT_PRO` and `POLAR_PRODUCT_MAX`. Free has no variable because there
is nothing to check out with.

## 3. The one thing that must be right

**Checkout metadata has to carry `metis_user_id`, and the browser must never be
the thing that decides what it says.**

`POST /v1/checkout` (below) is what puts it there. It takes the Supabase user id
off the access token Supabase itself verified, writes it into the session's
`metadata.metis_user_id` *and* into `external_customer_id`, and accepts nothing
about the account from the request body. The webhook reads the account back from
those and **from nowhere else** — in particular, never from a matching email
address.

That is not fussiness. If the id were a value the client sent, anyone could name
somebody else's and move their plan. And email matching is the same account
takeover by a different route: anyone who can put another person's address into
a billing form could be handed their subscription. `apply_subscription` is only
ever called with an id that the gateway itself wrote at checkout, and an event
without one is stored, marked as changing nothing, and ignored.

On a Polar event the id is read back from three places in order —
`data.metadata.metis_user_id`, then `data.customer.external_id`, then
`data.customer_external_id`. Only the first is metadata Metis sets on the
checkout, and whether Polar copies checkout metadata onto the *subscription* it
emits afterwards is its behaviour rather than ours; the external customer id is
stored on the customer record, so it survives where the metadata might not. All
three are values the gateway wrote, so none of this weakens the rule above.
(Stripe has metadata only. Its subscription events carry the customer as a bare
id rather than an object, so there is no second place to look.)

Without that chain, a payment whose metadata did not arrive would change no
entitlement, be stored, and be answered 200 — the processor satisfied, no retry,
and a customer who has paid and holds a receipt given nothing. It is the
quietest failure in the system, which is why it is worth two extra lookups.

## 4. Point the webhook at the gateway

```
https://<your-gateway>/v1/webhooks/polar
https://<your-gateway>/v1/webhooks/stripe
```

Subscribe to the subscription lifecycle events — created, active, updated,
canceled, revoked, past_due, and the cycle event for renewals. Product and
organisation events are harmless: they are verified and stored like anything
else, and then deliberately do nothing.

Copy the signing secret into the gateway's environment as either
`POLAR_WEBHOOK_SECRET` or `STRIPE_WEBHOOK_SECRET`. The endpoint answers **404**
for a processor with no secret configured, which is deliberate: any other status
would tell someone probing which processors this deployment knows about.

## 5. Check it before it matters

Use the processor's sandbox and its "send test event" button.

```bash
# A verified event should be stored exactly once, however many times it arrives.
curl -sS -X POST https://<gateway>/v1/webhooks/stripe \
  -H 'Stripe-Signature: t=...,v1=...' \
  --data-binary @event.json
```

Then in Supabase:

```sql
-- One row, processed_at set, error null.
select provider, event_id, event_type, processed_at, error
  from billing_events order by received_at desc limit 5;

-- The subscription, and the plan it produced.
select provider, provider_subscription_id, status, plan_key, current_period_end
  from subscriptions order by updated_at desc limit 5;

select user_id, plan from account_status where user_id = '<the test user>';
```

Send the same event twice and confirm there is still one row and the plan did
not change twice. Then send a `canceled` and confirm the plan drops to `free`.

Check the other direction too, with `POLAR_SERVER=sandbox` and a sandbox token:

```bash
# 200 with a checkout URL. Follow it, pay with a test card, and the webhook
# above should then move that same account — not one matched by email.
curl -sS -X POST https://<gateway>/v1/checkout \
  -H 'Authorization: Bearer <a signed-in user access token>' \
  -H 'Content-Type: application/json' \
  -d '{"plan":"pro"}'

# 403: the account already holds it. 400 for "free" or anything that is not a
# plan. 404 while POLAR_ACCESS_TOKEN or that plan's product id is unset.
```

A `past_due` deliberately keeps the plan until three days past the period end. A
card that fails and is retried successfully an hour later is the ordinary case,
and downgrading someone mid-session over it would take Metis away from a paying
customer because their bank was slow.

## 6. Wire the checkout button

The server half exists: **`POST /v1/checkout`** on the gateway.

```
POST /v1/checkout                 Authorization: Bearer <supabase access token>
{ "plan": "pro" }                 or "max"

200  { "url": "https://polar.sh/checkout/..." }   send the browser there
400  the plan was missing, "free", or not a plan
403  this account already holds that plan or a larger one
404  this deployment cannot take payments (no token, or no product id for it)
502  the processor refused, or answered without a URL. Nothing was charged.
```

Everything that decides who is being charged and for what is decided on the
server:

| | Where it comes from | Why not from the client |
|---|---|---|
| Supabase user id | the verified access token | an id the caller names is somebody else's plan |
| Product id | `POLAR_PRODUCT_PRO` / `POLAR_PRODUCT_MAX` | a product the caller names is a price the caller chose |
| `success_url` | `METIS_SITE_URL` | a redirect the caller names is an open redirect beside a payment form |
| Email | the user's auth record, prefill only | it is a convenience on the form and never how an account is matched |

The five variables it needs, all optional and all `sync: false` in `render.yaml`:

| Variable | What it is |
|---|---|
| `POLAR_ACCESS_TOKEN` | Polar organisation access token. Without it the endpoint is 404. |
| `POLAR_SERVER` | `sandbox` for Polar's test environment; anything else means production. |
| `POLAR_PRODUCT_PRO` | Product id the Pro subscription is sold as. |
| `POLAR_PRODUCT_MAX` | Product id the Max subscription is sold as. |
| `METIS_SITE_URL` | Site root. The customer returns to `<site>/account?checkout=success`. |

None of them are required to boot, which is the point: a gateway missing its
payment token still serves AI, and only the checkout endpoint notices. With the
token set and `METIS_SITE_URL` missing the endpoint still works and logs a
warning at startup — the customer finishes on Polar's own confirmation page
instead of back on their account.

Checkout is Polar-only. The receiving half is written once for both processors
and does not care which sent an event; the *creating* half talks to one API, and
a Stripe equivalent would be a code change rather than a secret.

What remains on the website is one function, marked in the code:

`website/src/lib/billing.ts` → `startCheckout(planId)`

Call the endpoint with the signed-in session's access token and assign the `url`
from the reply to `window.location.href`. Nothing else on the site changes:
every upgrade button already routes through this function and already renders a
waitlist state while billing is off.

## 7. Flip the switch

```sql
update billing_state
   set billing_is_live = true,
       note = 'Plans opened <date>.',
       updated_at = now();
```

That is the whole of it. No deployment, no rebuild, no release for the desktop
app. Within sixty seconds the gateway begins enforcing plans, `/v1/me` begins
reporting `billingIsLive: true`, the desktop app's next refresh picks it up, and
the website's upgrade buttons change from the waitlist state to checkout.

**Before you do:** everything currently free becomes paid at that moment. Say so
first, publicly, with notice. The one thing that does not change is the promise
in `Entitlements.Has` and `ProviderRouting.Decide` — anyone running Metis on
their own API key keeps every capability, on every plan, signed in or not.

## If something goes wrong

Put the brakes on without a deployment:

```sql
-- Managed AI refuses, with your words shown to the user.
-- Pro's connected accounts and local models are untouched: those requests
-- never reach the gateway, so there is nothing here for them to be affected by.
update billing_state
   set cost_protection_mode = 'refuse',
       cost_protection_note = 'Metis''s included AI is paused for an hour. Your own API key still works.';

-- Or degrade instead: cheapest model, no screenshots, capped output.
update billing_state set cost_protection_mode = 'degrade';

-- And back to normal.
update billing_state set cost_protection_mode = 'off', cost_protection_note = null;
```

To take billing back off entirely, set `billing_is_live = false`. Everyone
returns to having everything, and nobody is locked out while you work out what
happened.

Events that were verified but could not be applied are still in the table:

```sql
select provider, event_id, event_type, error, payload
  from billing_events
 where processed_at is null
 order by received_at;
```

They are deliberately not retried automatically. The endpoint returns 200 for
anything it has verified, even when the sync fails, because every processor
retries non-2xx and a single poison event would otherwise hammer the service
until somebody noticed.
