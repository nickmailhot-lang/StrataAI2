# PRD-15 comments, mentions and activity acceptance

The authoritative ticket is #16. This producer belongs to the first dependency
cycle in `docs/ticket-dependencies.md`, alongside PRD-08, PRD-17 and PRD-24. The
adopted modular monolith, PostgreSQL/RLS, transactional outbox, separate Worker
and MUI architecture remain unchanged. No requirement or acceptance criterion
is closed by this foundation.

CardComment now defines immutable Organization/Card/author identity, bounded
plain-text content, UTC timestamps and independent comment revision. Only the
author can request an edit or body-redacting deletion. Current authenticated
session, Organization/Board participation, active parents and object permissions
are additional Application admission requirements; Domain author equality does
not authorize an HTTP request. A Card move changes its current authorization
context rather than duplicating an obsolete Board ID in the comment identity.

Content is limited to 10,000 UTF-16 code units before normalization, accepts
valid Unicode including emoji and literal markup, rejects malformed surrogate
pairs and unsupported controls, normalizes line endings and trims outer
whitespace. MUI must render text safely; literal markup, URLs and mention-like
strings do not execute, fetch external content or establish notification
recipients. This is an input bound, not a large-data performance claim.

An actual edit consumes one comment revision and sets editedAt. Equal normalized
content is a no-op. Stale revisions, foreign authors, backwards timestamps and
invalid input preserve state. Deletion consumes one revision, redacts content
to NULL and retains stable author/created/edited/deleted attribution. It cannot
restore the body, and a repeated current-revision author deletion is a no-op.
HTTP original-retry recovery must use the durable command receipt; calling the
Domain method with a stale original revision is not receipt recovery. Activity
and audit must retain attribution without copying deleted/private comment
bodies into public event or notification payloads.

## Complete scope and evidence required

| Requirement | Current state | Evidence still required |
| --- | --- | --- |
| COMMENT-FR-001 authorized Card comments | Domain producer exists; no route or persistence yet | Current actor/Board participation, active parents, atomic create, forced RLS, authoritative MUI result |
| COMMENT-FR-002 author/content/time/edited state | Domain identity, content and timestamps implemented | Tenant-safe persistence/DTOs, exact UTC revision reads and bounded metadata disclosure |
| COMMENT-FR-003 author edits | Necessary author/revision/validation rules implemented | Current session/parent checks before and after lock waits, dual Card/comment concurrency policy, atomic audit/event/receipt, UI reconciliation |
| COMMENT-FR-004 author deletion | Domain body-redacting tombstone implemented | Explicit confirmation, current rights and revision checks, atomic persisted tombstone/history, replay and safe MUI removal |
| COMMENT-FR-005 @username | Unimplemented | Canonical unambiguous teammate handle/selection, current authorized recipient resolution, bounded structured references and safe UI |
| COMMENT-FR-006 @card/@board | Unimplemented | Current authorized mass-mention policy, bounded fanout, durable rate limits and stable refusal without partial effects |
| COMMENT-FR-007 mention notifications | Existing PRD-17 infrastructure; no comment mention producer | Atomic recipient intent, current eligibility/notification delivery, idempotent edits/replays, no protected body leakage |
| COMMENT-FR-008 immutable activity | Existing Work events/audit; no complete activity projection | Every significant domain event, immutable interpreter/projection, complete lifecycle and replay coverage |
| COMMENT-FR-009 event fields | Existing canonical Work event envelope | Current-authorized paginated activity DTO/projection with required safe metadata and complete event coverage |
| COMMENT-FR-010 historical actor deactivation | Domain retains stable author ID | Historical actor interpretation after deactivation without granting current access or replacing past attribution with mutable profile data |
| COMMENT-FR-011 paginated Board/Card activity | Unimplemented | Bounded indexed cursor reads, current scope/parents, move/archive/deletion and mid-read revocation, MUI paging |

All AC-COMMENT-15-01/02/03 and TC-01 through TC-13 remain incomplete at their full
server/state/client scopes. Domain tests cover only their named necessary rules.
Local warning-as-error compilation passes; new Linux test execution is pending.
Windows Application Control prevents local .NET test execution and is preserved.

