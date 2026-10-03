# Checklists and items (PRD-13)

This implements the persistence/domain foundation and authenticated checklist
reads/creation. PRD-13 is not complete.
PRD-13 depends on the existing Card and audit contracts (PRD-08/22) within the
canonical dependency cycle. Remaining API commands, MUI interactions,
authorized realtime delivery and complete lifecycle acceptance remain required.

Checklist identity, Organization and Card parent IDs are immutable. Item identity,
Organization and Checklist parent IDs are immutable. Both have canonical
30-digit persistent ranks compatible with the existing RankToken allocator,
UTC creation/update timestamps and positive revisions. Titles normalize outer
whitespace and are bounded to 160 UTF-16 units; item text is bounded to 2000.
NUL/blank content is rejected. No item assignee or due-date fields are introduced.

Completion records its original actor/time and clears both on uncompletion.
Completing an already completed item does not replace attribution or advance
revision. Reorder/edit/delete no-ops retain revision. Validation and timestamp
checks precede all field mutation, so a failed backwards update leaves the
complete prior entity intact. Deletion creates a tombstone and preserves content
and attribution for later audit/retention; edits to tombstones are rejected.
The eventual delete command must tombstone the Checklist and all its active items
and publish audit/events atomically. That aggregate command is not implemented yet.

Progress is derived from active item counts: completed / total, and zero when
empty. The domain calculation rejects foreign-tenant/parent or duplicate items.
Paged API responses must use canonical aggregate counts, not the current page's
subset. Item completion is separate from Card due completion.

Migration 040 adds forced-RLS tables with tenant-composite Card/Checklist parent
FKs, a tenant-composite completion-attribution FK to retained Organization
membership, bounded text/rank/revision/timestamp checks and unique active ranks
per parent. Board/List scope follows the current parent Card, avoiding redundant
Board IDs that would become stale during a same-Organization Card move. This
does not waive current parent/member/actor authorization in future commands.
The API role can read/insert/update; physical child deletion and Worker content
reads remain denied. No new framework, store or deployable service is added.

The production schema guard now requires migrations 039 and 040 in addition to
the previous 38. The exact-image security fixture removes each independently,
requires safe 503 responses from both processes/protected routes and restores
before proceeding. This also repairs omitted readiness coverage for the already
adopted Board timezone column in 039.

Fifteen Domain cases cover canonical identity/ranks, completion attribution,
no-ops, zero/deleted-item progress, foreign/duplicate children, invalid bounds,
backwards mutation atomicity, tombstones and UTC normalization. A mandatory
PostgreSQL fixture uses a non-superuser/no-bypass role and tests cross-tenant
reads/writes/parents/completers, missing scope, active rank collisions, completion
field shape, timestamps/revisions, tombstone progress and rank reuse. The generic
tenant-schema guard includes both new tables. Strict local solution build and
shell syntax checks pass; Linux execution of these new cases is pending CI.

Remaining PRD-13 work includes authorized/CAS/idempotent CRUD and position
commands with current parent/actor rechecks, bounded ordered reads and counts,
transactional audit/outbox, Card copy/move/delete integration, MUI recovery and
confirmation/focus, live/reconnect/access-loss scenarios, telemetry and full
normal/large-data performance evidence. No acceptance checkbox or issue closure
is implied by this foundation.

The first PostgreSQL run for 0d392f0 (37091711154) failed because migration 039
never inserted its version ledger row. Its timezone column existed, but the
stream's `\quit 3` ignored the argument and returned success before migration
040 ran. Migration 039 now records its version and supports the known existing
unrecorded column/constraint state, preserving policy values while checking the
column shape and rebuilding its constraint transactionally. The stream raises
a SQL exception on a missing ledger entry under ON_ERROR_STOP, so later migrations
cannot silently be skipped. The migration-runner fixture now exercises 039/040
upgrade/repeat, recovery of an existing Honolulu policy with a missing 039 ledger,
serialized runners, transactional SQL failure and explicit rejection of an
unrecorded migration. Shell syntax and emitted SQL inspection pass; corrected
PostgreSQL execution remains pending.

