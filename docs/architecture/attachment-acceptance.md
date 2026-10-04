# PRD-14 Attachments and Card Covers acceptance audit

The authoritative ticket is GitHub #15. The current source includes authorized
URL and raw-file commands, private object-storage/scanner adapters, fenced
publication, metadata reads, controlled downloads and MUI attachment controls.
The full ticket remains open: the complete enabled-provider release workflow,
retention/purge reconciliation and full cross-feature acceptance matrix are
unfinished. Durable previews, covers and guarded soft deletion now have source
implementations and scoped executed contracts; they are not full-ticket proof.
The opening matrix describes current scope; dated evidence below is historical
and must not be treated as proof that the current head has passed required-ci.

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
invariant, never an authorization decision or permission to issue a URL.
An actual cover also requires a current published verified derivative. The
Worker now publishes verified derivatives and the API/MUI provide authorized
preview delivery, cover selection/removal and current Card cover display. URL
attachments are untrusted absolute HTTP(S) metadata with no embedded credentials,
no binary storage/MIME/size and no scan/download/cover status. No URL is fetched.
Bounds are 255 characters for display name, 2048 for the canonical encoded URL,
127 for verified MIME and 512 for a server-owned object key. Configured upload
size/type policy is enforced by server admission and actual-byte processing;
a positive Domain byte count alone is not upload admission. Filenames/client
MIME never establish trust.
The binary factory must receive actual server-verified metadata and a generated
key; HTTP DTOs do not bind these fields. Public metadata excludes storage keys,
digests and scanner/provider details; authorization precedes disclosure.

| Requirement | Current evidence | Remaining work |
| --- | --- | --- |
| FR-001 upload size/type | Configurable admission policy; raw HTTP validation; bounded full-byte size/SHA verification; upload-intent claim/recovery and atomic Pending publication; MUI hashing/upload/retry controls; managed/API/PostgreSQL contracts | Complete enabled-file workflow in release images, remaining cross-feature/lifecycle/scenario coverage |
| FR-002 URL | Authorized idempotent command, forced-RLS persistence, cursor reads, safe MUI links and exact-image URL transaction checks | Complete linked interaction, concurrency, reconnect, accessibility and performance matrix |
| FR-003 metadata | Scoped variant DTOs; migrations 041/043/044; tenant-safe integrity/RLS; current authorization before paging/admission; stable errors and atomic receipts/audit/events | Preview/cover/deletion state integration, remaining move/copy/archive races and full release acceptance |
| FR-004 object storage | Official managed S3 adapter, private-owner/policy validation, non-overwrite multipart storage, explicit readiness/configuration refusal; no database BLOB; SDK transport/private-file contracts | Release-image enabled provider workflow, orphan/retention reconciliation and documented operator configuration/smoke evidence |
| FR-005 controlled downloads | Current Clean/scoped admission, time-bound snapshot, SHA-verified anonymous staging, final/periodic rights checks, private forced-download headers and MUI browser-owned delivery; managed/API/PostgreSQL contracts | Full enabled-file browser/provider acceptance and deletion/cover/reconciliation interactions |
| FR-006 scanning/quarantine | Separate Worker job, scope-only payload, restricted lease-bound SQL capability, complete-byte ClamAV protocol and atomic verdict/Card/audit/event persistence; fail-closed Pending/Rejected/Failed delivery | Final-attempt crash reconciliation/rescan, remaining mutation races and complete enabled image/operator coverage |
| FR-007 previews | Linux raster normalization; exact Worker isolated codec/privilege-drop/cancellation verification; durable private derivative jobs and fenced publication; current-authorized controlled PNG delivery and MUI viewer | Complete actual enabled-provider release/browser pipeline and cross-feature acceptance matrix |
| FR-008 cover | Nullable tenant/Card composite FK; current published-source prerequisite; dual CAS/idempotent commands and atomic audit/outbox; selected-source withdrawal clears once; PUBLIC consent/current anonymous image admission; minimal snapshot hint and MUI controls/display | Full native execution, real provider/upload-through-Worker integration, concurrency/reconnect and unchanged capacity/performance acceptance |
| FR-009 deletion | Guarded archive/restore/elevated confirmed soft deletion; current parent/source versions; atomic selected-cover clearing; audit/events; restore does not reselect | Full enabled-provider/browser lifecycle matrix and retention/purge reconciliation |
| FR-010 untrusted MIME/name | Canonical raw transport validation; server byte classification/full size/SHA checks; quarantine; safe opaque downloads; strict raw image decoder; forged-input/API/provider cases | Successful isolated image admission/publication plus full enabled release/security regression matrix |

Domain, managed provider, API and restricted PostgreSQL tests now cover the
implemented foundations described above; the dated job evidence below identifies
their executed scope. Local compilation passes, but local .NET execution remains
blocked by Windows Application Control. The latest executed environment-policy
head 1d4086c passes Linux managed/API, web and restricted PostgreSQL source gates;
its full immutable-image/release gate remains pending. Historical 6a5f0c9 had an
isolated-image failure that was subsequently repaired and verified in exact
Worker images; that dated failure is not the current implementation state.
Newer heads require their own exact-image results. Source capabilities and simulated
provider/browser evidence do not prove the complete configured file lifecycle.

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
Startup follow-up: enabled hosts resolve the official SDK/private storage adapter before readiness routes are exposed. Client initialization failure yields one fixed startup failure without inner provider diagnostics; it cannot cause an activation exception/500 on first readiness request. The SDK may resolve credentials lazily during requests; credential availability is then checked by readiness private-bucket calls and fails closed as a generic 503. Disabled hosts do not resolve that provider. A focused initialization-failure test compiles; Linux execution is pending.
Executed runtime wiring evidence at bc9a63c: Linux .NET job 111204046930 in run 37123484188 passed 457 Domain/Application/Infrastructure and 265 API cases with zero failures/skips. The 13 added cases cover actual local PING protocol, DI resolution of managed storage/scanner/coordinator/handler, API capability separation, malformed enabled configuration, Demo/disabled behavior and current private-bucket/database/scanner readiness with caller cancellation. PostgreSQL and web passed too. The follow-up startup-initialization case is separate and pending. A new mandatory exact-image configuration script submits 13 network-isolated API/Worker startup refusals for malformed enablement, Demo activation, invalid bucket/owner/region, absent Worker scope and invalid socket paths. It uses the already-built exact images with no provider connection/cloud credential; local shell syntax passes, execution awaits immutable-container CI. This does not establish a live managed bucket or antivirus deployment.
Application file publication now borrows the owning PostgreSQL command transaction and existing retry receipt mechanism. Shared attachment admission retains current internal Organization membership, Board permission/lifecycle and exact Card/List/Board scope. The server-owned upload identity/retry key must resolve to the same actor/Card, immutable measured Stored intent, nonexpired timestamp and original Card revision. Publication CAS preserves other Card fields, creates Pending FILE metadata from the verified persisted measurement, advances intent to Published, publishes its scope-only scan job, then appends audit/work event and rechecks current admission/actor before commit. Provider streaming is excluded from this transaction. Receipt replay re-admits current scope and original published file identity/private integrity without re-publishing effects or disclosing the digest.

The mandatory real restricted-API C# contract submits outsider refusal, mismatched upload identity, expired Stored and stale original Card revision, injected audit failure after tentative Card/metadata/intent/scan-job effects, final actor refusal after effects, successful publication and byte-identical private receipt replay, then membership-revoked receipt refusal. Card fields/revision, metadata, scan jobs, audit, events, receipt claims and Board sequence are checked together after every failed/committed path; measured Stored intent is retained after failures. Actor/session admission is explicitly synthetic; actual Organization/Board authorization, Application command, adapters, transaction and database roles are used. Append-only audit/parents are retained until isolated CI database teardown. Local warnings-as-errors compilation passes; Linux execution is pending. This service has no HTTP endpoint and no object/provider write. Authorized upload prepare/claim/stream/unknown-write recovery/current-body retry admission, Demo fault rollback, production deployment and the broader FILE delivery/preview/cover/deletion/reconciliation matrix remain outstanding. No issue is closed.
Initial Application publication execution at f92ab01 failed PostgreSQL job 111207052424/run 37124531298 during the first real Board access lookup: the new fixture's minimal Organization seed omitted canonical owner_user_id, and the production Organization projection correctly required it. The fixture now creates users first and supplies its owner reference. Production authorization/projection and transaction checks are unchanged. Worker scan and preceding migration/role contracts passed before this fixture error; publication acceptance remains unverified until the corrected executable passes.
Second fixture correction: d768198 passed current scope/expiry/original-revision refusal but failed PostgreSQL job 111207662122/run 37124745899 in the joint effects assertion because it queried a nonexistent boards.stream_sequence. The assertion now reads canonical work_event_streams.last_sequence (zero if no stream exists), retaining sequence rollback/duplication checks. Two additional scenarios create a measured Stored upload, then advance its actual Card revision or archive the Card; publication must refuse and retain the measured intent with no effects. Execution remains pending. The same run exposed an existing WatchControl focus race in web job 111207662003: the malformed-acknowledgment recovery button existed but focus stayed on Check current watching. The focus effect now consults the retained original intent and defers until its retry button ref is attached; it cannot consume the request using stale recovery render state. The original keyboard focus assertion remains required.
Executed supporting evidence: exact-image container job 111205976623/run 37123882475 at 6cb0dd0 passed the mandatory enabled-attachment configuration refusal step, all 13 network-isolated API/Worker cases, plus existing Worker scope and URL attachment steps. Overall container/browser/required-ci acceptance was still running. The WatchControl correction passes all 23 focused local tests with the unchanged focus assertion; web typecheck/lint and .NET warnings-as-errors build pass. Corrected publication sequence assertion and changed/archived Card scenarios are newly submitted for PostgreSQL execution.
Upload admission now executes short owning tenant commands with current shared Card/Board/List/Organization admission and actor rechecks. A first prepare must match current Card revision and configured byte bound; retained retries must match original actor, Card, normalized name, size, canonical digest and original revision. Expiry/abandonment cannot mint a replacement identity. A separate claim requires the original Prepared snapshot/version and unchanged Card revision, then generates a fresh bounded writer nonce. Active/stale duplicate claims do not inherit a writer or authorize a second provider write. Expected digest is only a retry-binding claim until trusted complete measurement.

