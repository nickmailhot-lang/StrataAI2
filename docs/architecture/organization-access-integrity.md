# Organization routing integrity

Migration 021 makes each global `user_organization_access` row match exactly one canonical tenant-scoped `organization_members` row, including role and status. Deferred foreign keys in both directions prevent a committed missing, invented or mismatched route. Existing membership synchronization triggers finish both writes before commit. Inserts, role/status changes, removals and physical cleanup preserve this relationship atomically.

The migration validates existing data and fails rather than silently repairing divergent membership or routing state. Runtime roles retain their existing grants; forced membership RLS and canonical authorization checks remain required. A global route remains a lookup hint and never authorizes tenant disclosure or a membership mutation.

The required PostgreSQL CI fixture uses the actual restricted API login to verify normal trigger synchronization, rollback, rejected route deletion/fabrication/role/status changes, rejected cross-tenant membership insertion, and hidden canonical membership without tenant scope. Migration upgrade/repeat checks and exact-image missing-schema rejection include version 021.

## Canonical Organization read

`GET /organizations/{id}` returns `{organization, role}` using the same record
and role contract as the authenticated directory. The Organization record contains
its stable ID, metadata, owner ID, status, audit timestamps and current version.
Any active internal member may read an active Organization; a Portal-only grant,
foreign membership, removed membership or inactive Organization cannot authorize
this metadata. Unavailable targets return the existing `organization_not_found`
error without private fields. Responses are private and must not be cached.

The Application service enters the existing Organization unit of work, then reads
canonical membership and Organization state and revalidates the current actor.
PostgreSQL uses the existing parent/member/account/session lock order and tenant
connection; Demo uses its owning gate. The read creates no audit mutation.
API-host fixtures cover current grants and lifecycle, while the required release
fixtures exercise Demo metadata updates, PostgreSQL owner/member reads, Portal and
foreign denial, and session revocation during a real parent-row wait. Compilation
and shell syntax checks are local evidence only; native execution remains pending
for this implementation revision.

This supplies the routing prerequisite for [account-deactivation ownership gates](account-owner-continuity.md). Those gates require organization locks before account/session locks, canonical ownership reads under those locks, and plan revalidation after account admission. Integrity evidence alone does not prove active-owner continuity. Full PRD-02/03 and architecture acceptance work remains open.