Authenticated GET/POST `/cards/{cardId}/checklists` now admit the current
Organization, Board, List, Card and actor inside the owning transaction. Reads
allow archived parents with `canEdit=false`, return at most 50 checklists with a
Card-bound rank/id cursor, and compute counts over all active items. Public
visitor read support is still required. Creation checks the Card revision,
allocates ordering space before mutation, advances the Card revision without
changing its content/dates, inserts the Checklist, records its audit identity,
and appends a content-free Card aggregate invalidation plus retry receipt in the
same transaction. Replay must pass fresh edit admission, so archived or revoked
access cannot reveal an old acknowledgement. Rank exhaustion is a stable conflict
before any write. The returned child is the persisted PostgreSQL row, including
its canonical timestamp precision.

Two API cases cover rejected input, private reads/writes, Card CAS, retry keys,
ordered 50/13 pages, empty progress, archived reads and access removal. A new
mandatory exact-image fixture additionally exercises INSERT failures both before
the child and after Card/child/audit writes, whole-command rollback, same-key
recovery, SQL-backed full aggregate counts/deleted-item exclusion, rank exhaustion
and lifecycle/replay admission. These new API/container cases await Linux CI.
The bf2ab79 PostgreSQL job passed the checklist storage, migration recovery and
runtime-role fixtures; the strict local solution build has zero warnings/errors.
Item editing/completion, checklist editing/reorder/delete, public views, copy and
retention integration, MUI and full performance evidence remain unfinished.

Checklist rename is exposed as PATCH `/cards/{cardId}/checklists/{checklistId}`.
Both Card and Checklist revisions must match. Normalized no-op titles preserve
both rows and emit no audit/event; a changed title advances both revisions and
publishes CHECKLIST_UPDATED as a Card aggregate invalidation atomically. Child
lookup is scoped to the admitted tenant and Card; wrong-parent IDs receive the
same stable unavailable response. Retry receipts recheck current parent, child
and edit access. A source API case exercises no-op preservation, stale individual
revisions, concurrent clients (one winner), wrong-parent IDs and revoked replay.
The exact-image fixture forces the event insert to fail after both updates,
checks whole-command rollback, retries the original key and verifies replay and
no-op event/audit counts. Linux execution remains pending for this increment.

Authenticated ordered item GET/POST routes are now under
`/cards/{cardId}/checklists/{checklistId}/items`. Create validates normalized text
and both parent revisions, leaves completion/attribution empty, and advances the
Checklist and Card revisions with one item audit/event/outbox/receipt transaction.
Item reads return at most 50 rows with a Checklist-bound cursor and a summary
computed over all active items, independent of the item page. Demo and PostgreSQL
stores use the same scoped ordering/count behavior. Source coverage creates 63
items, checks 50/13 pages and full counts, rejects wrong parents and stale writes,
and denies reads/replay after revocation. The mandatory exact-image fixture adds
item/queue INSERT rollback, same-key recovery, full-page aggregate counts and
deleted-item exclusion. These new cases await Linux CI; strict build passes.
Item edit/completion/reorder/delete and Checklist reorder/delete remain required.

Item PATCH requires explicit text/completed plus Card, Checklist and item
revisions. It edits text, completes or uncompletes (including a combined change)
with one revision advance per row. Completing records the actor/time; subsequent
text edits and completion no-ops preserve original attribution. Uncompletion
clears both fields. No-op input preserves all revisions and emits no audit/event.
Each changed fact emits its corresponding content-free Card event and item audit
inside the same transaction; combined changes publish both facts. Source tests
cover attribution, full/no-op/stale input, required completion flag, wrong-parent
IDs, revoked replay and independence from a completed Card due date. Exact-image
coverage forces event/queue failures after all updates, checks rollback/recovery,
unchanged completion attribution through another actor's text edit, combined
uncompletion/edit and aggregate counts. Strict build passes; Linux execution is
pending. Checklist/item reorder/delete, public reads, lifecycle integration and
MUI acceptance remain required.

