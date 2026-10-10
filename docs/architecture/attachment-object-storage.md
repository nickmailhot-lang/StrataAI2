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

## Actual selected-cover source archive and deletion

The complete enabled pipeline now selects its actually uploaded/scanned/published
attachment as a private Card cover after both browser viewports. It asserts the
canonical Card revision advances from 8 to 9 and that the cover delivers the same
sanitized PNG as the independently owned Board background; anonymous cover access
is refused. Archiving that selected source advances the Card exactly once to 10,
clears both cover identifiers and makes its image route return 404. Source deletion
then advances to 11 with the cover still empty and inaccessible. Attachment
revisions remain exactly 3/4/5; original Board-image receipts and both independent
Board image owners survive the attachment tombstone.

The complete two-viewport browser invocation passes 2/2 in 87.1 seconds without
failures/skips/retries/flaky cases, followed by every native HTTP pipeline assertion.
Existing concurrency, current-version refusal,
source restoration denial, Board archive, public/Private, session withdrawal,
readmission and retired-selection checks remain. Owned containers/database/private
provider volume/credential files are independently absent. This executes current
backend source with a separate native Worker and restricted PostgreSQL, explicit
local storage and scanner protocol simulator. It does not prove live peer-client
cover withdrawal, external S3/deployed malware engine, physical erasure/backup
expiry or current immutable/full release acceptance.

## Live peer cover removal and temporary refresh recovery

Both complete enabled-pipeline viewports now open a second actual session of the
same verified owner with the published PNG already rendered. After removal in
the first session, the peer receives a real SignalR event whose sequence exceeds
the pre-command canonical sync cursor, withdraws the image, and reads the exact
new Card revision and null cover. Both image endpoints return 404. The peer stays
on the same Card route with zero main-frame navigations: no reload is used.
This is two real sessions of one account, not a distinct-account permission matrix.

The expanded run exposed two temporary-refresh races. A completed fresh scoped
Board-image review was discarded while parent access was being refreshed; review
now retains its actor, rights and revision fences while its rendering stays hidden.
An uncertain original cover command was treated as a conclusive conflict during
the same temporary refresh; recovery now preserves its original key/body after
fresh account proof and lets the server authorize the replay. Known edit-rights
withdrawal still refuses the replay before any write, and new commands retain
the unavailable/current-version checks.

Each defect first fails its new focused regression against the old product code.
The repaired cover/background/attachment control selection passes all 46 cases;
web/browser typechecks, focused lint and a fresh web build also pass. Earlier
failed complete invocations remain failed evidence. The final complete invocation
passes both cases in 104.8 seconds with zero failures, skips, retries or flaky
cases, followed by every native HTTP pipeline assertion, including selected-source
archive/deletion and independent Board image retention. Cleanup independently
confirms zero owned containers, databases, provider volumes or credential files.
The original deadlines, command counts, canonical revisions and lifecycle assertions
remain unchanged. Actual source-archive live peer withdrawal, distinct-account
roles, disconnect recovery, external providers and current full immutable release
acceptance remain unproven by this run.

## Distinct Board-member live cover removal

The complete enabled fixture now creates a distinct participant account, issues
an ordinary Organization MEMBER invitation, accepts it as that participant, and
grants Board MEMBER through the normal API. The peer browser logs in with its own
credentials and asserts its actual profile ID differs from the owner's. Both
desktop/phone cases retain the already-rendered PNG, actual Worker/SignalR
sequence advancement, canonical removal, image 404 and zero-navigation assertions.
No membership, cover metadata, live frames or image bytes are fabricated.

The complete native invocation passes 2/2 in 103.3 seconds, zero failures/skips/
retries/flaky cases, followed by every original HTTP lifecycle/concurrency check.
Owned resources and credential files are independently absent. Both native
accounts first fail strict login while unverified; only their disposable records
are administrator-verified before actual login. This is not email-provider proof.
The release shell uses its existing isolated authentication fixture; browser
typechecking and shell syntax pass, but that Linux immutable invocation is still
pending CI. The demonstrated peer is an admitted MEMBER, not a read-only role.
Permission loss, reconnect, live source archive/delete, other roles/providers,
performance/capacity and current full release acceptance remain open.

