-- The public launch waitlist that backs the marketing site.

create table public.waitlist_signups (
    id             uuid primary key default gen_random_uuid(),
    position       bigint generated always as identity,
    email          text not null,
    referral_code  text not null,
    referred_by    uuid references public.waitlist_signups (id) on delete set null,
    referral_count integer not null default 0,
    source         text,
    ip_hash        text,
    created_at     timestamptz not null default now()
);

create unique index waitlist_signups_email_key
    on public.waitlist_signups (lower(email));

create unique index waitlist_signups_referral_code_key
    on public.waitlist_signups (referral_code);

create index waitlist_signups_referred_by_idx
    on public.waitlist_signups (referred_by);

create index waitlist_signups_ip_hash_created_at_idx
    on public.waitlist_signups (ip_hash, created_at desc);

alter table public.waitlist_signups enable row level security;

comment on table public.waitlist_signups is
    'Launch waitlist. RLS is enabled with no policies on purpose: every read and
     write goes through the security-definer functions below, so a browser
     holding the publishable key can never enumerate email addresses. The
     database linter reports the missing policies as informational; that is the
     intended design.';
