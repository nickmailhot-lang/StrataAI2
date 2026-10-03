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
