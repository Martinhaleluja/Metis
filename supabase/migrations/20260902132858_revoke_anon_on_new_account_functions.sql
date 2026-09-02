-- Take anon off the two functions created in 20260902132742.
--
-- Supabase's default privileges grant EXECUTE on every new function to anon
-- explicitly, which a `revoke ... from public` does not remove. This is the
-- same trap 20260817095105 was named after, and it catches every new function
-- that is not meant to be public.
--
-- billing_is_live() keeps anon deliberately: the pricing page has to know
-- whether the shop is open before anyone signs in. It returns a single boolean,
-- which is why it exists at all rather than a widened policy on billing_state.
revoke all on function public.my_usage_this_period() from anon;
revoke all on function public.set_my_test_plan(public.plan_tier) from anon;
