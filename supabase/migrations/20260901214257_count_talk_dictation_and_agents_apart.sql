-- Dictation is measured in seconds of audio, not in tokens.
--
-- Nothing already on the table can stand in for it: input_tokens is about text
-- and latency_ms is about how slow Metis was, not how long the person spoke.
-- Borrowing either would make the allowance mean something different from what
-- the pricing page says.
alter table public.usage_events
    add column if not exists billed_seconds integer not null default 0;

comment on column public.usage_events.billed_seconds is
    'Seconds of audio, for dictation events. Zero for everything else.';

-- Count the three allowances apart from one another.
--
-- request_count used to be count(*) over every event, agent steps included. On
-- the old plans that was harmless because only Free had a turn cap and it was
-- large. It is not harmless now: Free is sold as fifty talk messages AND ten
-- agent messages, and counting agents into the talk total would silently make
-- the first number smaller than the page promises, in a way the user could only
-- discover by running out early.
--
-- The three counters are therefore disjoint by construction: an event is a
-- talk message, an agent message, or dictation, and never two of them.
--
-- Dropped and recreated rather than replaced because the row type changes, and
-- Postgres will not alter the OUT parameters of a live function.
drop function if exists public.usage_this_period(uuid);

create function public.usage_this_period(target uuid)
returns table(
    spend_usd numeric,
    request_count integer,
    agent_steps integer,
    dictation_seconds integer,
    period_start timestamp with time zone)
language sql
stable
security definer
set search_path to ''
as $function$
    select
        coalesce(sum(events.estimated_cost_usd), 0)::numeric,
        count(*) filter (
            where events.feature is distinct from 'agent_step'
              and events.feature is distinct from 'dictation')::int,
        count(*) filter (where events.feature = 'agent_step')::int,
        coalesce(sum(events.billed_seconds) filter (
            where events.feature = 'dictation'), 0)::int,
        date_trunc('month', now() at time zone 'utc')
    from public.usage_events events
    where events.user_id = target
      and events.created_at >= date_trunc('month', now() at time zone 'utc')
      and events.status not like 'provider_%';
$function$;

grant execute on function public.usage_this_period(uuid) to service_role;
