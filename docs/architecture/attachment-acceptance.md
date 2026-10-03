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

The MUI URL creation control is now integrated in active Card/List detail. It
requires explicit profile/revision review, validates a bounded plain-text title
and credential-free HTTP(S) URL, pins the actor/title/URL/Card revision/retry key,
and validates the complete acknowledged scope/actor/intent before success. Dirty
fields survive a newer snapshot and require explicit discard for a new review.
Unconfirmed outcomes keep immutable original intent; retries are explicit, check
the same signed-in actor and never retarget a newer version. Definite rejection
blocks preserved fields until discard. Drafts hide during re-admission; navigation
aborts work and retires scope. Global attachment recovery blocks other mutations
while its own original retry remains enabled. All eight focused creation cases
pass. A Board integration case proves competing saves/Checklist creation blocked
and two identical original commands across newer snapshot; its corrected Save
card locator passes. Existing 26 Board cases passed in the same focused run.
Typecheck/lint pass. The previous full read-panel suite passed 892 cases; the
updated full creation suite is running.

Two release browser scenarios at desktop 1280/mobile 390 are discovered. They
require keyboard creation, local unsafe-URL refusal, a real committed command
whose reply is replaced with 503, identical original retry with one canonical
metadata row/Card increment, recovery focus, interlocks, safe link attributes,
explicit disclosure/hide and WCAG checks. Discovery is not executed acceptance;
CI must run them against the immutable images. Production URL transaction fixture,
latest required-ci and the complete attachment file/storage/scanner/cover/lifecycle/
performance matrix remain open. No binary upload or scanner capability is exposed.

Final local regression for the URL creation increment: all 901 tests across
69 web files pass; typecheck/lint pass. Linux web-quality job 111172347596 in run
37112277658 at f87f70b also passed all 901 tests/69 files, with PostgreSQL job
111172347727 passing. The latest .NET/image/browser and exact-image URL fixture
remain separately pending; source web success does not close any full ticket.

ARCH-07's adopted override explicitly permits a local filesystem adapter while
requiring managed object storage in production. `IAttachmentObjectStorage` now
separates private byte operations from Domain/Application commands. Its typed
reference derives keys only from server-owned Organization and attachment IDs;
no caller filename/path/key can select an object. It grants no authorization or
clean scan status, has no public/signed URL operation, and returns actual measured
byte count and SHA-256 metadata. The interface is not registered as a production
fallback or exposed by upload/download HTTP endpoints.

`LocalAttachmentObjectStorage` is an explicit local/test adapter requiring an
absolute operator-owned private root. It streams with a 64 KiB buffer, reads at
most one byte beyond the server limit, rejects empty/oversize streams, leaves the
source owned by its caller, writes a unique private temporary file, flushes to disk
and publishes a complete final name without overwriting existing keys (Windows
no-replace move; Unix atomic same-directory hard link, followed by temporary-name
cleanup).
Failed/cancelled writes attempt temporary cleanup; outage/crash orphan sweeping
remains future lifecycle work. Reads and deletes use typed tenant identity and
reject symbolic/reparse links in existing ancestors/objects. Unix roots must have
no group/other permissions and new objects have owner read/write only. Windows
requires the operator to provide a root with private inherited ACLs. The root and
its directories must not be mutable by untrusted processes; this is not a defense
against a compromised host or arbitrary directory replacement races. Fixed failure
codes/messages exclude provider paths and source exception details. No provider SDK,
broker, BLOB or new production topology was added.

Eleven new cases compile for actual byte/digest preservation, adapter restart,
same attachment ID in a foreign tenant, owner-only Unix permissions, concurrent
identity refusal/complete bytes, idempotent cleanup, empty/oversize/invalid limits,
pre-cancelled sources, failure after partial bytes, fixed safe errors and root/
ancestor/object links without deleting their targets. Warning-as-error build and
diff checks pass. Windows Application Control still prevents local execution;
Linux execution is pending. Managed-provider selection/credentials/readiness,
server upload policy/byte inspection, scanner Worker jobs/CAS/outbox, controlled
delivery, compensating/orphan cleanup and complete attachment acceptance remain
incomplete. This local interface/adapter foundation does not close PRD-14/ARCH-07.

