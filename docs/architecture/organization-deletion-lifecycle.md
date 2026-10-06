# Organization deletion completion — implementation contract

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-010,
[PRD-18](https://github.com/nickmailhot-lang/StrataAI2/issues/19) and
[ARCH-07](https://github.com/nickmailhot-lang/StrataAI2/issues/88) require
completion beyond the [deletion request acknowledgment](organization-deletion-retries.md).
This defines the completion implementation. Publication, graph stages and the terminal storage gate have infrastructure/Worker
implementations. Product API completion is not enabled.
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
source evidence only; runtime execution remains pending CI. Restricted PostgreSQL
progress storage, transactional request publication, production Worker registration,
graph processing, terminal events and completion observation remain to implement.
No deletion jobs are enabled or published by this Application foundation.

Migration 088 adds immutable accepted-request references and a forced-RLS
progress checkpoint. Initial storage admission requires the current DELETING
version and active Owner, with parent-before-membership gates. The initial
checkpoint must be ATTACHMENTS with the first step equal to the request, no
cursor or completion and version one. API grants allow only initial insert/read;
Worker grants allow reference/progress reads, without correlation content or
progress mutation. Lease-fenced mutation capabilities remain to implement.
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

Publication is not registered or invoked by the production API yet. The
restricted leased page implementation below must pass its runtime checks, and
product recovery/observation must be implemented before enabling the full flow. Terminal deletion and
completion observation remain pending.

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
Production API publication, live lifecycle event consumption, independent Owner
completion observation and full exact-image acceptance remain required before
enabling the complete product flow. Existing deletion acknowledgments remain request acknowledgments.

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
consumption or Owner completion observation. Product API publication remains disconnected until graph-stage runtime evidence
and the remaining recovery/observation flow are implemented and verified.

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
and terminal readiness after actor deactivation. Runtime results for the added
selected preview-backed cover/image fixture and explicit private-helper privilege
assertions remain pending CI. Large mutation throughput, HTTP/product integration
and live browser completion require further evidence before PRD acceptance.

The product API does not publish deletion work yet. Existing 202 acknowledgments
still confirm the request. Demo parity, independent Owner completion observation,
two-client invalidation/reconnect and exact-image acceptance remain unfinished.
