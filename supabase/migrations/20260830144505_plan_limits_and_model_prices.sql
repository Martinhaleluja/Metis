-- The numbers behind the plans, and what a model costs.
--
-- These are business assumptions, and business assumptions measured against real
-- usage change faster than a desktop application ships. Nothing here is compiled
-- into anything: the gateway reads these tables and refreshes them on a timer, so
-- raising an allowance or reacting to a provider's price change is an update
-- rather than a release everyone has to install.
--
-- The specification asked for exactly this, and it is worth saying why beyond
-- being asked. A monthly budget hard-coded in a binary is a number that can only
-- be corrected by replacing every copy of the binary, which in practice means it
-- never gets corrected and the rule stops matching what the company can afford.

create table public.plan_limits (
    plan public.plan_tier primary key,

    -- What Metis is willing to spend on this account's managed AI in a calendar
    -- month. Free is small rather than zero: a plan that cannot answer anything
    -- is not a trial, it is a screenshot of a product.
    monthly_budget_usd numeric(10,4) not null,

    -- The largest screenshot the gateway will accept, in bytes. Zero means this
    -- plan may not send one at all. An image is the most expensive part of a
    -- turn by a wide margin, which makes this the main lever between the plans.
    max_screenshot_bytes integer not null,

    requests_per_minute smallint not null,
    burst_requests smallint not null,

    -- Agents are the runaway-cost risk: one thirty-turn task is thirty requests,
    -- so a dollar budget alone lets a single stuck agent spend a month's
    -- allowance in ten minutes. The per-task ceiling is what stops that, and it
    -- is separate from the monthly one on purpose.
    max_agent_steps_per_month integer not null,
    max_agent_steps_per_task smallint not null,

    memory_entries_max integer not null,

    -- Which managed models this plan may ask for. Empty means "whatever the
    -- gateway considers cheapest", which is what Free gets.
    managed_models text[] not null default '{}',

    updated_at timestamptz not null default now()
);

comment on table public.plan_limits is
    'Per-plan allowances, read by the gateway and shown to the user. Server-only writes.';

insert into public.plan_limits (
    plan, monthly_budget_usd, max_screenshot_bytes, requests_per_minute,
    burst_requests, max_agent_steps_per_month, max_agent_steps_per_task,
    memory_entries_max, managed_models
) values
    -- Free: text-only on Metis's key. max_screenshot_bytes is 0 because seeing
    -- the screen is what Plus is for; a Free user who wants Metis to look at
    -- their screen can still do it on their own API key, for free, forever.
    ('free',  0.75,       0,  3,  6,    0,  0,   50,
     '{gemini-2.5-flash-lite}'),
    ('plus',  6.00, 4194304, 12, 20,  600, 30,  500,
     '{gemini-2.5-flash-lite,gemini-2.5-flash,gemini-3.5-flash-lite}'),
    ('pro',  12.00, 8388608, 25, 40, 2000, 60, 5000,
     '{gemini-2.5-flash-lite,gemini-2.5-flash,gemini-3.5-flash-lite,gemini-3.5-flash,gemini-2.5-pro}');

alter table public.plan_limits enable row level security;

-- Readable by anyone signed in, so the desktop app can size a screenshot down
-- before it uploads it and show a remaining allowance without a round trip to
-- the gateway. There is nothing sensitive here; these are the numbers on the
-- pricing page. There is deliberately no insert, update or delete policy: the
-- same server-only-writes pattern account_status already uses.
create policy "anyone signed in reads plan limits"
    on public.plan_limits for select
    to authenticated
    using (true);

create table public.model_prices (
    provider text not null,
    model text not null,
    input_usd_per_mtok numeric(10,4) not null,
    output_usd_per_mtok numeric(10,4) not null,
    cached_input_usd_per_mtok numeric(10,4),

    -- Prices change. Keeping the date in the key means a change is an insert
    -- rather than an update, and a usage row costed last month stays explicable
    -- afterwards instead of being silently repriced.
    effective_from timestamptz not null default now(),
    primary key (provider, model, effective_from)
);

comment on table public.model_prices is
    'Per-million-token provider prices. Verify against the provider''s live pricing page before inserting; training-data prices are wrong by definition.';

alter table public.model_prices enable row level security;

-- What a model costs Metis is the company's number, not the customer's. The
-- gateway reads it with the service key; the only people who may read it
-- through the API are the ones who already have internal cost visibility.
create policy "staff read model prices"
    on public.model_prices for select
    to authenticated
    using (
        exists (
            select 1 from public.account_status status
            where status.user_id = (select auth.uid())
              and status.role in ('developer', 'founder', 'admin')
        )
    );

-- Seeded from https://ai.google.dev/gemini-api/docs/pricing as read on
-- 2026-08-30. Checked against the live page rather than recalled, because a
-- price remembered from training data is a price that was true once and is now
-- the number Metis bills itself against.
--
-- Two of these have a free tier at low volume (2.5 Flash and 2.5 Flash-Lite),
-- which is why Free is pinned to Flash-Lite: at the volumes a free account
-- reaches, Metis often pays nothing at all, and the paid rate below is what it
-- costs when they do.
insert into public.model_prices (provider, model, input_usd_per_mtok, output_usd_per_mtok) values
    ('google', 'gemini-2.5-flash-lite', 0.10,  0.40),
    ('google', 'gemini-2.5-flash',      0.30,  2.50),
    ('google', 'gemini-3.5-flash-lite', 0.30,  2.50),
    ('google', 'gemini-3.5-flash',      1.50,  9.00),
    -- The >200k-token tier is $2.50/$15.00. The cheaper row is deliberately not
    -- used as the estimate: see the fallback rule in the gateway, which prices
    -- an unknown or ambiguous model at the most expensive row for its provider
    -- so an underestimate never becomes an overspend.
    ('google', 'gemini-2.5-pro',        2.50, 15.00);
