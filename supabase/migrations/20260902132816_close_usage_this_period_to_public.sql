-- Close usage_this_period(uuid) properly.
--
-- The revoke in the previous migration named `anon` and did nothing. Postgres
-- grants EXECUTE on every new function to PUBLIC, and `anon` inherits it from
-- there, so the grant has to be taken away from PUBLIC itself.
--
-- Nothing legitimate loses access. my_usage_this_period() reaches this function
-- as a security definer, so it runs with the owner's rights rather than the
-- caller's, and the gateway connects as service_role.
revoke all on function public.usage_this_period(uuid) from public;
revoke all on function public.usage_this_period(uuid) from anon;
revoke all on function public.usage_this_period(uuid) from authenticated;
grant execute on function public.usage_this_period(uuid) to service_role;