Checklist/item PATCH position routes take an explicit `beforeId` (null appends)
and the same parent/entity revision preconditions. Anchors must be active siblings
in the admitted tenant/parent and cannot be the moving entity. Bounded indexed
neighbor queries exclude the moving row and allocate an interval rank under the
owning command locks. Already-adjacent/already-last positions preserve all rows
and emit no audit/event; changed order advances the applicable parent/entity rows
with audit/events/receipt atomically. Missing/foreign/self anchors are stable
invalid-position errors; exhausted intervals fail before writes. Source tests
cover reorder/end/no-op/CAS/replay/foreign anchors and revoked replay; the mandatory
exact-image fixture checks real rank ordering, post-update event/queue rollback,
retry recovery, empty ordering space and unchanged completion/count data.
Strict build and shell checks pass; Linux execution of this increment is pending.
Deletion, public views, copy/retention, MUI and remaining acceptance are unfinished.

Item DELETE requires explicit confirmation, current Card/Checklist/item revisions,
and elevated Board administration on active parents. It tombstones the item,
preserves text/rank/completion attribution, advances all three revisions and
commits audit/event/outbox/receipt atomically. Active counts/read pages exclude
tombstones. Confirmed deletion of an already deleted item with current revisions
is a no-op; original-key replay requires fresh admin and active parent admission.
Other mutation receipts cannot disclose a newly deleted child. Source and mandatory
exact-image tests cover contributor denial, confirmation, retained history,
no-op/replay, late event/queue rollback, authoritative counts and archive denial.
Strict build and shell checks pass; Linux execution is pending. Checklist aggregate
delete/cascade and wider lifecycle/copy/public/MUI acceptance are unfinished.

Whole-checklist DELETE now requires explicit confirmation, elevated Board
administration and current Card/Checklist revisions on active parents. It
retains the Checklist tombstone and cascades to every active item, preserving
content/completion attribution and existing tombstones. PostgreSQL performs the
cascade and per-item audit in a scoped data-modifying statement without returning
an unbounded item collection. One CHECKLIST_DELETED Card aggregate invalidation
covers the cascade; individual direct item deletion still publishes its own
CHECKLIST_ITEM_DELETED event. Card revision, parent/child tombstones, item and
Checklist audits, event/outbox and retry receipt share the owning transaction.
The acknowledgement reports the active items actually deleted. Current-revision
repeat deletion is a no-op, while original-key replay retains its original count.
Child reads and earlier child mutation receipts are unavailable after cascade.

A source API case creates 63 items, completes one and separately deletes another,
then checks a 62-item cascade, retained attribution/old tombstone, contributor and
confirmation denial, no-op/replay and hidden child receipts. Mandatory exact-image
coverage forces audit/event/queue failures after cascade writes and checks complete
rollback, original-key recovery, per-item audit counts, old tombstone preservation
and archive replay denial. Strict build and shell syntax pass; Linux execution of
these new cases is pending. Public read, copy/move/parent-retention integration,
MUI/realtime/accessibility and full performance acceptance remain unfinished.

Checklist and item GET routes now use the existing current Board VIEW policy for
both visitors and authenticated actors. Public active Boards permit bounded,
read-only child pages; visibility changes immediately remove that permission,
and anonymous visitors cannot view archived Boards. Permitted members can still
read archived parents without editing. Deleted Cards, Lists and Checklists remain
hidden. Writes keep their authenticated edit/admin admission.

Read callbacks own the same tenant transaction and parent locks as mutations,
rechecking current visibility and parent identity before returning data. They
create no command receipt and invent no authenticated actor. Authenticated readers
still undergo current session verification before and after successful reads.
Malformed cursors are validated only after authorization, avoiding private-parent
discovery. Source API coverage uses real visibility/archive commands; mandatory
exact-image coverage checks public/outsider reads, denied writes, read-only state,
private retraction and unchanged child/audit/event/receipt state during reads.
Strict compilation is checked locally; Linux execution remains required.

PRD-07 List copy includes every active Checklist and its active items for every
retained copied Card, including archived Cards, without using UI pagination.
The source graph is unchanged. Copies preserve titles, text and ranks, receive
new IDs, creation/update timestamps and revision 1, and omit deleted children.
Copied items start incomplete with null completion attribution and 0% progress:
completion belongs to the original task's history and is not attributed to a
newly created task. This explicitly chosen policy concerns checklist completion;
the established Card-date copy policy remains separately defined in PRD-12.
Same-Board and same-Organization cross-Board copies follow the existing source
and destination edit admission. Checklist graph writes share the List/Card/label
copy transaction, root LIST_COPIED audit/event/outbox and original retry receipt.
Source cases cover both Board contexts and a 62-item graph after child deletion;
mandatory exact-image coverage copies 63 Checklists/63 items, forces child and
late audit/event/queue failures, and checks total rollback and retry recovery.
No new service, migration or database grants are needed. Linux execution pending.