Enabled Production runtime now registers the actual prefix inspector and configurable upload policy: default 20 MiB, hard bound 1 GiB, unique nonempty supported PNG/JPEG/WebP/PDF subset. Invalid limits/types fail startup. Both release-overlay hosts receive policy configuration. Measured Stored-file publication checks current type/size policy again before any effects, so a policy restriction after storage cannot bypass publication admission. Five additional policy configuration cases compile; exact-image startup refusal adds four policy cases (17 total).

The restricted real Application/PostgreSQL executable now submits normalized original retry identity, four immutable-body changes, outsider validation refusal, first Card revision conflict, expiry retention, fresh writer claim, stale/active duplicate refusal and two concurrent claim contenders with exactly one winner. Publication additionally submits current size/type policy restriction after storage, preserving all metadata/intent/job/audit/event/receipt/sequence effects unchanged on refusal. Current member revocation refuses upload-intent recovery as well as publication receipts. Actor/session and byte measurement are synthetic here; database roles, canonical scope/store/command/CAS are actual. Local warnings-as-errors build, shell syntax and diff checks pass; added Linux execution is pending. There is still no HTTP upload or provider write by these services. Actual prefix/byte streaming outside locks, lease-fenced measured completion/unknown-write reconciliation, HTTP/MUI and complete FILE lifecycle/security/browser acceptance remain outstanding. No ticket is closed.
Upload admission also owns measured completion and unknown-write callbacks after provider I/O. The canonical writer identity/retry/version/nonce is reloaded under current scope and actor checks. Stored completion requires actual measurement to match original size/digest, derived object identity and current inspected type policy; malformed/missing measurements cannot select the unknown-write action. Final actor refusal rolls back tentative measurement. An explicit unknown-write callback moves only its matching Writing snapshot to Reconcile; stale callbacks cannot revert already-committed Stored truth or start a replacement writer. No provider delete or blind Prepared reset is introduced.

The mandatory restricted C# executable submits wrong size/digest/object identity and missing measurements, final actor refusal with full measurement rollback, successful Stored completion with cleared nonce, stale unknown callback after committed Stored refusal, explicit unknown-result reconciliation and refusal to claim a replacement writer. Current authorization and actual persistence/transaction adapters are used; measurement and actor admission remain synthetic, with real provider byte streaming still outstanding. Local warnings-as-errors compilation passes; callback Linux execution is pending.
Actual-byte upload orchestration now commits preparation/nonce admission before any object I/O and re-admits measured completion/publication in separate short commands. The bounded caller-owned forward stream goes through the actual prefix inspector and provider full-byte measurement. Lost writes and cancellation retain Reconcile with independent bounded bookkeeping; no ambiguous final object is deleted. A live writer refuses provider access. An expired writer enters reconciliation; only a successful private lookup proving absence can reset to Prepared. Existing objects are read to EOF with bounded size and actual SHA-256, then checked against the original immutable request/current policy before Stored/publication. Read outages/corrupt objects leave the retained claim untouched. Stored/Published retries use original publication receipts without reading replacement bodies.

The mandatory restricted PostgreSQL executable now submits actual 100,003-byte input (PDF header classification only), full input consumption/caller ownership, provider-outside-owning-transaction assertions, original private receipt/no repeated I/O, lost committed reply, private-read outage without absence reset, full measurement recovery, known absence and one replacement writer using original identity, stored corruption refusal, live/expired writer and stale callback fencing, cancellation after object commit and provider-free-write recovery. Effect assertions inspect canonical metadata/job payload/Card audit/event/retry schema, not presumed columns. The object provider and root actor/session remain synthetic; scope, Application commands, inspection/crypto, adapters, CAS, database roles and transactions are actual. Local warnings-as-errors compilation passes; added Linux execution is pending. No HTTP upload/MUI binary activation, deployed S3/ClamAV, complete FILE lifecycle or issue closure is claimed.

Previously submitted measured/unknown callbacks at 9c782ca passed restricted PostgreSQL job 111212637865/run 37126471879; .NET, web, source, build-once images and security also passed. Container/browser/required-ci acceptance remains live. This is not a full green release claim.

Executed actual-byte orchestration evidence: a68cd1a passed restricted PostgreSQL job 111217410930 in run 37128077282. Its actual-byte contract reached all assertions for full forward input/measurement, provider outside owning transactions, lost replies/read outages/confirmed absence/corruption, live/expired writer fencing, cancellation recovery and changed/archived parent refusal; existing publication/Worker/migration/role contracts also passed. Web passed; remaining run stages were live.

The MUI attachment disclosure now admits a strict discriminated union of URL and private FILE metadata using the existing 15-field public contract. It validates supported actual MIME types, bounded integer byte sizes, null file URL, defined scan states, terminal scan timestamps/revisions and microsecond precision; unknown/secret/provider URL fields and hybrid variants fail closed. Mixed pages retain scope/cursor ordering and pagination checks. URL creation acknowledgments still require the URL variant. Files display escaped name, byte size and explicit text for Pending/Clean/Rejected/Failed; metadata does not activate downloads, previews or covers. Current access/revision changes still hide protected content. All 34 focused codec/disclosure/URL-create tests plus web typecheck and lint pass locally. Full web/container/browser acceptance is pending; upload HTTP transport/UI creation and remaining FILE lifecycle still outstanding.

Enabled Production now maps POST /cards/{id}/attachments to the measured streaming orchestrator. Normal Demo/disabled hosts do not map binary creation, while existing URL creation/read routes remain mapped. Runtime-derived availability permits an explicit synthetic provider override only in test host composition. Current session/Organization/Board/List/Card write admission precedes header/policy errors or body reads; every subsequent intent/completion/publication command re-admits independently. Idempotency-Key is mandatory. The raw application/octet-stream transport uses canonical base64 UTF-8 X-Attachment-Name (bounded 255 UTF-16 characters / 1360 encoded characters), original X-Attachment-Size (1..1 GiB), lower-case X-Attachment-SHA256 and X-Card-Version. These are retry-binding claims, not verified metadata; no caller MIME/key/verdict is accepted. If Content-Length exists it must match the original claimed size. Chunked input is still fully bounded/measured by the provider. Kestrel's writable request limit is set to configured policy before reading. Fixed error envelopes distinguish type/size/integrity/live-writer/unavailable failures. Attachment operations use fixed metric operation/error labels with no body/header/path identity fields.

The exact-image web edge now streams only the file collection route without request-body disk buffering, with a 1 GiB hard ceiling and bounded idle/proxy deadlines; Application applies the configured lower limit before reading. Synthetic HTTP tests submit real authentication/CSRF and actual inspector/streaming for authorized Pending publication, misleading filename with PDF bytes, private original receipt/replay, changed-name retry refusal, outsider/anonymous/revoked member refusal and unchanged unrelated Card fields; invalid/missing retry, content type, configured size and forged prefix never call the provider. Normal disabled Demo continues URL creation and returns no binary mutation route. Transport tests include malformed/oversized/base64/UTF-8/duplicate/size/digest/revision claims and unknown chunked length. These compile without warnings but Linux execution and exact-image edge validation are pending. Demo fault rollback is not claimed; real restricted PostgreSQL atomicity was proved separately. Production S3/ClamAV deployment, file creation UI and the broader FILE lifecycle remain outstanding, and no ticket is closed.

