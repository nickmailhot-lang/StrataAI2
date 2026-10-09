BEGIN;
-- The only mutable event state is first publication. Derive the update clock
-- from that retained fact, never from upgrade time or a later replay.
LOCK TABLE organization_metadata_events,organization_lifecycle_events IN ACCESS EXCLUSIVE MODE;
ALTER TABLE organization_metadata_events
 ADD COLUMN updated_at timestamptz GENERATED ALWAYS AS (COALESCE(ready_at,created_at)) STORED NOT NULL,
 ADD CONSTRAINT organization_metadata_event_clocks CHECK(isfinite(updated_at) AND updated_at>=created_at);
ALTER TABLE organization_lifecycle_events
 ADD COLUMN updated_at timestamptz GENERATED ALWAYS AS (COALESCE(ready_at,created_at)) STORED NOT NULL,
 ADD CONSTRAINT organization_lifecycle_event_clocks CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
-- Existing history guards compare the immutable payload explicitly and retain
-- first ready_at. The generated column cannot be supplied or forged by callers.
-- No writers, tenant policies, privileges or lease fences change.
INSERT INTO schema_migrations(version) VALUES('121_organization_event_delivery_clocks');
COMMIT;
