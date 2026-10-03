# Attachment covers and lifecycle decisions

This implementation decision links [PRD-14](https://github.com/nickmailhot-lang/StrataAI2/issues/15),
[PRD-18](https://github.com/nickmailhot-lang/StrataAI2/issues/19),
[PRD-05](https://github.com/nickmailhot-lang/StrataAI2/issues/6) and
[PRD-24](https://github.com/nickmailhot-lang/StrataAI2/issues/25).
It extends their existing acceptance work; it does not establish completion.

## Covers

ATTACH-FR-008 selection requires current owning-Board edit authority, the current
Card and File revisions, same Organization/Card identity, an Active Clean
PNG/JPEG/WebP original and a committed immutable publication receipt for its
verified PNG derivative. Clean MIME metadata alone is a necessary eligibility
check, never proof of preview publication or permission. A same-tenant/Card
composite foreign key will preserve ownership when the Card moves between Boards.
Selection/removal, Card revision, current Board stream sequence, audit and
CARD_COVER_CHANGED event must commit together and support original-command replay.

The cover is visible wherever the Card is admitted. On a PUBLIC Board this means
an explicit derivative-only public cover projection. Current internal attachment
metadata, original downloads and private previews retain Organization membership
admission; Board visibility does not make those private objects public. Cover
selection UI must explain public visibility when applicable. Public delivery must
re-admit the current PUBLIC Board, visible Card and selected Active Clean source,
without exposing original filenames, digests or provider identities. Owner Portal
access remains a separate future projection. Existing full-byte integrity staging,
outside-transaction provider work, post-stage/current/final delivery fences and
private no-store headers remain mandatory even for publicly visible cover bytes.

## Attachment archive and deletion

ATTACH-FR-009 follows LIFE-FR-001/002/006/007/008: Active, Archived and Deleted are
explicit distinct states. Archive is reversible; permanent deletion requires an
Archived source, current elevated authority and explicit irreversibility consent.
Archive/restore require current edit authority and permitted active parent context.
The latest archive timestamp is retained after restoration; deletion retains the
archive timestamp, deletion time/actor and original immutable source identity.

Archive clears a selected cover atomically and excludes the attachment from normal
pages/cover eligibility. Restore never silently reselects the prior cover. A
separate authorized bounded archive review may admit archived originals and
verified previews; it does not restore ordinary disclosure or public cover access.
Scanning may safely finish an archived Pending file, but new preview production
and cover selection require an Active attachment. Deleted sources cannot be
restored, scanned or delivered. Permanent deletion clears the selected cover and
revokes all normal original/preview access atomically with revisions, audit and
ATTACHMENT_DELETED; archive/restore have their corresponding lifecycle events.

Private provider objects, immutable upload/preview evidence, tombstones and audit
remain retained until bounded provider reconciliation/retention work has explicit
authority to remove them. Product deletion is irreversible independently of backup
retention windows. No uncertain provider write is blindly deleted or overwritten.
Existing deleted fixtures are not assigned invented archive history during upgrade.

## Implementation order and acceptance evidence

First establish domain transition/precondition rules, then persist lifecycle and
cover ownership with forced RLS and restricted grants. Next implement current
authorized CAS/replay commands, archive pages and delivery fences, then MUI/native
cover and archive controls. Upgrade, restricted-role, current-authority withdrawal,
late transaction fence, two-client recovery, keyboard/mobile and exact-image
performance tests are required before closure. The existing preview/scan recovery
and release-image contracts continue to run. No new service or datastore is added.

The first implemented step is the Attachment domain model. Archive/restore are
idempotent, retain the latest archive timestamp and advance revisions only on
transitions. Restore rejects unavailable parent context and Deleted state. Delete
requires Archived state, a nonzero actor and explicit consent, records deletion
identity/time and prevents restoration/scanning. Failed timestamp validation leaves
state/history unchanged. Archived Pending scanning can finish without re-enabling
cover eligibility. Necessary cover-source checks require Active state; they still
do not grant permissions or prove a published derivative. These rules have domain
tests linked to PRD-14/18 TC-01/02/03/07/10. Persisted lifecycle metadata, authorized
commands, archive review and cover delivery/UI remain subsequent work.

Migration 051 persists explicit lifecycle state, retained archive time and deletion
actor. Existing tombstones retain their actual history without invented archive or
actor values. New records start Active; transition triggers require the next File
revision, preserve ownership/history and make Deleted rows immutable. New deletion
requires an Archived source and retained actor evidence. Forced tenant RLS remains
mandatory. Active and Archived seek pages have separate partial indexes, and the
bounded preview maintenance index excludes Archived rows.

Normal metadata/original/preview admission now requires Active state. The private
preview source loader, opt-in Clean-to-preview enqueue and backfill require Active
sources; backfill rechecks lifecycle after acquiring the File lock. Existing scan
verdict completion remains permitted for Archived files. Current metadata DTOs and
strict web admission include lifecycle/history fields; invalid states, deletion
identity and malformed or missing history are refused in ordinary pages. Fixture
download/preview responses use the same shape. Existing scan/recovery contracts
perform real archive-before-delete transitions; the delivery withdrawal fixture
uses reversible archival rather than restoring a deleted tombstone.

The restricted lifecycle contract checks tenant RLS, defaults, direct Active-delete
refusal, lifecycle evidence/ownership protection, archive/restore/deletion
revisions, retained actor/history and immutable deletion. Migration tests preserve
legacy tombstones and verify the partial indexes, repeat/forward upgrades and
serialized-runner rollback. Runtime readiness requires migration 051. Actual new
Linux execution is pending; command authorization/consent, atomic Card/event/audit
effects, archive review and selected-cover clearing still require Application/API
implementation and acceptance tests.

The corrected 051 PostgreSQL job at 2149534 executed the migration/legacy upgrade,
restricted lifecycle, preview, scan and publication contracts successfully. Its
older storage fixture now accepts the earlier immutable-ownership refusal while
separately testing cross-tenant INSERT RLS, and uses archive-before-delete with
retained actor/history. The full web job identified two Board-screen receipt
fixtures missing lifecycle fields; c8223d0 corrects these without weakening strict
client admission. Both affected recovery tests pass locally, and c8223d0's actual
Linux PostgreSQL and web jobs passed. Full immutable-image release remains pending.

The Application/API command layer now exposes scoped archive/restore and canonical
deletion requiring the current Card context. Each command checks current Internal
Organization membership and Active Organization/Board/List/Card scope before
disclosing child metadata or validating revisions/consent. Archive/restore need
current edit rights; permanent deletion needs administration, Archived state and
explicit true confirmation. Both Card and attachment revisions are compared;
transitions advance each once, preserve unrelated Card fields and archive history,
and append audit plus a content-free Card invalidation and delivery job in the
same production transaction. A final current-authority/session check rolls the
transaction back on refusal. Deleted tombstones cannot be restored or disclosed
through normal reads. A same-state request with current revisions is a no-op.

The original retry key and fingerprint bind Card, attachment, transition,
revisions and consent. Replays check current authority, scope and matching current
lifecycle before returning the original receipt. Canonical /attachments deletion
participates in the same idempotency middleware as scoped Card commands. Separate
archive pages seek at most 50+1 rows using the partial archive index, bind cursors
to Card and archive collection, retain history and expose current restore/delete
capabilities. Public nonmembers and anonymous/Owner Portal callers obtain no
archive metadata. Native telemetry records bounded operation/error labels only.

API tests cover transitions, retained history, consent/precondition/version
refusal, contributor/admin policy, no-op, original replay/key reuse, current
lifecycle refusal, parent archival, revocation and bounded archive pages. A real
restricted Application contract injects audit and final actor failures and checks
complete production rollback of File/Card/audit/event/job/sequence/retry claim;
it then checks exactly-once committed effects, archive/restoration/history,
deletion and replay. Actor/session admission in that database contract is explicitly
synthetic; API tests supply separate HTTP/session evidence. Compilation passes
with zero warnings/errors; actual new Linux command execution is pending CI.
Selected cover persistence/clearing, protected archive original/preview review,
cover derivative admission and native MUI lifecycle/cover controls remain open.

The command commit c8aab6a passed its actual PostgreSQL job, including the injected
audit/final actor complete rollback and exactly-once committed lifecycle effects.
Its web job also passed; managed/exact-image release checks remain pending.

Migration 052 prevents lifecycle changes from stranding Pending scan jobs. A
private server-maintained lifecycle revision count advances only with a valid
archive/restore/delete transition; direct caller mutation is refused. Scan source
generation is the current File revision minus this guarded count. The original
immutable job reference and exact lease remain unchanged, and unrelated metadata
revision changes still supersede the original scan. Verdict publication compares
the locked current File revision, advances it once and retains lifecycle/history.
Terminal replay requires the original committed scan audit/event evidence and
returns no integrity or provider work. Exhausted scan recovery uses the same
generation binding and current revision CAS. Preview enqueue after successful
scanning uses the actual current Clean Active revision. Loader gates include
current nondeleted Organization/Board/List/Card context; Archived quarantine can
finish scanning without enabling normal file or preview delivery.

The forward migration initializes genuine Pending lifecycle histories only when
retained canonical archive/restore audit counts exactly account for the revision
delta; it does not invent Deleted history or treat arbitrary stale changes as
lifecycle revisions. Restricted Worker tests exercise both Archived and restored
Pending scan completion/replay. Recovery tests now exercise Archived and restored
exhausted sources while retaining their existing unrelated-stale/deleted/live
refusal and late full rollback checks. Runtime readiness and migration-runner
repeat/upgrade/count protection checks include 052. Actual new Linux execution
remains pending; local solution compilation and shell syntax pass.

Client lifecycle admission uses separate archive pages and ordinary Active pages.
Archive cursors include the collection and Card identity, retain microsecond/tied
UUID order and require the final row of a bounded 50-item page. Strict command
receipts bind scope, original identity/variant/scan evidence, both next revisions,
retained history, server timestamps, changed/no-op and deletion actor. Deleted
receipts cannot be admitted as restored metadata or archive entries, and private
object/integrity/delivery fields are refused. Seven lifecycle cases plus the
existing attachment admission cases pass locally (36 tests across 3 files).
Typecheck, lint and production build pass. Native lifecycle controls and their
browser/accessibility/performance evidence remain subsequent acceptance work.

The c8aab6a managed run exposed a fixture expectation error: removing an explicit
Board edit grant on a PUBLIC board leaves an Internal Organization member's
read-only archive access intact. The corrected HTTP test requires read-only
capabilities, denied restore replay and then denied reads after Organization
membership removal. It preserves current-authority enforcement. The first 052
migration-runner contract reached the count test but its upgrade fixture contained
only a retained legacy tombstone; the test now creates a separate Active URL row
under that existing canonical parent rather than assuming an Active seed or
resurrecting the tombstone. Neither failure weakens the production guards.

The exact 1486a4d Linux run passes PostgreSQL migration/runtime contracts,
including guarded lifecycle scan completion and recovery, and all 563 Domain
and 273 API tests. Web and source security jobs also pass; immutable release
integration is still pending. This supersedes the earlier pending execution
notes without claiming full ticket completion.

MUI attachment management now offers fresh Active and Archived reviews,
archive/restore commands, and administrator deletion with explicit consent.
Unconfirmed writes retain the original actor, both revisions, request payload
and retry key across Card refreshes. Competing Card mutations remain fenced
while the original retry stays available. Strict acknowledgments, current
capabilities, unavailable access, bounded archive paging and owned dialog focus
govern recovery. Ten control tests and all 29 Board screen tests pass
locally, as do typecheck, lint and production build.

Two native release scenarios are registered at desktop and mobile widths. They
exercise keyboard lifecycle controls, actual API commits with a lost archive
reply and identical retry, second-session live removal/restoration, irreversible
deletion consent, retained restoration history and accessibility assertions.
Local discovery confirms registration only; native execution and release
performance evidence remain pending. Cover selection/clearing, protected
Archived file review and selected public derivative admission remain open scope.

Protected archive delivery now has separate scoped original/preview options and
byte routes. Ordinary routes remain Active-only. Archive source lookup requires
Archived state and exact tenant/Card identity; existing Clean scan, immutable
publication receipt, original identity/integrity, current Internal membership,
owning Board access, deadline and actor/revision guards remain required. Full
provider staging still occurs outside database scope, with post-stage and final
stream fences. Restoring or deleting the source invalidates the admitted archive
snapshot. The retained preview receipt may serve authorized archive review but
does not permit new Archived preview production or ordinary/public disclosure.

New HTTP coverage checks Active/Pending/foreign/stale refusal without provider
reads, protected original bytes and safe headers, and restoration during staging.
Restricted PostgreSQL preview coverage checks committed receipt reuse, separate
ordinary/archive admission, foreign/stale refusal, staged restoration withdrawal
and Deleted refusal. Compilation passes locally; Linux execution remains pending.

The dd2381a and f34a8e0 PostgreSQL jobs now pass in Linux, including protected
archive preview receipt reuse and staged restoration withdrawal. Managed HTTP
execution remains pending at this checkpoint.

MUI archive review exposes explicit protected download/image review for Clean
archived files, including read-only members. Ordinary and archive delivery props
and strict metadata admission are separate. Each options review verifies the
current actor before and after scope/revision admission, uses the scoped archive
route, and expires after one minute. Changed Card revisions, unavailable access,
staged lifecycle commands and unmounted archive pages remove private links/images
and abort pending reads. Pending/rejected/failed files remain unavailable; PDF
original review does not enable image preview. No provider URL is admitted.

All 63 local tests across the new archived delivery cases and existing download,
preview, management and disclosure suites pass; typecheck, lint and production
build pass. Two additional desktop/mobile native scenarios are registered for
keyboard archive image review, readonly mutation controls, explicit protected
download admission, current denial and accessibility. Their provider responses
are explicitly simulated; they prove client behavior only after execution, not
server publication or integrity. Real API/PostgreSQL contracts remain required.

Migration 053 adds the PRD's nullable Card cover attachment identity, with the
same-tenant/Card composite FK and an index for selected-source checks. Existing
Cards retain no cover. Selection/removal requires a next Card revision; new
selection requires an Active Card/Board/List and an Active Clean image with
source-bound immutable preview/publication evidence. The source row is locked
during selection. Forced Card/attachment/preview RLS continues to apply; no new
public metadata or provider capability is granted.

A deferred source guard prevents committing an archive/deletion or unavailable
source while it remains selected. Explicit cover clearing and source lifecycle
changes may occur in either order inside one transaction; invalid final state
rolls the entire transaction back. Restoration does not select a cover. This
schema rule requires the upcoming Application lifecycle command to clear the
selection with its existing single Card revision/audit/event transaction.

The new restricted persistence contract exercises a real committed preview,
URL/unpublished/sibling-Card refusal, tenant read/write isolation, revision
guards, deferred source withdrawal rollback, atomic archive/clear and restoration
without reselection. Migration repeat/upgrade and runtime readiness include 053.
Local compilation and script syntax pass; actual Linux execution is pending.
Application cover commands/read projection, lifecycle clearing, selected public
derivative admission and MUI cover controls remain subsequent acceptance work.

The first 053 Linux run exposed a deferred-trigger privilege error: Worker scan
publication left its private function before the constraint trigger ran, so the
restricted Worker could not read Cards at commit. Forward migration 054 assigns
only this trigger its owner's guard capability. It accepts no caller arguments,
uses the already-admitted immutable row identity, has a fixed search path,
returns no metadata and remains revoked from PUBLIC. Worker general Card read
privileges remain unchanged. Existing restricted Worker permission/refusal tests
and actual scan/preview transactions must still pass. Readiness and migration
repeat verification include 054; new Linux execution remains pending.

The actual 4dc865a Linux PostgreSQL job passes after 054, including restricted
Worker publication and the real cover persistence contract. This confirms the
deferred guard fix without granting Worker general Card reads.

Authorized cover commands now provide private Internal GET/PUT Card cover
routes. Selection binds current Card and File revisions, Active owning parent
context, current edit authority and committed immutable image publication.
PUBLIC selection requires explicit visibility consent; removal has no source
version. Provider keys, digests, filenames and private receipt identities never
appear in cover command responses or content-free Card events. The store locks
Card then source, checks both revisions and advances Card once while preserving
all other Card fields. The File revision is unchanged by selection/removal.

Original retry receipts require current authority, the same current selection
and a still-admitted source. Key reuse with another intent is refused; a fresh
same-selection command is a no-op. Audit, current Board event/sequence, delivery
job and receipt commit with the mutation and roll back on late session failure.
Attachment archive/delete clears a selected source using that same single Card
revision before its File CAS, and emits both lifecycle and cover-clearing effects
inside the owning transaction. Restore never reselects a former cover.

New HTTP checks exercise current Internal authority, no-op/replay, malformed
selection, URL refusal and revoked receipt access. A new real restricted
PostgreSQL Application contract exercises published-source selection, dual CAS,
original retry/key reuse/no-op, injected audit and late actor full rollback,
single-revision archive clearing, both canonical events, replay withdrawal and
restoration without reselection. Local solution compilation passes with zero
warnings; these new Linux tests remain pending. Selected public derivative
delivery, cover UI/projection and complete native release evidence remain open.

The first cover Application contract reached its injected audit failure but
expected an exception; the production transaction layer intentionally maps that
database failure to work_storage_unavailable. The corrected contract asserts
that outcome and retains the complete before/after rollback snapshot. It also
checks PUBLIC selection refusal without consent, successful confirmed selection,
removal without source/selection consent, and zero provider byte reads for all
cover commands. Actual corrected Linux execution remains pending.

The corrected 1e292af Linux run passes the full restricted cover command
contract, including public consent and zero provider byte reads. The later
scan-only fixture then claimed one of its genuine work-event delivery jobs.
The fixture now runs cover commands after all scan-only queue assertions,
preserving real outbox publication and production queue ordering. The whole
PostgreSQL job still requires a new passing run.

Private GET /cards/{cardId}/cover/candidates now exposes at most 50 eligible
File choices and a revision-bound seek cursor. PostgreSQL applies current
Active/Clean image state and committed, source-bound immutable preview proof
before LIMIT 51; one EXISTS query avoids provider reads and per-row lookups.
Only source ID/version, display name and creation time are projected. Unsupported
providers expose no choices. The owning read scope rechecks current membership
and parent authority; a cursor from another Card/revision is refused. PUBLIC
visibility is shown for confirmation, while this metadata route remains Internal.
Real restricted PostgreSQL assertions cover published candidates, URL/foreign/
unavailable exclusion, seek boundaries, stale Card revision, archive withdrawal
and restoration with current File version. HTTP checks cover no-store, current
member, anonymous/foreign/revoked refusal and malformed cursor. Local compilation
passes; actual new Linux execution remains pending.

The reordered e27a7da run passes scan activation, cover commands and protected
preview reads. A later scan recovery fixture also uses the shared queue; cover
commands now deliver their six genuine outbox jobs through the restricted
PostgresWorkEventDeliveryStore/Worker handler and lease acknowledgment. Four
cover changes must have event-feed readiness. This validates delivery while
preserving canonical jobs rather than deleting or rescheduling fixture effects.
The complete Linux job and candidate assertions still await passing CI.

The actual 38d921d PostgreSQL job passes the complete restricted persistence
suite, including cover commands, bounded published candidates, real outbox
delivery, and subsequent scan recovery/lifecycle fixtures. Web quality passes;
managed and immutable release stages are still being observed.

Selected cover delivery now has a separate server-owned admission and private
full-byte preparation path. GET /cards/{cardId}/cover/image takes only Card
identity and an optional current Card revision. Current PUBLIC viewers may
receive the selected committed sanitized PNG anonymously; Internal members use
current membership/Board access. Private source metadata, candidate lists and
original downloads remain separate protected routes. Anonymous admission uses
a nullable actor and explicit Active PUBLIC parent locks, never a synthetic
session ID. The snapshot binds Card, Board, selected File, immutable preview,
actor and a one-minute expiry. Full size/SHA staging runs outside the database,
then current selection/source/parents/visibility are checked again. HTTP delivery
checks admission before headers, periodically and before final bytes; it returns
only image/png with generic cover.png, no-store, nosniff, sandbox/no-referrer and
no range/caching grants. Archived parents expose no eligible cover choices.

New real restricted PostgreSQL coverage uses a genuinely committed publication
and tests Internal/PUBLIC anonymous delivery, exact derivative bytes, private
metadata/original separation, provider admission refusals, corruption, expiry,
actor switching, selection/visibility changes during staging, parent archive and
restoration without reselection. HTTP coverage uses explicitly synthetic
selection/publication fixtures while executing real sessions, Board authority,
full private-object verification and response headers/refusals. Local compilation
passes with zero warnings; these new Linux tests remain pending. MUI selection,
cover projection, public consent/recovery controls and full native release
evidence remain open acceptance work.

The actual a81b6cc Linux PostgreSQL job passes the complete selected-cover byte
contract and the rest of restricted persistence. Current Internal/PUBLIC
anonymous scope, exact committed PNG, full staging outside DB, source metadata
separation, corruption, expiry/actor checks, staged selection/visibility
withdrawal, parent archive and restoration without reselection are executed
evidence. HTTP/managed and complete immutable release jobs remain pending.

Browser cover response admission now validates exact scoped payloads, safe Card
and File revisions, selected/null source consistency, PUBLIC consent, mutation
no-op versus one Card revision, bounded candidate names/identities, microsecond
timestamps and descending Card/revision-bound seek pages. Unexpected provider
fields or stale/foreign receipts are refused. Five local adversarial contract
tests, web typecheck and lint pass. This is preparation for the MUI controls and
cover projection, not evidence that those UI/native requirements are complete.

The a81b6cc managed job also passes, including the new selected-cover HTTP
scenario. Web/PostgreSQL/managed/source quality are all green for that exact
commit; build-once images are running, so the full release gate is not yet green.

Board snapshots now include only a nullable snapshot-specific HasCover boolean
for visible Cards. PostgreSQL reads it with the existing Card page in one query;
Demo projects its stored selection without changing persistent Card records.
Ordinary mutation/read receipts omit the optional display hint. No attachment
ID, provider key, publication digest or image URL is added to generic Card DTOs;
the Card-scoped delivery route still performs independent current admission.
Restricted projection checks cover unset/selected/removed hints for Internal
and anonymous PUBLIC snapshots and ensure the source identity is absent from
serialized Board data. Local solution compilation passes; new projection Linux
execution is pending. The browser model admits this optional hint for upcoming
cover rendering.

All f0c82ce source gates pass (web, managed, PostgreSQL and source-quality);
immutable image build remains live.

MUI CardCoverControl now reviews current Internal actor, exact Card cover and
bounded candidate page, checks actor again, and offers selection/removal. PUBLIC
selection requires a fresh visible warning/checkbox; removal keeps the source
attachment and requires no selection consent. A staged command captures actor,
original Card/File revisions, consent and retry key. Uncertain/malformed replies
retain only the exact original retry across newer snapshots; conclusive denials
require explicit discard/fresh review. Unavailable contexts hide source names,
and keyboard recovery respects the current MUI focus owner. The Board integrates
cover recovery into its competing mutation/close guards while leaving that
original retry available. Ten control scenarios and the existing 29 Board tests
pass locally; a new Board recovery integration scenario separately passes. Web
typecheck and lint pass. Actual new Linux/native release coverage is pending;
cover image rendering and remaining full PRD acceptance remain open.

All source gates for 1d4086c pass, including Linux managed/API tests and the
restricted PostgreSQL contracts. Immutable image build is still running.

Card faces now lazily render the selected sanitized image using only the
snapshot HasCover hint and current Card/revision image route; open Card details
load that same image eagerly. Scope uncertainty, archived parents, removal,
context changes and revision changes retire the old image. Failed delivery shows
a generic unavailable caption without attachment/provider identity. Forty-one
image and Board component scenarios pass locally, including refresh/removal and
original retry guarding; web typecheck/lint/production build also pass. The
display test checks both DOM images while MUI correctly hides the background
canvas from assistive technology during the details dialog.

Two desktop/mobile native client contract scenarios are registered for the
release browser suite: PUBLIC warning and keyboard checkbox consent, original
retry key/body after uncertain acknowledgment and a newer snapshot, competing
mutation guards, actual browser PNG decoding, focus, accessibility and removal
without reconsent. Publication, cover replies and provider delivery in those
scenarios are explicitly simulated, and the unchanged canonical backing Card is
asserted. Registration is not execution evidence. Full genuine upload-through-
Worker publication and native cover delivery/lifecycle acceptance remain open.

The restricted cover command contract now adds 1,000 newer disposable Clean
image metadata rows without preview publication, reads through the actual
candidate adapter, and requires that the genuine published source remains
visible while all unpublished names are excluded and provider reads remain
unchanged. It deletes only its own fixture rows and verifies canonical command
effects are unchanged. This tests eligibility before the SQL limit; it does not
claim a real object/Worker publication for those seeded rows or a measured
large-Board performance result. Local solution compilation passes; actual new
PostgreSQL execution is pending CI.

0126844 PostgreSQL execution found that the capacity fixture copied a restored
source's private lifecycle revision. 0795984 resets new rows to zero lifecycle
count and empty archive/deletion history, preserving all production INSERT
guards; corrected Linux execution remains pending.

The preview activation contract now uses an actual PNG source with a private
trailing sentinel and the real Skia decoder in its Worker handler wrapper. Its
published output measurement and subsequent preview/cover delivery must match
the normalized PNG, differ from original bytes and exclude the sentinel. The
restricted upload-intent adapter, scan/preview handlers, immutable publication,
Card cover commands and current-viewer delivery remain the actual production
components. Object storage and scanner verdicts remain explicit fixtures. This
does not replace the separate isolated Worker-image codec verification or prove
the complete managed AWS upload/browser path. The persistence executable uses
the same pinned Linux Skia native asset already used by Domain tests; local
warning-as-error compilation passes and new Linux execution is pending.

0795984 now passes the actual restricted PostgreSQL job, including corrected
capacity and the complete existing cover/preview/lifecycle chain. cd2ff77 has all
source gates green; immutable image build is queued. The cover command contract
also now races two distinct retry keys against the same original Card/source
revisions, requires one winner and one version conflict, verifies exactly one
audit/event and recovers the winner's exact original receipt. This new competing
writer check compiles locally; Linux execution remains pending.
