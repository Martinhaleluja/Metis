# Turning billing on

Everything below is built and tested. None of it is switched on, because no
payment processor has been chosen yet. This is the list of things to do once one
is, and the point of the design is that **none of them are code changes**.

The gateway ships a signature verifier for Polar and one for Stripe. Both are
inert until their secret is set, both land in the same normalised shape, and
everything downstream of that shape — idempotency, subscription sync, plan
derivation, the audit trail — is written once and does not care which processor
sent the event.

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

**Free** — $0
```
plan=free   plan_id=metis_free   ai_mode=metis_managed   ai_providers=gemini
```

**Plus** — $14/month
```
plan=plus   plan_id=metis_plus   price_usd=14   ai_mode=metis_managed
ai_providers=gemini,mistral,openrouter   byoa=false
```

**Pro** — $29/month
```
plan=pro    plan_id=metis_pro    price_usd=29   ai_mode=byoa   byoa=true
providers=openai,anthropic,gemini,mistral,openrouter
```

A Free product is worth creating even though nobody pays for it: it gives
cancellations somewhere to land, so a cancelled subscriber becomes an explicit
Free customer rather than a row with no product.

## 3. The one thing that must be right

**Checkout metadata has to carry `metis_user_id`.**

When the website sends someone to checkout, put their Supabase user id in the
session's metadata under that key. The gateway reads the account from there and
**from nowhere else** — in particular, never from a matching email address.

That is not fussiness. Email matching is an account takeover: anyone who can put
someone else's address into a billing form could otherwise change that person's
plan, or have theirs handed over. `apply_subscription` is only ever called with
an id that came from checkout metadata, and an event without one is stored,
marked as changing nothing, and ignored.

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

A `past_due` deliberately keeps the plan until three days past the period end. A
card that fails and is retried successfully an hour later is the ordinary case,
and downgrading someone mid-session over it would take Metis away from a paying
customer because their bank was slow.

## 6. Wire the checkout button

One function, marked in the code:

`website/src/lib/billing.ts` → `startCheckout(planId)`

Create the checkout session on the gateway — server side, so the product ids and
the API token stay off the client — and assign the URL it returns to
`window.location.href`. Nothing else on the site changes: every upgrade button
already routes through this function and already renders a waitlist state while
billing is off.

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
