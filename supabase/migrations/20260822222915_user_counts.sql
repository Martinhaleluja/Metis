-- How many people are using Metis.
--
-- The Supabase dashboard already answers this for whoever can log into it. This
-- function exists so the number can be read programmatically -- from a script,
-- or one day from an admin view inside Metis -- without granting anything a
-- read of auth.users, which would expose every address.
--
-- It returns counts and nothing else: no ids, no emails, no per-user rows. That
-- is what makes it safe to expose at all.
--
-- Unlike waitlist_count(), this is not public. The size of the waitlist is a
-- number the marketing site prints on purpose; how many people have accounts is
-- not, so the grant follows the same rule as the admin dashboard and answers
-- only for founders and admins. Everyone else gets null rather than an error,
-- because a distinguishable failure is itself an answer.
create or replace function public.metis_user_counts()
returns jsonb
language sql
stable
security definer
set search_path = ''
as $$
    select case
        when not coalesce((select status.role in ('founder', 'admin')
                           from public.account_status status
                           where status.user_id = (select auth.uid())), false)
            then null::jsonb
        else jsonb_build_object(
            'total_accounts',    (select count(*) from public.account_status),
            'verified_accounts', (select count(*) from public.account_status
                                   where email_verified),
            'joined_7d',         (select count(*) from public.account_status
                                   where created_at > now() - interval '7 days'),
            'joined_30d',        (select count(*) from public.account_status
                                   where created_at > now() - interval '30 days'),
            'active_7d',         (select count(*) from auth.users
                                   where last_sign_in_at > now() - interval '7 days'),
            'active_30d',        (select count(*) from auth.users
                                   where last_sign_in_at > now() - interval '30 days'),
            'waitlist',          (select count(*) from public.waitlist_signups),
            'as_of',             now()
        )
    end;
$$;

comment on function public.metis_user_counts() is
    'Aggregate account counts for founders and admins. Returns null for anyone
     else. Exposes no identifiers of any kind.';

revoke all on function public.metis_user_counts() from public;
grant execute on function public.metis_user_counts() to authenticated;