## Actual source archive with live desktop and phone peers

The mandatory enabled pipeline now has a separate complete browser phase after
its private lifecycle-cover selection. The owner and distinct admitted member's
desktop and phone sessions have the actual Worker-published PNG open. An ordinary
owner API archive command uses reviewed Card/attachment revisions 9/3. Its
acknowledgment advances once to 10/4 and clears the selected cover atomically.
Both peer sessions receive actual Worker/SignalR sequence advancement beyond the
pre-command canonical cursor, withdraw the displayed image, read null cover IDs
at Card revision 10, and receive image 404. Their normal source download-options,
download and preview routes also return 404. Both stay on the same Card route with
zero main-frame navigation. The owner's open page withdraws its image as well.

The following HTTP phase recovers the exact original archive key/body instead
of issuing a second archive. Existing source deletion at 11/5, restoration refusal,
independent Board-image retention, concurrency, original Board-image receipts,
visibility/session and retirement checks remain. The original two cases pass
together in 111.4 seconds, followed by this complete additional case in 38.2 seconds
and all remaining HTTP assertions; zero failures/skips/retries/flaky cases and
process exit zero. Owned containers/database/provider volume/credential files are
independently absent. Browser types include the new explicit configuration;
shell syntax and diff checks pass. The mandatory release shell invokes both
configurations against the same built images, without changing existing deadlines.

This is API-initiated archive with actual observing browser clients, not a new
keyboard archive-control proof. Native strict disposable-account verification,
restricted PostgreSQL, separate Worker, local storage and scanner-protocol
simulation limits remain. Live permanent-deletion/permission-loss/reconnect and
the complete role/cross-feature/capacity/performance matrix, external provider
authority and current immutable/full release acceptance remain open.

## Missed source-archive event recovered after reconnect

The same mandatory archive phase retains both continuously connected desktop
and phone peers and adds a third real phone session of the admitted member.
All initially render the actual published PNG. The extra session's real socket
is closed before archive; reconnection attempts are temporarily refused at the
transport, without fabricating any event or API response. Its received sequence
remains unchanged while the owner archives and the two connected peers observe
the withdrawal. On readmission it must reconnect to the actual server, receive
an event sequence above the pre-archive canonical cursor and complete another
Board read. It then verifies Card revision 10, null cover identifiers, cover/source
delivery 404 and zero main-frame navigation alongside the original peers.

Existing degraded snapshot checks stay enabled and may conceal or refresh the
image during the outage. The recovery proof additionally requires actual stream
reconnection, received sequence advancement and renewed Board reading; it does
not depend on keeping stale pixels visible while disconnected.

The complete native invocation passes the original 2/2 in 94.5 seconds, the whole
expanded archive case 1/1 in 37.0 seconds and every subsequent HTTP assertion,
with zero failures/skips/retries/flaky cases and process exit zero. Original
archive key/body recovery, canonical revisions, connected-viewpoint checks and
150-second case deadline remain. Browser typechecking and diff checks pass;
owned containers/database/provider volume/credential files are independently
absent. Native verification/provider and immutable release limits remain. This
adds archive reconnect evidence, leaving permission loss, live permanent deletion,
the full role/cross-feature/performance matrix and external-provider acceptance open.

## Board membership loss with published cover and attachment review open

The mandatory lifecycle configuration now also runs a complete permission-loss
case before archive. A distinct admitted member's desktop and phone sessions
render the actual published private cover, then open the attachment review using
keyboard controls and display its actual file group. The owner reviews the active
MEMBER revision and removes that Board membership with the ordinary versioned
command. Both open sessions withdraw the cover, file review and cover controls
without main-frame navigation. Their Board, cover metadata/image, attachment page,
download-options, download and preview requests return 404, as does each attempted
cover removal. The owner's canonical cover remains exactly unchanged at 9/3 and
its PNG remains readable; Organization membership is exactly unchanged.