Upload-option disclosure now uses a short owning tenant command with current write admission and actor rechecks. Enabled hosts expose GET /cards/{id}/attachment-upload-options with exact Organization/Board/Card/current revision, configured byte limit and sorted supported type subset. Outsiders/revoked members cannot obtain options; disabled hosts do not map the route. This supports reviewing a file upload using server configuration before hashing/transmission. Actual prepare/publication still revalidate current policy independently. Restricted PostgreSQL and HTTP fixtures now assert canonical scoped options and outsider/revocation refusal. The browser options codec validates exact fields, current scope/revision, bounded numeric limit and canonical unique supported allowlist, refusing private extras. Local compilation passes; added Linux execution is pending and no file creation control is activated by this disclosure alone.

Executed HTTP transport evidence: 51ea852 passed Linux .NET job 111219519004/run 37128794008, including the added authenticated/raw transport, original receipt/revocation, invalid-request and disabled-Demo URL continuity assertions. PostgreSQL/web/source/build-once images also passed. Container job 111220623939 failed Start release topology: retained diagnostics artifact 11276241928 reports nginx unknown directive `36}/attachments$` at the newly introduced regex location. The bounded `{36}` expression must be quoted as one nginx configuration argument; it is now quoted. CI now validates `nginx -t` inside the exact newly built web image with network disabled before exporting that same image, so syntax failures are surfaced before topology deployment without rebuilding/skipping acceptance. Local Docker is unavailable; corrected exact-image validation/execution remains pending. Added upload-option scope tests/codec checks compile, and all 15 focused codec cases plus web typecheck/lint pass locally.