Executed Linux evidence for the local adapter at f8177cf: .NET-quality job
111174509747 in run 37113044853 passed all 294 Domain/Application/Infrastructure
cases and 265 API cases, with zero skips. This includes all eleven new storage
cases. Web, PostgreSQL, immutable image build and security jobs also passed;
exact-image container/browser integration is still running.

The Application malware-scanning boundary now requires an admitted typed object
reference, a positive bounded persisted byte count and canonical SHA-256 digest.
`IAttachmentMalwareScanner` receives only a forward-only, non-writable private
byte stream and cancellation, excluding display names, URLs and provider paths.
`AttachmentQuarantineScanner` binds terminal evidence to the original request:
Clean/Rejected require consumption of the complete expected bytes, object EOF and
a constant-time digest match. An early Clean response, modified/truncated/overlong
object, missing object, unknown verdict, provider timeout or stream disposal cannot
release quarantine. Unexpected length exposes at most one extra byte. Streams are
retired on success/failure/cancellation; fixed codes exclude provider diagnostics.
Actual job cancellation propagates without producing a terminal verdict.

Seventeen new cases compile for complete synchronous/asynchronous scanning,
infected bytes, early verdicts, digest/length/EOF checks, missing objects, provider
faults, unknown results, cancellation, byte-read bounds and request validation.
Warning-as-error build passes. Linux execution remains pending for this increment.
This boundary produces evidence only: there is no scanner provider registration,
persisted file digest migration, scan job publication, Worker lease/CAS completion
or HTTP upload/download capability yet. Worker must verify current persisted job
scope and Pending record/version/digest before applying evidence atomically with
its event/outbox. Neither a provider verdict nor this evidence grants user access
without the existing current authorization and lifecycle checks. Full PRD-14 and
ARCH-07 acceptance remains incomplete.

Migration 042 adds private canonical SHA-256 metadata without changing migration
041 or inventing byte claims for legacy rows. New FILE inserts require a measured
digest; canonical digests require positive size within the 1 GiB storage ceiling.
URL digests are forbidden. Once recorded, the digest and binary object key/size/MIME
cannot be rewritten, including after scan completion or deletion. Existing FILE
metadata without a digest survives upgrade with NULL but cannot transition to
active Clean, enter an integrity-bound scan or controlled delivery. Tombstones
remain possible without inventing a hash. Explicit verification/backfill of old
private bytes is future work; there is no automatic publication or data deletion.
The Domain file factory now requires canonical measured SHA-256 and bounded size;
URL models retain NULL, and private digests are not added to public metadata DTOs.

Runtime readiness requires all 42 canonical migrations. The restricted PostgreSQL
fixture tests missing/new, malformed, modified and URL digests, immutable object
metadata, retained digest on tombstones and existing tenant/key constraints. The
forward-upgrade fixture seeds a real pre-042 Pending FILE, verifies unchanged NULL
digest/state/version across repeat migration, refuses Clean publication without
integrity and permits retained deletion. Serialized/failure/unrecorded fixtures
move to 043/044/045; readiness-removal tests include 042. Four extra Domain cases
compile. Build, script syntax and diff checks pass; actual migration/SQL execution
and new Domain/scanner execution remain pending Linux CI. Full ticket acceptance
and production provider/upload/scanner Worker integration remain incomplete.

Executed scanner evidence at 3ad35fb: Linux .NET-quality job 111176112195 in run
37113628061 passed 311 Domain/Application/Infrastructure and 265 API cases,
including all 17 new scanner cases, with zero skips. At 6f62778 PostgreSQL job
111176909326 in run 37113914092 passed migration 042, legacy forward/repeat
upgrade/refusal/deletion and restricted attachment integrity fixtures. Its .NET
job 111176909210 passed 314 of 315 cases but caught a real prior local-storage
publication race: both concurrent writers reported success. This source failure
keeps image/release gates closed; it is not treated as accepted flaky testing.

