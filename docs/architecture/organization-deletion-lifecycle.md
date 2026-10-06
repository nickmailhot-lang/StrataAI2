# Organization deletion completion — implementation contract

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-010,
[PRD-18](https://github.com/nickmailhot-lang/StrataAI2/issues/19) and
[ARCH-07](https://github.com/nickmailhot-lang/StrataAI2/issues/88) require
completion beyond the [deletion request acknowledgment](organization-deletion-retries.md).
This defines the completion implementation. Publication, graph stages and the terminal storage gate have infrastructure/Worker
implementations. Product deletion now publishes the accepted request and first
job atomically. Browser completion, Demo terminal processing and full release
acceptance remain unfinished.
A 202 remains an acknowledgment of the accepted request.

## State and graph treatment

ACTIVE transitions to DELETING only through the current Owner/version command.
DELETING withdraws ordinary Organization and descendant access immediately and
suspends reminders. The terminal storage foundation adds an irreversible DELETED
state, retained deleting actor, deletion timestamp and terminal version.
Neither a receipt replay nor membership restoration may reactivate that parent.

Retain Organization, membership and descendant identifiers and relationships.
Retain existing audit and immutable event history with original attribution.
Completion tombstones remaining Boards, Lists, Cards and attachments, retaining
previous archive history and existing deletion attribution. Already deleted
records must not receive replacement actors, timestamps or repeated events.
Clear selected Card covers and Board image references as part of the affected
record transaction. Retained comments, checklist and assignment records remain
undiscoverable through deleted parents; their historical attribution is retained.
No child restore, copy, download, preview, public share, search or notification
projection may expose the deleted graph.

Private original/preview bytes and immutable upload evidence follow the existing
[attachment retention contract](attachment-covers-lifecycle.md): retain them until
bounded provider reconciliation has explicit removal authority. Product deletion
revokes application access irreversibly. Terminal product status must not claim
that provider versions or backups have been physically erased. Uncertain writes
must be reconciled before any provider removal; backup windows remain an
operational policy, separate from product tombstone completion.

## Worker authority and atomic boundaries

Publish the completion job in the same owning transaction as DELETING, reminder
suspension, request audit and durable acknowledgment. Queue metadata contains
only validated references and the accepted version, never names, content, object
keys, bearer tokens or provider credentials. Duplicate requests retain one job.

The separate Worker uses explicit Organization scope, forced RLS and restricted
capabilities. It validates the exact current unexpired job lease and the retained
accepted request before any effect. Execution derives authority from that
committed request; it must not require the initiating browser session to remain
alive. A later membership/account change cannot rewrite the recorded actor.

Process each collection with bounded UUID-seek pages, including archived rows;
never use a user-filtered or active-only Board directory as a completion input.
Each page transaction couples child tombstones, reference cleanup, retained
attribution, required audit/events and a durable progress cursor. A failed write
or final lease check rolls back the entire page, including the cursor. Reclaimed
leases resume committed progress without repeating effects. Preserve the
Organization-before-Board lock order and avoid holding database transactions
across provider operations. Use smaller jobs within the existing lease deadline.

Mark the Organization DELETED only after authoritative graph checks establish
that every required stage completed. Couple that terminal transition with exactly
one ORGANIZATION_DELETED envelope and its delivery publication. A crash after
commit but before job acknowledgment must recover the original completion.
There is no success based solely on an empty UI directory or a missing normal GET.

## Completion observation and acceptance

Provide a minimal, independently authorized completion observation for the
original request. It must preserve current account and Owner admission, disclose
no historical Organization name or descendant content, and distinguish pending,
completed and unavailable. The browser retains request recovery independently
of ordinary surface admission and reports completion only from this contract.

Other previously authorized clients receive or recover content-free lifecycle
invalidation, recheck current access, and withdraw private snapshots. Required
terminal delivery and reconnect recovery must work after ordinary Board reads
become unavailable; existing Board-directory events alone do not prove this.

Required evidence includes restricted-role cross-tenant denial; an archived and
large multi-page graph; unchanged existing tombstones/audits; atomic publication
and page rollback; expired/reclaimed leases; restart after every committed phase;
provider uncertainty; normal/private/public disclosure withdrawal; duplicate
terminal completion; and two-client reconnect. Execute the actual Worker and
browser desktop/phone keyboard/accessibility scenarios against immutable CI
images. Until those checks and the remaining PRD criteria pass, keep PRD-03,
PRD-18 and ARCH-07 open.

## Application implementation progress

The bounded-page Application contract now provides `OrganizationDeletionJobs`,
strict reference-only request/step/accepted-version metadata and an
`OrganizationDeletionPageHandler`. Durable keys distinguish continuation steps
while preserving duplicate publication identity. Each dispatch submits exactly
one 128-record page with the unchanged claimed job, actor, lease and Worker
identity. Invalid scope/metadata or unavailable storage cannot acknowledge success.
The storage contract requires atomic effects, progress and continuation publication,
with final lease fencing and non-repeating committed-step recovery.

Domain fixtures cover duplicate/continuation keys, metadata privacy and invalid
input, claimed scope, page bounds, lost lease and cancellation. Compilation is
source evidence only. Restricted PostgreSQL progress, publication, graph stages,
terminal delivery and observation have runtime results documented below. The
product command now invokes publication; this Application contract alone does
not establish browser or exact-image acceptance.

Migration 088 adds immutable accepted-request references and a forced-RLS
progress checkpoint. Initial storage admission requires the current DELETING
version and active Owner, with parent-before-membership gates. The initial
checkpoint must be ATTACHMENTS with the first step equal to the request, no
cursor or completion and version one. API grants allow only initial insert/read;
Worker grants allow reference/progress reads, without correlation content or
progress mutation. Later migrations add the narrow lease-fenced capabilities
described below.
Both runtime hosts require the migration ledger; CI fixtures cover repeat/upgrade,
rollback, restricted-role isolation and disabled Worker writes. Compilation and
script syntax checks cannot prove execution; PostgreSQL CI remains required.

## Atomic publication foundation

`PostgresOrganizationDeletionJobPublisher` borrows the owning Organization
command transaction. It admits the current active Owner against the accepted
DELETING parent/version, then couples the immutable request, initial checkpoint,
and reference-only first job. It cannot publish outside that command scope or
commit independently. Matching retries preserve the original correlation,
completed first job, and advanced checkpoint; mismatched or partial publication
is refused.

The mandatory restricted PostgreSQL executable includes
`OrganizationDeletionPublicationContract`: command refusal, final synthetic actor
refusal, a queue collision after tentative request/checkpoint writes, concurrent
replay, changed request/version refusal, and replay after progress advances.
The contract compiled with zero warnings and passed against real restricted
PostgreSQL in [CI run 37494481167](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37494481167/job/112375365028)
at revision `1669b1e`. This proves the listed publication/storage boundaries;
terminal graph processing and exact-image acceptance remain pending.
Actor admission is synthetic in this contract,
so it does not prove HTTP/session authorization.

The product service now invokes the registered publisher in the same owning
command. The runtime page and observation results below cover their restricted
storage boundaries; full HTTP/Worker/browser completion remains to verify.

## Terminal storage gate

Migration `089_organization_deletion_terminal` adds retained terminal actor/time,
an immutable DELETED Organization state, a tenant-isolated completion envelope,
and indexes for final graph proof. The restricted Worker receives only
`finish_organization_deletion`, not direct Organization or checkpoint mutation.
The API cannot invoke that capability or directly create the terminal state.
Normal Organization discovery, metadata writes, search admission and ownership
continuity treat DELETED as withdrawn access.

`PostgresOrganizationDeletionFinalizer` validates job references and opens an
explicit tenant transaction. The capability locks the parent before the job,
checks the accepted request and exact current unexpired lease, and requires the
FINALIZE checkpoint with the same step. It refuses any remaining Board, List,
Card or attachment, including archived descendants and selected cover/image
references. It atomically commits terminal state/version, completed checkpoint,
one audit entry, one immutable ORGANIZATION_DELETED envelope, and its
reference-only delivery job. A final lease fence after publication rolls back
all tentative effects on expiry. Duplicate recovery verifies the original
terminal envelope and delivery identity without producing another event.

`OrganizationDeletionTerminalContract` exercises the actual restricted adapter
and SQL capability, including remaining descendants, API refusal, tenant/lease
fences, late queue-write expiry rollback, one completion across replay, retained
terminal identity and cross-tenant event isolation. Earlier page processing is
staged by the administrator fixture: these checks do not establish traversal,
physical object removal, realtime delivery or browser completion. Compilation passed with zero warnings/errors. Migration and this restricted
terminal contract passed at `d715ce5` in [CI run 37496328863](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37496328863/job/112381688920).
This verifies the terminal gate; preceding graph stages were staged by the fixture.

The standalone finalizer adapter is not separately registered. The Worker page
processor below invokes its SQL terminal capability inside the page transaction.
Product API publication and independent Owner observation are implemented below;
live lifecycle consumption and full exact-image acceptance remain required. Existing deletion acknowledgments remain request acknowledgments.

## Durable completion readiness

Migration `090_organization_lifecycle_delivery` adds a narrow Worker-only
`deliver_organization_lifecycle_event` capability. The registered production
`OrganizationLifecycleDeliveryHandler` accepts one canonical nonzero event
reference, rejects malformed/private/duplicate metadata and invalid claim scope,
and invokes the restricted adapter. Neither runtime role receives direct event
mutation rights.

The delivery transaction locks the terminal parent before the job and source,
validates the exact unexpired lease, tenant, actor, event reference, stable job
key, original correlation and completed checkpoint, then sets readiness once.
It checks the lease again after the readiness write. A failed late check rolls
that write back. An already ready event still needs a valid current claim;
reclaimed delivery preserves the original readiness timestamp.

Seventeen handler cases and the actual restricted PostgreSQL delivery contract
cover bad references/scope, cancellation, unavailable delivery, API refusal,
late readiness rollback, duplicate delivery, expired and superseded claims,
reclaimed acknowledgment and disabled direct event mutation. Domain test, persistence contract and Worker projects compiled with zero
warnings/errors. The restricted delivery contract passed at `736b386` in
[CI run 37497033927](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37497033927/job/112384103047),
including late readiness rollback, duplicate/reclaimed delivery and disabled
direct mutation. Handler runtime tests and overall release results must be
reviewed separately from this database result.

Readiness is a durable delivery milestone. It does not establish SignalR/browser
consumption. Product publication and the independent observation reader are now
implemented, with their separate verification boundaries recorded below.

## Bounded graph candidate traversal

Migration `091_organization_deletion_candidates` and
`PostgresOrganizationDeletionCandidateReader` provide a Worker-only reference
snapshot for every deletion stage. Parent/request/checkpoint and the exact
current claim must match before disclosure, and a final lease check follows the
read. Pages use UUID seek with a maximum of 128 candidates. Archived descendants
and archived parents remain in scope; already deleted Cards/Boards with selected
cover/image references remain candidates for reference cleanup.

The projection contains IDs, parent references, revisions and lifecycle states.
It exposes no names, content, URLs, object keys or provider credentials. The API
cannot invoke the capability and the Worker receives no direct descendant table
access. FINALIZE returns an explicit empty-stage envelope, rather than claiming
completion from an empty user directory.

The mandatory restricted traversal contract seeds 100,000 archived Cards plus
two active Cards, 200 Lists, active/archived Boards, and active/archived/deleted
attachments. It walks bounded pages, checks ordering/continuations and complete
counts, and tests current scope/request/step/version/lease and page-limit fences.
Checkpoints are admin-staged and the scale fixture extends its disposable claim;
this tests traversal, not mutation, normal lease throughput or product deletion.
Compilation passed with zero warnings/errors. The actual restricted traversal
contract passed at `e01fa5a` in [CI run 37498205706](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37498205706/job/112388090296):
100,002 Cards, including 100,000 archived Cards, were read in bounded UUID-seek
pages in 7,067 ms including administrator checkpoint staging. The 200-List and
archived parent/attachment checks also passed. This timing is the fixture's
traversal result, not a product mutation or browser performance claim.

Candidate reads alone grant no mutation authority. The page processor described below
couples current row/version/lease validation, tombstones, reference cleanup,
audit/events, checkpoint advancement and next-job publication in one transaction.
It must preserve prior deletion/archive attribution and provider evidence. The reader alone is not a production deletion processor.

## Atomic Worker deletion stages

Migration `092_organization_deletion_pages` and the registered production
`PostgresOrganizationDeletionPageStore` implement attachments → Cards → Lists →
Boards → FINALIZE, using the existing 128-candidate handler and durable jobs.
Each page locks its parent, validates the accepted request/current lease, and
re-reads each source under Board/child locks before effects. It couples
tombstones, selected cover/image cleanup, retained attribution, audit/events,
immutable step receipt, checkpoint and reference-only continuation publication.
Deferred cover constraints are checked before the final lease fence. Late expiry
raises an error and rolls back every effect and publication.

The original archive timestamp remains unchanged. Already deleted records keep
their deleting actor/time and do not emit another deletion event; only remaining
selected references require cleanup. FILE source/digest/storage and immutable
provider evidence remain retained. Active attachments can be directly tombstoned
only by the admitted, leased Worker scope, avoiding an invented intermediate
archive history. Normal attachment lifecycle commands retain their existing
archive-first rules.

Committed step receipts allow duplicate/reclaimed jobs to acknowledge their
original work after the checkpoint advances or terminal deletion completes.
Recovery verifies the matching continuation identity and still requires the
current live claim. Processing derives authority from the accepted request,
independently of a subsequently deactivated initiating account. FINALIZE invokes
the existing graph-proving terminal capability; completion event readiness uses
the registered restricted delivery handler.

`OrganizationDeletionPagesContract` exercises actual restricted stages on 261
Cards, 131 attachments (including FILE metadata), active/archived parents and
prior tombstones. The extended fixture has a normally constrained preview
manifest/publication, selected Card cover, and three Board image owners, including
copied ownership and a selected image on a previously deleted Board. It asserts
reference cleanup, one cover-change event, historical Board cleanup, and unchanged
preview/publication/ownership evidence. This is administrator-seeded historical
metadata; it does not prove provider writes or deletion through the product API. It injects expiry after continuation insertion, verifies full
rollback, duplicate/reclaim recovery, drains real page/event handlers through
terminal readiness after actor deactivation, and checks preserved prior history
and provider metadata. Compilation passed with zero warnings/errors. The initial
PostgreSQL run exposed an ordinary attachment archive regression: a combined SQL
predicate attempted the private Worker capability check under an ordinary caller.
The trigger now uses a separate procedural Worker/transition branch, preserving
the private capability grants. The archive/delete storage contract also checks
that ordinary direct deletion fails with the lifecycle constraint, rather than a
capability permission error.

The correction at `8664556` passed the actual restricted graph stages and
attachment storage contract in [PostgreSQL CI job 112398997723](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37501388855/job/112398997723).
That run covers the original 260-Card/130-attachment fixture, bounded pages,
late-expiry rollback, replay/reclaim recovery, retained history/provider metadata,
and terminal readiness after actor deactivation. The explicit API/Worker private-helper privilege assertions added at `173c665`
also passed in [PostgreSQL CI job 112399819815](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37501631140/job/112399819815).
The added selected preview-backed cover/image fixture passed at `b043f5c` in
[PostgreSQL CI job 112401327853](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37502071534/job/112401327853),
including cover/background cleanup and unchanged prior history/provider evidence. Large mutation throughput, HTTP/product integration
and live browser completion require further evidence before PRD acceptance.

The product API now publishes deletion work. Its 202 still confirms the request;
only an authoritative observation confirms completion. Demo terminal parity,
browser observation, two-client invalidation/reconnect and exact-image acceptance
remain unfinished.

## Demo publication transaction foundation

The registered Demo deletion publisher journals the immutable accepted request,
initial checkpoint and first reference-only job together in host-local memory.
It requires an owning Organization command, a DELETING parent at the accepted
version, the current Owner membership and an active account. A standalone call,
another Organization scope or a Work-only transaction cannot publish it.
Matching duplicates preserve the original identity/correlation; conflicting
requests or versions are rejected. The journal participates in the owning
Organization rollback alongside the parent and acknowledgment stores.

The Demo scope now distinguishes Organization commands from Work commands while
preserving existing Work scope admission. Five API-host fixtures cover successful
publication/replay and rollback on refusal, final actor loss, exception or
cancellation, followed by recovery that proves the failed journal was removed.
Compilation passed with zero warnings/errors; runtime execution is pending CI.
The production publisher is registered alongside the Demo implementation. The
product service now invokes them atomically with the request. Demo page execution,
completion observation and browser integration remain required; an in-memory
journal is not durable across process restarts and does not replace production
PostgreSQL jobs or the separate Worker.

## Independent original-Owner observation

`GET /organizations/{organizationId}/deletion-requests/{requestId}` now exposes a
minimal request-bound observation with `private, no-store` caching. The optional
`expectedActorId` must match the signed-in account. The response contains only
`requestId`, `state`, `version`, `eventId` and `completedAt`; it contains no
Organization name, descendants, counts, storage keys or private content.

Admission requires the original accepted requester to remain a current active
Owner and have current account/session authority. Normal Organization and Board
reads remain withdrawn. PostgreSQL takes the parent lock before membership,
account and session admission, then validates the immutable request and progress.
`PENDING` requires DELETING at the accepted version with incomplete progress.
`COMPLETED` requires DELETED at accepted version plus one, matching retained actor,
COMPLETE checkpoint/time and the original terminal event identity/version/time/
correlation. Event transport readiness is independent of committed completion.
A final actor/session check discards the observation if authority expires during
its read. Missing/foreign requests and former/nonoriginal Owners return the same
`organization_not_found`; actor/session failure returns `session_unavailable`;
unconfirmed storage returns `organization_storage_unavailable`. A missing normal
GET or an unavailable status must never be interpreted as completed deletion.

Demo reads use both owning gates and the accepted journal. They can report a
proven pending request; Demo terminal graph/event execution remains unfinished,
so no completed snapshot is invented. Product deletion commands now publish
canonical roots. Browser status consumption and full HTTP/Worker acceptance
still need completion.
Legacy DELETING rows without a canonical accepted request are unavailable here.

API-host cases exercise pending status after ordinary access withdrawal, exact
content-free fields, original/current Owner admission, account-switch refusal,
foreign request/scope denial and final actor failure. The restricted PostgreSQL
graph fixture exercises pending and actual terminal reads, disabled account and
Owner demotion denial, final admission failure, and stable event/time recovery.
Its account/session admission fixture is synthetic and does not prove production
HTTP session expiry. API and persistence contract compilation passed with zero
warnings/errors. The restricted observation cases passed at `ae4ad1d` in
[PostgreSQL CI job 112406199262](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37503503720/job/112406199262).
This does not establish production HTTP session or browser completion acceptance.

## Product publication and retained terminal acknowledgments

The product command now couples DELETING, reminder suspension, request audit,
accepted root, initial checkpoint, first reference-only job and 24-hour receipt
in its owning transaction. Keyed requests use their original retry UUID as the
root reference. Receipt recovery returns the original acceptance without
republishing a job or resetting progress. The HTTP 202 includes `requestId` and
a Location pointing to the independent observation endpoint. An unkeyed HTTP
caller receives a fresh request reference; it must supply a retry key itself to
recover a lost response.

Only deletion acknowledgment recovery admits DELETED as well as DELETING into
its owning command scope. It still requires the current active original Owner,
matching fingerprint, unexpired receipt and final actor/session admission. A
new request requires ACTIVE; no recovery may reactivate a terminal parent or
restore normal reads/commands. This closes the race where the Worker finishes
before a lost 202 can be retried. Old DELETING records without canonical roots
still require deliberate legacy recovery; replaying their old receipt does not
invent a new actor/request or silently requeue deletion.

The explicit general Worker loop retains its configured Organization scopes
(`STRATAAI_WORKER_ORGANIZATION_IDS`). Deletion now has the automatic production
routing loop described below, so new accepted deletions require no scope update. Demo now
journals the actual product request but still lacks terminal page/event execution.
Browser observation is implemented; native completion proof and other-client
lifecycle recovery remain required.

API-host checks cover actual DELETE response/reference and journal-backed status,
concurrent same-key acceptance, and root-publication exception/final actor loss
rolling back the parent and receipt before successful retry. The exact-image
Organization command script snapshots roots, progress and deletion jobs alongside
its existing reminder/audit/receipt rollback checks and asserts atomic API
publication plus independent pending observation. The restricted graph fixture
additionally checks terminal receipt recovery scope, normal-command withdrawal,
final actor refusal and unchanged completion evidence. API/persistence contracts
compiled with zero warnings/errors and the container script passed syntax checks;
the restricted terminal acknowledgment/observation/graph cases passed at
`429c624` in [PostgreSQL CI job 112410192340](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37504683537/job/112410192340).
Actual product API-host and exact-image HTTP/Worker results remain pending; this
restricted receipt-read fixture does not replace full HTTP terminal retry proof.

CI at `429c624` found an unhandled injected publication failure in the API-host
test. `2b261f9` introduces an expected publication-refusal exception shared by
the adapters and application boundary. This maps to
`503 organization_storage_unavailable` and makes the owning command roll back
every effect. Scope misuse, unexpected faults and cancellation are not caught by
that mapping. All seven focused API-host publication/rollback checks passed
locally.
The repaired full .NET CI job passed all 514 API-host and 677 domain checks at
`2b261f9`: [job 112423617351](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37508622106/job/112423617351).
The exact-image command fixture now lets the release Worker consume
the product-published root, requires actual graph completion and leased terminal
event readiness, and checks unchanged original-key HTTP replay after completion.
It never stages terminal state or marks jobs successful itself. Runtime execution
of that expanded fixture remains pending CI.

## Browser observation and tab-local recovery

After a 202, the Owner page retains the original request/account/reviewed-version
references and offers **Check deletion status**. It checks the current profile
before and after the independent status request, matches the original UUID and
exact pending/terminal revision, and requires valid terminal event/time evidence.
Only that verified terminal snapshot displays completed deletion. A refusal,
missing normal GET, empty directory, malformed response, changed account or
transport deadline never establishes completion. Uncertain status reads retry
the same reference without another DELETE. Explicit completion checking moves
focus from the removed button to the polite status notice.

The current tab stores only request UUID, account UUID, reviewed version and
whether the request was acknowledged, scoped by Organization. It stores no name,
graph content, provider data, tokens or completion snapshot. Refresh verifies the
current account before restoring that reference and does not reopen ordinary
Organization reads. An uncertain acknowledgment keeps the original retry UUID;
a known acknowledgment restores status checking. Cached data never reports
terminal completion. Account switches clear the reference; same-account session
expiry can retain it for later sign-in. Browser storage refusal leaves current-page
recovery available but cannot provide refresh recovery. Closing the tab removes
this session-local cache; production authority remains in PostgreSQL.

A temporary account-verification failure after refresh retains the reference
and offers **Retry account verification**. Retry does not fall back to a normal
Organization read, issue a new DELETE or infer completion. Only successful
original-account verification restores request retry or status checking.

Component checks cover pending/terminal evidence, focus, acknowledged/uncertain
refresh recovery, account switching after response, and 404/503 behavior. Parser
checks reject mismatched request/revision, invalid event/time/state, extra fields
and unbounded/malformed cache data. Native desktop/phone fixtures now refresh an
uncertain request, recover its original key, check actual pending status, refresh
a known acknowledgment, verify non-Owner status denial and run accessibility
checks. These native cases do not establish completed production Worker/browser
or other-client lifecycle delivery. The full local browser suite passed 1,565
tests across 126 files before the final account-verification retry refinement;
all 36 focused routing, parser and recovery checks passed after that refinement.
Type checks, lint, browser-fixture type checks and production build passed.
Current native CI remains required; PRD-03 remains open.

## Automatic production deletion discovery

Migration `093_organization_deletion_discovery` exposes one Worker-only,
read-only routing capability with a fixed search path. It returns at most 100
Organization UUIDs, ordered by a seek cursor, from canonical accepted roots with
matching current checkpoint or terminal completion delivery. It exposes no names,
memberships, sessions, job metadata or graph content. API and PUBLIC receive no
execution grant; direct table access remains governed by forced RLS. Discovery
does not claim jobs or change state. Every dispatched job still establishes its
own explicit tenant session and leased, source/version-fenced graph authority.

The separate production Worker enables the loop by default; operators can suspend
it with `STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false`. Invalid values
and enabled Demo discovery fail startup. UUID pagination wraps after the last
eligible page, so lower newly queued UUIDs and delayed or crashed jobs are visited
again. Future backoff and live leases are excluded; expired leases are routing
candidates for the existing queue recovery policy. DELETED parents with matching
pending terminal-event jobs remain discoverable until delivery completes. Logs
use stable outcomes without exception bodies or source metadata.

Restricted contracts cover more than 100 roots, seek/wrap, delayed/service/live
lease exclusion, expired crash eligibility, API denial, unchanged root/checkpoint/
queue snapshots, and no widening of direct Worker reads. The actual graph contract
checks discovery after terminal parent publication and before leased event
readiness. The exact-image HTTP fixture enables this loop with no explicit
Organization IDs and requires actual graph/event completion and original-key
terminal acknowledgment recovery. CI suspends automatic processing during its
intentional pending/rollback scenarios, then enables it for this runtime proof.
Compilation and shell checks are recorded separately from real PostgreSQL and
exact-image execution; current CI proof remains required.

At `39b5942`, the restricted graph/observation/terminal-discovery checks passed
in [PostgreSQL job 112431137680](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37510807056/job/112431137680).
The overall job then failed while seeding the new 105-root fixture because its
User insert omitted required timestamps. The fixture now supplies timestamps for
both User and Organization; the full discovery contract still requires a passing
rerun. No product schema constraint was relaxed.

The desktop/phone deletion fixture also keeps a second authorized administrator
on the normal Organization page while the Owner requests deletion. It requires
automatic surface denial, removal of Organization/Board content and zero document
reloads. This proves a distinct acceptance path from the initiating Owner's
independent status route when executed. Current native runtime evidence is pending;
it does not establish durable terminal-event consumption or disconnected recovery.
