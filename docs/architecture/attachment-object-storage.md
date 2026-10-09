# Attachment object storage decision and provider contract

Decision: use managed AWS S3 as the initial production object-storage adapter.
ARCH-07 explicitly adopts managed production object storage behind Application
interfaces, permitting MinIO/filesystem locally. This selects the previously open
initial provider within that adopted topology; it adds no database, broker or
deployable application service. `IAttachmentObjectStorage` and Domain remain
vendor-independent. The explicit local filesystem adapter remains available for
local/tests, with no production fallback. The decision implements ARCH-07-FR-001,
FR-010/FR-012 and PRD-14-FR-004, subject to the remaining integration below.

The Infrastructure adapter uses official `AWSSDK.S3` 4.0.104.1 (and its locked
`AWSSDK.Core` dependency). Credentials must come from runtime workload identity or
runtime secrets when the client is registered; no credentials enter settings,
images, metadata or logs. Regional HTTPS AWS endpoints are required. Custom
endpoints, HTTP, access-point ARN buckets and ambiguous dotted bucket names are
not supported by this adapter; MinIO requires a separately configured local
adapter. Expected bucket owner is a required 12-digit account ID and is included
in every request. This implementation never creates or changes a bucket.

Before each byte operation, the adapter requires all four bucket public-access
blocks, an explicit non-public bucket policy status and exactly one
BucketOwnerEnforced ownership rule. Missing/inaccessible/unsafe configuration
fails closed with fixed `object_storage_unavailable`, before upload/download/
deletion. The runtime identity needs the read permissions for these checks.
The operator must provision a private general-purpose bucket, an explicit private
policy, least-privilege prefix access and abort-incomplete-multipart lifecycle
rules. Administrative bucket settings are a trusted operator boundary: a
configuration check is not a defense against an administrator making the bucket
public while an operation is already running.

Keys derive only from server-owned Organization/attachment UUIDs. Multipart
upload uses one 5 MiB buffer, reads at most one byte beyond the server's positive
limit (maximum 1 GiB), measures actual bytes and SHA-256 and accepts non-seeking
sources. Parts are uploaded privately with server-owned application/octet-stream
and SSE-S3 AES256; no filename, client MIME, ACL or public URL is supplied.
Completion includes expected byte count and `If-None-Match: *`, so the managed
provider refuses replacement of an existing identity. Part ordering/ETags are
collected from SDK acknowledgements. Caller owns the source stream. Private reads
wrap the SDK response so closing the returned forward-only stream retires the
response/transport as well as its bytes. Missing keys are distinguished only
inside the already-admitted application workflow.

AWS documents [conditional multipart completion](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TCompleteMultipartUploadRequest.html),
[initiation and encryption](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/TInitiateMultipartUploadRequest.html)
and the [official SDK installation boundary](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/net-dg-install-assemblies.html).
The SDK handles embedded errors in HTTP-200 completion responses. Transient/
conditional-conflict failures use fixed unavailable outcomes; a definitive 412
refuses an existing object. Application original-intent/idempotency remains
required above storage and must not interpret a provider timeout as no write.

Failed/empty/oversize/cancelled uploads attempt a separately bounded 10-second
abort of their multipart ID. Aborting parts never compensates by deleting the
final key: completion may have succeeded before an acknowledgement timeout.
Cleanup outage/crash can retain unpublished parts; provider lifecycle and durable
orphan reconciliation are required. Deletion removes normal current-key access;
in a versioned bucket, provider retention/version cleanup is separate lifecycle
work. This primitive does not authorize a user, publish a clean verdict or clear
a cover. Requests need bounded caller cancellation/provider timeouts when wired
into the upload and Worker workflows.

Nineteen adapter cases compile against
the real vendor SDK types using a substituted SDK client. They cover multi-part
non-seeking byte/digest measurement, expected owner/scope on all calls, source/
response ownership, duplicate identity, commit-with-lost-reply preservation,
empty/oversize rejection, all four public-access flags, public policy, ownership,
privacy outage, source/part failure, cancellation and invalid limits/configuration.
These are adapter contract tests, not executed real-bucket or transport proof.
Locked restore, warning-as-error build and dependency vulnerability audit pass;
Linux .NET-quality job 111179398305 in run 37114789590 at db6bb53 passed all
334 Domain/Application/Infrastructure and 265 API cases, zero skips, including
these 19 adapter cases. Web/PostgreSQL passed; image/security/container/browser
gates are tracked separately.

Two additional transport cases use the actual official SDK's signing,
serialization and response pipeline, substituting only its final HttpClient
handler. They require HTTPS regional bucket addressing, signed requests, expected
owner on all calls, private bucket checks, server encryption/MIME with no ACL,
generated tenant key, part bytes, ordered completion XML and If-None-Match. They
also require an embedded error in an HTTP-200 completion response to remain a
fixed unavailable failure with part-only cleanup. Fixture credentials and private
response bytes remain within the intercepted in-memory transport; no network or
cloud account is used. The two cases compile but execution is pending Linux CI;
they do not prove real bucket/IAM/credential/readiness acceptance.

