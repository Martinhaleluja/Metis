-- Storing a customer's own provider API key, properly.
--
-- user_ai_connections has had a secret_id column since it was created, with a
-- comment promising the secret itself lives in Supabase Vault. Nothing ever
-- wrote one: POST /v1/connections returned 501 saying exactly that. This is the
-- missing half.
--
-- Vault rather than encrypting in the application: the encryption key is managed
-- by Supabase and never lands in a Render environment variable, so an accidental
-- environment dump does not hand over every customer's provider key at once, and
-- rotating does not mean a bespoke re-encrypt-every-row migration.
--
-- The wrappers below exist because PostgREST exposes only the `public` and
-- `graphql_public` schemas. The `vault` schema is not reachable over the REST
-- interface at all, so the gateway cannot call vault.create_secret directly, and
-- exposing `vault` to PostgREST to avoid writing these would be a far worse
-- trade than writing them.
--
-- Every function here is service_role only. `authenticated` must never be able
-- to read a secret back, including its own: the desktop app keeps its keys in
-- Windows Credential Manager and has no reason to ask, and a read path that a
-- browser could reach is a read path an XSS bug could reach.

create or replace function public.store_provider_secret(
    target uuid,
    provider_key text,
    secret text,
    hint text,
    model_name text
)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    existing uuid;
    created uuid;
begin
    select connections.secret_id into existing
      from public.user_ai_connections connections
     where connections.user_id = target
       and connections.provider = provider_key;

    -- Reconnecting an existing provider replaces the secret in place rather
    -- than leaving the old one behind. There is a unique constraint on
    -- (user_id, provider), so there is only ever one to replace.
    if existing is not null then
        perform vault.update_secret(existing, secret, null, null, null);
        update public.user_ai_connections
           set key_hint = hint,
               model = model_name,
               last_tested_at = now(),
               last_test_ok = true
         where user_id = target
           and provider = provider_key;
        return existing;
    end if;

    created := vault.create_secret(
        secret,
        'metis:' || target::text || ':' || provider_key,
        'Metis bring-your-own provider key',
        null);

    insert into public.user_ai_connections
        (user_id, provider, model, secret_id, key_hint, last_tested_at, last_test_ok)
    values
        (target, provider_key, model_name, created, hint, now(), true);

    return created;
end;
$$;

revoke execute on function public.store_provider_secret(uuid, text, text, text, text)
    from public, anon, authenticated;
grant execute on function public.store_provider_secret(uuid, text, text, text, text)
    to service_role;

-- Reads a stored key back so the gateway can proxy a request on the customer's
-- own credential. This is the single most dangerous function in the schema and
-- it is granted to service_role and nothing else.
create or replace function public.read_provider_secret(target uuid, provider_key text)
returns text
language sql
stable
security definer
set search_path = ''
as $$
    select secrets.decrypted_secret
      from public.user_ai_connections connections
      join vault.decrypted_secrets secrets on secrets.id = connections.secret_id
     where connections.user_id = target
       and connections.provider = provider_key;
$$;

revoke execute on function public.read_provider_secret(uuid, text)
    from public, anon, authenticated;
grant execute on function public.read_provider_secret(uuid, text) to service_role;

-- Disconnecting. Removes the row, the secret, and leaves a record that it
-- happened, because "I removed my key" is exactly the kind of claim someone
-- needs to be able to check later.
create or replace function public.forget_provider_secret(target uuid, provider_key text)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
    doomed uuid;
begin
    delete from public.user_ai_connections
     where user_id = target
       and provider = provider_key
    returning secret_id into doomed;

    if doomed is null then
        return false;
    end if;

    delete from vault.secrets where id = doomed;

    insert into public.audit_logs (user_id, action, detail)
    values (target, 'connection.revoked', jsonb_build_object('provider', provider_key));

    return true;
end;
$$;

revoke execute on function public.forget_provider_secret(uuid, text)
    from public, anon, authenticated;
grant execute on function public.forget_provider_secret(uuid, text) to service_role;

-- ---------------------------------------------------------------------------
-- The orphan.
--
-- user_ai_connections carries a "delete own connections" policy, so a customer
-- can delete the row directly with the publishable key and never go through
-- forget_provider_secret. That leaves the vault secret with nothing pointing at
-- it: their API key stays encrypted at rest, indefinitely, after they believe
-- they removed it. Nobody would ever find it again to delete it.
--
-- The policy is worth keeping — being able to disconnect without the gateway
-- being up is the right behaviour. So the cleanup follows the row instead.
create or replace function public.forget_orphaned_provider_secret()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
    if old.secret_id is not null then
        delete from vault.secrets where id = old.secret_id;
    end if;
    return old;
end;
$$;

drop trigger if exists on_connection_deleted on public.user_ai_connections;

create trigger on_connection_deleted
    after delete on public.user_ai_connections
    for each row
    execute function public.forget_orphaned_provider_secret();

revoke execute on function public.forget_orphaned_provider_secret()
    from public, anon, authenticated;
