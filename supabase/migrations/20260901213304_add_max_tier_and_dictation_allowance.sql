-- The plans become Free, Pro and Max.
--
-- What was Plus at $14 is now Pro at $20, and what was Pro at $29 is now Max at
-- $50. The names moved as well as the prices, which is why this cannot be a
-- simple UPDATE: 'pro' means one thing before this migration and a different
-- thing after it, so the remap has to run in an order where no row is ever
-- ambiguous. That happens in the next migration, once 'max' exists — Postgres
-- will not let a value added to an enum be used in the same transaction.
--
-- 'plus' is deliberately left in the enum. Postgres cannot drop an enum value
-- without rewriting every dependent column, and a legacy value that nothing
-- writes any more costs nothing. Entitlements.ParsePlan maps it to Pro so an
-- old row still resolves to the plan its owner actually paid for.
alter type plan_tier add value if not exists 'max';

-- Dictation becomes a plan line of its own, separate from conversation.
--
-- The two are different things and cost different amounts: a talk message is an
-- answer from a reasoning model, dictation is a transcription. Metering them
-- together would mean speaking a long note ate the same allowance as asking a
-- hard question, which is neither fair nor what the pricing page says.
--
-- Minutes rather than requests, because that is how transcription is billed and
-- how a person thinks about dictating. 0 means no cap.
alter table public.plan_limits
    add column if not exists max_dictation_minutes_per_month integer not null default 0;

comment on column public.plan_limits.max_dictation_minutes_per_month is
    'Minutes of managed dictation a month, or 0 for no cap. Dictation on the device''s own speech engine, or on a key the user brought, is never counted here — Metis is not paying for it.';