The initial transport execution at b410687 (.NET job 111179958841, run
37114989667) failed both cases at the typed HttpClient Authorization property
before upload. The test now checks raw Authorization header presence and exact
SigV4 scheme, without depending on typed parsing. This retains the signing
requirement; provider implementation is unchanged. Execution of this correction
remains pending, so signed transport/embedded-error proof is not yet accepted.

Executed correction evidence at 92dba62: .NET-quality job 111180631021 in run
37115220262 passed 336 Domain/Application/Infrastructure and 265 API cases,
zero skips, including both official-SDK intercepted transport cases. Web and
PostgreSQL also passed. This proves the isolated signing/serialization and
embedded-error contract; it does not prove a deployed managed bucket or full
required-ci, whose immutable image/container/browser stages are separate.

Remaining integration: runtime provider/client registration and readiness,
credential/region/prefix policy diagnostics, managed-bucket transport acceptance,
file upload authorization/admission/type inspection/idempotency and compensation,
durable metadata/digest/scan-job publication, scanner provider, Worker lease/CAS/
outbox, controlled delivery, preview/cover, tombstone/retention reconciliation and
the complete security/browser/accessibility/performance matrix. No binary upload
or download endpoint is enabled by this adapter alone. No ticket is closed.

### Local ClamAV transport foundation

