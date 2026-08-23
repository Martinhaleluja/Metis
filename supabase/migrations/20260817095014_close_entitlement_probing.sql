-- Two holes the database linter found, both mine.
--
-- First: has_feature and flag_enabled took a user id as an argument and ran as
-- SECURITY DEFINER, which meant anyone -- signed in or not -- could ask them
-- about somebody else and learn who the administrators are. A function that
-- answers questions about an arbitrary user must not be reachable from the
-- API. The client-facing versions now take no target at all and answer only
-- about the caller, so there is nothing to probe with.
--
-- Second: the two trigger functions were exposed as callable RPC endpoints.
-- They exist to fire on writes to auth.users and were never meant to be part
-- of the API surface.

drop function if exists public.has_feature(uuid, public.metis_feature);
drop function if exists public.flag_enabled(uuid, text, public.metis_environment);

-- Answers only about whoever is calling.
create or replace function public.my_feature(feature public.metis_feature)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select case
        when status.user_id is null then false
        when not status.email_verified then false
        when feature = 'computer_control' then true
        when feature = 'admin_dashboard' then status.role in ('founder', 'admin')
        when feature in ('developer_mode', 'experimental_features', 'staging_access', 'internal_cost_visibility')
            then status.role in ('developer', 'founder', 'admin')
        when feature in ('custom_ai_provider', 'system_commands')
            then status.plan = 'pro' or status.role in ('developer', 'founder', 'admin')
        else false
    end
    from public.account_status as status
    where status.user_id = (select auth.uid());
$$;

create or replace function public.my_flag(flag_key text, environment public.metis_environment)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select coalesce(
        (
            select case
                when not flag.enabled then false
                when not (environment = any (flag.environments)) then false
                when exists (
                    select 1 from public.feature_flag_users assigned
                    where assigned.key = flag.key and assigned.user_id = (select auth.uid())
                ) then true
                when flag.min_role is not null and status.role >= flag.min_role then true
                when flag.rollout_percent >= 100 then true
                when flag.rollout_percent <= 0 then false
                else (
                    ('x' || substr(md5(flag.key || (select auth.uid())::text), 1, 8))::bit(32)::bigint % 100
                ) < flag.rollout_percent
            end
            from public.feature_flags as flag
            left join public.account_status as status on status.user_id = (select auth.uid())
            where flag.key = flag_key
        ),
        false
    );
$$;

-- Signing out should not let anyone ask these anything.
revoke execute on function public.my_feature(public.metis_feature) from anon;
revoke execute on function public.my_flag(text, public.metis_environment) from anon;
grant execute on function public.my_feature(public.metis_feature) to authenticated;
grant execute on function public.my_flag(text, public.metis_environment) to authenticated;

-- Triggers are not API endpoints.
revoke execute on function public.handle_new_user() from public, anon, authenticated;
revoke execute on function public.sync_email_verified() from public, anon, authenticated;

comment on table public.audit_logs is
    'Server-only. RLS is enabled with no policies on purpose: the audit trail is
     readable through the service role and nothing else. The database linter
     reports this as an informational finding; it is the intended design.';
