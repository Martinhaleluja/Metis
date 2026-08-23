-- Foreign keys without a covering index. Postgres has to scan the child table
-- to check every delete on the parent, so removing a user would scan the whole
-- audit log to find their rows. Cheap to fix now while the tables are empty.
--
-- The "unused index" findings from the same report are left alone: the tables
-- have never been read because nothing is deployed yet, so "never used" says
-- nothing about whether they will be. Dropping them would be optimising
-- against no evidence.

create index audit_logs_user on public.audit_logs (user_id);
create index feature_flag_users_user on public.feature_flag_users (user_id);
create index user_ai_connections_provider on public.user_ai_connections (provider);
