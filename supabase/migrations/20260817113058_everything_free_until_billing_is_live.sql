-- Metis is free until there is a way to take payment.
--
-- One row decides it, so switching billing on later is an update rather than a
-- migration. The client has the same switch compiled in; both have to agree,
-- and both default to free.
--
-- Role checks are deliberately untouched. Free does not mean everyone is staff:
-- developer diagnostics and the admin dashboard stay closed regardless.

create table public.billing_state (
    id boolean primary key default true check (id),
    billing_is_live boolean not null default false,
    note text,
    updated_at timestamptz not null default now()
);

insert into public.billing_state (billing_is_live, note)
values (false, 'Early access. Every paid capability is free until a payment provider is settled.');

alter table public.billing_state enable row level security;

create policy "anyone may read whether billing is live"
    on public.billing_state for select
    to authenticated
    using (true);

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

        -- Everything else is free while billing is off.
        when not (select billing_is_live from public.billing_state where id) then true

        when feature = 'computer_control' then true
        when feature in ('custom_ai_provider', 'system_commands')
            then coalesce((select status.email_verified
                               and (status.plan = 'pro' or status.role in ('developer', 'founder', 'admin'))
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)
        else false
    end;
$$;

revoke execute on function public.my_feature(public.metis_feature) from public;
grant execute on function public.my_feature(public.metis_feature) to authenticated;
