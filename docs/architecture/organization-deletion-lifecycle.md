# Organization deletion completion — implementation contract

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-010,
[PRD-18](https://github.com/nickmailhot-lang/StrataAI2/issues/19) and
[ARCH-07](https://github.com/nickmailhot-lang/StrataAI2/issues/88) require
completion beyond the [deletion request acknowledgment](organization-deletion-retries.md).
This defines the next implementation; terminal processing is not implemented.
A 202 remains an acknowledgment of the accepted request.

## State and graph treatment

ACTIVE transitions to DELETING only through the current Owner/version command.
DELETING withdraws ordinary Organization and descendant access immediately and
suspends reminders. The completion implementation adds an irreversible DELETED
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
restricted leased page implementation and its full graph processing must exist
before accepted product requests enqueue these jobs. Terminal deletion and
completion observation remain pending.
