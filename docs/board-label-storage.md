# Board label storage

PRD-10's storage foundation is migration `028_board_labels`. Labels belong to
one Board and Organization; Card associations carry both contexts and use
composite foreign keys to their Card and Label. They cannot link a Card to a
different Board's label, even within the same Organization. The unique Card/Label
tuple prevents duplicate associations. Both tables have enabled and forced
PostgreSQL RLS, explicit tenant read/write policies, timestamps and positive
versions. Indexed active label rank and reverse association lookups support
ordering and filtering.

Labels allow a blank name and use a fixed color vocabulary: green, yellow,
orange, red, purple, blue, sky, lime, pink and black. Future UI must expose an
accessible text name for unnamed labels; color alone is insufficient. Names have
a 160-character limit and ranks use the existing 30-digit token format.
Deletion uses a tombstone with a required deletion timestamp. Removing Card
associations must preserve the separate audit history; the label's API deletion
must remove its associations and emit its audit/event in the same command.

API runtime grants permit label read/insert/update and association
read/insert/update/delete. They do not permit physical Label deletion. The
separate Worker receives no label or association table access. RLS is a tenant
boundary, not a Board authorization grant: future services must retain current
Board command locking, actor/session checks and edit/admin policy.

The required PostgreSQL source job runs `test-board-label-storage.sql` with a
restricted role. It tests tenant and missing-context reads, cross-Board and
cross-Organization associations, duplicate tuples, invalid colors/versions,
deletion timestamps and cross-tenant writes. All fixtures and their role roll
back. Runtime-login tests separately inspect the API/Worker grants. The migration
runner now upgrades through 28, repeats the upgrade, and retains serialization
and failed-migration rollback checks. The generic tenant catalog guard inspects
both new tables without adding exemptions. Local shell syntax/diff checks pass;
actual database execution remains pending Linux CI.

This is not a completed Labels feature. CRUD/assignment APIs, typed label route
discovery, fresh authorization, atomic events/audit, UI indicators and controls,
filtering, accessibility, telemetry and performance verification remain required.
List/Card copying and cross-Board movement must integrate this metadata before
their full acceptance. Cross-Board label reconciliation and member eligibility
are part of the original PRD-07/08/10/11 dependency group, not optional omissions.
