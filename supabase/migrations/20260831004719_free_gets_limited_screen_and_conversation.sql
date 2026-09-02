-- Free gets a little of everything, rather than none of some things.
--
-- The plans shipped with max_screenshot_bytes = 0 on Free, which denied screen
-- vision outright: the one capability the whole product is built around was
-- simply absent until you paid. That is a poor trial. Someone who cannot see
-- Metis do the thing Metis is for has not tried Metis, and a free plan whose
-- job is to make the paid ones make sense has to demonstrate the thing being
-- sold.
--
-- So Free now gets a small screenshot allowance and a small monthly
-- conversation, and the difference between the plans becomes how much rather
-- than whether. A mebibyte is enough for a downscaled full-desktop capture; it
-- is not enough for the large, detailed one a pointing question wants, which is
-- part of what Plus buys.

alter table public.plan_limits
    add column if not exists max_turns_per_month integer not null default 0;

comment on column public.plan_limits.max_turns_per_month is
    'Conversation cap on Metis''s own AI. 0 means no separate cap — the dollar budget is the only limit.';

update public.plan_limits
   set max_screenshot_bytes = 1048576,
       max_turns_per_month  = 120,
       monthly_budget_usd   = 1.00,
       memory_entries_max   = 50,
       updated_at           = now()
 where plan = 'free';

-- Plus and Pro are bounded by money rather than by a count. Two ceilings that
-- can disagree is one ceiling too many, and the dollar budget is the one that
-- actually protects the company.
update public.plan_limits
   set max_turns_per_month = 0,
       updated_at          = now()
 where plan in ('plus', 'pro');

-- Unchanged in shape; restated so a rebuild from these files ends up with the
-- same function body as the live project.
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
