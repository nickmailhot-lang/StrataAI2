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

Card details now include an on-demand MUI label reader with text names, color
descriptions, an unnamed-label fallback, explicit empty/loading/error states,
and a keyboard-operable next-page button. It validates the Organization, Board,
Card, exact Card revision, palette, identifiers, duplicate IDs and page cursor
before displaying a response. Scope/revision/access changes remount the reader
and abort its request; a denied later page removes already-rendered label data.
Reads reuse the shared HTTP Problem boundary and 15-second deadline covering
response-body parsing. The panel reads authenticated assignments; anonymous
public Board indicators and mutation controls remain outstanding.
Validation for the reader: all 453 web unit/component tests passed, including
10 new label-reader cases for readable names, pagination, denied later pages,
invalid scope/revision/palette/duplicates, and late responses. Typecheck, lint,
and production build passed. Two browser cases at 1280px and 390px were collected
for keyboard expansion and persisted label deletion; their runtime execution
awaits exact-image CI.

The Board toolbar now provides a label creation dialog to current editors on
active Boards. It permits blank names, exposes the fixed palette through named
keyboard-selectable options, and validates the returned definition against the
submitted name/color and current scope. Unknown outcomes preserve the original
body and UUID key; the pending dialog hides editable fields and disables cancel
until the same intent is resolved. Other Board actions are held during recovery.
Permission loss aborts the request and clears recovery, and an epoch check blocks
late acknowledgments or failures from restoring revoked state. Focus returns to
Board refresh after the dialog exits.

Creation validation: eight component cases and the existing Board cases passed
(32 focused tests). The two collected browser cases now create a label using
keyboard controls, lose the first actual server acknowledgement, retry the same
key/body, assert one persisted definition and focus recovery, then inspect Card
labels and persisted deletion. Browser runtime execution remains pending CI.

Board snapshots now carry bounded Card face previews: at most six active label
names/colors in label rank order plus the total assignment count. PostgreSQL
loads these with one additional query on the same tenant session, without a
query per Card. Demo applies the same scope and lifecycle rules. Public Board
viewers receive the same readable indicators through the existing Board read
authorization; private Boards retain their access checks. Archived Cards and
Cards in archived/deleted Lists contribute no face previews. The MUI indicators
are noninteractive spans inside Card links, with textual names, named-color
fallbacks, accessible descriptions and an explicit remaining-label count.

Preview validation: 27 focused indicator/Board tests passed and a compiled host
regression covers bounded public previews, private denial and archived-parent
exclusion. The required exact-image fixture checks a 52-label Card returns six
indicators plus the correct count to an authorized and an anonymous public
reader. Existing desktop/mobile browser cases now assert Card-face indicators.
These new server/browser runtime checks await CI.

POST `/labels/{labelId}/move` accepts the current label `version` and optional
`beforeLabelId`; null places it at the end. It allocates rank under the existing
Board command lock, validates a same-Board active anchor, preserves sibling
records, and emits the standard LABEL_UPDATED event. Stale revisions and
exhausted/equal rank intervals return conflicts without partial changes. Retry
fingerprints include the requested position, and historical receipts retain
current label/Board admission. Host tests cover ordering, unchanged neighbors,
exact retries, stale/self/outsider requests, and exhausted space; the required
exact-image fixture adds audit rollback, before/end ordering and unchanged
neighbor records. Local strict compilation passed; runtime evidence awaits CI.

The authenticated editing picker uses GET `/cards/{cardId}/label-options`, with
the same optional label-ID cursor. Each 50-item page pairs a current Board label
with an `assigned` flag and reports the current Card revision. PostgreSQL computes
assignment flags in the bounded label query, so the client need not load all
assignments or combine differently aged directories to decide its next action.
Only editors of an active Board/Card/parent List can read these editing options;
viewers retain the separate read-only assigned-label endpoint and face previews.
The command gate revalidates scope/access after waits and the session after reads.
Host assertions cover unassigned/assigned/deleted options and denied parents or
actors; the required exact-image fixture covers two pages, removal reflected at
the next Card revision, invalid cursors and revoked editors. Strict compilation,
shell syntax and diff checks passed; execution awaits Linux CI.

This is a foundation, not PRD-10 closure. Accessible label administration, filtering, relative reorder
controls, copy/move metadata reconciliation, and exact-image concurrency,
rollback, telemetry, and browser acceptance remain required.
