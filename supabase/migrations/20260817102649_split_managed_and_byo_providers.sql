-- A provider can be offered two different ways, and conflating them was a
-- mistake in the first cut of this table.
--
--   managed  Metis holds the key and pays for the inference. Google only, for
--            now, because that is the one with a free tier that does vision.
--   byo      The user connects their own key. Every provider stays available
--            here, because a Pro user paying Anthropic directly costs Metis
--            nothing and removing the option would only push them elsewhere.
--
-- Keeping both columns means adding a managed provider later is a single update
-- rather than a schema change and a release.

alter table public.ai_providers
    add column managed_available boolean not null default false,
    add column byo_available boolean not null default true;

update public.ai_providers set managed_available = true where key = 'google';

-- Ollama runs on the user's own machine and has no key to connect, so it is
-- neither managed nor bring-your-own. It is simply local.
update public.ai_providers set byo_available = false where key = 'ollama';

comment on column public.ai_providers.managed_available is
    'Metis supplies the key and pays for the inference. The gateway also checks
     that the provider key is actually configured before offering it, so this
     column says what is intended and the gateway says what is currently true.';

comment on column public.ai_providers.byo_available is
    'A Pro user may connect their own key for this provider.';