The repair uses POSIX `link` to publish the fully flushed private inode on Linux/
macOS; existing destination names atomically refuse publication. Windows retains
its no-replace move. The .NET 10 Unix `File.Move(overwrite:false)` implementation
checks destination existence before attempting `rename`, leaving a replacement
race ([runtime source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs)).
There is no copy/rename fallback on unsupported Unix filesystems/native binding;
they fail closed with a fixed storage error. Cleanup removes only the writer's
temporary name, preserving the winning object. The existing concurrency regression
now synchronizes eight distinct adapters per object across 16 rounds, requires
exactly one winner and matches its digest to the complete final bytes, while
retaining cleanup/idempotent deletion checks. Build/diff checks pass; executed
Linux proof of the repair and latest required-ci remain pending.

Executed repair evidence at 92f7a9e: .NET-quality job 111177642115 in run
37114177223 passed all 315 Domain/Application/Infrastructure and 265 API cases,
zero skips, including the strengthened eight-adapter/16-round publication race
regression. Web, PostgreSQL, immutable image build and security jobs passed;
container/browser integration is still running. This corrects the proven prior
race without waiving its acceptance test.

The initial managed production adapter now selects AWS S3 behind the same
Application interface, using the official locked vendor SDK only in
Infrastructure. It checks private bucket policy/public-access/ownership before
each operation, includes expected bucket owner and generated tenant key, streams
bounded private multipart bytes with measured SHA-256, refuses identity overwrite
on completion, preserves committed objects after unknown outcomes, retires SDK
responses and attempts bounded unpublished-part cleanup. Configuration and
adapter contract cases compile; build/locked restore/vulnerability audit pass.
Execution, runtime registration and real provider integration remain pending.
See [provider decision and integration limits](attachment-object-storage.md).

Two official-SDK transport cases now compile with an intercepted HttpClient,
checking real signing/serialization/private scope/encryption/conditional headers
and embedded HTTP-200 completion errors. They make no cloud/network calls.
Execution is separately pending; no managed bucket or runtime capability is
claimed from compilation. Locked restore/build/audit and current source checks
remain documented separately from image/browser gates.

Executed managed adapter evidence: db6bb53 .NET-quality job 111179398305 in run
37114789590 passed 334 Domain/Application/Infrastructure and 265 API cases,
zero skips, including all 19 managed S3 adapter cases; web/PostgreSQL passed.
The later two real-SDK transport cases failed at typed Authorization before any
upload in b410687 job 111179958841. They now require raw signature header presence
and the same SigV4 scheme instead; correction execution and complete release gates
remain pending. No signing acceptance is inferred from compile-only correction.

Executed official-SDK transport correction at 92dba62: .NET job 111180631021
in run 37115220262 passed all 336 Domain/Application/Infrastructure and 265 API
cases, zero skips, including both signed/conditional/encrypted transport and
embedded HTTP-200 error cases. Web/PostgreSQL also passed. Managed cloud/IAM,
runtime configuration and complete release gates remain separate/incomplete.

