# Board label command foundation

PRD-10 now has authenticated bounded label directory reads and create, update,
and confirmed soft-delete commands. Names may be blank, have a 160-character
limit, and are trimmed. Colors use the documented fixed palette. Updates accept
a valid fixed-width rank and require the current label revision. New labels
append within the Board without rewriting neighboring ranks.

Commands reuse the existing Board authorization lock, fresh actor/session
verification, immutable request fingerprint, and one tenant transaction for the
label, audit, event, delivery job, and retry receipt. A historical create or
delete receipt requires current Board permission and an active Board. Deleted
label records retain their identity while directory reads exclude them.
PostgreSQL deletion removes associations atomically and increments affected
non-deleted Card revisions; this integration still needs its exact-image API
fixture before claiming runtime acceptance.

Migration 029 adds forced-RLS typed label routing metadata. A single LABEL lookup
can discover only the requested route and cannot expose protected label data.
The API can maintain routes; the Worker receives no label table privileges.
Runtime startup now requires all 29 ordered migrations. The migration runner and
missing-migration fixtures cover the new baseline. The existing required storage
fixture also exercises missing, correct, wrong-type, and blank route scopes.
Label events contain references and versions only; deleted labels translate to
Board invalidation during replay so stream continuity is preserved.

Local validation: warnings-as-errors solution build, shell syntax, and diff
checks passed. Two new API host regressions compile and await Linux CI execution
because this Windows host prevents running rebuilt test executables.

This is a foundation, not PRD-10 closure. Card assignment/removal APIs, public
Card indicators, accessible label administration, filtering, relative reorder
controls, copy/move metadata reconciliation, and exact-image concurrency,
rollback, telemetry, and browser acceptance remain required.
