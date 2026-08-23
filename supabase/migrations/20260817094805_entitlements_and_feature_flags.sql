-- The server's copy of the entitlement rules.
--
-- The desktop client has the same table in C# and uses it to decide what to
-- show. This one decides what may actually happen. Both exist on purpose: a
-- program running on someone else's machine can be edited, so what it claims
-- about itself is a request rather than a fact.

create type public.metis_feature as enum (
    'custom_ai_provider',
    'computer_control',
    'system_commands',
    'developer_mode',
    'experimental_features',
    'staging_access',
    'admin_dashboard',
    'internal_cost_visibility'
);

create or replace function public.has_feature(target uuid, feature public.metis_feature)
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
    where status.user_id = target;
$$;

-- Feature flags, resolvable by environment, role, specific user, or a
-- percentage of everyone.
create type public.metis_environment as enum ('development', 'staging', 'production');

create table public.feature_flags (
    key text primary key check (key ~ '^[a-z0-9_]+$'),
    description text,
    environments public.metis_environment[] not null default '{development}',
    min_role public.user_role,
    rollout_percent smallint not null default 0 check (rollout_percent between 0 and 100),
    enabled boolean not null default false,
    updated_at timestamptz not null default now()
);

create table public.feature_flag_users (
    key text not null references public.feature_flags (key) on delete cascade,
    user_id uuid not null references auth.users (id) on delete cascade,
    primary key (key, user_id)
);

alter table public.feature_flags enable row level security;
alter table public.feature_flag_users enable row level security;

-- Flags are readable by any signed-in client, because it has to know what to
-- show. Only the server writes them.
create policy "signed in clients read flags"
    on public.feature_flags for select
    to authenticated
    using (true);

create policy "read own flag assignments"
    on public.feature_flag_users for select
    to authenticated
    using ((select auth.uid()) = user_id);

-- The percentage bucket is derived from the user id and the flag key together,
-- so a user sits in a stable bucket per flag rather than being re-rolled on
-- every call, and being in the first 10% of one flag says nothing about where
-- they fall on another.
create or replace function public.flag_enabled(
    target uuid,
    flag_key text,
    environment public.metis_environment
)
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
                    where assigned.key = flag.key and assigned.user_id = target
                ) then true
                when flag.min_role is not null and status.role >= flag.min_role then true
                when flag.rollout_percent >= 100 then true
                when flag.rollout_percent <= 0 then false
                else (
                    ('x' || substr(md5(flag.key || target::text), 1, 8))::bit(32)::bigint % 100
                ) < flag.rollout_percent
            end
            from public.feature_flags as flag
            left join public.account_status as status on status.user_id = target
            where flag.key = flag_key
        ),
        false
    );
$$;