The fixture restores only that Board membership through the normal API, including
after a failed assertion without changing its failed result. Fresh peer sessions
then execute the existing complete connected/archive/reconnect case. No membership,
file metadata, image or realtime reply is fabricated. Current authorization and
restricted persistence remain the security boundary.

The complete native invocation passes original 2/2 in 91.5 seconds, both whole
lifecycle cases 2/2 in 74.3 seconds and every subsequent HTTP assertion, with zero
failures/skips/retries/flaky cases and process exit zero. Original Card/attachment
revisions, source archive receipt recovery, deletion, independent Board images,
connected viewport/reconnect checks and deadlines remain. Browser typechecking
and diff checks pass; owned resources and credentials are independently absent.
This proves Board-member access withdrawal on an open published source, not the
whole role/parent/move/copy/lifecycle/performance matrix. Native account/provider
limits, external authority and current immutable/full release acceptance remain.

## Real browser file upload through Worker, preview and download

The same mandatory lifecycle configuration now also runs the primary FILE
workflow at desktop and phone sizes on separate private Boards/Cards in the
fixture Organization. Keyboard controls open upload review; the native file
input receives a real PNG with known trailing metadata. Exactly one actual raw
upload carries its original size, SHA-256, base64 filename, Card revision 1 and
idempotency key. The real acknowledgment persists Pending at Card/attachment 2/1
and the client displays the authoritative success notice. Actual restricted
Worker scanning and contained decoding publish the eligible source at 4/3.

The attachment disclosure displays the real Clean state. Keyboard preview review
renders an actual one-pixel PNG with a complete IEND and without the original
trailing metadata. Keyboard download review exposes the scoped private link;
activating it produces a real browser download with the correct filename and
every original byte intact. Upload count remains one. WCAG 2.2 AA-tagged Axe
checks have zero violations in the selected-file form and completed delivery
state at both sizes. Neither file metadata, scan/publication state, options nor
binary replies are fabricated.

Two earlier complete invocations remain failed, with private reports retained.
The first compares a request-body mirror that returns null for these File uploads;
exact byte verification now uses server-admitted size/hash and the actual browser
download. The second expects the wrong success wording; it is corrected to the
existing “File attached. Safety scan pending.” No product code or deadline is
changed to obtain the result. The final complete native invocation passes the
original 2/2 in 93.2 seconds and the expanded lifecycle/upload configuration 4/4
in 150.8 seconds, zero failures/skips/retries/flaky cases, followed by every
original HTTP assertion and process exit zero. Owned resources and credential
files are independently absent. Browser types and diff checks pass; source checks
confirm the reused local backend images and fresh web build match current product
source. This is current native evidence, not current immutable release identity.

The original source's revisions, permission loss, connected/reconnecting peer
archive, deletion, independent Board images and receipt/concurrency assertions
remain. This proves the normal PNG browser workflow; other formats, invalid/
quarantined input, lost-upload replies, large files, full role/parent/move/copy and
capacity/performance acceptance remain open, alongside native account/provider
limits, external operator/backup authority and current full immutable release.

## Lost browser upload reply recovered after actual Worker publication

The mandatory configuration retains both normal upload cases and adds desktop
and phone original-receipt recovery cases on separate owned Boards/Cards. The
browser submits its real File normally. Chromium's [Fetch response-stage interception](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/)
observes the actual committed 200/Pending receipt and fails only its response
delivery; it never rewrites or proxies the original binary request. No fabricated
receipt or provider reply is supplied to the application.

The application exposes original-upload recovery. Before retry, the actual Worker
has scanned, decoded and published at Card/attachment 4/3, and the browser admits
the newer Card snapshot. File selection and competing Card/attachment/close
controls stay disabled. Keyboard retry uses the exact original idempotency key,
hash, name, size, Content-Type and Card revision 1. The real recovery response
equals the observed initial 2/1 receipt. Current metadata still has exactly one
attachment, Clean at version 3 and Card revision 4: no second upload effect or
revision rollback. Both recovered files then pass the existing sanitized browser
preview, exact actual download and WCAG-tagged delivery checks.

