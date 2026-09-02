-- Reading usage back, and the switch that stops Metis spending money it does
-- not have.
--
-- usage_events has been written since the gateway was first built and read by
-- nothing. That is the half that matters for a budget: a meter nobody reads is
-- a log, not a limit.

-- What this account has spent this calendar month.
--
-- A security-definer function rather than a PostgREST aggregate, so the
-- index-friendly predicate is written once. The existing index on
-- (user_id, created_at desc) is what makes it cheap; a query written by hand at
-- each call site would eventually be written without the date bound.
--
-- Provider failures are excluded. A request the provider refused produced no
-- tokens and cost nothing, and charging someone's allowance for an outage on
-- Metis's side would be taking payment for a failure.
create or replace function public.usage_this_period(target uuid)
returns table (
    spend_usd numeric,
    request_count integer,
    agent_steps integer,
    period_start timestamptz
)
language sql
stable
security definer
set search_path = ''
as $$
    select
        coalesce(sum(events.estimated_cost_usd), 0)::numeric,
        count(*)::int,
        count(*) filter (where events.feature = 'agent_step')::int,
        date_trunc('month', now() at time zone 'utc')
    from public.usage_events events
    where events.user_id = target
      and events.created_at >= date_trunc('month', now() at time zone 'utc')
      and events.status not like 'provider_%';
$$;

revoke execute on function public.usage_this_period(uuid) from public, anon, authenticated;
grant execute on function public.usage_this_period(uuid) to service_role;

-- The same question, asked about yourself, for the account page and the usage
-- meter in the desktop app. Separate from the function above because that one
-- takes any user id and must never be reachable by a signed-in browser.
create or replace function public.my_usage_this_period()
returns table (
    spend_usd numeric,
    request_count integer,
    agent_steps integer,
    period_start timestamptz
)
language sql
stable
security definer
set search_path = ''
as $$
    select * from public.usage_this_period((select auth.uid()));
$$;

revoke execute on function public.my_usage_this_period() from public, anon;
grant execute on function public.my_usage_this_period() to authenticated;

-- ---------------------------------------------------------------------------
-- Cost protection.
--
-- An emergency brake for the case where managed AI is costing more than Metis
-- can afford this month. It is a row rather than a deploy because an emergency
-- that takes a release to stop is not something you can stop.
--
-- Bring-your-own-key accounts are untouched by all of this, and not because of
-- a special case written here: their requests never reach the gateway at all,
-- so there is nothing for this switch to act on. That property is what makes it
-- safe to pull.
alter table public.billing_state
    add column if not exists cost_protection_mode text not null default 'off'
        check (cost_protection_mode in ('off', 'degrade', 'refuse')),
    add column if not exists cost_protection_note text,
    add column if not exists managed_models_paused text[] not null default '{}';

comment on column public.billing_state.cost_protection_mode is
    'off: normal. degrade: force the cheapest model, drop screenshots, cap output. refuse: managed AI returns 503 with cost_protection_note as the message. Staff bypass both.';