Parent lifecycle integration retains Checklist/item history without rewriting
child IDs, versions or completion attribution. Card/List archives expose permitted
read-only pages, reject editing and old mutation receipts, and restoration reopens
editing on the same graph. Current Card/List identity is checked after parent-lock
admission, so movement uses the current parent. Deleted parent Cards/Lists hide
children and prior receipts while storage retains history for the wider PRD-18
retention process; this is not a physical-purge implementation.

Source API cases cover Card and List archive/restore/confirmed deletion with
completed-item history. Mandatory release-image coverage moves a copied 63-item
graph, archives/restores both parents, deletes the List with reviewed impact and
compares the entire retained child graph. It also observes a real PostgreSQL
Board-lock wait for each anonymous GET, commits visibility retraction during that
wait, and verifies a content-free denial with no child or command side effects.
Strict build and shell syntax pass; Linux execution of the added cases is pending.

The 1376b0c release-image run exposed a cascade audit permission error (42501):
INSERT RETURNING on audit_events requires SELECT, while the API deliberately has
append-only audit access. Cascade audit now inserts without RETURNING and counts
the updated items instead; the data-modifying audit CTE still always executes in
the same transaction. The mandatory fixture explicitly verifies audit SELECT is
absent before running rollback/success/count checks. Runtime grants are unchanged.
The failed run is retained as evidence; the repair requires a new exact-image run.

The browser Checklist response boundary validates current Organization/Board/Card
identity, child parent IDs, positive safe revisions, canonical rank order, unique
IDs/ranks, active lifecycle, UTC microsecond timestamps and completion attribution.
Progress must match its completed/total counts; bounded item pages must fit those
full aggregate counts. Seek pages must advance beyond their incoming cursor and
next cursors must identify the actual final row of a full 50-row page. Malformed,
foreign or internally inconsistent data is rejected before UI use. Thirty-eight
focused cases exercise this boundary; typecheck, lint and production build pass.
This boundary prepares the MUI implementation; it does not complete that UI.

Card detail now offers lazy MUI Checklist/item disclosures with named keyboard
controls, bounded next/first pages, text completion status and accessible progress.
Opening Card detail alone sends no new Checklist request. Reads use the shared
same-origin bounded transport and validated response boundary; mismatched Card
revisions are rejected. Access re-admission immediately hides data and cancels
pending reads; a refreshed Card revision reloads an open disclosure. Fixed error
messages offer safe retry and Board refresh. This is the read surface; create,
edit/reorder/delete and mutation recovery controls remain required.

Forty-three focused reader/response tests and all 26 Board tests pass locally;
typecheck, lint and production build pass. Two mandatory browser cases are
registered for desktop/390px keyboard reads, full-page WCAG checks and two-session
CHECKLIST_ITEM_COMPLETED refresh through the real API/outbox/live layer. Listing
those cases confirms discovery only; actual exact-image browser execution is
pending, including existing cached-detail/performance budgets.

The 7ac2db3 Linux run passed all 246 Domain and 236 API tests. Its mandatory
Checklist release-image step passed public reads/retraction, both post-wait
visibility denials, all child commands and rollback checks, append-only cascade
audit, complete graph copy, movement and retained parent lifecycle. Overall CI
and newer UI release-image checks remain separately required.

Card detail now includes MUI Checklist creation with an explicitly reviewed actor
and Card revision. Dirty titles survive a newer snapshot and require discard/current
review after conflict. Unconfirmed transport or malformed acknowledgement retains
the original title, Card revision, actor and idempotency key; retry checks the same
current actor and validates authoritative scope/title/new child revision/Card CAS.
Definite denials block resubmission until current review. An unresolved operation
locks other mutations, and Card navigation cancels the owning request. Recovery
focus survives temporary re-admission while respecting intentional focus changes.
The title field is hidden during access re-admission and disabled without editing
authority. Ten creation tests and all 26 Board tests pass, alongside the 43 reader
tests; typecheck/lint pass. Desktop/mobile browser scenarios now also lose a real
committed creation response and compare the recovered original request/key and
persisted empty Checklist across both sessions. Execution is pending. Item and
Checklist edit/reorder/delete recovery controls remain unfinished.

