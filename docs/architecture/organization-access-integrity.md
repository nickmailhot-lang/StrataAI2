# Organization routing integrity

Migration 021 makes each global `user_organization_access` row match exactly one canonical tenant-scoped `organization_members` row, including role and status. Deferred foreign keys in both directions prevent a committed missing, invented or mismatched route. Existing membership synchronization triggers finish both writes before commit. Inserts, role/status changes, removals and physical cleanup preserve this relationship atomically.

The migration validates existing data and fails rather than silently repairing divergent membership or routing state. Runtime roles retain their existing grants; forced membership RLS and canonical authorization checks remain required. A global route remains a lookup hint and never authorizes tenant disclosure or a membership mutation.

The required PostgreSQL CI fixture uses the actual restricted API login to verify normal trigger synchronization, rollback, rejected route deletion/fabrication/role/status changes, rejected cross-tenant membership insertion, and hidden canonical membership without tenant scope. Migration upgrade/repeat checks and exact-image missing-schema rejection include version 021.

This supplies the routing prerequisite for [account-deactivation ownership gates](account-owner-continuity.md). Those gates require organization locks before account/session locks, canonical ownership reads under those locks, and plan revalidation after account admission. Integrity evidence alone does not prove active-owner continuity. Full PRD-02/03 and architecture acceptance work remains open.
