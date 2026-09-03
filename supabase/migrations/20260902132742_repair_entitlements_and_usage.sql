-- Repair the entitlement and usage functions after the Free/Pro/Max remap.
--
-- 20260901090100 moved every plan row ('pro' -> 'max', 'plus' -> 'pro') and
-- 20260901090200 grew usage_this_period a fifth column, but neither touched the
-- two functions that read those things. Both have been wrong in production
-- since. This file fixes them, and closes two access holes found alongside.


-- 1. my_usage_this_period() threw on every call.
--
-- usage_this_period(uuid) gained `dictation_seconds`, so `select *` against a
-- four-column declaration failed with 42P13 "return type mismatch ... returns
-- integer instead of timestamp with time zone at column 4". The website's
-- account page calls this on load, so its usage meters have been in the error
-- branch for every signed-in user.
--
-- The website already types five columns, so widening the function is what
-- makes the two agree. A return type cannot be changed in place, hence the drop.
drop function if exists public.my_usage_this_period();

create function public.my_usage_this_period()
returns table (
    spend_usd numeric,
    request_count integer,
    agent_steps integer,
    dictation_seconds integer,
    period_start timestamptz)
language sql
stable
security definer
set search_path = ''
as $$
    select * from public.usage_this_period((select auth.uid()));
$$;

revoke all on function public.my_usage_this_period() from public;
grant execute on function public.my_usage_this_period() to authenticated, service_role;


-- 2. my_feature() was still asking about plans that no longer exist.
--
-- Two errors, in opposite directions. 'max' was absent from the middle branch,
-- so the most expensive plan was refused six capabilities it had paid for. And
-- custom_ai_provider was granted to 'pro' -- the $20 tier -- when Entitlements.Has
-- has always reserved bringing your own key for Max.
--
-- The billing_is_live bypass that used to sit above these branches is gone. A
-- gate that is switched off is a gate that has not been built: the plan should
-- be observable the moment it is written, not on the day money changes hands.
-- Existing accounts are protected by being staff on Max, not by a global escape
-- hatch.
--
-- Free earns agents, screen vision and memory here because those are limited by
-- allowance rather than by permission -- the count is the lever, not a refusal.
-- This mirrors Entitlements.Has so the two sides cannot disagree.
create or replace function public.my_feature(feature public.metis_feature)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select case
        -- Staff capabilities are about who you are, not what you paid.
        when feature in ('developer_mode', 'experimental_features',
                         'staging_access', 'internal_cost_visibility')
            then coalesce((select status.role in ('developer', 'founder', 'admin')
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        when feature = 'admin_dashboard'
            then coalesce((select status.role in ('founder', 'admin')
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Anyone with an account, once the address is proven. The three that
        -- moved down here are metered by allowance on every plan.
        when feature in ('computer_control', 'managed_ai_routing', 'usage_visibility',
                         'autonomous_agents', 'managed_screen_vision', 'persistent_memory')
            then coalesce((select status.email_verified
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Pro and above.
        when feature in ('managed_premium_models', 'advanced_automation',
                         'browser_assistance')
            then coalesce((select status.email_verified
                               and (status.plan in ('pro', 'max')
                                    or status.role in ('developer', 'founder', 'admin'))
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Max only.
        when feature in ('custom_ai_provider', 'system_commands',
                         'advanced_agents', 'provider_management')
            then coalesce((select status.email_verified
                               and (status.plan = 'max'
                                    or status.role in ('developer', 'founder', 'admin'))
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        else false
    end;
$$;


-- 3. The pricing page could never show a buy button.
--
-- billing_state's select policy is granted to `authenticated`, but the public
-- pricing page reads it signed out, as `anon`. It got an empty array back and
-- concluded billing was off -- permanently, whatever the row actually said.
--
-- Widening the policy would expose cost_protection_note along with it, and an
-- RLS policy cannot restrict columns. A function that returns the one boolean
-- the public is entitled to know is the narrower fix.
create or replace function public.billing_is_live()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select coalesce((select state.billing_is_live
                     from public.billing_state state
                     where state.id), false);
$$;

revoke all on function public.billing_is_live() from public;
grant execute on function public.billing_is_live() to anon, authenticated, service_role;


-- 4. The account page's plan switcher silently did nothing.
--
-- account_status has a select policy and no update policy, so the website's
-- update matched zero rows and returned success. The page then re-read the
-- unchanged plan and reverted with no message.
--
-- Adding an update policy would let anyone grant themselves Max, so instead
-- this function does the write and refuses anybody who is not staff. It is what
-- lets the testers move between plans without paying while a real customer
-- still has to buy one.
create or replace function public.set_my_test_plan(target_plan public.plan_tier)
returns public.plan_tier
language plpgsql
volatile
security definer
set search_path = ''
as $$
declare
    caller uuid := (select auth.uid());
    caller_role public.user_role;
begin
    if caller is null then
        raise exception 'Not signed in.' using errcode = '28000';
    end if;

    select status.role into caller_role
      from public.account_status status
     where status.user_id = caller;

    if caller_role is null or caller_role not in ('developer', 'founder', 'admin') then
        raise exception 'Only staff accounts may change plan without paying for it.'
            using errcode = '42501';
    end if;

    update public.account_status
       set plan = target_plan,
           updated_at = now()
     where user_id = caller;

    return target_plan;
end;
$$;

revoke all on function public.set_my_test_plan(public.plan_tier) from public;
grant execute on function public.set_my_test_plan(public.plan_tier) to authenticated, service_role;


-- 5. Anyone could read anyone else's usage.
--
-- usage_this_period(target uuid) takes the account to report on as an argument
-- and was executable by `anon`, so a user id -- which is not a secret -- was
-- enough to read somebody's spend and request count.
--
-- NOTE: the revoke below is not sufficient on its own and is kept only because
-- it is what was applied. Postgres grants EXECUTE on a new function to PUBLIC,
-- and Supabase's default privileges grant it to anon explicitly on top of that,
-- so naming anon alone leaves both standing. The next two migrations do it
-- properly. Left here rather than rewritten so the file matches what ran.
revoke execute on function public.usage_this_period(uuid) from anon;
