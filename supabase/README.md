# The Metis backend

Everything Metis keeps on a server lives in one Supabase project. Accounts,
roles, entitlements, feature flags, usage records and the launch waitlist are
all here; there is no second database and no other provider.

## The project

| | |
|---|---|
| Name | `metis-staging` |
| Reference | `liaqpxhgxmdjzwxullag` |
| API URL | `https://liaqpxhgxmdjzwxullag.supabase.co` |
| Region | `eu-west-1` |
| Postgres | 17 |

The name says staging because it was the first one built. It is the project the
desktop app and the launch site both talk to today. If a separate production
project is ever created, the only things that change are the URL and the
publishable key; the migrations in this directory recreate the rest.

### Keys

The **publishable key** (`sb_publishable_…`) identifies the project and grants
nothing by itself — row level security decides what any request may actually
read. It is meant to be public, and already is: every visitor to the launch site
downloads it. It is therefore safe in `settings.json`, in the website's `.env`,
and in a compiled build.

The **service role key** is the opposite: it bypasses row level security
entirely. It belongs only in server environment variables — never in the desktop
app, never in the website, never in this repository.

## Why this directory exists

Until now the schema existed **only inside the hosted project**. Nothing in
version control described it, so a deleted or expired project would have taken
every table, policy and function with it, and no one could have rebuilt what the
app depends on.

The files in `migrations/` were exported from the live database rather than
written from memory, and each one was verified byte for byte against the
`supabase_migrations.schema_migrations` record it came from. They are the real
history, not a reconstruction.

## Applying them

```bash
supabase link --project-ref liaqpxhgxmdjzwxullag
supabase db push
```

Migrations are append-only. To change something, add a new file rather than
editing one that has already run — the two corrective migrations in the history
are there precisely because that rule was followed.

## What is in the database

**Accounts.** `profiles` holds what a user may edit about themselves;
`account_status` holds what only the server may set — their role and plan. They
are two tables on purpose: row level security grants or denies a whole row, so a
single table carrying both a display name and a role would either stop users
renaming themselves or let them promote themselves to founder.

**Entitlements.** `my_feature(feature)` answers what the caller is allowed to do,
and `my_flag(key, environment)` resolves feature flags by environment, role,
explicit assignment or a stable percentage bucket. Both answer only about
whoever is calling — an earlier pair took a user id and could be used to discover
who the administrators were.

**Billing.** `billing_state` has one row and one job: while `billing_is_live` is
false, every paid capability is free. Turning billing on later is an update
rather than a migration. Staff capabilities are deliberately exempt — free does
not mean everyone is a developer.

**Usage.** `usage_events` records how much, how often and how slow, written by
the API with the service key. It deliberately carries no prompt text, no
screenshot and no response: it is a cost ledger, not a record of what anyone was
doing on their screen.

**Waitlist.** `waitlist_signups` backs the launch site. Row level security is
enabled with **no policies at all**, so the publishable key cannot touch a row
directly and no visitor can enumerate anyone's address. The only ways in are
`join_waitlist(...)`, which returns the caller's own position and referral code
and nothing else, and `waitlist_count()`, which returns a single number.

## How many people are using Metis

The Supabase dashboard answers this for anyone who can log into it. To read it
programmatically:

```
POST /rest/v1/rpc/metis_user_counts
```

It returns total accounts, verified accounts, how many joined in the last 7 and
30 days, how many signed in over the same windows, and the waitlist total. It
returns **counts only** — no ids, no addresses, no per-user rows.

Unlike `waitlist_count()`, it is not public. The waitlist total is a number the
marketing site prints on purpose; how many accounts exist is not. The function
answers only for `founder` and `admin` roles and returns `null` to everyone else,
including anonymous callers.

### A trap worth knowing about

Supabase sets `ALTER DEFAULT PRIVILEGES` on the `public` schema so that every
newly created function is granted to `anon`, `authenticated` and `service_role`
**explicitly**. Two consequences, and the history here contains one migration for
each of them:

- Revoking from `anon` alone is not enough, because `PUBLIC` also carries the
  grant and `anon` inherits it.
- Revoking from `PUBLIC` alone is not enough either, because the explicit `anon`
  grant survives it.

So a function that should require a session must revoke from **both** and then
grant back to exactly the role that should have it:

```sql
revoke all on function public.example() from public, anon;
grant execute on function public.example() to authenticated;
```

Run `supabase db lint`, or the advisors in the dashboard, after adding any
`security definer` function. The linter catches this immediately.

## The free tier, and the one thing to watch

The free plan covers 50,000 monthly active users, 0.5 GB of database per active
project and two active projects at a time — far beyond what testing needs.

The catch is that **a free project pauses after 7 days with no activity**, and a
paused project answers nothing. Two things guard against it: any real use keeps
it awake, and the desktop app is built to let an already-signed-in user carry on
working when the backend cannot be reached, so a pause degrades the experience
rather than locking anyone out.

## Known stale corner

The `metis_feature` enum still contains `computer_control` and `system_commands`,
from when Metis was an assistant that could drive the machine. Metis is a
learning instrument now and does neither. The values are harmless — nothing asks
for them — and removing a value from a Postgres enum is awkward, so they are left
until there is a reason to rewrite the type.
