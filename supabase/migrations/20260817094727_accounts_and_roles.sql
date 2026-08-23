-- Metis accounts.
--
-- Roles and plans live in their own table, apart from anything the user may
-- edit. This is the whole point of splitting them: Postgres row-level security
-- grants or denies a whole row, not a column, so a profiles table that carried
-- both a display name and a role would either stop users renaming themselves or
-- let them promote themselves to founder. Keeping the two apart means the
-- account_status table simply has no write policy at all, and only the service
-- role -- which bypasses RLS and lives on the server -- can change what someone
-- is allowed to do.

create type public.user_role as enum ('user', 'pro', 'developer', 'founder', 'admin');
create type public.plan_tier as enum ('free', 'pro');

-- Editable by the person it belongs to.
create table public.profiles (
    id uuid primary key references auth.users (id) on delete cascade,
    display_name text check (char_length(display_name) <= 80),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

-- Not editable by anyone but the server.
create table public.account_status (
    user_id uuid primary key references auth.users (id) on delete cascade,
    role public.user_role not null default 'user',
    plan public.plan_tier not null default 'free',

    -- Denormalised from auth.users so entitlement checks are one read, and so
    -- the client cannot claim to be verified when it is not.
    email_verified boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table public.profiles enable row level security;
alter table public.account_status enable row level security;

create policy "read own profile"
    on public.profiles for select
    using ((select auth.uid()) = id);

create policy "edit own profile"
    on public.profiles for update
    using ((select auth.uid()) = id)
    with check ((select auth.uid()) = id);

-- Select only. There is deliberately no insert, update or delete policy here:
-- with RLS enabled and no policy, those operations are denied outright for
-- every ordinary client, whatever it sends.
create policy "read own account status"
    on public.account_status for select
    using ((select auth.uid()) = user_id);

-- Every new sign-up gets both rows, as an ordinary free user. A role is
-- something granted afterwards by the server, never something chosen at
-- registration.
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    insert into public.profiles (id, display_name)
    values (new.id, nullif(new.raw_user_meta_data ->> 'display_name', ''));

    insert into public.account_status (user_id, email_verified)
    values (new.id, new.email_confirmed_at is not null);

    return new;
end;
$$;

create trigger on_auth_user_created
    after insert on auth.users
    for each row execute function public.handle_new_user();

-- Keeps the denormalised copy honest when the address is confirmed later.
create or replace function public.sync_email_verified()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    update public.account_status
    set email_verified = new.email_confirmed_at is not null,
        updated_at = now()
    where user_id = new.id;
    return new;
end;
$$;

create trigger on_auth_user_verified
    after update of email_confirmed_at on auth.users
    for each row execute function public.sync_email_verified();
