# PRD-14 Attachments and Card Covers acceptance audit

The authoritative ticket is GitHub #15. Attachment source implementation has
begun after the Checklist command/read foundation. Neither feature is closed:
Checklist/operator image acceptance is pending, and the full attachment ticket
requires storage, scanning, API, UI, events, permissions and lifecycle evidence.

`Attachment` is a framework/provider-free Organization-scoped Domain entity.
It retains stable Card/uploader identity, display name, kind, timestamps/version,
server-owned binary MIME/byte count/key or URL metadata, scan status/time and
soft-deletion time. File factories always start in Pending quarantine. Clean,
Rejected and Failed are terminal scan outcomes; duplicates do not invent versions,
and a conflicting result cannot overwrite a settled outcome. Failed processing
has an explicit retry transition back to Pending; malware rejection cannot be
silently retried/cleared. Invalid or backwards transitions cannot partially mutate
state. Deletion preserves metadata and disables delivery/image/cover eligibility.

Only clean active PNG/JPEG/WebP file metadata is image/cover eligible, and cover
eligibility requires the same Organization and Card. This flag is a Domain
invariant, never an authorization decision or permission to issue a URL. URL
attachments are untrusted absolute HTTP(S) metadata with no embedded credentials,
no binary storage/MIME/size and no scan/download/cover status. No URL is fetched.
Bounds are 255 characters for display name, 2048 for the canonical encoded URL,
127 for verified MIME and 512 for a server-owned object key. Configured upload
size/type policy still belongs to future server admission; a positive Domain
byte count is not upload admission. Filenames/client MIME never establish trust.
The binary factory must receive actual server-verified metadata and a generated
key; HTTP DTOs must not directly bind these fields. Future public metadata DTOs
must not disclose storage keys, scanner/provider detail or protected uploader data.

| Requirement | Current evidence | Remaining work |
| --- | --- | --- |
| FR-001 upload size/type | Positive binary size and canonical verified-MIME Domain invariants compile | Configurable server policy, byte inspection, actual authorized upload and client confirmation |
| FR-002 URL | Domain metadata validation, URL-kind separation and no-fetch behavior compile | Authorized/idempotent URL command, persistence, safe external-link UI |
| FR-003 metadata | Scoped stable identity/metadata/time/version and variant separation compile | Migration/FKs/RLS, paginated store/DTOs, authorization-before-disclosure and stable API errors |
| FR-004 object storage | Server-owned object key is separate from URL metadata; no BLOB introduced | Adopted object-storage adapter, credential/readiness validation, compensating cleanup and exact-image proof |
| FR-005 controlled downloads | Pending/failed/rejected/deleted files have no delivery eligibility | Current-rights/lifecycle revalidation and bounded signed URL or controlled streaming |
| FR-006 scanning/quarantine | Pending-first Domain state machine with explicit failure retry compiles | Production scanner/quarantine interface/provider, separate Worker job/outbox, idempotent CAS verdict and scan events |
| FR-007 previews | Clean active supported-image eligibility compiles | Verified byte/image processing, safe bounded preview delivery and accessible MUI viewer |
| FR-008 cover | Eligibility rejects URL/unsafe/deleted/foreign-Card/foreign-Organization metadata | Card cover reference, atomic versioned command/FK, authorization, UI/realtime |
| FR-009 deletion | Domain tombstone disables eligibility and retains history | Explicit administrative consent, parent-version checks, atomic cover clearing, audit/event and object retention/cleanup policy |
| FR-010 untrusted MIME/name | Domain APIs explicitly require server-verified MIME/size/key; validation rejects malformed fields | Actual inspection/scanning/provider pipeline and forged-client-metadata integration tests |

New Domain cases cover scope/identity, quarantine, image/non-image cover rules,
terminal/duplicate/invalid scan states, failed scan retry, malware refusal,
tombstones, invalid/backwards updates, unsafe URLs and canonical encoded length,
key traversal, MIME/byte invariants and control/direction-spoofed display names.
They compile; local .NET execution is blocked by Windows Application Control.
Linux execution is pending. No persisted attachment, authorized upload/download,
cover mutation, scanner execution or user-facing attachment capability is claimed.

All AC-ATTACH-14-01/02/03 and TC-01 through TC-13 remain incomplete until their
full production/state/client scopes are executed. Audit/events/notifications,
realtime reconnect/conflicts, parent archive/delete/move/copy and retention,
original-intent retry, disclosure fencing, public/Owner Portal separation,
keyboard/mobile/scroll context and the unchanged performance/capacity budgets
must be implemented and proved before closure. This audit preserves all ten FRs
and shared dependency scope rather than treating the Domain foundation as done.

Migration 041 adds metadata-only `attachments` with forced tenant RLS, composite
Card/Organization and uploader-membership FKs, mutually exclusive file/URL shape,
quarantine/terminal timestamp shape, positive size/version and bounded names,
MIME, URLs and keys. A globally unique generated object key prevents two metadata
rows sharing binary ownership; active Card reads have a timestamp/ID cursor index.
Card identity avoids stale duplicated Board identity after a move. SQL shape
checks complement the stricter Domain URL validation; they do not admit uploads,
authorize delivery or verify bytes. Scan transition CAS and atomic events remain
Application/store work, and no cover reference is added yet.

Runtime connection admission now requires all 41 ordered migrations. API metadata
access has SELECT/INSERT/UPDATE without hard DELETE; Worker still has no attachment
table grants until the scoped scanning command path is implemented. CI adds actual
migrated PostgreSQL checks under a nonsuperuser/NOBYPASSRLS role: both tenant reads,
foreign update rejection, tenant write rejection, foreign Card/uploader rejection,
39 malformed file/URL shapes including nullable bypasses, unique binary keys,
valid scan/tombstone history and missing-context refusal. Upgrade/repeat checks
and missing-ledger exact-image readiness probes include migration 041. Local
warning-as-error build, shell syntax and diff checks pass. PostgreSQL execution
is pending Linux CI; no SQL/runtime proof is inferred from compilation.