Checklist title management now reviews current server edit admission and supports
bounded next/first selection pages before choosing a Checklist. The dirty rename
draft retains its chosen child, actor and Card/Checklist revisions independently
of Card refreshes. Both changed and normalized no-op acknowledgements validate
scope, child identity, title, rank, original creation time and exact revision
deltas. Unconfirmed changes retain the original request/key; definite denial or
conflict requires explicit discard and current review. Recovery focus survives
re-admission, and create/rename recovery flags are independent so one control
cannot release another's mutation lock.

All 68 focused Checklist browser tests and all 26 Board tests pass, with typecheck,
lint and production build. Desktop/390px scenarios add real committed rename
response loss, original-request recovery, two-session live title convergence and
mutation-interlock/WCAG checks; scenario discovery passes, execution is pending.
Checklist deletion/ordering and item mutation controls remain unfinished.

The complete GitHub issue snapshot was re-analyzed after this increment: 92
canonical tickets, 7 dependency groups and 3 cyclic groups. All requirement body
digests match the checked dependency map; 91 canonical issues remain open. These
counts describe the full unchanged objective and do not assert acceptance.

MUI Checklist management now offers administrator-only deletion after a current
paged review. The confirmation shows the full active-item count and requires an
explicit checkbox before submitting current Card/Checklist revisions. Loss of
administration disables confirmation/submission/retry. Unconfirmed deletion
retains the original actor, confirmed body, revisions and key; acknowledgment must
identify the same retained Checklist tombstone, title/rank/creation time, exact
revision advances and complete reviewed cascade count. Definite denials require
fresh review. Access re-admission hides the confirmation and focus recovers on
the original retry control. Other mutation locks remain held during recovery.

The 75 focused Checklist tests passed, followed by all 22 management tests after
the management entry/paging labels were updated. Typecheck/lint and production
build are checked. Two additional mandatory desktop/390px browser cases perform
keyboard confirmation, lose a real committed DELETE response, recover the exact
original request, observe live removal in a second session, verify hidden item
reads and run WCAG checks. All four Checklist browser cases are discovered;
exact-image execution remains pending. Ordering and item mutation controls remain
unfinished, and wider PRD/architecture acceptance remains open.
### Checklist position controls

The MUI manager now reviews a Checklist's current Card/child revisions and lets
contributors choose a before-sibling anchor across bounded seek pages. The end
choice is offered on the final page. Selection excludes the moving Checklist;
each page requires current edit admission, the same actor and the same Card
revision. A concurrent revision or rejected move requires a new review.

Position acknowledgments preserve scope, identity, title and creation time,
validate the chosen rank interval, and distinguish changed revisions from
already-in-position no-ops, including when the moving row is the seek boundary.
Unconfirmed responses retain the original destination, versions, actor and key;
re-admission does not change the pending command. Shared Card recovery locks
continue to prevent overlapping mutations.

Local verification: 71 parser/manager tests passed, including bounded later-page
position selection, interval and seek-boundary validation, no-op revisions,
conflicts and original retry recovery. Type checking and lint passed. The new
desktop/mobile exact-image browser cases cover keyboard before/end choices,
accessibility, lost committed acknowledgment and second-session live ordering;
browser execution remains pending CI. Item mutation and item ordering UI remain
unfinished, so this does not complete PRD-13.
### Item creation controls

The MUI manager lets admitted editors choose an active Checklist and create an
item with normalized multiline text, bounded to 2000 characters. The command
uses the reviewed Card and Checklist revisions and verifies the original actor
before sending. Acknowledgment validation requires a scoped new incomplete
item, canonical null completion attribution, initial item revision/timestamps,
unchanged parent identity/title/rank and exact parent revision increments.