The Application upload policy now accepts only an explicitly configured,
nonempty canonical subset of PNG/JPEG/WebP/PDF and positive size ceiling up to
the 1 GiB storage bound. Invalid/duplicate/oversized configuration fails rather
than broadening allowed types. `IAttachmentFileTypeInspector` inspects at most
256 actual bytes; no client MIME or filename is an input. Infrastructure recognizes
bounded initial signatures/headers, including PNG IHDR shape with initial 32,768
axis/40-million-pixel bounds, JPEG start markers, WebP RIFF/format/chunk tags and
PDF version/newline. Unknown/active SVG/HTML/executable/archive/spoofed/truncated
prefixes cannot pass this initial policy. WebP's declared total size must fit the
policy and later match the measured complete object. Format references:
[PNG specification](https://www.w3.org/TR/png-3/),
[WebP container](https://developers.google.com/speed/webp/docs/riff_container),
[PDF specification resource](https://pdfa.org/resource/iso-32000-2/).

Before metadata publication, measured receipt checks require the original typed
Organization/attachment reference, positive bounded actual size, canonical SHA-256
and matching declared container length where present. Failures use fixed codes
without byte/provider details. Nineteen policy/classification cases compile for
supported types, restricted configuration, unknown/spoofed headers, bounded prefix,
initial image shape, container length, foreign tenant receipt, malformed digest/
size and fail-closed settings. Build/diff checks pass; Linux execution is pending.

This is bounded initial classification, not complete structural parsing, malware
proof or safe preview eligibility. Header-only test fixtures intentionally make
no claim of valid complete image/PDF bytes. Worker image decoding/format validation
must still apply equivalent resource bounds to all image types before previews;
malware scanning must consume/verify full bytes. File download must use controlled
current authorization, nosniff and safe disposition. Upload orchestration must
persist an original-intent record before provider writes and reconcile private
objects on metadata/type-integrity failure, without deleting ambiguous duplicate
identities. Runtime policy/provider registration and upload/Worker endpoints are
still absent; no acceptance is inferred from these unexposed primitives alone.

File metadata persistence now accepts only server-owned measured object receipts:
identity/Organization/key derive from the typed reference, and Domain validates
the bounded size, canonical digest, MIME and display text. Both Demo/PostgreSQL
create a Pending FILE with normalized microsecond timestamps and no scan date,
using the existing store and (in production) owning command transaction. The
private `AttachmentFileRecord` contains metadata and a scoped integrity request;
ordinary pages/receipts still contain neither key, digest nor that private record.
PostgreSQL private reads require the tenant command/read scope before DB access,
exact Card/attachment/Organization, active FILE metadata, non-null digest and the
generated storage key. Legacy noncanonical-key/hashless rows cannot enter this
private read path. Worker still needs its verified-lease SQL boundary; no direct
Worker attachment grants have been introduced.

Three new store cases compile for Pending file metadata/private integrity,
timestamp precision and projection secrecy, foreign tenant/Card/uploader refusal,
same-tenant wrong Card, URL-vs-file identity isolation, duplicate refusal without
replacing integrity, invalid digest/size and pre-cancellation without partial
metadata. The existing production guard case now also checks both file paths
refuse unscoped calls before a configured unreachable database is accessed.
Warning-as-error build/diff checks pass. Linux execution is pending; real
PostgreSQL adapter command execution, file upload authorization/current parent
CAS, original intent/outbox/scan job and compensating/orphan lifecycle remain
required. These primitives perform no object I/O, scan dispatch, status mutation
or HTTP binary exposure by themselves.

Executed policy evidence at 88a3581: .NET-quality job 111182204090 in run
37115777924 passed all 355 Domain/Application/Infrastructure and 265 API cases,
zero skips, including all 19 byte-classification/policy cases. Web/PostgreSQL and
immutable image build passed; remaining security/container/browser gates are
tracked separately from source proof.

`AttachmentUploadReader` now prepares a non-seeking request stream after admission:
it reads at most 256 actual prefix bytes (or one byte beyond a smaller configured
size limit), applies server policy, and replays those bytes exactly once before
the untouched remainder. It never trusts source Length, seeks or buffers the whole
file. The private prefix is zeroed when replay finishes, validation/fault/cancel
fails or the wrapper retires early. The original stream remains caller-owned.
Pre-cancellation consumes nothing; actual cancellation propagates; other source
failures return a fixed safe source-unavailable code. Six new cases compile for
short/chunked/long sources, full byte/digest preservation, no duplicated prefix,
bounded oversize reads, unknown type, ownership/zeroing, faults/cancellation and
early disposal. Build/diff checks pass; Linux execution is pending.

Private file-record Integrity is also ignored by JSON serialization, with explicit
projection tests excluding both derived key and digest even on accidental record
serialization. No HTTP action binds this record as input. Full upload orchestration
must still persist/admit original intent before invoking the reader, retain private
object compensation/reconciliation and atomically publish metadata/scan job/current
Card CAS/audit/events afterward. File endpoint/provider registration, scanner Worker
and controlled delivery remain unimplemented; these additions do not close tickets.

Executed replay/file projection evidence at 1debf98: .NET-quality job
111184233772 in run 37116498421 passed all 364 Domain/Application/Infrastructure
and 265 API cases, zero skips, including all six upload-reader cases and the
private record serialization checks. Web/PostgreSQL/image build passed; complete
security/container/browser and release acceptance remain separately pending.

The Domain original upload-intent state model now pins tenant/Card/uploader,
nonempty retry key, original Card revision, normalized display name and bounded
expected size/canonical SHA-256 claims. Those claims bind retries but do not
establish verified content. Intent expiry is bounded to 24 hours. Writer tokens
and renewable leases are bounded to ten minutes and the intent expiry; stale or
foreign tokens cannot finish or replace a current writer. Unknown writes/expired
writers move to Reconcile, never directly to a new upload. Only verified provider
absence permits a new writer using original identity, or measured original bytes
can become Stored. Size/digest/MIME disagreements do not partially advance state.

Publication requires Stored/unexpired state; the eventual Application transaction
must still re-admit current permission/lifecycle, CAS original Card revision and
atomically persist Pending attachment/audit/events/scan job. Publication is
idempotent once achieved; active writers/published records cannot be abandoned
through upload cleanup. Abandonment retains claims/measurements for private orphan
reconciliation and does not authorize object deletion. Thirteen new Domain cases
compile for original intent, renewal, unknown outcomes, verified absence/recovery,
stale callbacks, mismatch atomicity, expiry, retained cleanup history, lease bounds,
backward clocks and invalid scope/claims. Build/diff checks pass; Linux execution
is pending. Durable intent schema/store, upload orchestration and reconciliation
jobs are still required; this Domain model alone is not a persisted workflow or
completed attachment acceptance.

Upload-intent persistence foundation (migration 043): tenant-forced RLS, tenant/Card/uploader foreign keys, retained unique uploader retry identities, immutable original Card revision/name/byte claims/expiry, monotonically versioned state transitions, bounded writer leases, protected verified measurement, terminal tombstones, and matching Pending FILE metadata required for publication. API has SELECT/INSERT/UPDATE only; Worker has no table grants. Readiness requires the new ledger entry. Clean/repeat/forward migration coverage and a mandatory restricted-role SQL fixture cover missing/cross-tenant context, malformed inserts, immutable identities, leases, reconciliation, publication, terminal history and expiry. This SQL is newly submitted for Linux execution; local build and shell syntax pass, without claiming local PostgreSQL execution.

Executed Domain intent evidence at 5a7d98c: Linux .NET job 111185575657 in run 37116981800 passed 377 Domain/Application/Infrastructure and 265 API cases with no skips, including all 13 upload-intent cases. Web, PostgreSQL, image build and security also passed; container acceptance was still running when recorded. The durable intent store, authorized HTTP upload/publication, reconciliation jobs and complete FILE acceptance remain pending. No ticket is closed by this foundation.

Executed migration 043 correction: bbe3b5f initially failed the forward-upgrade fixture because its two new invocations used the general apply helper (default CI database), instead of the fixture's existing isolated-database `run` function. Commit 21622f1 corrects those calls. PostgreSQL job 111187794526 in run 37117772435 then passed clean/repeat/forward/serialized/failure rollback, the restricted upload-intent SQL matrix and real runtime role grants. No transition check was removed. Source checks, immutable image build and security also passed; container acceptance remained in progress.

Application persistence integration: registered Demo and PostgreSQL upload-intent stores retain immutable original intent and return detached read-only snapshots with the expected digest excluded from JSON. Production calls require the owning tenant transaction before database access. A closed server action enum generates parameterized, tenant/Card/uploader/attachment/revision CAS updates; callbacks additionally require the exact current writer nonce, live lease and matching measured claims. Unknown outcomes require reconciliation; only verified absence permits another writer. Publication requires matching Pending private FILE metadata; terminal identities remain used. Five focused application-store cases cover copy isolation, eight competing claims, stale versions/nonces, wrong scope/measurement, expiry, JSON privacy, cancellation and unscoped production refusal. They compile but their Linux execution is pending at submission.

A separate mandatory PostgreSQL contract executable now exercises the actual C# store and transaction adapter with the restricted API login, not just equivalent SQL. It checks tenant/uploader/Card scope, eight independent transaction contenders, writer renewal/reconciliation, stale nonce callbacks, publication preconditions, intentional metadata/publication rollback followed by successful original-intent publication, and retained expiry. This fixture uses disposable seeded metadata and performs no object I/O; it is not HTTP authorization, actual byte measurement, original Card CAS, audit/event/job or scanner proof. Its locked dependency graph and local warnings-as-errors build pass; Linux execution remains pending. Upload HTTP orchestration, object/scanner runtime registration, Worker delivery and complete file acceptance remain outstanding.

Executed C# persistence evidence at b8994ae: PostgreSQL job 111189413499 in run 37118350158 passed, including the actual restricted-login C# contract executable and SQL/migration/runtime-role checks. The contract explicitly proved one winner among eight real transactions and complete rollback of matching Pending metadata plus upload publication, followed by successful publication from the same stored original intent. Follow-up contract coverage adds mismatched recovery measurements, successful existing-object reconciliation, retained Stored→Abandoned measurement identity and terminal resurrection refusal; that extension compiles but awaits its own Linux run. This evidence does not enable binary HTTP upload or satisfy the complete ticket by itself.

Scan publication producer integration: the registered Application interface publishes only a canonical Published upload and matching private Pending FILE record from the owning transaction. PostgreSQL rechecks the persisted intent revision/claims/timestamps and file revision/measurement/object identity before inserting through the existing durable-job adapter, which never commits this borrowed transaction. Queue payload is exactly attachmentId/cardId/version; keys, digests, filenames, MIME types and bytes are excluded. Retries retain the first job identity/metadata/correlation via the existing tenant/type/key uniqueness. Demo verifies its stored snapshots and uses an in-memory host-only queue. The strict reference parser rejects extra/duplicate fields, malformed IDs/revisions, oversized JSON and unsafe correlation inputs with fixed failures. Twenty-four focused producer/contract cases plus extended store guards compile. The mandatory actual C# PostgreSQL executable now proves metadata/intent/scan-job rollback and retained first publication across duplicate retries; execution of these new cases is pending at submission. This producer alone does not enable HTTP upload or Worker provider execution; lease-scoped integrity loading/completion, runtime scanning and complete file acceptance remain outstanding.

Executed producer evidence at 186ce1d: Linux run 37119045704 passed 406 Domain/Application/Infrastructure and 265 API cases with no skips (job 111191421462), and PostgreSQL job 111191421738 passed the actual restricted-login C# metadata/intent/scan-job atomic rollback and duplicate-publication contract. Web/source gates passed; immutable image/container/security stages remain separate.

Worker handler contract: the new Application handler validates the complete envelope and strict reference payload, asks a delivery store to prove a live canonical lease before any object read, skips already-applied/superseded jobs without provider I/O, and refuses absent or foreign private integrity. It sends only full-stream integrity-bound evidence to the completion store; retry/lost-lease results fail safely for the existing bounded job processor, and shutdown before completion records no effect. Twenty-two handler cases compile, including Clean/Infected/Unavailable evidence, all invalid envelope/scope paths, duplicate/superseded work, retry and cancellation. Execution is pending. It is deliberately not registered in production until its lease-scoped PostgreSQL loader/completion and runtime storage/scanner providers exist; this is not Worker end-to-end acceptance. Scan producer correlation now matches the existing work-event ASCII 1..64 character boundary, with added negative cases.

Worker PostgreSQL delivery (migration 044): narrow SECURITY DEFINER loader/completion functions with fixed search path and explicit tenant/actor/file/Card/version/job/service/payload/idempotency/live worker+lease checks. PUBLIC/API execution is revoked; only Worker gets loader/completion execution, with no direct attachment or Card write grants and no helper execution. Only Ready returns private measured size/digest; superseded, lost-lease and already-applied outcomes return neither. Scan-job original identity is immutable, and another queue type cannot be converted to a scan; scan payloads/retry identity/correlation are checked on insert. Maximum bigint revision is refused because scan completion must advance it.

Completion follows the current Board gate, rechecks Card movement and private persisted integrity, and atomically advances file status/version, current Card version, append-only audit and ready board event. A final lease fence raises and rolls back all tentative effects and sequence allocation if the lease expires mid-completion. Failed evidence leaves quarantine unchanged and asks for bounded retry until the canonical final queue attempt, which records Failed. Committed status/audit/event identity permits acknowledgement recovery without reading provider bytes again. Actual final-attempt crash recovery that leaves Pending metadata with a dead-letter queue still needs operator/reconciliation treatment; no completion claim is made for that edge case.

The mandatory restricted-login C# executable now tests Worker capability isolation and private-data refusal for foreign tenant/actor/nonce/worker/Card references, ten immutable queue mutations, cross-job conversion refusal, API capability refusal, injected late lease expiry and full rollback, successful Clean/Card/audit/event publication, duplicate acknowledgement with no second provider read, and five canonical unavailable-scanner attempts ending in Failed. Object/scanner providers in this fixture are explicitly synthetic in-process providers with real measured test bytes; this is real PostgreSQL/adapter/handler/coordinator evidence, not deployed S3 or antivirus-provider acceptance. Fixtures retain append-only audit and its parent records until isolated CI database teardown. Local warnings-as-errors build and shell syntax pass; migration/runtime-role/C# execution are pending at submission. Production Worker registration still waits for runtime object/scanner setup. HTTP upload/current permission/original Card CAS, controlled delivery/previews/covers/deletion and the full feature matrix remain outstanding.

Executed Worker boundary evidence at e030da0: PostgreSQL job 111195796812 in run 37120604692 passed clean/repeat/forward migrations, runtime capability grants and the actual restricted-login C# Worker contract. This includes tenant/actor/worker/nonce/Card admission, immutable queue identity/conversion refusal, late lease-expiry rollback of all tentative effects and sequence allocation, successful integrity-bound Clean/Card/audit/ready-event publication, provider-free committed replay, queue acknowledgement fencing and five unavailable-scanner attempts ending in retained Failed. Actual providers remain synthetic for this database test. Web also passed; full .NET/image/container/security evidence remains separate. Follow-up explicitly bounds scan JSON to 256 bytes and checks its object shape before object-key enumeration, with eight direct database malformed-payload cases. This avoids scalar/null payloads reaching unbounded or type-specific enumeration errors; follow-up execution is pending.

Executed payload guard follow-up at 804dca0: run 37120940765 passed PostgreSQL job 111196756043 including all eight malformed database queue-payload cases and the restricted Worker/upload contracts. The .NET, web, image build and security jobs passed; container acceptance remained live. Further mandatory C# database coverage now submits an Infected result and verifies retained Rejected status, then deactivates a pending attachment and verifies Superseded with no integrity disclosure or object/scanner read. Fixture intent revisions are taken from their current persisted Card. Deactivation uses one stable statement timestamp for both tombstone/update fields. These added scenarios compile; their Linux execution is pending.

Executed follow-up evidence at 4f76146: PostgreSQL job 111198826830 in run 37121669344 passed the actual restricted Worker infected-to-Rejected and deactivation-to-Superseded/no-provider-read cases, alongside the existing claim, rollback, replay and retry matrix. Source, .NET, web, image build and security passed; container acceptance remained live. Scanner transport commit 5f6f890 passed Linux .NET job 111202973904 in run 37123116370: 444 Domain/Application/Infrastructure and 265 API cases, zero failures/skips, including 14 actual Unix socket protocol cases. PostgreSQL and web also passed; this proves the emulated daemon transport, not deployed ClamAV/signature acceptance.

Runtime provider integration now explicitly enables managed S3 in Production, registers the actual restricted scan store/coordinator/local ClamAV handler in scoped Worker execution, and adds guarded database/private-bucket readiness plus Worker PING/PONG readiness. API has no Worker scan capability registration. A two-second readiness deadline keeps generic 503 responses bounded; malformed enabled configuration fails startup. An optional immutable-release Compose overlay mounts an existing private socket directory read-only, adds no application images/builds and is included in release bundles. Synthetic provider/DI configuration/readiness and actual PING protocol tests compile without warnings; their Linux execution and Compose overlay validation are pending. No real bucket or antivirus daemon is provisioned. Binary HTTP upload, production deployment security, covers/previews/deletion/reconciliation and full feature acceptance remain outstanding; no ticket is closed.
Startup follow-up: enabled hosts resolve the official SDK/private storage adapter before readiness routes are exposed. Missing credential-chain/client initialization yields one fixed startup failure without inner provider diagnostics; it cannot cause an activation exception/500 on first readiness request. Disabled hosts do not resolve that provider. A focused initialization-failure test compiles; Linux execution is pending.