The remaining work includes ordered migrations and restricted grants, atomic
idempotent commands and event/audit/notification production, immutable activity,
authorization and rollback under live waits, safe MUI comment/mention/activity
controls, two-client/reconnect recovery, native keyboard/mobile/accessibility,
error/empty/loading states, operator-safe telemetry and the unchanged PRD
performance/capacity budgets. No endpoint may expose this Domain foundation
without completing its current actor and owning transaction boundary.

The account model currently has stable IDs and display names, without a proven
unique @username contract. Recipient resolution and mass-mention permissions
must be made explicit in the subsequent Application/account/UI implementation;
they must not infer authority from arbitrary body text, display-name uniqueness,
an email address or a current generic Board view permission. These unresolved
cross-PRD choices do not waive mentions or their notifications.

Migration 055 now adds metadata-only card_comments with forced tenant RLS,
tenant/Card and tenant/author-membership foreign keys, a Card cursor index,
bounded nonblank plaintext and finite timestamp shape. A revision trigger keeps
ownership/createdAt immutable, admits only one-revision edits and body-redacting
deletion, preserves edited history on deletion and refuses deleted-body revival.
The API runtime receives SELECT/INSERT/UPDATE without hard DELETE; the Worker
receives no comment table privileges. This is a storage boundary, not current
session/Board authorization or an HTTP command implementation.

The readiness ledger now requires all 55 migrations. Upgrade/repeat fixtures
include 055, and synthetic serialization/failure/unrecorded fixtures move to
056/057/058. Exact-image missing-ledger checks include 055. New PostgreSQL CI
checks exercise both tenants, cross-tenant writes, foreign Card/author affinity,
malformed content/transitions, immutable ownership, stale revision evidence,
valid edit/redaction, retained history, no revival, no hard DELETE and missing
tenant context. Real runtime login tests check the exact API/Worker grants.
Local warning-as-error compilation and script syntax pass; new actual migrated
PostgreSQL execution remains pending CI. No acceptance criterion is closed.

04fdca9 now passes the actual PostgreSQL job, including migrated comment shape,
forced RLS, immutable author/history, redaction and runtime-grant checks, plus
ordered upgrade/repeat/serialization and the existing full persistence chain.

The Production adapter now implements owning-scope create/find, at most 51 rows
of newest-first timestamp/ID seek metadata, author/version edit CAS and redacted
deletion CAS. It validates create/edit content through CardComment, preserves
tombstones/edited attribution and refuses outside-scope access. Equal body,
original-receipt recovery, current session/Board/parent permission and atomic
Card/audit/event/notification effects remain the Application command's duties;
no HTTP command is exposed and the Demo comment adapter remains unfinished.
New actual restricted-adapter checks cover round trips, foreign tenant/author,
stale/no-op/deleted CAS, late owning-transaction refusal rollback and 64 rows
including tied timestamps across lookahead/seek pages. These are metadata
fixtures, not evidence of current Board/session HTTP authorization or client
acceptance. Local warning-as-error compilation passes; new Linux execution is
pending CI.

7c0ddd4 now passes the actual restricted PostgreSQL adapter contract: owning
scope, normalized text, foreign tenant/author refusal, revision CAS, rollback,
redaction, retained attribution and bounded tied seek paging. Web quality also
passes; the remaining managed/API/image gates are tracked separately.

Demo now registers the comment store against the same owning Work unit. Its
metadata contract matches bounded seek paging, author/revision CAS and redacted
history. Failed commands restore comments, shared Work state, events/stream
positions and notifications before releasing the command gate; failed keys
remain retryable. Final permission/session checks and cancellation run before
successful receipt retention. Tests cover returned refusal, exception and late
session refusal, then retry and replay the original key with exactly one event
and notification. The event fixture uses existing assignment production and
proves rollback, not comment mention delivery.

These Demo stores are ephemeral for the canonical host lifetime. The canned
DemoState reset does not currently reset canonical Work stores. Rollback covers
only the registered Work/event/notification participants; reminder/watch stores
and the existing no-op Demo audit implementation remain outside this claim.
No performance budget or full comment acceptance criterion is closed. Local
warning-as-error compilation passes; new Demo runtime tests await Linux CI.