The complete native invocation passes original 2/2 in 93.7 seconds and the whole
expanded configuration 6/6 in 235.8 seconds, with zero failures/skips/retries/flaky
cases, all following HTTP assertions and process exit zero. The original normal
uploads, Board-membership withdrawal, connected/interrupted source-archive peers,
source deletion, receipt/concurrency and independent Board images remain. Each
case keeps its 150-second deadline. Browser typechecking/diff checks pass; owned
containers/database/provider volume/credential files are independently absent.
Product source is unchanged. Native account/provider and current immutable
release limits remain; other formats, quarantined/invalid/large inputs, full
role/parent/move/copy/lifecycle/performance and external operator/backup acceptance
remain open.

## Real browser upload rejected by the Worker scanner protocol

The mandatory lifecycle configuration retains its normal upload, original-receipt
recovery and permission/archive cases and adds desktop (1280px) and phone (390px)
quarantine cases on separate owned Boards/Cards. Both use the browser's actual
File input and keyboard upload. The valid PNG contains the harmless, explicit
`STRATAAI_CI_HARMLESS_REJECT_FIXTURE` marker. The CI-only clamd protocol simulator
reads the bounded complete stream and returns `FOUND` for that marker; ordinary
fixture files still return `OK`. This is a protocol rejection fixture, not a
malware signature or evidence of a deployed malware engine's detection accuracy.

The actual separate Worker must persist Rejected at attachment version 2 and Card
revision 3. The browser admits the updated Card and displays the textual rejection
status. Download and preview controls, links and images are absent. Direct
download-options/download/preview requests must return 404 without attachment
bytes or a Content-Disposition header. Public metadata must omit storage keys,
the cover candidate list must be empty, and the current cover must remain null.
Each case retains the 150-second deadline, one real upload and WCAG-tagged form
and rejected-state accessibility checks. No terminal metadata or browser provider
receipt is fabricated.

All six file-upload cases also require the actual browser's scoped SignalR event
sequence to reach the canonical delivered server cursor before file review.
Server readiness and a Card response alone do not prove browser receipt of the
final scan/publication events. Keyboard actions use the existing
`pressAdmittedAction` helper: current enabled focus and one locator-targeted
Enter/Space activation, with no repeated activation, command or test retry.
The original cover/background phase uses that same helper. Protected revalidation
and delivery assertions remain in place; case deadlines are unchanged.

Executed native evidence: the complete original phase passes 2/2 in 93.0 seconds
and the expanded lifecycle phase passes 8/8 in 296.4 seconds. There are zero
unexpected results, skips or flaky cases; subsequent HTTP lifecycle assertions
and the process exit pass. Independent verification confirms no owned containers,
database, private provider volume or credential files remain; the original three
running services are preserved. Earlier failed reports are retained: three
expanded runs exposed phone activation/delivery failures (6/8, 7/8 and 7/8), and
one original phase failed 1/2 before the expanded phase. The final complete run
adds real browser event receipt and uses the existing keyboard helper throughout;
it does not remove those delivery/consent/recovery assertions or relax deadlines.

This is bounded native local-provider and protocol-simulator evidence. Product
source is unchanged; current immutable release CI, deployed scanner/storage,
other supported formats, invalid/large inputs, Failed/rescan/operator coverage,
remaining role/lifecycle/cross-feature cases and performance acceptance remain
open. Estimated PRD-14 work remaining: **33%**.

## Rejected cover selection and owned dialog focus recovery

Both quarantine viewport cases now submit ordinary cover PUTs for the actual
Rejected attachment revision 2 and a forged published revision 3. Each must return
404 with `card_not_found`. The complete current cover and attachment page must
remain unchanged, the delivered Board cursor must not advance, and controlled
cover-image delivery must remain 404. Candidate exclusion alone is not the
server authorization boundary.

An actual native phone background-recovery focus assertion failed before these
new assertions were reached. The unchanged focus expectation prompted a focused
regression against the installed MUI trap. Access refresh removes the retry
element; its old DOM reference can no longer identify the mounted dialog's trap
container or sentinels. The focus helper now accepts the captured live dialog for
a disconnected owner, and recognizes only sentinels in that same MUI dialog root.
Connected owners identify their own current dialog directly. Board background
and Card cover recovery supply their captured dialog for layout/blur decisions.
Another control or dialog remains an intentional focus destination. Authorization,
receipt bodies/keys and input/revision fences are unchanged.

