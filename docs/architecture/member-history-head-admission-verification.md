# Board member consent after live admission

This supports the [operational MVP milestone](operational-mvp-milestone.md),
[Board membership](../board-invitations.md), and the complete
[member scenario](../../tests/browser/board-members.spec.ts).

The retained strict notification artifact from [CI run 38316219188](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/38316219188)
identifies `eea64913c964d59cab098a2f562c0e411a92ec17`. Its phone member case
reports a context-close error from cleanup. The private trace retains the
earlier cause: a role-confirmation keypress waits until the unchanged 90-second
case deadline after the review had initially admitted enabled confirmation
focus. This is not proof of the exact hosted invalidation source.

A component regression uses the actual Board stream controller with a
synthetic connection. It delivers a changed head, completes an explicit
protected directory read, opens a fresh role review, then advances through the
coalesced refresh and dialog transition. The original component no longer has
the reviewed `Change Board role?` dialog. The repaired component retains that
dialog, Cancel focus and enabled confirmation, with zero PATCH/DELETE requests.
An initial regression checked only controls retained during the exiting dialog
and passed against the original; it is excluded from the before/after claim.
The final assertion checks the reviewed dialog's accessible name as well.

The member directory now selects the existing immediate-invalidation path.
A changed Board head withdraws stale administration before reporting live;
the subsequent protected Board and directory reads still establish access.
This prevents a delayed duplicate invalidation from retiring a freshly admitted
review. Actual changed/denied authority continues to retire consent. Queued
reads during in-flight operations and uncertain-write warnings retain their
existing behavior. The complete browser file and its mutation, conflict,
version, original-key, count and deadline assertions are unchanged.

All **33 member-page and Board-stream component checks** pass, alongside web
and browser types, focused lint, fresh terminal solution/web builds and **213
CI coverage guards**. After those builds complete, the full desktop/phone
browser file passes **2/2** in **97,060.793 ms**, with zero unexpected, skipped
or flaky cases. It retains cancellation focus, conflicting role change,
lost-acknowledgment recovery, single destructive confirmation, denied recipient
access, unchanged Organization membership and responsive width checks.

The isolated capture uses verified fixture accounts, strict API and Worker
verification, PostgreSQL with canonical restricted runtime roles, a separate
Worker with discovery enabled, and Nginx serving the MUI build. Independent
checks find zero differences across **2,099 hosted source/runtime files**,
**1,414 native files** and all **four web assets** before further edits. Owned
containers, database, roles and credential files are removed; original stopped
PostgreSQL is preserved. Private reports remain outside the repository under
`ci-member-head-admission-hosted-live1-20261012` and
`ci-member-head-admission-native-browser-live1-20261012`; the folder suffix is
the fixture helper's naming convention, while the report records the actual
October 13 UTC execution time.

This is mounted compiled-host evidence, not a paired reproduction of the
hosted timeout or certification of retained release images. Complete current
CI and all original PRD-05 acceptance criteria remain mandatory. The issue
stays open at **14% estimated work remaining**, unchanged planning estimate.