The Application producer now defines authenticated internal list/create/edit/
delete operations. COMMENT requires active explicit Board participation in
addition to current Organization membership/session, Board rights and active
parents. Organization governance and PUBLIC view alone do not grant COMMENT.
Author equality is necessary for edit/delete, which also require current edit
rights. Confirmed body-redacting deletion uses the author's policy rather than
elevated parent deletion; tombstones are retained and cannot be revived.

Commands CAS the Card aggregate and comment revision in the owning transaction,
then append content-free Card invalidation plus Comment audit attribution.
Equal normalized edits and repeated current-revision deletion are no-ops.
Reads expose at most 50 items with version-bound, bounded timestamp/UUID seek
cursors. Read admission remains authenticated internal Organization/Board view;
archived parents are read-only. Original successful receipts are disclosed only
if current comment metadata/body still exactly matches, preventing recovery of
an edited/redacted body; unrelated newer Card versions permit exact recovery.

New Application tests use actual Demo stores/unit/Board policy with a controlled
session verifier: normalization, dual revisions, no-op, confirmation, replay,
redaction, content-free events, invalid input/cursors, foreign author, governance
without participation, session loss, archive and Board membership revocation.
Compilation passes; new runtime execution is pending Linux CI. No HTTP route,
mentions or complete activity projection is exposed by this producer slice.
Durable receipt retention/redaction needs an explicit policy before full release;
receipt admission prevents body replay but does not erase stored former receipts.

9755d1b passes the Linux Domain test step, including the three actual Demo
Application producer cases. The shared Demo rollback cases also pass on
851bb0c; PostgreSQL and web quality pass on that commit. API/image gates remain
live and are not represented as a fully green release.

Authenticated GET/POST/PATCH/DELETE comment routes now use the shared Problem,
retry-key and session boundaries, no-store responses and content-free bounded
operator tags. API-host tests exercise real login/membership, author refusal,
invalid/stale input, confirmation, normalization, original replay, retained
redaction, archive/restore and membership revocation. Compilation passes; these
new HTTP tests await Linux execution. MUI, mentions and complete activity remain
unfinished, and no full acceptance criterion is closed.

The receipt retention risk identified above is now addressed for new comment
commands: receipts contain IDs/revisions/changed status only, never comment
plaintext. Responses hydrate the current row at the recorded comment revision
through a separate currently authorized read. Edits/deletion or scope/session
loss refuse old recovery; unrelated newer Card versions preserve the original
receipt version. A change between commit and hydration can yield a stable
unavailable response; the same original key remains the recovery mechanism.
Tests inspect the actual retained Demo receipt and serialization round trip.
No HTTP comment commands existed before this body-free receipt change, so no
historical externally submitted comment body receipts require migration.

CI on 851bb0c reported one API regression: concurrent administrator self-
demotions both returned 404 because generic post-command replay admission
required the grant that the valid command intentionally retired. The generic
fresh-command post-check is removed to match the Production unit contract;
producer-specific final admission remains authoritative. Comment Complete
retains explicit current rights/session checks, and rollback on its returned
refusal/exception/session loss remains intact. The shared unit still checks
current session/cancellation before receipt retention and reauthorizes replay.
The existing administrator continuity test remains unchanged and must pass in
CI; this is a correction to the Demo change, not a weakened acceptance test.

The browser response boundary now validates scoped comment pages and command
acknowledgments before MUI may display them. It checks exact payload shape,
current Card revision, author identity, finite microsecond history, bounded
Unicode plaintext, body-free tombstones, dual revision/no-op semantics and
explicit deletion confirmation. Pages enforce a 50-item bound, descending
createdAt/UUID order, uniqueness and exact version-bound seek continuation.
Literal markup and mention-like text stay plaintext; this parser does not
resolve recipients, render controls or grant authorization.

Four browser contract tests pass locally, covering normalization (including
valid emoji and .NET/JavaScript whitespace differences), malformed surrogate/
control/oversize input, foreign/stale/extra metadata, redaction/history,
lookahead/tied cursor boundaries and mutation acknowledgments. Typecheck and
lint pass. MUI controls and native accessibility/collaboration are still required.
