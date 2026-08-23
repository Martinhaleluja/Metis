-- The waitlist is reachable from the browser only through these two functions.

create or replace function public.waitlist_count()
returns bigint
language sql
security definer
set search_path = ''
stable
as $$
    select count(*) from public.waitlist_signups;
$$;

comment on function public.waitlist_count() is
    'Total number of people on the launch waitlist. Exposes a count and nothing else.';


create or replace function public.join_waitlist(
    p_email         text,
    p_referral_code text default null,
    p_source        text default null
)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_email    text := lower(btrim(p_email));
    v_ip       text := nullif(btrim(split_part(
                   coalesce(current_setting('request.headers', true)::json ->> 'x-forwarded-for', ''),
                   ',', 1)), '');
    v_ip_hash  text;
    v_recent   integer;
    v_referrer uuid;
    v_code     text;
    v_attempt  integer := 0;
    v_row      public.waitlist_signups%rowtype;
begin
    if v_email is null or v_email !~ '^[^@[:space:]]+@[^@[:space:].]+\.[^@[:space:]]{2,}$' then
        return jsonb_build_object('ok', false, 'error', 'invalid_email');
    end if;

    if length(v_email) > 254 then
        return jsonb_build_object('ok', false, 'error', 'invalid_email');
    end if;

    if v_ip is not null then
        v_ip_hash := md5(v_ip);
    end if;

    -- Joining twice is not an error; it returns the original place in the queue
    -- so a returning visitor sees their number instead of a duplicate warning.
    select * into v_row
    from public.waitlist_signups
    where lower(email) = v_email;

    if found then
        return jsonb_build_object(
            'ok',            true,
            'already_joined', true,
            'position',      v_row.position,
            'referral_code', v_row.referral_code,
            'referrals',     v_row.referral_count,
            'total',         (select count(*) from public.waitlist_signups)
        );
    end if;

    -- Modest per-address throttle so one client cannot flood the table.
    if v_ip_hash is not null then
        select count(*) into v_recent
        from public.waitlist_signups
        where ip_hash = v_ip_hash
          and created_at > now() - interval '1 hour';

        if v_recent >= 5 then
            return jsonb_build_object('ok', false, 'error', 'rate_limited');
        end if;
    end if;

    if p_referral_code is not null then
        select id into v_referrer
        from public.waitlist_signups
        where referral_code = upper(btrim(p_referral_code));
    end if;

    -- Short shareable code, retried on the unlikely collision.
    loop
        v_attempt := v_attempt + 1;
        v_code := upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 7));

        begin
            insert into public.waitlist_signups (email, referral_code, referred_by, source, ip_hash)
            values (v_email, v_code, v_referrer, nullif(btrim(coalesce(p_source, '')), ''), v_ip_hash)
            returning * into v_row;
            exit;
        exception
            when unique_violation then
                -- A racing request took the email while we were working.
                select * into v_row
                from public.waitlist_signups
                where lower(email) = v_email;

                if found then
                    return jsonb_build_object(
                        'ok',            true,
                        'already_joined', true,
                        'position',      v_row.position,
                        'referral_code', v_row.referral_code,
                        'referrals',     v_row.referral_count,
                        'total',         (select count(*) from public.waitlist_signups)
                    );
                end if;

                if v_attempt >= 5 then
                    raise;
                end if;
        end;
    end loop;

    if v_referrer is not null then
        update public.waitlist_signups
        set referral_count = referral_count + 1
        where id = v_referrer;
    end if;

    return jsonb_build_object(
        'ok',            true,
        'already_joined', false,
        'position',      v_row.position,
        'referral_code', v_row.referral_code,
        'referrals',     0,
        'total',         (select count(*) from public.waitlist_signups)
    );
end;
$$;

comment on function public.join_waitlist(text, text, text) is
    'Adds an address to the launch waitlist and returns that person''s own row
     only. Idempotent, so joining twice returns the original position.';


revoke all on function public.waitlist_count() from public;
revoke all on function public.join_waitlist(text, text, text) from public;

grant execute on function public.waitlist_count() to anon, authenticated;
grant execute on function public.join_waitlist(text, text, text) to anon, authenticated;