Browser upload preparation now computes the original retry-binding SHA-256 with 512 KiB Blob slices, explicit policy/hard bounds, cancellation checks before/after reads, exact complete-slice/unchanged-size validation, bounded progress callbacks and zeroed read/hash buffers. It never calls whole-file arrayBuffer. The exact locked @noble/hashes 2.4.0 SHA-256 primitive uses the official streaming API (https://github.com/paulmillr/noble-hashes); this one MIT dependency adds no framework/service, and installation with scripts disabled reports zero npm vulnerabilities. Actual server measurement remains authoritative. Twelve local cases compare native independent SHA-256 across padding, chunk and multi-chunk boundaries, then assert size refusals/cancellation/truncated/changing source behavior and buffer cleanup. File acknowledgment admission separately requires exact original scope/actor/name/size/Card revision, allowed actual MIME, version-one Pending metadata and unchanged creation/update timestamp; URL/terminal/changed claims fail closed. All 28 focused digest/metadata/options/acknowledgment tests, typecheck/lint and production web build pass locally; no file creation control is activated yet. Full Linux/security/container/browser execution remains separate.

At e8558a2, Linux .NET job 111221198182, PostgreSQL job 111221198199 and web job 111221198111 in run 37129359491 passed, including added current upload-option scope and outsider/revocation assertions. Source gate passed. Corrected exact-image nginx configuration validation/build/container acceptance was still running.

MUI file creation is now wired into Card detail. An explicit review obtains the current actor and exact scoped server upload options before exposing selection. Native file selection shows escaped filename/size, server limits and supported formats; client File.type never determines acceptance. Local preparation uses the bounded streaming digest. The actor is rechecked before preparation and again after lengthy preparation, before any POST. The original File object, normalized UTF-8 name, size, digest, actor, Card revision and UUID key are retained once a request is sent. Raw same-origin octet-stream requests use the existing CSRF transport. Pending acknowledgments must match original scope/actor/name/size/revision and supported public metadata; actual MIME is server-owned and may reflect a current policy broadened after review, so it is not compared with File.type or an obsolete review subset.

Unknown/malformed/lost replies, live-writer conflicts and rate limiting retain the original retry. A newer snapshot or temporarily hidden re-admission cannot rebase its body/key/revision or emit another command automatically. A five-minute client wait and Stop action abort pending work promptly even if transport/digest promises ignore cancellation; stopping a POST is not proof of absence. The selected file survives local preparation cancellation. Current access loss/scope change aborts pending operations and hides selection; late results cannot disclose old content or submit a command in a new scope. Terminal refusals preserve the selection blocked until explicit discard/load-latest. Native controls and focus restoration provide keyboard access; names wrap on mobile. File recovery joins the existing Card/Board mutation interlocks, excluding the file control's own recovery so the original retry remains available. Fixed live-upload error code survives the safe Problem boundary; server diagnostics remain excluded.

Local verification: 16 initial form cases and an actual Board integration case verify original File/header identity across newer snapshots, busy/recovery interlocks, caller MIME spoofing with server MIME, wrong/secret/terminal acknowledgments, actor changes before/after hashing, scoped option refusal, empty input, Card revision conflict, cancellation, late results and scope changes. All 939 tests across 71 files passed with two local workers; an additional five-minute stalled-POST case then passed (17 form cases). Typecheck/lint and production build pass. The first unrestricted local full run had three timing failures in existing BoardFilter/ListCopy/BoardDate tests. The canvas test now waits for the fresh parent result callback after switching canvas, retaining its exact projection assertion; no product/security assertion or timeout is weakened. Resource contention was reduced for the local rerun only; CI retains the full suite. Playwright discovers two new 1280/390px exact-image browser cases with native file bytes/digest, original retry, keyboard focus, Pending disclosure and axe checks. File-provider replies in those browser cases are explicitly simulated and actual API metadata is asserted unchanged; discovery is not execution and this does not claim production S3 publication. Full new CI/browser execution is pending. Download/preview/cover/deletion and operational provider/reconciliation acceptance remain outstanding. No issue is closed.

Prior digest commit 65c299b passed Linux web/.NET/PostgreSQL/source/build-once images/security in run 37129713839; its container/browser stage was still running. The corrected nginx route in e8558a2 passed exact web-image configuration validation and release-topology startup; no full release green claim is made.

Controlled download admission now issues a server-only, nonserialized one-minute snapshot only for an exact scoped FILE with Clean verdict, valid scan metadata and matching private size/object identity. It uses the existing short owning tenant read command, current Organization/Board/Card/List admission and root actor checks. Revalidation binds the original actor, Card location and complete persisted file metadata/private integrity; an expired snapshot or clock rollback cannot extend authorization. Provider preparation must occur outside this transaction and must be followed by revalidation before delivery. This snapshot is not a bearer credential or a signed URL, and no HTTP download route or normal file delivery is enabled by it.

The restricted Application/PostgreSQL publication contract now submits Pending/Rejected/Failed/missing/outsider refusal, Clean admission, zero serialized snapshot fields, current revalidation, final root-actor refusal, actor substitution, changed Card List scope with fresh authorized admission, expiration/clock rollback, changed verdict/version, deletion and current membership revocation. Scan/deletion/movement transitions in these added admission cases are explicitly administrator fixtures, not production command/Worker proof; actual Worker verdict/lease tests remain separate. Local solution warnings-as-errors compilation passes. Linux execution is pending. Controlled byte delivery, previews, covers, deletion commands and operational reconciliation remain outstanding; no issue is closed.

At 1c55dba, web/.NET/PostgreSQL/source/build-once images/security passed in run 37131821130. The exact-image container/browser acceptance stage was still running; the newly added browser file-provider replies remain explicitly simulated.
Executed download-admission evidence: fc31ce0 passed restricted PostgreSQL job 111230672714/run 37132652250, reaching the publication contract's final success after all added admission/refusal/revalidation checks. Existing actual-byte upload, Worker scan and persistence contracts passed in that job as well. Web job 111230672649 also passed; .NET/security/images/container/browser/full required-ci evidence remained separate. The refreshed complete GitHub ticket inventory still contains 92 canonical definitions (91 open, ARCH-01 closed, duplicate PRD-03 excluded), unchanged body hashes and dependency groups; newer issue timestamps reflect progress without satisfying closure.
Controlled byte delivery now prepares the complete private object outside the owning database command, verifies its actual size and constant-time SHA-256 against the admitted Clean integrity record, then re-admits current scope/actor/version before transferring ownership. The adopted Linux release API uses exclusive mode-0600 ephemeral files with DeleteOnClose, an open inode retained through delivery, a 64 KiB zeroed buffer, declared-size-plus-one EOF checks and two concurrent staging/delivery slots (at most two hard-bounded 1 GiB staged files). Missing/corrupt/short/long objects never return a prepared delivery stream. Source closure, failed preparation, cancellation and response disposal release staging and slots; no provider object is rewritten or deleted. Other host platforms fail closed for this staging implementation. Upload limits remain separate from historical clean-file delivery bounds.

Enabled hosts expose GET /cards/{cardId}/attachments/{attachmentId}/download and the canonical GET /attachments/{attachmentId}/download?cardId={currentCardId}. The required current Card context avoids an unscoped metadata/object-key lookup. Preparation has a 55-second deadline. Response execution revalidates again before disclosure, periodically during transfer and immediately before final bytes, preserving the original one-minute admission expiry. Slow/revoked transfers abort and dispose content. Delivery forces application/octet-stream + attachment disposition, strips path components from the proposed filename, uses private/no-store, nosniff, sandbox/default-src-none and no-referrer, and emits no signed URL, digest, ETag, Last-Modified or range capability. Range requests receive the complete currently authorized body, never a partial unverified bypass. Download operation telemetry uses one fixed label with no identity/name/provider content. Normal Demo/disabled hosts still do not expose binary routes.

Ten added Linux staging cases submit exact single/chunk/multichunk bytes, private modes, read-only ownership/double disposal, short/long/corrupt/missing objects, source-close failure, read cancellation and bounded concurrency/cancelled waiters. The HTTP fixture uses actual identity/session/current admission, actual private disk verification and delivery, with explicitly synthetic object storage and Clean scanner metadata; it submits quarantine/outsider/anonymous refusal, exact downloaded bytes/safe headers/path stripping/full Range handling/canonical Card-context route, corruption before delivery and membership revocation during preparation plus revoked retries. The restricted PostgreSQL contract submits actual preparation and byte identity while asserting provider reads occur outside the owning database transaction. API-only preparation registration and Worker separation are asserted. Local warnings-as-errors compilation passes; added Linux execution and release acceptance are pending. This is not a deployed managed-bucket/antivirus proof. No MUI download activation, previews, covers or deletion command is claimed, and the ticket remains open.

At documentation head 5d0615b, Linux web/.NET/PostgreSQL/source/build-once images/security passed in run 37132889807; the container/browser stage remained live.
Executed preparation evidence: 6880755 passed PostgreSQL job 111234352979/run 37133896819, including actual private disk preparation/read-only byte identity and the provider-outside-command assertion, then all prior admission/publication/scan/upload contracts. Web job 111234353134 passed too; .NET host/staging case execution and later release gates remained pending.

The edge now routes both scoped and canonical download paths explicitly to the API with response buffering disabled, no proxy temporary-file capacity and bounded proxy/client idle waits. This preserves streaming aborts without a retained edge-buffer tail and prevents the canonical /attachments path from falling through to the SPA. The mandatory exact-image attachment script submits both download paths on the normal disabled release host and requires API 404, not successful SPA HTML. Local shell syntax/diff checks pass; exact-image nginx configuration/topology execution remains pending. This smoke is routing/disabled-host proof only, not live cloud download acceptance.
MUI native download review now appears only on admitted Clean FILE metadata, including read-only viewers. An explicit Check action reads the current profile, current server download options and profile again; exact Organization/Board/Card/Card revision/file identity/file revision/actor fields must match and private extras fail closed. Enabled API options use current owning-transaction Card state and download admission with no object read. The metadata page/scan state never authorizes file bytes by itself. Options are private/no-store. A reviewed link lasts one minute locally, is removed on expiry/scope/Card/file revision change, and opens the browser download in a separate tab with no opener/referrer. The original Card remains open; the page never fetches/allocates a whole file Blob or claims completed download. Native requests bind reviewed actor/file version to the server, which refuses stale/mismatched claims before provider reads and independently performs current complete download admission. These query claims cannot elevate authorization and are not bearer tokens.

The review has bounded reads, cancellation, generic failures, explicit Card refresh, late-result suppression and keyboard focus restoration while preserving focus moved by the user. File groups carry accessible names, proposed names remain escaped and wrap on mobile. Twenty-six focused local control/Card cases pass, including exact safe native URL construction, no byte prefetch, changed/private option refusals, post-options session change, expiry, cancellation/late completion, scope/revision removal and quarantine exclusion. Web typecheck/lint and production build pass; solution warnings-as-errors compilation passes. Existing bundle-size warning remains; performance targets are not claimed. The HTTP fixture additionally submits options without provider reads and mismatched actor/file revision refusal. Linux execution of these additions is pending.

Two new 1280/390px release-image browser cases are discovered, covering keyboard review/focus/axe and actual native browser download event, filename and exact received bytes, with explicitly simulated file/provider replies. Actual API metadata is asserted unchanged; discovery is not execution or live production-bucket delivery proof. Preview/cover/deletion/reconciliation and the full attachment acceptance matrix remain unfinished; no issue is closed. At 6b1ab13, web/.NET/PostgreSQL/source/build-once image/security checks passed in run 37134204248, while exact-image container/browser acceptance remained running.
Executed review evidence: ca73ae7 passed Linux PostgreSQL job 111238189773, web job 111238189867 and .NET job 111238189906 in run 37135190098, including the added HTTP options/current-actor/file-version refusal assertions. Source and exact image builds passed; security/container/browser were still separate. At 6b1ab13, container job 111236519525 passed release-topology startup, enabled-attachment refusal cases and the required URL attachment transaction step, including both new disabled download-routing 404 assertions through the exact edge/API images. Full container/browser/required-ci acceptance was not yet complete.

A focused follow-up run exposed a download-link focus timing race: the element was rendered while a passive effect had not yet focused it. Focus restoration now runs after refs attach and before paint, retaining the same assertion and user-owned focus guard. An added scenario moves focus to another action during the asynchronous review and confirms it remains there. All 27 focused control/Card cases, typecheck/lint and solution warnings-as-errors compilation pass. Native browser tests remain pending execution; previews/covers/deletion/reconciliation and operational provider proof remain outstanding.
Preview transformation now has a separate Worker-only decoder primitive. Exact SkiaSharp/SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1 are pinned in central versions and transitive locks. Official stable release/package/license were checked at https://github.com/mono/SkiaSharp/releases/tag/v4.153.1 , https://www.nuget.org/packages/SkiaSharp/4.153.1 and https://github.com/mono/SkiaSharp/blob/v4.153.1/LICENSE.md . This MIT library provides CPU raster codecs; the Linux native package is referenced by Worker and native test executables, not the API executable. Official versioned SKCodec/SKManagedStream source confirms that codec pixel decoding must require Success (rather than accepting convenience partial-pixel output), and source ownership must be preserved explicitly. No rendering service/framework or new application image is introduced.

The primitive admits only actual PNG/JPEG/WebP codec formats matching trusted inspected type, requires a readable seekable owned source within the original 1 GiB bound, checks dimensions <=32768 and <=40 million pixels before decoded bitmap allocation, normalizes all eight EXIF origins and sRGB color, preserves transparency, downsizes without enlarging to at most 1024 pixels per side and emits fresh PNG <=8 MiB. Original appended payload/metadata are not copied into a display response. Native I/O callbacks return EOF on I/O/cancellation and report fixed sanitized failures at managed boundaries; caller-owned original streams remain open. Decoded bitmap/output surface pixels are cleared after processing, and the bounded owned PNG buffer is zeroed on disposal. Hash and byte stream are excluded from serialization. This primitive does not authorize a caller, verify an original digest, mark scanning Clean or publish metadata; Worker must obtain durable current capability and verified private bytes before invoking it.

Twenty-one native decoder cases compile: real PNG/JPEG/WebP formats with output decode/hash/dimensions/ownership, declared-vs-actual type refusal, payload stripping, transparency, eight independently embedded EXIF orientations with pixel/dimension comparisons, dimension/pixel refusal, truncated pixels, output bound, native-callback I/O/cancellation, policy bounds and owned PNG cleanup. Worker-only decoder registration/API capability exclusion is asserted. Locked restore and solution warnings-as-errors compilation pass. NuGet vulnerability inventory including Worker transitives reports no known vulnerable packages in current sources; this is not a guarantee about native code, deployment or all security gates. Native Linux execution/exact-image/security gates are pending because local .NET execution remains blocked by Windows Application Control.

Durable preview jobs, private derivative objects and persistence, lease-fenced publication/recovery, current-authorized inline image delivery, covers and deletion are still required. No preview endpoint/UI is exposed yet. Native codec internal allocations/CPU are not asserted to be completely controlled by the pixel/output caps; cooperative cancellation at read/stage boundaries is not process isolation or a hard native CPU deadline. Resource containment and operational execution must be completed before activating untrusted-image generation. No issue is closed. At a002e4a, web/.NET/PostgreSQL/source/images/security passed in run 37135617558; container/browser/full required-ci remained live.
Initial decoder run 37137614417 passed web and restricted PostgreSQL checks but failed three serialization assertions: a substring check for Bytes incorrectly matched the allowed SizeBytes field. The other 491 Domain cases passed with zero skips, including the remaining native decoder cases. The corrected test compares the exact four permitted JSON property names and the size value, so both private payload and private hash fields remain excluded without denying public size metadata. Full Linux reexecution and release gates remain pending.

Release CI now requires the exact built Worker image to decode and freshly encode a fixed complete CRC-valid public red PNG, independently decode the emitted PNG and check its pixel/dimensions, source ownership and read-only result before exporting images. The explicit --verify-attachment-preview-runtime command executes before host/configuration/provider/database startup, accepts no arbitrary image/path/URL or configuration arguments, opens no HTTP listener and emits only fixed success/failure text. The verification container runs as numeric nonroot user 1654, without network, capabilities or privilege escalation, with read-only root, bounded scratch/memory/processes/CPU and a 30-second external timeout. These are fixture execution limits, not a claim that production untrusted preview jobs already run in isolation. The same verified image is exported once for later container/security/release stages; no image is rebuilt for this check. Solution warnings-as-errors compilation passes. Actual image execution is pending Linux CI; provider jobs, private derivative persistence/publication and production resource containment remain outstanding.

Preview generation now has a separate async Worker transformation interface instead of registering the raw native decoder in the long-lived Worker. The fixed Linux x64 release executable launches one child at a time, under a 30-second parent deadline, with only a fixed six-variable GC/diagnostics/allocator environment and bounded private stdin/stdout pipes. Neither provider credentials, tenant/actor identities, object keys nor client filenames/URLs enter the child. Input frames contain only trusted type, original size/digest and exact private bytes; parent and child independently require complete size/SHA/EOF agreement. Output is limited to a fixed binary frame and <=8 MiB PNG, checked for signature, IHDR dimensions and final IEND before an owned read-only result computes its actual digest. Result bytes/hash remain excluded from serialization. Failure frames are fixed codes; normal private-input stderr is classified to a fixed enum and discarded rather than logged. The explicit release verifier has a separate fixed-public-PNG factory with no image/provider/environment arguments; only that private construction path can print sanitized ASCII diagnostics retaining at most a 4096-byte prefix and 4096-byte tail. The temporary verification tracer has been removed; verification and ordinary generation launch the same fixed privilege-drop/native helper. The ordinary public constructor used by Worker registration cannot enable reporting. Cancellation/timeout/error kills and reaps the child, disposes any untransferred output and releases the slot only after successful reaping. An unreaped kernel process retains the slot and requires Worker recovery; source ownership stays with the caller.

A small native launcher is compiled with warnings-as-errors inside the existing Worker build stage and copied into that same Worker image. It uses the system util-linux privilege-drop helper; no deployable image/service/framework is added. The system privilege-drop helper drops root to uid/gid 1654 and clears groups/capabilities (or preserves an already nonroot uid), sets no-new-privileges, disables core dumps and sets parent-death kill. It installs hard process bounds before .NET exec: 1 GiB address space, 10 CPU seconds, 1 GiB file size, 256 file descriptors and 64 uid tasks. Managed heap/virtual region are separately bounded. Landlock ABI >=3 is required; unsupported kernels or enforcement errors fail closed. Filesystem restrictions are applied by the single-threaded launcher before .NET starts, so all existing/future CLR/native threads inherit them. Read access is limited to the release code/runtime/system libraries, self-process/standard runtime metrics and specific loader/random files. Writes/creation/removal/truncation are limited to a server-generated mode-0700 scratch directory. Original staging is a mode-0600 inode unlinked before reading source bytes, so a killed child cannot retain an original by pathname. Normal cleanup removes the empty private scratch directory; abnormal parent loss can leave empty scratch metadata requiring operational housekeeping, not a named original.

Inside the child, an x64-only seccomp filter synchronizes onto all CLR threads before any source frame is read. It denies socket/network operations, execution/fork/tracing/process-memory access, namespace/mount manipulation, BPF/userfaultfd and io_uring. clone permits only CLR threads; clone3 returns ENOSYS for libc's inspected-clone fallback. Each invocation checks zero capabilities and verifies refusal of socket creation, execution, an over-limit address-space reservation and reading /etc/passwd. These probes create no external traffic or giant committed buffer. The same fixed public PNG release command now traverses the actual isolated child, checks pixel/dimensions/ownership, submits mismatched digest refusal and successful subsequent recovery. Mandatory pre-export image checks cover both nonroot/no-capability parent and the normal root-parent privilege-drop path. Deployment of untrusted preview jobs still requires these Linux release checks and operational capability support; no preview job or HTTP/UI activation is introduced by the transformation.

Fifteen managed pipe-boundary cases compile, covering exact framing/byte identity/no scope keys, size/digest/type refusal, read-only owned result/hash/serialization, pre-allocation dimension/length refusal, PNG structural mismatch/trailing output, fixed-code refusal and cancellation. Worker registration selects only the isolated async generator, and API exclusion remains asserted. Warnings-as-errors solution compilation passes; new managed/Linux/native-launcher/exact-image execution is pending. The kernel/resource behavior is based on official https://docs.kernel.org/userspace-api/landlock.html , https://man7.org/linux/man-pages/man2/seccomp.2.html , https://man7.org/linux/man-pages/man2/prlimit.2.html , https://man7.org/linux/man-pages/man1/setpriv.1.html and https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector . These are enforcement mechanisms, not a claim that all native vulnerabilities are eliminated or production managed-bucket execution is proven. Durable preview artifact jobs/private persistence/fenced publication/current-authorized display, covers and deletion remain outstanding; PRD-14 stays open.

At previous head 5a4d883, web/PostgreSQL/.NET/source/exact-image native fixture/image export/security passed. Container run 37138168610 failed in the enabled-attachment startup-refusal fixture after 30 seconds, before authenticated browser checks; full required-ci therefore failed. Main 9ac39c0 removes duplicate default/override environment entries and adds fixed case/exit-status diagnostics without printing provider startup output. This removes Docker/runtime precedence ambiguity; actual corrected release execution and any further underlying startup diagnosis remain pending.
Executed isolation review: heads 08df80c (run 37141380512, managed job 111256360676) and 7e3a215 (run 37141897045, managed job 111257909081) each passed 510 Domain and 270 API tests with zero failures/skips, plus web, PostgreSQL and source gates. Native launcher compilation, image construction and exact nginx configuration passed, but the isolated public-PNG child failed before preview decoding. The fixed signal classification in exact-image job 111258818937 identifies RuntimeAbort; no successful isolated transformation, release export or full required-ci result is claimed for these heads. Startup output remains private. The diagnostic reader now retains only the first 4096 bytes, returns only a fixed enum, drains and zeros subsequent chunks under the existing deadline, and does not lose a known category merely because a fail-fast stack exceeds the retained prefix. Two added managed cases exercise oversized-report draining, prefix-only classification and cancellation; local compilation passes, their CI execution is pending.

Separately, corrected startup-refusal fixture 9ac39c0 passed all required enabled-attachment refusal cases and the URL-attachment/atomic transaction step in exact-image container job 111252591420 (run 37139663138). That job reached authenticated browser checks and remains separate from isolation acceptance. PRD-14 stays open; durable private preview publication/delivery, covers, lifecycle/deletion and operational provider proof remain outstanding.

At 8449c77, CI run 37143115248 passed 513 Domain and 270 API tests with zero failures/skips, plus web/PostgreSQL/source gates. Exact-image job 111262363260 records only the native launch progress marker, then a runtime abort before the managed environment marker; private original bytes are not read at that stage. No isolated decode/export/full-CI success is claimed. The fixed environment now additionally sets MALLOC_ARENA_MAX=1 and checks both exact keys and exact values before source admission. This limits glibc arena reservations independently of host CPU count while keeping the 1 GiB address-space cap and all other kernel restrictions unchanged; it is a constrained startup repair trial awaiting actual image evidence. Official allocator semantics: https://sourceware.org/glibc/manual/latest/html_node/Memory-Allocation-Tunables.html . Four new cases compile for inherited provider credential removal and changed/missing/unknown policy values; their execution is pending.

Public-fixture startup diagnosis: f1a806c exact-image job 111266010613 in run 37144322680 identifies an unhandled framework-assembly load failure for System.Security.Cryptography before managed admission; its 517 Domain and 270 API tests passed. The temporary fixed-public-fixture trace in a1a20fe job 111268464330 (run 37145261762) records EMFILE while opening System.Reflection.Metadata.dll under the 64-descriptor hard cap. Optional lttng/ICU probing ENOENT messages are not interpreted as missing required providers. Both native and managed descriptor limits are now 256, allowing CLR/framework handles while remaining finite. The 1 GiB address-space cap, managed heap/region bounds, CPU/task/file-size caps, filesystem, identity and synchronized syscall restrictions remain unchanged. The tracer package and selection branch are removed before validating the ordinary release profile again. Local warnings-as-errors compilation passes; successful isolated decoding/export/full required-ci is still unproven pending the new image run.

Root-parent fixture correction: run 37145867224 / exact-image job 111270610325 executes the nonroot isolated public PNG fixture successfully, including integrity refusal and subsequent recovery. The root fixture omitted CAP_CHOWN, which the parent needs to assign its freshly created private scratch directory to the decoder uid. Its minimal parent capability set now includes CHOWN alongside SETUID, SETGID and SETPCAP; setpriv still clears every child capability before the native launcher. Ownership refusal reports the fixed Scratch stage, and a separate mandatory exact-image negative fixture requires refusal when CHOWN is absent. No DAC bypass or child capability is added. Linux documents this ownership capability at https://man7.org/linux/man-pages/man7/capabilities.7.html. Positive root execution and full required-ci remain pending the corrected run.

The root parent also requires CAP_KILL to terminate its uid-1654 child on timeout/cancellation; root uid alone does not bypass capability checks. The exact-image positive fixtures now hold an internally owned public-fixture source read, cancel generation, require cancellation propagation and then generate successfully through the same singleton slot. The minimal positive root fixture adds KILL only to the parent; the decoder continues to require zero effective/permitted/inheritable/ambient capabilities. This is actual subprocess cleanup/recovery verification, separate from cooperative protocol unit tests. Execution remains pending Linux CI.

Root ownership verification result: d0e14b8 run 37146425087 / exact-image job 111272121952 passes the nonroot/no-capability public fixture, the root-to-nonroot privilege-drop fixture and the missing-CHOWN refusal fixture. Source Web/.NET/PostgreSQL jobs also pass. This proves the corrected isolated PNG/integrity recovery profile in both parent identities; the added subprocess cancellation/recovery check is a subsequent change and has not yet executed. Full container/browser/security/required-ci are not claimed complete.

Cancellation verification result: 6519e0f run 37146887410 / exact-image job 111273446962 passes both nonroot and root-parent public PNG fixtures with integrity refusal, a cancelled held source read, cancellation propagation and subsequent generation through the same generator slot. The missing-CHOWN refusal and export of the original built images also pass. Web/.NET/PostgreSQL source jobs pass. This establishes the tested cancellation/cleanup/recovery path; it does not claim arbitrary native fault injection or completed private preview job publication. Full downstream container/browser/required-ci remain pending.

Browser regression repair: completed run 37139663138 reports 96 passing and 10 failing browser cases, including both native-download client widths and the desktop upload client. The installed MUI Dialog marks its fallback paper with data-mui-focusable; owned focus recovery now recognizes only that same dialog fallback, the owner or document body, while preserving another control/dialog selected by the user. A real MUI Dialog component test covers delayed download admission and fallback recovery. File client browser fixtures now drain their disposable Organization's real parent events and await the initial/reconnect Board reads before operating simulated provider responses; their final assertions still require no persisted file metadata. Local full web tests pass: 959 tests in 73 files, with typecheck, zero-warning lint and production build also passing. Playwright discovery validates 9 updated cases across 5 specs, not their execution. Exact-image browser execution of these repairs and full required-ci remain pending.


Migration 045 adds the private preview intent ledger, with forced tenant RLS and composite source/job tenant foreign keys. Its queue references contain only attachment ID, Card ID and source revision; an immutable job trigger refuses redirection and conversion from unrelated jobs. Worker-only load/declaration functions prove the current exact actor/worker/lease/job claim, canonical Published upload integrity, Clean image status, current source revision and active Card/List/Board before disclosing source or recovery measurements. Declaration records bounded output size/SHA/dimensions before provider writes, accepts an identical replay and refuses a different encoding. A final lease fence rolls back tentative declarations. Neither API nor Worker has direct preview-ledger grants; API cannot execute either Worker capability. The new application contract and PostgreSQL adapter compile with warnings as errors. Restricted C# persistence contracts cover forged claims, malformed job identities, direct-role refusals, actual forced RLS, late lease loss rollback, immutable output/replay conflict and current lifecycle withdrawal. Linux execution is pending CI. The ledger records a declaration, not proof that storage contains an artifact or a public preview grant. No automatic preview enqueue or handler activation is included until private provider reconciliation and fenced publication are implemented; PRD-14 remains open.


At 2fe60b0, Linux PostgreSQL job 111276437181 in CI 37148221437 passed the actual restricted preview intent contract, including canonical Clean admission, private recovery measurements, immutable queue/manifest, late lease insertion rollback, current lifecycle refusal and actual forced tenant RLS. Clean/repeat/forward/serialized migration tests and runtime grant refusals also passed; source .NET/web/PostgreSQL gates are green. Release-image/browser/security/required-ci completion for that exact head remains pending.

The storage recovery coordinator now stages the full canonical original through the existing anonymous Linux integrity preparer, calls the already isolated generator, records exact bounded encoded-output measurements, and re-admits the current capability before private provider writes. Artifact identity is the server-generated preview job UUID in the existing private object namespace, distinct from the original attachment UUID. An existing artifact is read completely and verified against the immutable declaration before reuse. Missing artifacts can be regenerated only if the encoding exactly matches the declaration. Writes remain conditional and non-clobbering; returned provider receipts alone are insufficient, so stored bytes are independently read and verified before returning private evidence. Unknown or corrupt outcomes retain the declaration/artifact; recovery never deletes or overwrites them. Caller cancellation and a 55-second deadline bound the operation; all source/result staging is disposed. The returned evidence excludes private identity/measurements from JSON and does not publish a preview or grant display access. Linux recovery cases use the actual private integrity preparer with fixture storage and a fixture generator; they test unknown writes before/after commit, corrupt retained artifacts, changed encoding, lifecycle withdrawal after declaration, invalid receipts, cancellation and owned disposal. Their Linux execution is pending the next exact-head CI. No automatic enqueue, handler/DI activation, public preview route or cover control is introduced at this stage.


The recovery test matrix additionally checks corrupt original bytes (no decoder/declaration/write), corrupt stored bytes despite an otherwise valid provider receipt (no returned evidence, overwrite or cleanup), and current-scope withdrawal after a committed provider write (private object/declaration retained without returning evidence). Test compilation passes with warnings as errors. The Linux Domain test step for the preceding 9875514 recovery commit has passed; the full run and these additional cases await exact-head CI completion.


At 1e147d3, exact-head Linux .NET CI 37148605519 passed 547 Domain and 270 API tests with zero failures/skips, including the actual anonymous integrity preparer's preview recovery cases. PostgreSQL/web/source gates also passed. Full release-image/container/browser/security/required-ci completion remains pending.

Migration 046 introduces a separate forced-RLS, immutable publication receipt referencing the manifest, same-tenant work event, audit ID and current Board. Receipt insertion checks the declared source revision, exact publication/revision/timestamp and matching actor/Card/attachment audit/event identity. Worker-only final publication revalidates the exact live job claim, current Clean source and persisted output measurement under Organization/Board/Card/List/job/file/manifest gates, advances File/Card revisions, allocates the current Board sequence, writes a ready event/audit/receipt together, and checks the live lease again after all tentative effects. Any final lease loss rolls back all those effects while preserving the prior private recovery declaration. Committed replay returns Applied with no source or output claims. The previous source loader becomes a private helper; migration and provisioning explicitly revoke its old Worker grant, including during forward upgrades. API cannot finish publication or invoke the helper, and neither runtime has direct receipt-table grants.

The delivery handler now composes live admission, private storage recovery and fenced publication, and compiled tests cover replay without provider work and final publication refusal. Restricted PostgreSQL contracts test forged claims/output, current parent withdrawal, late lease loss after the receipt insertion, rollback of revisions/sequence/audit/event/receipt, immutable receipts and replay. The successful fixture then moves the stable Card to another same-tenant Board and executes the real handler, real anonymous integrity preparer, PostgreSQL store and fixture storage/transformation; it requires verified source/output reads, publication on the new Board with no old-Board sequence effect, and replay without provider I/O. This uses a fixed fixture transformation; it does not replace the separately mandatory actual isolated decoder test in the exact Worker image. Solution compilation passes with zero warnings/errors. Actual new Linux publication tests/release execution are pending. Automatic Clean-to-preview enqueue and handler/DI activation remain the next dependency, followed by current-authorized display/covers/lifecycle; PRD-14 stays open.


The first 046 CI (37149135632, main 3416cd7) exposed a pre-existing runtime-role provisioning defect before the new publication contract ran: the upgrade fixture created an existing Worker role without LOGIN, and provisioning only supplied LOGIN when creating a missing role. Both existing runtime roles are now explicitly configured with LOGIN alongside the same NOSUPERUSER/NOBYPASSRLS/NOCREATEDB/NOCREATEROLE/NOREPLICATION/NOINHERIT settings and required distinct passwords. The real-role test deliberately starts both roles as NOLOGIN and requires provisioning to recover actual restricted connections. The old-loader grant-removal fixture remains intact. Shell syntax checks pass; new PostgreSQL execution is pending CI. No publication acceptance claim is made from the failed run.


Preview artifacts now use the fixed private attachment-previews/{Organization:N}/{Job:N} namespace through a server-owned reference factory, distinct from original attachments even when UUID values match. Original references preserve their existing object keys and constructor. The local private provider honors the same namespace distinction; S3 uses the generated fixed key. Recovery/delivery/publication require preview artifact references and refuse preview references as original source claims; scan-job publication likewise refuses a preview-namespace source. No client-supplied key/prefix is accepted. New local/provider tests use the same UUID for an original and preview, require independent reads across adapter restart, cross-tenant refusal and preview cleanup without deleting the original. Real SDK signed-transport tests now exercise both fixed namespaces, conditional private multipart writes and embedded completion-error refusal. These compile with zero warnings/errors; actual new Linux namespace/publication execution is pending CI. Automatic preview enqueue/DI activation remains disabled until the full delivery dependency is proven.


Migration 047 activates new-upload previews through the existing Worker/Organization queue. The legacy eleven-argument scan-finish function remains compatible and does not enqueue new types; an explicitly opted-in twelve-argument Worker overload applies the canonical scan transaction, queues only active/current Clean PNG/JPEG/WebP originals at their exact resulting revision, verifies deduplicated queue identity and fences the still-live scan claim again after insertion. Lease loss at that point rolls back scan verdict/File/Card/audit/event/sequence and preview job together. Rejected, terminal Failed and supported non-image originals do not enqueue previews. Existing already-Clean originals are not backfilled by this migration; bounded recovery/backfill remains outstanding.

The claim function preserves SECURITY INVOKER/RLS and skips preview claims and terminal expiry unless the transaction-local preview Worker setting is enabled. Old binaries call the unchanged claim SQL without that setting and leave the new type untouched. New default adapters explicitly set disabled; only the enabled Production attachment Worker registers a capable queue adapter, preview intent/publication store, real anonymous integrity preparer, existing isolated generator, recovery coordinator and handler. API/Demo/disabled registrations do not gain these Worker capabilities. The real-role fixture checks Worker-only access to the new scan-finish overload.

Compiled restricted contracts force scan lease loss after preview queue insertion and require all scan/queue effects to roll back; then require one canonical preview job, deduplicated scan replay, old SQL/default claim and expiry exclusion, capable recovery of final-attempt expiry, actual staged handler publication/replay, and PDF/rejected/five-attempt Failed refusal. Fixed fixture storage/scanning/transformation is used around the real PostgreSQL stores and real anonymous integrity preparer; it does not substitute for the separate mandatory actual native decoder/containment checks in the exact Worker image. Worker registration/API exclusion tests were updated. Solution compilation passes with zero warnings/errors and shell syntax checks pass; actual new Linux activation/release execution is pending CI. Current-authorized preview delivery/UI/covers, existing-Clean backfill/recovery and deletion/lifecycle acceptance remain outstanding; PRD-14 stays open.

Migration 048 adds read-only API access to the forced-RLS preview manifest and publication receipt; direct mutations and all Worker/helper capabilities remain denied. The owning authorized Card read joins a committed receipt to the exact current Clean original size/SHA/MIME and canonical source key, derives the fixed private preview namespace internally, and permits later File metadata revisions only when immutable source integrity still matches. Original-file admission now explicitly excludes preview references.

Controlled preview endpoints expose only fully staged, size/SHA/EOF-verified published PNG derivatives with fixed inline presentation, private/no-store, nosniff, sandbox/no-referrer and no ranges/cache validators. They re-admit the current actor, parent scope, exact File snapshot, committed preview binding and one-minute lifetime after staging, before headers, periodically and before final bytes. Provider I/O remains outside database transactions. Canonical preview routes require current Card context, and Nginx streams both scoped/canonical preview responses without response buffering or temporary disk files. Disabled attachment deployments omit preview routes.

The MUI Card attachment panel offers an explicit image-preview review for Clean PNG/JPEG/WebP files, checks current actor before and after strictly scoped publication options, and uses a native bounded image request bound to actor/File revision. It removes the image on scope/revision/access changes, hide, error or review expiry, provides stop/retry/refresh controls and preserves owned keyboard focus. PDFs and unsafe scan states have no image control. Component cases cover these paths; desktop/mobile exact-image browser scenarios use explicitly simulated publication/provider replies and require native decode, keyboard focus and axe accessibility. This browser fixture is separate from real private HTTP and restricted PostgreSQL read contracts.

The preceding activation commit 5e7032e passed all 558 Domain/270 API tests, web/source gates, real PostgreSQL restricted activation/rollback/legacy-claim/provider staging checks and mandatory native image builds. Its downstream container/browser/security/required-ci status remains pending at this checkpoint. The new preview-read solution compiles with zero warnings/errors; focused new UI cases pass (17), with typecheck/lint/build and shell syntax passing. New Linux HTTP/SQL, full web and exact-image browser execution must be inspected before acceptance. Existing-Clean backfill/recovery, covers and attachment lifecycle/deletion remain outstanding; PRD-14 is open.

Migration 049 adds bounded recovery for older Clean PNG/JPEG/WebP uploads. A private forced-RLS per-Organization cursor scans at most 32 rows from an ordered partial index before evaluating eligibility. It advances past invalid, inactive, already published and failed sources and wraps at the end so sources that later become eligible are revisited. Replicas skip an owned cursor and locked parent/File gates. Only the enabled Production attachment Worker, within its explicit configured Organization scope, runs one page per five-second round-robin tick. The adapter bounds connection/transaction work to two seconds, SQL execution to 1500 ms and lock waits to 250 ms. No object reads, decoding, writes or deletes occur during the sweep.

The Worker-only producer derives canonical retry identity from the current Clean original revision and Published upload intent, gates current Organization/Board/Card/List/File state and source key/size/SHA/MIME, and retains existing jobs in every state. It never resets Failed attempts or replaces unknown provider bytes/manifests. A committed receipt for the same immutable source suppresses redundant work even after later File metadata revisions. Queue publication does not change File/Card revisions or audit/realtime events; those remain atomic effects of the existing leased publication handler.

Restricted contracts now create 36 real legacy Clean uploads through upload publication and the legacy scan-finish path, test multiple bounded pages, replica skip-lock, foreign-context/API refusal, private cursor denial, receipt reuse, parent archival and restoration on wrap, cancelled work and byte-for-byte terminal job preservation. Registration tests require Worker capability and API exclusion; migration-upgrade, runtime grant and schema-refusal checks include 049. These tests compile locally; actual new Linux PostgreSQL/runtime execution remains pending CI. The preceding main 4968355 passed web, .NET and PostgreSQL/source gates; its immutable image/container/release checks remain pending. PRD-14 remains open for full release proof, terminal scan recovery, covers and attachment lifecycle/deletion acceptance.
The first 049 run (37152774402) reached the restricted recovery contract and correctly rejected its attempted fixture mutation of an existing storage key via the immutable object-identity trigger. The fixture now exercises real parent archival/restoration across the durable cursor instead; it never changes immutable source identity or weakens the guard. This preserves source admission and tests late eligibility after cursor advancement. Actual repaired execution remains pending CI.
Migration 050 completes Pending quarantine metadata after an exhausted scan job reaches Failed or a final Running lease expires before verdict publication. A separate private forced-RLS cursor scans finite indexed pages before eligibility checks, skips owned cursors/parent/job/File locks, advances past ineligible rows and wraps to revisit expiry. The Worker-only function requires matching tenant context, exact canonical immutable scan references/retry identity, an exhausted terminal or expired final claim, the current original Pending File revision/uploader/key/size/SHA/MIME and its Published upload intent. It accepts Active/Archived parent gates but refuses deleted parents/sources, live or non-final claims, stale File revisions and already terminal verdicts. No original or preview provider bytes are opened, decoded, deleted or overwritten, and no terminal job attempts are reset.

Recovery follows Organization/current Board/Card/List/job/File lock order. It retains an already Failed job row or terminalizes an expired final lease, then publishes Failed File metadata, current Card revision, current Board sequence, ready scan-completed event and audit in one transaction. A final exact terminal-job snapshot fence after all effects rolls the entire page and cursor back if evidence changes. The enabled Production attachment Worker runs one bounded page per explicitly configured Organization round-robin tick with the same two-second application/1500 ms SQL/250 ms lock deadlines as preview maintenance. API/Demo/disabled registration does not gain this capability. Clean verdicts committed before lost acknowledgements are never downgraded.

The restricted contract creates more than one page of real live publication/claim transactions before exhausted sources, proves direct expired-final and ordinary queue-terminalized recovery, owned-cursor skip-lock, foreign/API/cursor denial, stale/non-final/live/deleted refusal, retained Clean results, stable Card movement to a new Board, exact audit/ready events, old lease refusal and replay without duplicate revisions. It injects a change to terminal evidence after event insertion and requires rollback of File/Card/job/sequence/audit/event/cursor, then safe recovery after removing the fault. Provider-read counts must remain unchanged. Solution compilation passes with zero warnings/errors and edited shell syntax checks pass; actual new Linux execution is pending CI. The preceding bbf7df3 passed web/.NET/PostgreSQL/source and immutable image builds; downstream container/security/release results remain pending. PRD-14 stays open for full release/browser proof, covers and attachment lifecycle/deletion.

Exact-head CI 37153480540 at 80dd049 passed actual Linux .NET, PostgreSQL,
web/source, immutable image building, security and CodeQL checks. The restricted
050 scan-recovery contract executed successfully, including the late terminal
snapshot fence and provider-read refusal. Container/browser/release completion
is still pending. Follow-on 2064019 browser repairs passed 84 focused local web
cases plus typecheck, lint and production build; its Linux PostgreSQL and web
gates have passed, with remaining exact-image checks pending. Covers and lifecycle
decisions and the first domain implementation are tracked in
[attachment-covers-lifecycle.md](attachment-covers-lifecycle.md).

Pending uploads across Board movement retain their original Card revision.
The API-host contract now covers both Prepared and Stored intents, followed by
a real authorized cross-Board move and an HTTP retry of the original upload.
That retry must return version_conflict without provider reads/writes, metadata
publication, intent rebasing or another Card revision. Current destination
options supply the new revision; a separately reviewed upload with a new key
can publish there. Withdrawal of destination membership refuses the old retry
before provider I/O. Stored measurements are an explicit server-owned fixture;
this host test does not claim PostgreSQL or real object-provider execution.
The solution compiles with zero warnings/errors; Linux execution is pending CI.

Published file upload receipts now retain their original Board and Card revision
after cross-Board movement. Replay plans original/current Board command gates in
canonical order, requires current edit eligibility and active lifecycle in both,
and rechecks current Card/List routing after waiting. It returns the original
receipt without another upload, scan job, audit, event or Card revision. The
API-host transport contract covers movement, destination withdrawal/restoration,
and original source withdrawal while destination options remain accessible.
The restricted PostgreSQL publication contract covers immutable recovery and
original/current Board archival independently, with unchanged publication effect
counts. Its routing transitions are explicit administrator fixtures; HTTP actor
admission remains the API-host contract. Compilation passes with zero warnings
and errors; actual new Linux API/PostgreSQL execution remains pending CI.

URL creation receipts use the same original/current Board gate plan after Card
movement. Replay binds original Card revision, actor, attachment kind and current
metadata identity, and returns the unchanged acknowledgment. API-host coverage
checks a real cross-Board move, exact recovery without a Card revision or duplicate
metadata, destination withdrawal/restoration and original source withdrawal while
destination metadata remains accessible. New Linux execution is pending CI.

Archive, restore and confirmed-delete receipts now survive authorized Card
movement using the same original/current gate plan. Delete replay additionally
requires administrator access on both Boards; continued edit access after
administrator demotion cannot disclose the old deletion acknowledgment. Existing
current-child lifecycle matching, deletion attribution and parent lifecycle
checks remain required. Three API-host contracts cover actual movement, exact
receipt recovery, destination withdrawal/restoration and source withdrawal,
with unchanged Card and child metadata. Compilation passes with zero warnings
and errors; actual new Linux execution is pending CI.

The mandatory exact-image attachment fixture now invokes
`test-moved-attachment-receipts.sh`. It creates real private Boards, Lists, Cards
and URL/lifecycle commands through Nginx and the restricted API, moves each
stable Card, then checks the exact original receipt for URL creation, archive,
restore and confirmed delete. It withdraws/restores original and destination
authority separately; delete uses administrator demotion while retaining edit
membership. Every retry compares full Card/child and tenant command effect
digests, excluding only Worker readiness timestamps; legitimate membership
command effects establish a fresh baseline before each comparison. No fixture
publication or routing mutation substitutes for these commands. Shell syntax
checks pass; actual release-image execution remains pending CI.

The supported-capacity release fixture now runs 20 actual URL creations, 20
archives and 20 confirmed deletions on the existing 200-List/5,000-active-Card
Board with 100,000 archived Cards in the Organization. It requires independent
attachment IDs, exactly 60 Card revisions/audits/events/receipts, retained deleted
tombstones and an unchanged original deletion retry after the later commands.
The retained `attachment-capacity` artifact contains all timing samples and
nearest-rank p95 for each mutation, requiring each below 500 ms. Conditions are
one serial client through Nginx with no intentional network delay. It measures
URL metadata/lifecycle commands; binary provider, scanner, preview, concurrent
clients and browser timing are not measured by this fixture. Shell syntax and
diff checks pass; actual execution/performance evidence remains pending CI.

Two native browser scenarios at 1280 and 390 px now cover moved attachment
identity and current destination lifecycle through the real scoped Worker.
Independent authorized contexts open source/destination Boards before a real
HTTP Card move, then require destination appearance and source disappearance
without manual reload. Keyboard interaction opens the moved Card, verifies its
unchanged URL attachment and archives that stable child on the destination;
current metadata and an accessibility scan are checked. No publication or
delivery replies are simulated. Discovery registers both scenarios; actual
release-browser execution remains pending CI. Binary pipeline proof is separate.

Attachment management rechecks the current account after metadata reads and
mutation replies, before disclosing private review or success. An account change
or failed post-response account check refuses disclosure; terminal access refusal
clears private review/draft and retires the original retry. Tests cover account
changes during review and after a committed reply, plus the existing pre-write
actor switch refusal. All 12 focused component tests, typecheck and lint pass.

URL creation also rechecks the account after mutation replies before showing
success. Account changes or failed post-response checks retire the original
retry and clear private title/URL drafts; terminal access denial does the same.
Validation/version refusals preserve drafts for fresh review. Nine focused
tests pass, including account change after a committed reply and pre-write
actor refusal. Typecheck and lint pass; actual Linux/release execution is pending.

The moved-receipt release fixture now observes each real retry waiting at its
first canonical Board gate before withdrawing original-source authority. A
trusted membership update removes edit membership (URL/archive/restore) or
demotes administrator to Member (delete); no receipt or domain state is edited.
After release, the request must return 404 without attachment disclosure or
command effects. A real Owner restoration command then recovers the unchanged
original acknowledgment. This tests fresh permission admission after lock waits
for all four receipt types. Shell syntax/diff checks pass; execution is pending.

Cover command receipts now recover after Card movement through original/current
Board admission, retaining original Card/source revisions and selected-cover
matching. The API-host contract covers a no-op empty selection through actual
HTTP movement and independent source/destination permission withdrawal. The
restricted PostgreSQL contract uses a genuinely published image, trusted routing
fixtures, exact original selection receipt recovery, independent Board archival
and unchanged Card/File/command effects, then restores the fixture route. It
does not claim HTTP actor/movement proof for the published image. Full solution
compilation passes with zero warnings/errors; actual Linux execution is pending.

File upload acknowledgement disclosure is now fenced by a post-response current
account check. Changed/unavailable account admission or terminal access denial
purges the retained File reference, private name and original retry intent.
Validation/version refusals keep the selected file for fresh review; ambiguous
provider replies retain the original actor/key/digest/bytes for admitted retry.
All 18 focused upload component tests pass, including post-publication account
change and both existing pre-write actor checks; typecheck and lint pass.
Actual Linux/release execution is pending CI.

The first moved-cover PostgreSQL run (37202861596 / 3857b36) failed because its
actor was an Organization/Board Member and the newly seeded destination omitted
that actor's Board grant. The canonical command correctly denied recovery. The
fixture now adds a destination Member grant and explicitly verifies editable
current destination access to the selected published image before replay.
No production authorization check is weakened. Compilation passes with zero
warnings/errors. The repaired restricted PostgreSQL integration job passes at
7750334 in run 37203161282; the complete release gate remains pending.

Exact-image run 37201694235 at 48555e4 passed attachment step 41, including
moved URL creation/archive/restore/delete original receipt recovery, independent
original/current Board permission withdrawal and restoration, and unchanged
command effects. This commit predates the live-lock-wait extension, so it does
not establish that extension's execution or full binary-provider acceptance.

The cover editor now clears private candidate names, selection review and retry
intent after account change, unavailable post-response account admission, or
terminal access refusal. It offers keyboard-focused fresh Card admission without
retaining a private selection. Lost mutation replies still retain exact retry
intent; revision/validation refusals preserve explicit fresh review. All 13 cover
component tests pass, including three post-write admission/refusal cases.

The moved published-cover persistence contract now observes the exact restricted
command blocked by a real canonical Board row lock using the blocker backend PID.
It independently withdraws original and destination Board membership during that
wait, requires refusal without receipt disclosure or Card/File/command effects,
then restores membership and recovers the unchanged original acknowledgment.
Membership and routing setup remain explicit trusted fixtures; published-image
metadata and the command use genuine restricted adapters. Linux execution is
pending; compilation alone does not prove this race.