Linux run 37110102227 at 0ab0c97 passed the actual PostgreSQL attachment fixture,
ordered upgrade/repeat/serialization and tenant catalog guards. Its unfiltered
.NET host suites passed 280 Domain and 257 API cases, including all 34 new
attachment Domain cases. This is source/database evidence, not upload/scanner or
complete immutable-image acceptance.

`IAttachmentMetadataStore` now has matching Demo/PostgreSQL URL creation,
Card/Organization-scoped lookup and newest-first timestamp/ID reads capped at 51
(the eventual API returns 50 plus a continuation). Timestamps are normalized to
PostgreSQL microseconds before storage so ties have stable identities. Domain URL
validation is reused; SQL inserts enforce Card/uploader composite FKs. Production
calls reject a missing owning tenant transaction before database access or
metadata validation. Read records exclude object keys and scanner/provider detail.
New cases exercise 63 tied-timestamp rows across pages, both foreign scopes,
identity reuse refusal, foreign parents/uploaders, metadata separation, malformed
cursor pairs and unscoped production calls. They compile; execution is pending
Linux CI. These primitives are deliberately not exposed through HTTP yet: current
permission/lifecycle admission, Card CAS, replay receipts and atomic audit/outbox
must wrap them before a user-facing URL attachment command or read is enabled.

Run 37110453074 at 396b24f failed two new Demo store cases before assertions:
the isolated fixture omitted identity services required by the Organization
store constructor. Its production scope-refusal case and 280 prior Domain cases
passed; PostgreSQL and web jobs passed. The fixture now registers Demo identity
and its clock using the existing registrations. Pagination/scope/identity
assertions are unchanged; repaired execution remains pending.

The internal URL command/read API now wraps the metadata primitives with current
Organization membership and Board view/edit admission under parent locks.
GET /cards/{id}/attachments returns 50 newest-first metadata records and a Card-
bound timestamp/ID cursor; malformed cursors are evaluated only after admission.
POST /cards/{id}/attachments/url accepts title, URL and expected Card revision;
it reuses Domain validation, retains actor/intent retry receipts, increments Card
version without changing other fields, and writes metadata/audit/content-free
Card invalidation plus Worker outbox in the existing production transaction.
Replay revalidates current admission and the referenced active metadata. Public
visibility alone grants no internal attachment read. Anonymous reads require
sign-in; public/Owner Portal safe projections are unfinished. Archived parents
retain read-only metadata, while deleted parents refuse disclosure and creation.

Eight new API-host cases compile for canonical receipt/replay/key reuse and stale
CAS, card-field preservation, invalid URL schemes/credentials without effects,
revocation, 50+13 paging and Card-bound cursors, public nonmember denial, anonymous
denial, archived/deleted Card/List parent handling and retained metadata history.
Warning-as-error build passes; execution and production rollback/outbox evidence
are pending CI. No client attachment UI, upload/provider/scanner/download/cover
or complete FR/AC/TC acceptance is claimed.

A mandatory exact-image CI fixture now exercises the internal URL path through
Nginx against PostgreSQL: canonical receipt/replay/key reuse/stale Card CAS,
validation and outsider/anonymous refusal, one audit/event/outbox, and rollback
when INSERT is separately denied on metadata, audit, events or jobs. Each failed
transaction must leave Card/metadata/audit/event/job/receipt state unchanged;
then the same original key/body must succeed exactly once after repair. It also
checks the real adapter's 50+13 cursor paging over 63 records with ties, revoked
member replay/read refusal and archived-parent read-only retention. Fixture
accounts/URLs/scopes/bodies stay in disposable scratch; no raw response is a
retained artifact. Shell syntax passes; actual immutable-image execution remains
pending CI. The fixture is a production transaction proof requirement, not a
substitute for the full upload/scanning/cover/client acceptance matrix.

Run 37110818558 at 2fe518d passed all 283 Domain and 257 API-host tests,
including the three repaired metadata store cases. Run 37110854018 at ea9a245
passed all 283 Domain and 265 API-host cases, including the eight URL command/read
cases. PostgreSQL/web/source gates passed for each; immutable-image stages are
still running. Later bfa4ba5 passed .NET/PostgreSQL but failed the separate
Checklist create passive-effect assertion, repaired at bd05d37 with all 10
focused cases passing. The exact-image URL fixture is still unexecuted.

The MUI Card detail now has an explicit attachment disclosure with bounded reads,
scope/revision validation before display, abort/disclosure fencing during access
checks, safe plain-text external URL links with noopener/noreferrer/no-referrer,
read-only indication, cursor paging and explicit retry/refresh. No URL is fetched
for a preview. The response codec rejects unexpected private fields, binary kinds
and delivery state until those paths exist; it keeps .NET timestamp cursor precision
with bigint and rejects scope/actor/intent/version mismatches, stale/deleted rows,
unsafe links, invalid ordering/ties/duplicates/continuations and excessive pages.
Current URL enum ordinals are explicit (kind 1, scan status 0), matching the API's
existing numeric contract for these newly added Domain enums. Unknown states fail
closed. No command is emitted by this read-only panel. URL creation UI and all file
capabilities remain incomplete. All 46 focused panel/codec/Board cases pass;
typecheck/lint pass. Full web regression is running. Real browser keyboard/mobile,
context/performance and latest immutable-image proof remain pending.
