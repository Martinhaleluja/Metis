-- Taking payment, before there is a payment provider.
--
-- Which processor Metis will use is still being decided. Everything here is
-- written so that the answer does not change any of it: the gateway verifies a
-- webhook signature behind an interface with a Polar implementation and a Stripe
-- implementation, both inert until their secret is configured, and both landing
-- here in the same normalised shape.
--
-- Nothing in this file turns billing on. billing_state.billing_is_live stays
-- false and every paid capability stays free until it is deliberately changed.

create table public.billing_events (
    provider text not null,
    event_id text not null,
    event_type text not null,
    received_at timestamptz not null default now(),

    -- Null means received and verified but not yet applied. That is a real,
    -- recoverable state and not an error: a verified event whose downstream
    -- sync failed still returns 200 to the processor, because processors retry
    -- non-2xx and a permanently poisonous event would otherwise be redelivered
    -- forever. The row stays here, with its reason, to be drained by hand.
    processed_at timestamptz,
    error text,

    payload jsonb not null,
    primary key (provider, event_id)
);

comment on table public.billing_events is
    'Every verified webhook, keyed by the processor''s own event id so redelivery is a no-op. Server-only: RLS is enabled with no policies on purpose.';

alter table public.billing_events enable row level security;
-- No policies. The audit trail of money is read with the service role and
-- nothing else. The database linter reports this as informational; it is the
-- intended design, exactly as with audit_logs.

-- ---------------------------------------------------------------------------
-- subscriptions.provider
--
-- It defaulted to 'paddle', from a processor that was considered and not
-- chosen. A default is a guess, and a guess that survives into a row is a row
-- that lies about where the money came from. The table is empty today, so this
-- costs nothing now and would be expensive to fix in a year.
--
-- 'paddle' stays in the allowed list because removing a name costs nothing to
-- keep. 'manual' covers comped accounts and staff subscriptions granted by hand,
-- which otherwise end up mislabelled as whichever processor is live.
alter table public.subscriptions alter column provider drop default;

alter table public.subscriptions drop constraint if exists subscriptions_provider_known;
alter table public.subscriptions add constraint subscriptions_provider_known
    check (provider in ('polar', 'stripe', 'paddle', 'manual'));

-- The plan a subscription buys. Kept beside the subscription rather than derived
-- from a price, because a price changes and what someone bought does not.
alter table public.subscriptions
    add column if not exists plan_key public.plan_tier,
    add column if not exists external_customer_id text;

-- ---------------------------------------------------------------------------
-- Applying a subscription to an account.
--
-- This is where a verified webhook becomes an entitlement, and it lives next to
-- my_feature so the rule that turns a subscription status into a plan is
-- readable beside the rule that turns a plan into a capability.
create or replace function public.apply_subscription(
    billing_provider text,
    external_subscription_id text,
    target uuid,
    plan public.plan_tier,
    status text,
    period_end timestamptz,
    cancels_at_period_end boolean,
    external_customer text default null
)
returns public.plan_tier
language plpgsql
security definer
set search_path = ''
as $$
declare
    effective public.plan_tier;
    previous public.plan_tier;
begin
    -- An active or trialing subscription grants the plan it was bought for.
    --
    -- past_due keeps it, but only until a few days past the period end. A card
    -- that fails and is retried successfully an hour later is the ordinary case,
    -- and downgrading someone mid-session over it would take Metis away from a
    -- paying customer because their bank was slow. Anything else is Free.
    effective := case
        when status in ('active', 'trialing') then plan
        when status = 'past_due' and period_end is not null
             and now() < period_end + interval '3 days' then plan
        else 'free'::public.plan_tier
    end;

    insert into public.subscriptions (
        user_id, provider, provider_subscription_id, status,
        current_period_end, cancel_at_period_end, plan_key, external_customer_id
    ) values (
        target, billing_provider, external_subscription_id, status,
        period_end, coalesce(cancels_at_period_end, false), plan, external_customer
    )
    on conflict (provider, provider_subscription_id) do update
        set user_id = excluded.user_id,
            status = excluded.status,
            current_period_end = excluded.current_period_end,
            cancel_at_period_end = excluded.cancel_at_period_end,
            plan_key = excluded.plan_key,
            external_customer_id = coalesce(excluded.external_customer_id,
                                            public.subscriptions.external_customer_id),
            updated_at = now();

    select account.plan into previous
      from public.account_status account
     where account.user_id = target;

    update public.account_status
       set plan = effective,
           updated_at = now()
     where user_id = target;

    if previous is distinct from effective then
        insert into public.audit_logs (user_id, action, detail)
        values (target, 'plan.changed', jsonb_build_object(
            'from', previous,
            'to', effective,
            'provider', billing_provider,
            'subscription', external_subscription_id,
            'status', status));
    end if;

    return effective;
end;
$$;

revoke execute on function public.apply_subscription(
    text, text, uuid, public.plan_tier, text, timestamptz, boolean, text)
    from public, anon, authenticated;
grant execute on function public.apply_subscription(
    text, text, uuid, public.plan_tier, text, timestamptz, boolean, text)
    to service_role;

-- What the account page shows. The customer may read their own subscription
-- already through the existing select policy; this adds the one thing that
-- policy cannot express, which is "and nothing if there isn't one".
create or replace function public.my_subscription()
returns table (
    provider text,
    status text,
    plan_key public.plan_tier,
    current_period_end timestamptz,
    cancel_at_period_end boolean
)
language sql
stable
security definer
set search_path = ''
as $$
    select subscription.provider,
           subscription.status,
           subscription.plan_key,
           subscription.current_period_end,
           subscription.cancel_at_period_end
      from public.subscriptions subscription
     where subscription.user_id = (select auth.uid())
     order by subscription.updated_at desc
     limit 1;
$$;

revoke execute on function public.my_subscription() from public, anon;
grant execute on function public.my_subscription() to authenticated;
