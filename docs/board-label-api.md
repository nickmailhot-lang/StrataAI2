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
checks passed. Linux CI for c8f9639 passed PostgreSQL routing/storage tests and
web checks but found that the retry middleware omitted the new `/labels` path,
making the delete retry regression return 404. The route is now covered; the
unchanged receipt equality assertion and an invalid-key assertion await the
corrected Linux run. This Windows host prevents running rebuilt test executables.
The required exact-image fixture now covers create/delete audit rollback,
association removal and retained Card revisions, outsider/editor authorization,
identical receipts, changed fingerprints, stale writes, malformed cursors, and
observed Board lock waits with lifecycle and session revocation. Its runtime
execution is pending CI; merely adding the fixture is not acceptance evidence.

Card assignment/removal commands now use PUT/DELETE
`/cards/{cardId}/labels/{labelId}?version={cardVersion}`. They require an active
Card and parent List, an active same-Board label, and current editing permission.
The Card revision advances only when the association changes. Fresh no-op
requests still require the current revision and store their own retry receipt;
they do not add duplicate events. LABEL_ADDED/LABEL_REMOVED events identify the
Card and its revision without copying label content. Historical association
receipts recheck current Card/parent/label scope before returning prior data.
Both demo and PostgreSQL definition deletion reconcile affected Card revisions.
Two new host regressions and an extended exact-image fixture cover this behavior;
local strict compilation passes, with runtime execution pending CI.

The exact-image label fixture passed on commit 865a944 in CI run
[37015140289](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37015140289).
All nine jobs passed, including source tests, restricted PostgreSQL checks,
security, container integration, required gate, and release bundle. Container
integration reported 66 main browser cases passed, one conditional skip, and a
separate mobile case passed. This proves the executed command foundation and
existing browser coverage; it does not prove a label UI that is not implemented.

Authenticated GET `/cards/{cardId}/labels?after={labelId}` now supplies 50-item
pages of current, non-deleted assignments, Card version, and current editing
capability. The typed Card hint is revalidated after the Board lock, and a fresh
session check precedes returning the page. Archived/deleted Cards or parent Lists
remain unavailable through this active-Card endpoint. Malformed cursors return
400. PostgreSQL reads seek by label ID and fetch at most 51 rows, independently
of the total association count. Host tests cover assignment/deletion reads and
denials; the exact-image fixture now adds 52-assignment paging, uniqueness, and
observed post-wait permission revocation on the later page. These new read tests
await the next CI run.

This is a foundation, not PRD-10 closure. Public
Card indicators, accessible label administration, filtering, relative reorder
controls, copy/move metadata reconciliation, and exact-image concurrency,
rollback, telemetry, and browser acceptance remain required.
