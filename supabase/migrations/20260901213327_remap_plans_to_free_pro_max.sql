-- Move every plan-typed row onto the new naming, highest first.
--
-- Order matters and is the whole reason this is a separate migration. Both
-- target names already exist, so remapping plus -> pro before pro -> max would
-- promote the old Plus rows twice and land them all on Max. Doing the top of
-- the ladder first means no row is ever touched by both statements.
update public.account_status set plan = 'max'  where plan = 'pro';
update public.account_status set plan = 'pro'  where plan = 'plus';

update public.subscriptions set plan_key = 'max' where plan_key = 'pro';
update public.subscriptions set plan_key = 'pro' where plan_key = 'plus';

delete from public.plan_limits where plan = 'plus';
delete from public.plan_limits where plan = 'pro';
delete from public.plan_limits where plan = 'max';

-- The allowances, in the customer's units.
--
-- Free is a real trial rather than a demonstration: fifty answers is enough to
-- find out whether Metis is useful, dictation is generous because it is cheap,
-- and ten agent messages is enough to watch an agent work once without funding
-- somebody's unattended overnight run.
--
-- Pro and Max have no count on talk or dictation. They are bounded by money
-- instead, which is the honest ceiling on a plan somebody is paying for: a
-- count refuses a person who has spent almost nothing. Agents keep a count on
-- every plan because one action there turns into dozens of paid calls without
-- another, so spend notices far too late.
insert into public.plan_limits (
    plan, monthly_budget_usd, max_screenshot_bytes, requests_per_minute,
    burst_requests, max_agent_steps_per_month, max_agent_steps_per_task,
    memory_entries_max, managed_models, max_turns_per_month,
    max_dictation_minutes_per_month)
values
    ('free', 1.00, 1048576, 3, 6, 10, 20, 50,
     array['gemini-2.5-flash-lite'], 50, 300),

    ('pro', 9.00, 8388608, 20, 40, 400, 60, 2000,
     array['gemini-2.5-flash-lite', 'gemini-2.5-flash'], 0, 0),

    ('max', 22.00, 8388608, 30, 60, 2000, 120, 10000,
     array['gemini-2.5-flash-lite', 'gemini-2.5-flash', 'gemini-2.5-pro'], 0, 0)
on conflict (plan) do update set
    monthly_budget_usd = excluded.monthly_budget_usd,
    max_screenshot_bytes = excluded.max_screenshot_bytes,
    requests_per_minute = excluded.requests_per_minute,
    burst_requests = excluded.burst_requests,
    max_agent_steps_per_month = excluded.max_agent_steps_per_month,
    max_agent_steps_per_task = excluded.max_agent_steps_per_task,
    memory_entries_max = excluded.memory_entries_max,
    managed_models = excluded.managed_models,
    max_turns_per_month = excluded.max_turns_per_month,
    max_dictation_minutes_per_month = excluded.max_dictation_minutes_per_month,
    updated_at = now();
