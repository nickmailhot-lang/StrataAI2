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

Card details now expose an editing picker with explicit Add/Remove buttons,
named colors, bounded next-page navigation and a reload action. Options are
usable only for the displayed Card revision. A confirmed change refreshes the
Board and returns focus after refresh completes. Unknown outcomes retain their
original label/action/Card revision/key outside the conditional Card body, so a
newer snapshot does not replace an unresolved intent. Recovery holds the parent
dialog open and blocks competing work; fresh Board permission loss clears the
intent and fences late responses. Definite conflicts discard stale choices and
require a fresh read instead of silently resubmitting against a new revision.

Picker validation: seven new component cases plus Board tests passed (31 focused
tests), typecheck and lint passed. The two desktop/mobile browser cases now add
through the UI, lose the first persisted assignment acknowledgment, compare the
same retry URL/key, recover focus, and remove through the UI. These runtime
scenarios await CI and are not yet acceptance evidence.

Board editors can now open Manage labels to rename/recolor current definitions
and move them before another label or to the end of the entire Board directory.
The directory loads at most 50 records per request. A selected source can remain
selected while paging through ordering destinations, so labels on different
pages can be reordered. Move submits the saved metadata and original revision;
unsaved field changes require a separate Save. Administrator deletion also
requires the current directory's delete capability and an explicit confirmation
that all Card associations will be removed.

Unknown mutation outcomes retain the original URL/body/revision/key and prevent
closing the dialog or competing Board changes. Definite conflicts discard the
stale directory and require a new read. Board scope, edit permission, or admin
capability changes abort pending work and fence late responses. Returned entities
must match Board/Organization/label identity, expected revision and mutation
content before the UI acknowledges completion. Confirmed writes refresh the
Board; closing returns focus only after the transition and Board refresh finish.

The 14 management cases and 24 Board cases passed locally (38 focused tests),
along with typecheck, lint and production build. Component coverage includes edit/recolor, before/end ordering across pages,
explicit deletion, capability denial, demotion during a pending command,
unchanged retry identity, mismatched acknowledgments, stale revisions, and safe
denials. Desktop/mobile browser scenarios now rename/recolor with a lost persisted
acknowledgment, reorder, and explicitly delete through the UI in addition to the
existing creation and Card assignment/removal coverage. Browser collection is
not execution evidence; these scenarios await the required exact-image CI job.

PRD-10 remains open. Filtering integration with PRD-16, remaining copy/move
metadata reconciliation, two-client/reconnect proof, performance/telemetry,
and current exact-image/browser acceptance remain required.

Release proxy coverage: CI runs 37030478776 and 37031146291 exposed HTTP 405
for label deletion through the web origin, despite direct API checks passing.
The `/labels` resource is now routed to the API in both Nginx and Vite. The
required label-command fixture now uses the release web origin on port 8088,
covering definition mutations, reads, retry receipts, authorization waits and
filtering through the exact web/API images. Shell syntax and local web build
passed; execution of this repair is pending CI. Existing browser assertions
retain their expected statuses and recovery behavior.

## Current native acceptance gaps

The four-case star/label baseline at main `e375d2bb` ended with three failures
and one pass in 3.7 minutes. Both label scenarios failed: desktop at
`card-labels.spec.ts:107` after a real successful Card-label DELETE while requiring
focus to return to Edit Card labels; phone at line 145 while requiring the Match
filters menu to be enabled and opened. These are separate unresolved failures;
the latter is not a label-management retry failure. The star fixture was repaired
and rerun independently, without rerunning or claiming these label cases passed.
No label product behavior or acceptance assertion was changed by that repair.
PRD-10 remains open with **35%** estimated work remaining (planning estimate),
including these native gaps and the outstanding full-PRD requirements above.

## MUI trap fallback and keyboard menu repair

A separate desktop diagnostic invocation failed an earlier assignment return-focus
check (line 79). Passive fixed-label focus recording showed that disabling the
returned trigger moved focus to its own MUI trap container (`role=presentation`),
rather than the marked dialog paper. The shared focus helper now recognizes that
exact ancestor container as owned fallback. Other controls and other dialogs
remain deliberate destinations. An installed-MUI regression fails before the
repair and passes afterward; all 170 selected cases across ten focus-helper
consumer suites pass, with web/browser typechecks, targeted lint and production
build. The existing bundle-size advisory remains.

The next two-case native invocation passed the previously failing focus steps but
failed both cases at the Match filters menu check. The actual trace reported a
strict-locator violation: the combobox and open listbox share that accessible
label. A role-only intermediate fixture then failed because MUI hides the
underlying combobox from accessibility queries while its menu is open. The final
fixture observes the visible named listbox after keyboard opening, selects its
named option, and retains enabled admission and persisted-filter assertions.
No consent, timeout, command count, retry identity or server authority was changed.

That two-case invocation progressed through the menu but failed Clear after
reload at line 162 on both widths; neither trace contained a Clear command.
The fixture now waits for Clear to be enabled before its single keyboard command.
The separate cross-client case also missed its second picker activation after
a real label rename (only its first options read was sent). It now observes the
read-only picker opening before issuing the single assignment/removal command.
These fixture repairs do not manufacture, repeat or acknowledge a mutation.

Subsequent execution remains failing. The three-case invocation with Clear and
cross-client picker admission failed desktop at the second picker activation,
phone at Move-before option selection, and the cross-client case later during
assignee editing (source frames 131/135). The final two-case invocation, after
requiring observed picker opening, visible Move-before menu and enabled mutation
controls, failed desktop at Add-blue return focus (line 97) and phone at Clear's
keyword result (line 171). These runs are not native acceptance. Original retry
key/body, authoritative version/order, consent and persistence checks remain.
The shared-helper source regression and 170 passing component cases establish
only the repaired ownership contract, not resolution of every focus/activation
failure. PRD-10 stays open at **35%** estimated remaining work (planning estimate),
pending these browser repairs, current immutable CI and full-PRD acceptance.

## Enabled focus before a single keyboard command

The subsequent trace audit distinguishes activation from return focus: the last
desktop case sent commands for only one distinct label, so Add blue never ran.
Phone sent both label assignments but no Clear command. The failure frame alone
did not prove a completed command had lost focus. The fixture now establishes
enabled, actual keyboard focus before its single Enter keypress. Only focus
preparation can repeat during protected foreground reads; mutation dispatch
remains outside that preparation. Both full desktop/phone label workflows pass
together in 2.3 minutes against the same frozen compiled Production web/API,
restricted PostgreSQL and scoped Worker. This is a distinct invocation from the
still-failing earlier cross-client assignee workflow; it does not prove a green
current immutable release or full PRD-10 acceptance.

On the repaired assignee build, the last three-case invocation passes both full
desktop/phone label workflows (two pass, one unrelated chooser failure; 3.6
minutes). Enabled focus precedes the one Enter/Space activation, including checked
deletion consent. Original committed lost-response key/body recovery, filtering
persistence and command counts, saved label order/version, Card removal and focus
assertions pass. The chooser repair's dedicated full cross-client case then passes
(one pass, 1.5 minutes). These are separate invocations against the same compiled
web/API and real restricted PostgreSQL/scoped Worker. Earlier failures remain
documented; current immutable CI and outstanding full-PRD requirements still
prevent closure. PRD-10 remains open at **35%** estimated remaining (planning estimate).