Dirty text survives concurrent Card updates and definite denial while a new
review is required. Unknown, malformed or lost responses retain the normalized
text, actor, versions and key; the original displayed draft remains locked
during recovery. Local verification: all 96 focused checklist parser/reader/
create/manager tests passed; type checking, lint and production build passed.
Two new desktop/mobile exact-image browser cases were discovered for keyboard
creation, accessibility, committed/lost response recovery and second-session
progress/contents. Browser execution remains pending CI. Editing/completion,
deletion and ordering of individual items remain unfinished.
### Item editing and completion controls

The MUI manager now provides a bounded item review and an editor for normalized
multiline text plus explicit completion/uncompletion. Each review checks the
current actor, edit admission, Card revision and Checklist revision; later-page
selection retains the chosen item's own revision. Item drafts live outside the
revision-keyed read disclosure so live updates cannot discard dirty choices or
an unconfirmed command.

Response validation preserves parent identity/title/rank, item identity/rank/
creation time and checks all three revision increments (or exact no-op state).
A new completion must record the reviewed actor and mutation time. A text-only
edit on a completed item must preserve the original completer/time, while an
uncompletion clears both. Changed parent/item timestamps must acknowledge the
same aggregate mutation. Definite conflicts retain dirty text/completion for
review; unknown/lost/malformed responses retain the original actor/body/key.
The parent manager owns recovery locks and returns keyboard focus after the
original acknowledgment or explicit abandonment of a non-pending review.

Local verification: 99 focused parser/root-manager/item-manager tests passed,
including later-page item revisions, completion history, original retries,
actor changes, conflicts, malformed acknowledgments and integrated recovery/
focus. Type checking, lint and production build passed. Two desktop/mobile
exact-image browser cases were discovered for combined text/completion with a
lost committed response, retained completion history on text edits,
uncompletion, accessibility and another session's live aggregate progress.
Browser execution is pending CI. Individual item deletion and ordering UI and
the remaining full PRD acceptance are still required.
### Individual item deletion controls

Admitted administrators can select an active item through the bounded manager,
review its text and explicitly confirm deletion. Contributors do not receive
deletion controls; loss of administrator access blocks both a new deletion and
recovery of a pending one. The original confirmation, actor, Card/Checklist/item
revisions and key remain fixed after a lost or malformed acknowledgment.

Acknowledgment validation requires a new tombstone with matching mutation time,
exact aggregate/child revision increments and preserved parent identity/title/
rank plus item identity/text/rank/creation/completion history. Only the original
successful receipt resolves recovery. A fresh, already-deleted no-op is not
offered by the active-item selector. The backend remains the permission boundary.

Local verification: 106 focused parser/root-manager/item-manager tests passed;
type checking and lint passed. An initial recovery-focus test failed after
administrator access returned; both manager effects now observe permission
changes, and the full focused suite passed on rerun. Two desktop/mobile browser
cases were discovered for keyboard confirmation, retained completion history,
original committed/lost-response retry, accessibility and another session's
empty aggregate progress. Exact-image browser execution is pending CI.
Individual item ordering UI and full PRD-13 acceptance remain unfinished.
### Individual item ordering controls

Editors can choose a before-item anchor across bounded item pages or the end on
the final page. Every page rechecks the original actor, current edit admission
and Card/Checklist revisions. Shared position-bound calculation excludes the
moving row and recognizes an already-in-position row at a seek boundary.

Position acknowledgments preserve parent identity/title/rank and item identity,
text, creation time and completion attribution. They verify rank bounds,
aggregate mutation timestamps and exact changed/no-op Card/Checklist/item
revisions. Unconfirmed responses retain the original actor, destination,
versions and key; definite rejection requires a fresh review. Shared recovery
locks and focus handling apply to item ordering as to other checklist changes.

Local verification: 115 focused parser/root-manager/item-manager tests passed,
including bounded later-page anchors, completion-history preservation, no-op
state, invalid rank/acknowledgment recovery and original retry intent. Type
checking, lint and production build passed. Two desktop/mobile browser cases
were discovered for keyboard before/end choices, accessibility, a lost committed
acknowledgment and another session's live item order. Their exact-image
execution remains pending CI. Full acceptance still requires the complete
browser suite, concurrency/disconnect/access-loss verification, telemetry and
the documented performance/large-data criteria; PRD-13 is not complete.
