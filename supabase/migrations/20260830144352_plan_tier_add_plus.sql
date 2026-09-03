-- Metis grows a middle plan: Free, Plus, Pro.
--
-- This file contains one statement and nothing else, on purpose. Postgres
-- permits `alter type ... add value` inside a transaction block, but has
-- historically refused to let the *new* value be referenced in that same
-- transaction. `supabase db push` wraps each file in a transaction, so anything
-- that mentions 'plus' has to arrive in a later file. Splitting it costs one
-- file and is correct on every version rather than only on recent ones.
--
-- Enum values cannot be dropped once added. 'plus' is therefore permanent, which
-- is accepted: it is the name of a plan people will be paying for.

alter type public.plan_tier add value if not exists 'plus' after 'free';
