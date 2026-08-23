-- A Pro user's own provider credentials, and what everyone's requests cost.

create table public.ai_providers (
    key text primary key check (key ~ '^[a-z0-9_]+$'),
    display_name text not null,
    -- Configurable rather than hard-coded through the application, so adding a
    -- provider is a row rather than a release.
    enabled boolean not null default true,
    supports_vision boolean not null default true,
    sort_order smallint not null default 100
);

insert into public.ai_providers (key, display_name, sort_order) values
    ('anthropic', 'Anthropic', 10),
    ('openai', 'OpenAI', 20),
    ('google', 'Google', 30),
    ('openrouter', 'OpenRouter', 40),
    ('ollama', 'Ollama (on this PC)', 50);

-- The credential itself is never in this table. It lives in Supabase Vault,
-- which is encrypted at rest and unreadable through the API no matter what
-- policies exist here; this row holds only the reference and the metadata the
-- client is allowed to see. That separation is what makes it safe for a user to
-- read their own row: there is nothing secret in it.
create table public.user_ai_connections (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users (id) on delete cascade,
    provider text not null references public.ai_providers (key),
    model text,
    secret_id uuid not null,

    -- Enough to show "Connected — sk-...4f2a" without ever holding the key.
    key_hint text check (char_length(key_hint) <= 12),
    last_tested_at timestamptz,
    last_test_ok boolean,
    created_at timestamptz not null default now(),
    unique (user_id, provider)
);

alter table public.ai_providers enable row level security;
alter table public.user_ai_connections enable row level security;

create policy "anyone signed in reads the provider list"
    on public.ai_providers for select
    to authenticated
    using (enabled);

create policy "read own connections"
    on public.user_ai_connections for select
    to authenticated
    using ((select auth.uid()) = user_id);

-- A user may disconnect their own provider. Creating and updating a connection
-- goes through the server, because it has to write the secret into the vault
-- and validate the credentials against the provider first.
create policy "delete own connections"
    on public.user_ai_connections for delete
    to authenticated
    using ((select auth.uid()) = user_id);

-- What each request cost. Deliberately carries no prompt text, no screenshot,
-- and no response: this table answers "how much, how often, how slow" and is
-- not a record of what anyone was doing on their screen.
create table public.usage_events (
    id bigint generated always as identity primary key,
    user_id uuid not null references auth.users (id) on delete cascade,
    request_id uuid not null,
    provider text not null,
    model text,
    feature text not null,
    input_tokens integer,
    output_tokens integer,
    estimated_cost_usd numeric(10, 6),
    latency_ms integer,
    status text not null default 'ok',
    environment public.metis_environment not null default 'production',
    created_at timestamptz not null default now()
);

create index usage_events_user_time on public.usage_events (user_id, created_at desc);
create index usage_events_time on public.usage_events (created_at desc);

alter table public.usage_events enable row level security;

-- Users may read their own usage, so Metis can show them what they have used.
-- Only the server writes it.
create policy "read own usage"
    on public.usage_events for select
    to authenticated
    using ((select auth.uid()) = user_id);

-- Subscriptions. The backend is the source of truth: this table is written from
-- verified payment-provider webhooks and never from anything the client says.
create table public.subscriptions (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references auth.users (id) on delete cascade,
    provider text not null default 'paddle',
    provider_subscription_id text not null,
    status text not null,
    current_period_end timestamptz,
    cancel_at_period_end boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique (provider, provider_subscription_id)
);

create index subscriptions_user on public.subscriptions (user_id);

alter table public.subscriptions enable row level security;

create policy "read own subscription"
    on public.subscriptions for select
    to authenticated
    using ((select auth.uid()) = user_id);

-- Who changed what, for the things worth being able to reconstruct later.
create table public.audit_logs (
    id bigint generated always as identity primary key,
    user_id uuid references auth.users (id) on delete set null,
    action text not null,
    detail jsonb,
    created_at timestamptz not null default now()
);

create index audit_logs_time on public.audit_logs (created_at desc);

alter table public.audit_logs enable row level security;
-- No policies at all: the audit trail is readable only through the server.
