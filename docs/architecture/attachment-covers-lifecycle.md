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