The corrected regression looks up the current retry element after remount and
uses the installed sentinel's actual focus fallback. Against the original
implementation the two-file run fails 2/19, with 17 passing. After repair all 308
tests in the 18 affected control suites pass; web/browser typechecking, focused
lint and a fresh production web build pass. Earlier fixture and diagnostic test
failures remain retained privately. This demonstrates the specific owned-focus
gap; it does not identify every possible native focus transition or prove the
remaining feature/release acceptance matrix.

The complete native pipeline using the freshly built repaired web bundle passes
original 2/2 in 91.9 seconds and expanded 8/8 in 295.4 seconds, with zero
unexpected/skipped/flaky cases and retry maximum 0. Both rejected cover-write
variants pass at both widths. All subsequent HTTP lifecycle/privacy/ownership
assertions and the process exit pass. Independent verification confirms no owned
containers, database, provider volume or credential files remain. All 91 CI
coverage guards and ordinary mandatory coverage verification pass. Current
immutable release CI, deployed providers and the outstanding feature/full matrix
remain unproven. Estimated PRD-14 work remaining: **33%**.

## All supported upload formats through the browser and Worker

The intact mandatory lifecycle phase adds desktop (1280px) and phone (390px)
happy-path cases for JPEG, WebP and PDF, retaining all eight earlier cases.
The [fixture module](../../tests/browser/attachmentFileFixtures.ts) contains actual
1x1 JPEG/WebP encodings generated once with the installed Chromium canvas and a
complete one-page PDF with calculated object offsets and cross-reference table.
JPEG retains a deliberate trailing private marker; WebP keeps its exact complete
RIFF container length. No dependency or product policy change is introduced.

Each format uses actual browser File selection, hashing/raw upload, Pending
acknowledgment, separate Worker scanning and real scoped SignalR delivery through
the canonical server cursor. All four supported types now pass actual keyboard
download, safe link attributes and exact browser-downloaded original-byte checks.
Form and delivered-state WCAG-tagged checks remain mandatory.

JPEG and WebP reach Card/attachment 4/3, produce fresh controlled PNG previews
that render at their real 1px width and differ from original bytes, and are then
selected as private covers through the normal keyboard controls. The real PUT
advances Card to 5; current cover metadata and rendered PNG are verified, the
owner receives the same sanitized preview bytes, and a fresh anonymous session
receives 404. PDF reaches Clean at Card/attachment 3/2 and can be downloaded,
while preview/options return 404, the UI offers no image preview, cover candidates
are empty, and an ordinary attempted cover PUT returns `card_not_found` without
changing the cover. Public PDF metadata omits storage keys.

The complete native invocation passes original 2/2 in 92.7 seconds, expanded
14/14 in 542.5 seconds, all subsequent HTTP lifecycle/privacy/ownership checks and
process exit 0. Both reports have zero unexpected/skipped/flaky results and retry
maximum 0. Independent verification confirms no owned containers, database,
provider volume or credential files remain. Browser typechecking, fixture-byte
provenance checks, all 91 CI coverage guards and ordinary mandatory verification
pass. All cases retain their 150-second deadlines and the full unfiltered enabled
phase; no normal/recovery/quarantine/permission/archive assertion is removed.

This is native local-provider and explicit scanner-protocol-fixture evidence for
these small static samples. Current immutable full-release CI, deployed providers,
invalid/large/complex encoded inputs, additional per-format recovery/role/lifecycle
cases and the full cross-feature/performance/AC/DoD matrix still need proof.
Estimated PRD-14 work remaining: **32%**.

## File rejection recovery publication

The complete 2,075-case frontend invocation retained an attachment rejection
assertion failure: the rejected-file state was visible before the parent's
recovery callback had been observed. The same complete attachment and Board
test files subsequently passed **57/57** without a source change, establishing
that the full-suite failure must remain recorded as timing-sensitive.

