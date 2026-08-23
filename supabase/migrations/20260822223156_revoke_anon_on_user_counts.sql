-- The same trap as revoke_anon_execute_properly, walked into from the other
-- side.
--
-- That migration learned that revoking from `anon` alone is not enough, because
-- PUBLIC carries the grant. The converse is also true and is what happened to
-- metis_user_counts: Supabase sets ALTER DEFAULT PRIVILEGES on the public
-- schema so every new function is granted to anon, authenticated and
-- service_role *explicitly* at creation. An explicit grant is not touched by
-- revoking from PUBLIC, so the function shipped reachable at
-- /rest/v1/rpc/metis_user_counts without a session.
--
-- Nothing was exposed by it -- the function answers only for founders and
-- admins and returned null to an anonymous caller, which is why that check is
-- inside the function rather than left to the grant. But an endpoint that
-- should need a session should not be callable without one.
--
-- The rule worth remembering: revoke from both PUBLIC and anon, then grant back
-- to exactly the role that should have it.

revoke all on function public.metis_user_counts() from anon;
grant execute on function public.metis_user_counts() to authenticated;
