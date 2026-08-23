-- Revoking from `anon` alone did nothing, because Postgres grants EXECUTE on
-- new functions to PUBLIC and anon inherits that. The grant has to be taken
-- away from PUBLIC and then given back to the one role that should have it.

revoke execute on function public.my_feature(public.metis_feature) from public;
revoke execute on function public.my_flag(text, public.metis_environment) from public;

grant execute on function public.my_feature(public.metis_feature) to authenticated;
grant execute on function public.my_flag(text, public.metis_environment) to authenticated;
