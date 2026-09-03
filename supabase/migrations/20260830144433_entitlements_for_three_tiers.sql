-- Who may do what, now that there are three plans.
--
-- This is the server's copy of the table in Metis.Core/Services/Entitlements.cs
-- and it has to say the same thing. The C# copy decides what the desktop app
-- shows; this one decides what row level security actually permits. Where they
-- disagree, this one wins and the user sees a control that does nothing, which
-- is why they are written to be read side by side.
--
-- The ordering of the cases below matters and mirrors the C# exactly:
--   1. staff capabilities, decided by role whether or not billing is live
--   2. everything free while billing_is_live is false
--   3. not signed in, or email unverified, earns nothing
--   4. the plan table

create or replace function public.my_feature(feature public.metis_feature)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select case
        -- Staff capabilities are about who you are, not what you paid.
        when feature in ('developer_mode', 'experimental_features', 'staging_access', 'internal_cost_visibility')
            then coalesce((select status.role in ('developer', 'founder', 'admin')
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)
        when feature = 'admin_dashboard'
            then coalesce((select status.role in ('founder', 'admin')
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Everything else is free while billing is off. Taking a working
        -- feature away from someone who has been using it since before there
        -- was anything to buy is the one outcome this whole design exists to
        -- prevent, and this line is where that promise is kept.
        when not (select billing_is_live from public.billing_state where id) then true

        -- Anyone with an account, once the address is proven.
        when feature in ('computer_control', 'managed_ai_routing', 'usage_visibility')
            then coalesce((select status.email_verified
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Plus and above.
        when feature in ('managed_premium_models', 'managed_screen_vision',
                         'advanced_automation', 'autonomous_agents',
                         'persistent_memory', 'browser_assistance')
            then coalesce((select status.email_verified
                               and (status.plan in ('plus', 'pro')
                                    or status.role in ('developer', 'founder', 'admin'))
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        -- Pro only.
        when feature in ('custom_ai_provider', 'system_commands',
                         'advanced_agents', 'provider_management')
            then coalesce((select status.email_verified
                               and (status.plan = 'pro'
                                    or status.role in ('developer', 'founder', 'admin'))
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)

        else false
    end;
$$;

revoke execute on function public.my_feature(public.metis_feature) from public;
grant execute on function public.my_feature(public.metis_feature) to authenticated;