File upload recovery now publishes its retained-intent/blocked state in a
layout effect, before paint. This lets the parent fence competing commands
before displaying a retained selected file or original retry. The actual File,
actor, digest, idempotency key, account checks, explicit discard and original
request remain unchanged. No test assertion, case or deadline is relaxed.

Both complete files pass **57/57** after the change; frontend type checking,
lint and a production build pass. Private reports:
`frontend-recovery-diagnostic-20261010.json` and
`frontend-recovery-layout-final-20261010.json`.
The subsequent complete frontend invocation passes **2,075/2,075 actual cases
across 142 files**, zero failures or pending cases, in **1,124,231.467 ms** of
wall time. Report counters agree with the actual rows; both the previously
failing attachment assertion and Card archive recovery case pass. Report:
`frontend-recovery-layout-full-20261010.json`.
The original 2,073/2 result remains retained. This successful invocation does
not prove an independent Card archive defect repaired by the attachment change
or establish native browser/immutable-release acceptance. The invitation
authority repair remains isolated while its broader backend gates finish.
Estimated PRD-14 work remaining stays **32%**; the issue remains open.

## Unsupported contents and misleading file labels

The browser's file-picker filter is guidance. File MIME labels and extensions do
not determine admission: the client sends raw bytes as `application/octet-stream`,
and the server classifies the bytes under the configured upload policy. Display
names remain metadata. Controlled original downloads use a safe leaf filename,
attachment disposition and opaque binary delivery; preview and cover delivery use
verified sanitized PNG derivatives.

An authorized upload refused with HTTP 400 `attachment_type_not_allowed` now
explains that the file contents are not an allowed type. The selected file stays
visible; upload/retry and sibling mutations stay unavailable until the user
chooses **Discard selected file and load latest**. This explicit action clears
the selection and loads the current Card before another file can be chosen.
Both shared browser error filters retain this approved code only at HTTP 400;
unknown codes and server diagnostic fields are still excluded.

The first complete expanded native attempt passed 16/18 cases; both new
unsupported-content cases reached the expected server refusal but failed the
specific browser notice because the shared error filters discarded its code.
That attempt stopped before the subsequent HTTP assertions and cleaned up its
owned resources. The transport regression reproduces the defect (14 passing,
one failing); the corrected control regression likewise fails only its new
notice assertion against the previous control. After both filters and the
control are corrected, all 46 focused boundary/transport/upload tests and the
full web suite (1,984 tests in all 142 files) pass. Type checks, lint, fresh web
builds, 91 CI coverage guards and the ordinary mandatory verifier pass.

Four additional desktop/phone native cases retain the entire previous enabled
phase. JPEG bytes labelled `text/html` with a path-like `.html` display name are
classified as JPEG, scanned and published by the separate Worker, rendered as
sanitized previews/private covers, and downloaded with a safe basename and exact
original bytes. Unsupported bytes labelled as PNG produce one real raw POST and
HTTP 400 `attachment_type_not_allowed`; attachment paging, null cover and the
canonical delivered Board cursor remain unchanged. The browser preserves the
selected file, blocks upload/retry and sibling writes, and supports keyboard
discard/latest recovery to the unchanged empty Card without another POST.
WCAG-tagged checks cover review, refusal and recovered states.

The complete corrected native invocation passes original 2/2 (100.1 seconds),
expanded 18/18 (721.3 seconds), all subsequent HTTP lifecycle/privacy/ownership
checks and process exit 0. Both reports have zero unexpected/skipped/flaky results,
top-level errors and retries. Independent verification confirms no owned
containers, database, provider volume or credential files remain. The original
running services are preserved. No case filter, assertion removal, deadline
extension or retry relaxation is introduced.

This proves the described small local-provider samples and explicit scanner
protocol fixture, not real malware-engine detection, deployed providers or the
current immutable full-release CI. Complex/large/adversarial encodings, remaining
role/recovery/lifecycle combinations, retention/purge reconciliation and the full
cross-feature/performance/AC/DoD matrix remain open.
Estimated PRD-14 work remaining: **32%**.
