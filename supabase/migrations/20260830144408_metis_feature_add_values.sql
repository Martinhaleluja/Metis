-- The capabilities the three plans divide between them.
--
-- Same rule as the previous file: `add value` statements only, so a later
-- migration may reference them. These mirror the MetisFeature enum in
-- src/Metis.Core/Models/AccountModels.cs value for value, and a test in
-- tests/Metis.Tests reads this directory from disk to prove they still do. A
-- capability that exists on one side and not the other is a permission question
-- with two different answers, which is the failure this whole design is built
-- to avoid.
--
-- Everything here gates what *Metis* pays for, never what a user's own API key
-- may do. Someone running Metis on their own key never reaches the gateway, so
-- none of these are ever consulted for them.

alter type public.metis_feature add value if not exists 'managed_ai_routing';
alter type public.metis_feature add value if not exists 'managed_premium_models';
alter type public.metis_feature add value if not exists 'managed_screen_vision';
alter type public.metis_feature add value if not exists 'advanced_automation';
alter type public.metis_feature add value if not exists 'autonomous_agents';
alter type public.metis_feature add value if not exists 'advanced_agents';
alter type public.metis_feature add value if not exists 'persistent_memory';
alter type public.metis_feature add value if not exists 'browser_assistance';
alter type public.metis_feature add value if not exists 'usage_visibility';
alter type public.metis_feature add value if not exists 'provider_management';