`ClamAvAttachmentMalwareScanner` implements the official [ClamD INSTREAM
protocol](https://docs.clamav.net/manual/Usage/ClamdProtocol.html) over an
operator-owned Unix domain socket. The production Worker image runs Linux.
There is no plaintext TCP fallback: ClamD TCP provides neither authentication
nor encryption. Operators must mount the private scanner socket into the Worker
with permission restricted to its runtime identity and provision/update the
external scanner independently; the three application images are unchanged.

The adapter streams 64 KiB chunks with four-byte network-order lengths and a
zero-length terminator, bounds bytes to 1 GiB, and accepts only a bounded
NUL-terminated printable response. Exact `stream: OK` is Clean; a nonempty
`stream: … FOUND` is Infected. Errors, missing sockets, oversized/malformed
responses and the configured 100 ms–30 s total deadline are Unavailable.
Caller cancellation propagates. It does not dispose the caller's stream,
retain/log signature text, pass names/paths to the daemon, or publish a verdict.
The existing quarantine coordinator still proves full-stream size/digest before
the lease-fenced database adapter can commit metadata, audit and outbox effects.

Fourteen tests exercise real Unix socket framing, complete multi-chunk bytes,
fragmented responses, clean/infected/error/EOF/malformed/oversized responses,
deadline, cancellation, missing sockets and invalid paths. Warning-as-error
compilation passes locally; execution is pending Linux CI. These fixtures emulate
the daemon protocol; they do not prove deployed ClamAV, fresh signatures, scanner
limits, mounted socket permissions or production provider readiness. Runtime
registration and deployment acceptance remain open.


## Source tombstones and independent Board image ownership

Board background identities are independent, while their immutable published
preview bytes can have multiple Board owners within the same Organization.
`BoardBackgroundImage.Preview` retains the existing published preview reference;
copying creates another Board image identity without implying exclusive byte
ownership. A source attachment tombstone therefore does not authorize deletion
of that shared preview object. Provider reconciliation must account for every
surviving owner before releasing bytes; backup/version-retention authority remains
separate from immediate irreversible product deletion.

The MUI attachment deletion review explains, before consent, that Board
backgrounds using an image keep their copies. This clarification applies to PNG,
JPEG and WebP attachments; URL and PDF reviews retain the ordinary irreversible
deletion warning. The explicit unchecked consent and scoped, versioned deletion
command are unchanged. All 16 management-control cases pass, including review of
each supported file type with no write before consent; web typecheck, focused lint
and a fresh production web build pass. This web build is newer than the previously
recorded complete strict Board browser invocation, which must not be presented as
execution of this changed UI.

The complete Linux Board-image API source case now permanently deletes its
already archived source attachment after copying the Board. Original download
options, download and preview all return 404 without another provider read or
attachment delivery headers. Restoration with the current tombstone/Card versions
is refused. Both original and copied Board backgrounds still deliver the expected
sanitized PNG; the existing archive, corrupt-byte, public/Private withdrawal and
background retirement checks continue afterward. It passes in 7.7 seconds with
zero errors/failures/skips/not-run, using a fresh locked Release build with zero
warnings/errors. The owned no-network Linux source-test container is removed.

Storage and publication metadata are synthetic Demo fixtures; real session,
lifecycle commands, private Linux byte staging and HTTP checks execute. This is
source evidence, not physical object erasure, backup expiry, real external
storage/scanner or current immutable/full release acceptance.

## Executed enabled pipeline with published source deletion

A subsequent owned native fixture builds the current API and Worker images from
their actual Dockerfiles with locked restores, uses restricted PostgreSQL roles
at schema 114, and serves the fresh deletion-consent web build. Production runtime
and verified-email requirements remain enabled in both hosts. Only the disposable
registered account is explicitly verified by the administrator fixture, after
asserting the normal unverified-login refusal; this is not email-provider proof.

Actual raw upload reaches Pending, exchanges the Unix-socket scanner protocol,
runs the native contained image decoder in the separate Worker and publishes the
sanitized PNG. The complete unchanged two-viewport background-pipeline browser
invocation passes 2/2 in 84.7 seconds, with zero failures/skips/retries/flaky cases.
It exercises public consent refusal, original publication recovery, native image
rendering, independent Board copy, source retirement, live session withdrawal,
fresh login and accessibility assertions.

The subsequent native HTTP equivalent exercises concurrent selection with one
commit/one stale refusal, byte-identical original receipt recovery, PNG private
headers/sanitization, actual source archive and deletion, refusal of all three
deleted attachment delivery routes and current-version restoration, retained PNGs
for both Board owners, source Board archive, public/Private copy withdrawal,
logout/readmission and selection retirement. The initial harness stopped at the
strict login refusal and cleaned up; explicit test-account verification repaired
the fixture without relaxing host policy. The successful run independently leaves
zero owned containers/databases/provider volumes/credential files.

Storage is the explicit IntegrationTest private local adapter and the scanner is
the declared protocol simulator. These are actual host/Worker/decoder/database
and browser executions, not external S3, deployed ClamAV, physical erasure or a
current immutable `required-ci` result. The mandatory release pipeline now also
asserts source deletion and surviving Board owners; its actual Linux shell still
requires current immutable execution.

A complete rerun adding the post-tombstone receipt assertion subsequently passes
the desktop browser case but fails the phone case's return-focus assertion after
successful retry publication; HTTP lifecycle assertions are not reached in that
invocation. Its owned resources are independently removed. Focus recovery now
transfers ownership to the restored control before focusing it, so the removed
retry control is no longer the owner across later access refreshes. A blur into
that recorded destination preserves the transfer; another user-selected control
still cancels recovery. All 29 background/attachment control cases, web typecheck,
focused lint and a fresh web build pass. The complete native invocation then
passes both browser viewports with zero skips/failures/retries/flaky cases and all
subsequent HTTP lifecycle/concurrency assertions, including byte-identical original
receipt recovery after source deletion. Its original focus assertion and deadlines
remain intact. The final run independently leaves no owned containers, database,
provider volume or credential files. API/Worker source is unchanged from the
locally built images; the fresh web bundle includes the focus repair. This scoped
native result does not establish current full immutable CI.

## Real upload-through-Worker Card cover browser workflow

Both enabled-pipeline browser viewports now also exercise the actually uploaded,
scanned and published source as a Card cover. A direct PUBLIC command without
consent is refused without changing canonical cover metadata. Keyboard selection
requires explicit consent; only the first successful command response is lost.
Recovery preserves the exact original body/key and yields one canonical revision.
The browser renders the actual PNG; a fresh anonymous context receives sanitized
PNG bytes but cannot read private cover metadata. Removal advances one revision,
withdraws rendered disclosure and makes the image route return 404. Neither cover
metadata nor image replies are fabricated. Each viewport has exactly three UI
writes: selection, original receipt recovery and removal.

Initial complete runs expose a lost review key during a foreground refresh and
return-focus loss after committed recovery; those failed invocations remain
failures. The keyboard scenario now establishes enabled focus before one page
activation key and observes the actual canonical Card revision before another
review. Cover recovery now transfers focus ownership to the restored control in
a layout effect and retains it across later access refreshes; a user-selected
different control still cancels recovery. A focused regression covers both paths.
All 43 cover/background/attachment control cases, web/browser typechecks, focused
lint, fresh web build, shell syntax and diff checks pass.

The complete expanded two-viewport invocation and all subsequent HTTP pipeline
assertions then pass with no failures/skips/retries/flaky cases. Canonical Card
revision is exactly 8 after the two cover selection/removal pairs, 9 after source
archive and 10 after source deletion; attachment revisions remain 3/4/5. Original
Board-image concurrency, receipt recovery, retained PNGs, source tombstone,
Board archive, public/Private, logout/readmission and retirement assertions remain.
Failed-case cleanup uses current canonical revisions to remove only the owned
fixture's retained cover before the next viewport, without changing the failed
result. Final owned containers/database/provider volume/credential files are
independently absent. Private local storage, scanner protocol simulation,
administrator test-account verification and current immutable/external-provider/
full acceptance limits remain as recorded above.
