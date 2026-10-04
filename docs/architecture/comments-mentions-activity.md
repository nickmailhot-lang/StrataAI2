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
| COMMENT-FR-001 authorized Card comments | Owning commands, protected HTTP, forced RLS storage and guarded MUI controls implemented; scoped source/storage checks pass | Complete actual API/DB/Worker native scenarios, lifecycle races and performance acceptance |
| COMMENT-FR-002 author/content/time/edited state | PostgreSQL/HTTP DTOs and bounded MUI parser retain canonical author/history, UTC revisions and redacted tombstones | Complete native rendering and historical-readable-author/lifecycle evidence |
| COMMENT-FR-003 author edits | Author/current rights admission, dual revisions, body-free receipts, atomic audit/events and guarded UI edits implemented | Complete session/grant/parent changes under live waits, native reconciliation and performance evidence |
| COMMENT-FR-004 author deletion | Confirmed author deletion persists a redacted tombstone; original former-body receipt is refused; MUI removal/retry guards implemented | Complete actual native confirmation/replay/lifecycle evidence and retention/purge policy |
| COMMENT-FR-005 @username | Bounded plaintext tokens, current account handles and guarded account UI; owning Board-participant lookup adapters implemented; no comment recipient/notification producer yet | Native account UI, Application teammate admission/selection and atomic current-recipient publication |
| COMMENT-FR-006 @card/@board | Bounded lexical declarations exist; actual mass-mention producer unimplemented | Explicit confirmation, current authorization, bounded fanout, durable rate limits and stable refusal without partial effects |
| COMMENT-FR-007 mention notifications | Existing PRD-17 infrastructure; no comment mention producer | Atomic recipient intent, current eligibility/notification delivery, idempotent edits/replays, no protected body leakage |
| COMMENT-FR-008 immutable activity | Existing Work events/audit; no complete activity projection | Every significant domain event, immutable interpreter/projection, complete lifecycle and replay coverage |
| COMMENT-FR-009 event fields | Existing canonical Work event envelope | Current-authorized paginated activity DTO/projection with required safe metadata and complete event coverage |
| COMMENT-FR-010 historical actor deactivation | Domain retains stable author ID | Historical actor interpretation after deactivation without granting current access or replacing past attribution with mutable profile data |
| COMMENT-FR-011 paginated Board/Card activity | Unimplemented | Bounded indexed cursor reads, current scope/parents, move/archive/deletion and mid-read revocation, MUI paging |

All AC-COMMENT-15-01/02/03 and TC-01 through TC-13 remain unproven at their full
server/state/client scopes. Domain, protected HTTP, restricted database and MUI
tests prove their named checks; registered native scenarios are not execution
proof. Local warning-as-error compilation passes. Each committed slice's Linux
source, storage and immutable release gates are recorded separately below.
Windows Application Control prevents local .NET test execution and is preserved.

The remaining work includes account-setting and mention producer migrations,
atomic recipient intents and notification delivery, immutable activity,
authorization and rollback under all live waits, mention/activity MUI controls,
complete two-client/reconnect proof, native keyboard/mobile/accessibility,
error/empty/loading states, operator-safe telemetry and the unchanged PRD
performance/capacity budgets. No endpoint may expose this Domain foundation
without completing its current actor and owning transaction boundary.

The account model now has a persisted globally unique current-handle registry
with immutable alias reservations, alongside stable IDs and display names.
Account setting and recipient resolution still need their current-session and
Board scopes. Mass-mention permissions must be made explicit in Application/UI;
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

The MUI Card comments control is now integrated into Card detail. It reviews
current profile and scoped pages, renders literal plaintext and retained
history, offers only the current author's edit/removal controls, confirms body
removal, and captures both revisions plus an immutable original retry key/body.
Only uncertain responses offer original recovery; definite refusals retire the
protected review and require explicit discard/current review. Drafts and
unconfirmed commands fence competing Board/Card actions and Card closure.
Unavailable or newer contexts hide old rows; acknowledgments display only at
the corresponding current Card revision. Focus recovery respects another
control's focus ownership. There are no automatic mention recipients.

Typecheck and lint pass. All 49 selected client/Board tests pass, including
plaintext rendering, author/no-op/edit/delete policy, confirmation, immutable
retry across newer snapshots, account changes, malformed acknowledgment,
400/401/403/404/409/429 refusals, empty/read-only/stale states and actual Board
closure/competing-action guards. These component tests are not native proof.

Two desktop/mobile release-browser scenarios are registered (1280/390px).
They use actual login, commands, comment storage and event delivery; only the
first already-committed create response is replaced with 503. They assert the
same original key/body, single persisted comment, dual revisions, edits,
confirmation, redacted tombstone, refusal of the original former-body receipt,
exactly three comment events, focus and axe checks. Registration passed; native
execution remains pending the immutable-image CI fixture. No full acceptance
criterion is closed; live automatic comment-page reconciliation, readable
historical author names, mentions, activity projection, large-data and complete
release evidence still require work.

Clean opened comment views now automatically reread the first bounded page
when the Card aggregate changes or the Board reconnect sequence advances.
Old seek cursors retire; current profile/scope/revision validation runs again.
Drafts and uncertain receipts keep their original concurrency boundary rather
than being overwritten by live updates. Refresh does not request focus away
from another control. An acknowledged row retires when its Card context changes.

All 46 selected component/Board tests pass after this change, including the new
live/reconnect/focus test. Typecheck and lint pass after correcting the test
fixture's overly narrow editedAt type. The two registered native scenarios now
include a second independent authenticated browser session, automatic create/
delete delivery and recovery of an edit missed while that session is offline.
They use the actual API and separate Worker; execution is still pending CI,
and registration does not close two-client/reconnect acceptance criteria.

4551933 now passes the complete Linux Domain and API-host jobs, alongside web
quality and PostgreSQL integration. This includes the unchanged administrator
continuity test after the Demo correction and the new comment HTTP cases.
Source/image/security/native-release gates remain distinct; this is not a full
immutable release claim.

The next mention producer now defines canonical case-insensitive ASCII handles
(3–40 characters; first letter; remaining letters/digits/underscore), reserved
@card/@board namespaces and deterministic u_<account UUID> defaults. Custom
claims reserve u_ for generated handles. The account registry must enforce
uniqueness and current-handle lookup; this Domain contract does not establish
those persisted facts or expose an account setting yet. Current display names
and email addresses are never used as unambiguous usernames.

Mention tokens are bounded lexical declarations in normalized plaintext, with
UTF-16 offsets matching the browser/persisted body. Standalone @username,
@card and @board are recognized; email/URL/escape/partial unsupported-name
fragments are literal. Unknown handles remain literal. Only an explicitly
current-authorized canonical handle map can bind user references, with a
64-token/map window and at most 20 distinct direct users; repeated occurrences
retain their spans but do not establish additional distinct recipients.
These bounds do not replace durable mass-mention authorization, explicit
confirmation, rate limits, bounded fanout, current recipient eligibility or
transactional notification production. Existing comment routes still store
mention-like text literally and do not produce mention notifications yet.

New Domain cases cover namespace/reserved names, emoji/line normalization and
exact spans, mass declarations, literal email/URL/escape/unsupported fragments,
unknown/foreign map refusal and token/recipient overflow without truncation.
Warning-as-error compilation passes; their Linux execution awaits CI. The
historical-author/readable profile policy, handle registry/claim lifetime,
mention UI and all remaining full acceptance criteria stay open.

Migration 056 adds the global account handle registry required by PRD-02 and
COMMENT-FR-005/006 (PRD-15), with PRD-24 identity ownership protections and
ARCH-04 migration/runtime-role checks. Existing accounts receive deterministic
u_<UUID> defaults; restricted registration seeds new defaults atomically through
a private trigger. Current handles are unique, canonical lowercase ASCII and
revisioned. A former handle stays reserved to its original account, including
after deactivation. Only the current-handle table will resolve new mentions;
historical aliases are not additional recipient addresses.

The adopted claim policy bounds each account to 32 lifetime reservations,
including its generated default. Returning to an already owned alias does not
consume another slot. This prevents unbounded alias hoarding; it is a shared
PRD-02/15/24 behavior that the eventual account setting must explain before a
claim. @card/@board and another account's generated u_ namespace cannot be
claimed. A current-account row lock serializes its updates, and unique indexes
arbitrate cross-account collisions. A refused claim rolls back its reservation.

These two tables are explicitly global identity metadata in the tenant-catalog
classification; no tenant table loses forced RLS. The API can read current
handles and update only handle/revision/update-time columns. It cannot insert
or delete current handles, read or directly change reserved aliases, or invoke
the private trigger capabilities. The Worker has no registry access. Updates
also require the owning transaction's identity subject; this is database
defense, not session authentication. Current-session ownership and bounded
Board-scoped teammate disclosure must still be established by Application
commands before any HTTP setting or recipient resolver is exposed.

Privileged disposable-test account deletion may cascade the registry. The
runtime API cannot delete accounts; user deactivation retains the registry
and historical aliases. This does not define future account-erasure retention.
The new restricted SQL contract checks backfill/new registration, canonical and
reserved shapes, subject/revision refusals, stale CAS, no-op, immutable history,
former/current/default collision, same-owner reclaim, lifetime bounds,
deactivation retention and exact privilege denial. Ordered upgrade/repeat and
immutable-image readiness now require all 56 canonical migrations. Local
warning-as-error compilation and shell syntax validation are the available
local checks; PostgreSQL execution is required in Linux CI before a storage
pass is claimed. Full mentions, notifications, activity and acceptance remain
unfinished; existing comment HTTP still treats mention-like text literally.

Registry migration a635fcb passes its complete PostgreSQL integration job
111323487289 in run 37164139775. Actual logs confirm canonical/default backfill,
subject/CAS guards, immutable aliases, bounded reservations, deactivation
retention and restricted privileges, alongside ordered repeat/upgrade and
serialized-runner checks. Web quality also passes. This is storage evidence,
not complete immutable-image/browser/security release acceptance.

The Production account adapter now requires an owning identity transaction,
reads the exact requested account, normalizes claims, rejects reserved/foreign
generated handles, locks the current registry row and checks its revision.
Equal normalized claims preserve history; changes advance exactly one revision
at database microsecond precision. Uniqueness and policy refusals use savepoints
so they return stable errors without poisoning the caller's transaction. An
owning transaction refusal rolls back both the current handle and reservation.
This adapter does not authenticate the supplied account argument; the future
current-session Application command must prove ownership before invoking it.

A new actual C# restricted-login contract uses the existing Production identity
unit with explicitly synthetic actor eligibility. It tests scope refusal,
canonical default/history, normalization, stale/past revisions, no-op, invalid
names, atomic rollback, concurrent two-account collisions, recovery reads after
failed SQL, retained/former/default reclaim and the 32-reservation policy.
Compilation passes; its Linux execution remains pending until its own CI job
confirms the contract. Demo parity, account setting receipts/audit/events,
current-session proof, scoped teammate lookup, mention notification producers,
mass-mention abuse controls and full activity/acceptance remain outstanding.

The actual restricted Production C# handle contract now passes in PostgreSQL
job 111324137136, run 37164359628, for 21275ee. Its log confirms owning scope,
normalization, CAS/no-op, rollback, concurrent collision, former/default reclaim,
reservation bounds and recovered savepoints. The complete managed-host and
immutable release gates remain separate; the synthetic actor fixture is not
HTTP session or Board recipient authorization evidence.

Demo now has its own DI-owned account handle registry, eagerly seeded by
canonical account insertion. Its exact account transaction subject is required
for handle reads/claims. Canonical/default names, microsecond timestamps,
one-revision changes, no-op/stale/past refusal, reserved namespaces, immutable
former aliases, same-owner/default reclaim and the same 32 lifetime bound match
the storage contract. Deactivation retains aliases. Independent host providers
retain independent registries; this is not a Production fallback or a claim
that the canned Demo-reset surface resets canonical account state.

The generic owning Demo identity boundary and registration boundary now capture
and restore registered account/profile/session/token/event state, handle
current/reservation/count state and all six existing identity retry stores.
Returned failure, exception or final cancellation restores those participants;
success commits them. Nested commands entered from these scopes fail before
waiting on the shared gate. Generic commands retain existing actor admission;
they do not perform a new generic post-write session check that would invalidate
intentional logout. The future handle producer must check its current session
again before acknowledging/committing a handle change.

This is scoped identity rollback. Specialized sign-in, recovery, token-proof,
revocation and cross-module deactivation cleanup boundaries have not all gained
this rollback proof. Demo audit is still the existing no-op, and Organization/
Work lifecycle effects are not included in these identity participant snapshots.
No full Demo atomicity or audit acceptance is claimed.

Seven new Domain cases use the actual Demo DI registry and owning unit with
explicitly synthetic actor eligibility. They cover missing/foreign subject,
host isolation, namespace/normalization/CAS/history, concurrent ownership,
deactivation retention, quota/reclaim, failed-command profile/event/receipt/
reservation rollback (failure/exception/cancellation), failed registration and
retry, denied actor and nested generic/specialized commands. Warning-as-error
compilation passes after satisfying the xUnit filtering assertion analyzer.
Their execution requires Linux CI; local test execution remains blocked by
Windows Application Control and is not bypassed. The actual session command,
handle receipts/events/UI, teammate lookup and complete PRD acceptance remain
unfinished.

Production handle storage now additionally requires the exact owning identity
subject. The generic identity unit establishes an in-process subject lease only
after locking the account and verifying its actor/session; unrelated accounts
cannot be supplied to handle reads or claims inside that transaction. The lease
and transaction scope clear on every exit. This actor-bound capability is
separate from the SQL identity-subject GUC, whose value alone never grants
Application admission. The restricted C# contract now checks both foreign read
and foreign write rejection without advancing the other account. Compilation
passes; these expanded Production subject checks await their exact commit's
Linux execution. Current-session handle setting and all remaining mention/
activity acceptance are still unfinished.

7583037 passes its complete managed source job: 595 Domain and 278 API tests,
zero failures, alongside web and PostgreSQL. 4528b75 also passes all source
gates; restricted PostgreSQL logs confirm owning-subject read/write isolation
for the expanded Production handle adapter. Immutable image/security/native
release gates remain separate and do not close complete PRD acceptance.

Migration 057 adds forced account-subject RLS for immutable handle-claim retry
receipts. Stored data contains only account/key identity, a canonical request
fingerprint, user/handle revisions, Changed and finite creation/expiry metadata.
It stores no former handle, profile, credentials or response body. Retention is
exactly 24 hours from server statement time. An existing original key cannot be
overwritten, including after expiry until maintenance removes it. Reads retain
an expired marker so the future command can refuse expired recovery explicitly.
New commands use new keys; their original version fences also remain required.

Only the exact owning identity subject can invoke the Production/Demo receipt
adapters. Save uses insert-on-conflict-without-update, preserving original
fingerprints, revisions and expiry. Demo receipt state participates in account
rollback. Its process-local cache is bounded to 10,000 records and removes at
most 100 expired records when inserting a new key; an existing key is checked
before cleanup. This is ephemeral Demo behavior, not a Production fallback.

The API has subject-scoped SELECT/INSERT and no UPDATE/DELETE/maintenance
execution. The separate Worker has only expired key/expiry metadata, DELETE and
the narrow invoker purge capability under GLOBAL_IDENTITY_RETRY_CLEANUP. It
cannot read fingerprints, acknowledged revisions, Changed or private creation
metadata. Subject spoofing cannot widen maintenance into live receipts. Purge
uses a bounded indexed expiry page of 100; the existing Worker cleanup now
covers seven receipt types with a maximum aggregate result of 700. Ordered
upgrade/repeat, runtime grants and exact-image readiness require migration 057.

The new actual SQL contract checks body-free catalog shape, forced RLS,
missing/foreign subjects, canonical keys/fingerprints, positive revisions,
immutable identity/history, exact retention and expired-only private Worker
read/delete/purge bounds. The restricted C# contract exercises owning subject,
typed acknowledgment shape, original-key preservation, exact expiry, refusal
rollback and retained expired keys. Demo cases add scope/expiry/immutability
and include new receipt state in failed-command rollback. Compilation and shell
syntax checks pass; these new execution checks await their own Linux CI run.
Actor eligibility in persistence fixtures remains explicitly synthetic.

This completes receipt storage groundwork, not the account-setting producer.
Current-session checks before commit/recovery, dual account/handle version
admission, atomic user revision/audit/identity events, current-only hydration of
the acknowledgment, stable HTTP errors and MUI original-intent recovery still
need implementation. No account handle HTTP command or mention notification
producer is exposed by this slice; all unfinished acceptance remains open.

## Account handle command composition

The owning Application service now reads only the current account setting and
claims a normalized handle with both account and handle version fences. It
reproves the current actor before reads, after reads and before final success.
A changed handle advances the account revision exactly once while retaining
its existing profile fields, then appends the standard USER_PROFILE_UPDATED
audit and identity event with empty safe metadata. A current no-op advances
neither revision nor event. Handle/reservation, account, audit, event and the
immutable body-free original receipt share the owning transaction.

Original-key recovery checks normalized request identity and exact recorded
handle revision before hydrating the current handle. An unrelated later profile
edit permits recovery of the original acknowledgment; renaming away or reclaiming
the same name at a newer handle revision refuses obsolete recovery. Final
session denial, exception or cancellation rolls back the whole Demo command.
Demo actor tests are synthetic and its audit adapter remains a no-op.

The new restricted PostgreSQL command contract injects an actual audit insert
failure and checks rollback of account/handle/reservation/event/receipt, then
retries the same original key. It checks exactly one body-free audit/event,
normalized recovery, key collision, stale fresh requests, no-op and current-only
hydration after profile edits/renames/reclaims. Compilation passes with warnings
as errors; new execution evidence awaits Linux CI. Successful append-only audit
and its disposable account are retained until the isolated CI database teardown.
This service is not yet exposed over HTTP or MUI; cookie lifecycle, native UI,
scoped recipient resolution and mention notifications remain unfinished.

## Protected account handle HTTP boundary

GET/PATCH /me/mention-handle now expose only the authenticated current account.
There is no account-ID route or teammate/profile lookup capability. PATCH uses
the shared cookie authorization, CSRF header and canonical single UUID retry-key
middleware; this new command requires a nonempty key. Both endpoints and key
refusals set private, no-store. The shared Problem boundary returns stable
validation, conflict, reservation-limit, revoked-session and storage errors.

The actual host tests use separate authenticated cookie clients, concurrent
identical retries, normalized original recovery, another account's collision
and independent key namespace, unrelated profile change, rename refusal and an
original revoked cookie. Invalid/missing/repeated keys and reserved names retain
state; unauthenticated access, missing CSRF and account-ID lookup are refused.
These are Demo-host middleware/session tests, not PostgreSQL HTTP/live-lock-wait
or native MUI execution evidence. Local warning-as-error compilation passes;
their Linux execution still awaits the new commit's CI. Account UI, actual
Production cookie lifecycle under waits, scoped recipient selection and mention
notification/activity acceptance remain open.

The preceding command slice's corrected CI run 37167703326 has passed actual
restricted PostgreSQL command composition, including the injected audit refusal
and complete rollback/retry/current-only hydration assertions. The first fixture
commit omitted required account timestamps; d9488b9 supplies finite server
timestamps and the corrected contract passes. This does not substitute for the
new HTTP tests or completion of the full build-once release pipeline.

The release profile scenario now invokes a real handle HTTP contract using its
existing authenticated cookie against the exact built API and restricted
PostgreSQL runtime. It checks private current-setting responses, changed/no-op
dual revisions, original retries/collisions, actual body-free audit/event/receipt
rows, recovery after a separate profile edit, refusal after rename and protected
GET denial after deactivation. Its queries use the disposable account only;
no credential/provider fixture substitutes for actual account commands. Shell
syntax passes. Execution awaits the new immutable-image CI run. Session expiry
and revocation during live waits, native account UI and full mention acceptance
remain unfinished.

## Account handle client contract

The account client now validates the exact setting/acknowledgment shape, current
verified subject and account revision, positive safe revisions, canonical handle
and its owning generated namespace, finite UTC microsecond history and no-op or
changed acknowledgment against the original intent. It refuses foreign-account,
stale-profile, malformed, extra-field/private, unsafe-version and invented-no-op
acknowledgments. This is client admission, not server authority.

The normalized original handle, both original revisions, serialized request
body and UUID key are retained in a frozen intent with a copied frozen original
setting; changing a caller's draft cannot change the pending retry. Approved
handle refusal and invalid-key codes survive the shared bounded Problem
normalizer without retaining server private fields. Four account contract cases
and the transport/Problem checks pass locally (19 total), along with typecheck
and lint. These helpers are not yet a visible MUI control or native UI proof.

The protected HTTP commit 35733d9 now passes its complete source gate in CI run
37167855911: 602 Domain and 285 actual API-host cases, zero failed/skipped, plus
web and restricted PostgreSQL checks. The new seven host cases include actual
cookie authorization and revocation. Immutable image build/security/runtime
stages remain separate live evidence; this is not a full release success claim.

## Visible MUI account handle setting and recovery

The Profile page opens a named modal account handle setting with a bounded
plaintext field, native form submission, explicit save/review/close controls
and status/error feedback. Reads bracket the protected setting with fresh
current-account admissions and matching root versions before displaying it.
Fresh saves check the current root version; original retry recovery retains
the frozen original handle/body/key and both original revisions. Success must
match the original intent and pass a fresh protected setting read before
confirmation. A later current handle is displayed only through that fresh
admission, never copied from an obsolete acknowledgment.

Network/503/malformed acknowledgment or stalled transport/body retains the
original attempt and blocks editing and modal dismissal. Definitive refusal
retires the attempt and requires explicit current-setting review. Account
switch/revocation clears protected state and returns to sign-in. The complete
operation has a 15-second deadline; abort, unmount and operation epochs reject
late results and prevent follow-up protected requests after cancellation.
Keyboard retry focus is restored only for an interaction inside the visible
dialog; it does not pull focus from another control or a hidden document.

While the dialog is open, competing profile save/logout/deactivation commands
are refused and their controls disabled; ordinary profile polling pauses.
Closing triggers a fresh profile read. Existing unsaved profile edits survive
the resulting account-version conflict and cannot overwrite it with a stale
version. The dialog is keyed to the actual account, fencing account switches.
It is disabled while the profile/logout has an unresolved original attempt.

Local component fixtures cover committed-but-lost reply recovery with identical
key/body, transport/body deadlines and ignored late replies, no-dismiss/edit
recovery, wrong-revision acknowledgment, explicit review after conflict,
account switch before display, aborted unmount, guarded retry focus and the
actual Profile-page interaction with unsaved edits. These are client protocol
fixtures, not actual browser/API/DB native execution. Typecheck/lint pass;
37 selected dialog/Profile/deactivation component cases passed; the final focus
addition also passed its eight-case dialog/Profile-interaction rerun. Native desktop,
mobile, keyboard/axe and Production live-wait session checks remain required.

Release runs 37168007618 and 37168307460 failed the newly added fixture because
its 37-character canonical handle plus four padding spaces exceeded the
Domain's 40-code-unit raw-input bound. bc81736 changes only the fixture padding
to one space on each side (39 total), preserving the server/client bound. Shell
syntax and the actual generated-length assertion pass. Corrected run
37168895243 is live; exact-image execution remains unproven until it passes.
No full ticket or acceptance criterion is closed by this UI slice.

## Native account handle release scenarios

Two Playwright cases now register/login actual release accounts and drive the
Profile-page dialog at 1280px and 390px. They submit with real keyboard events,
check dialog accessibility and page overflow, then let the actual handle PATCH
commit before replacing its first response with 503. Recovery must focus the
original-retry control, keep editing/dismissal and competing account actions
disabled, and resend the identical key/body. Actual protected reads and the
identity stream must show one changed handle/account revision and one empty-
metadata USER_PROFILE_UPDATED event after the duplicate HTTP calls.

The scenarios retain an unsaved profile draft across modal closure, require
explicit review after an actual peer profile mutation, submit a fresh handle
intent with the new root revision and independent key, refuse the original
former-alias receipt after rename, then revoke the actual session and require
sign-in before another handle command can be sent. No synthetic response
supplies account/handle/receipt content; only the committed first reply is lost.
Both cases register successfully with Playwright. This is registration evidence,
not native execution success. The full exact-image CI must execute them.

Corrected fixture commit bc81736 has passed source gates and immutable image
build in run 37168895243; security/container checks are live. UI commit 2a530b4
has passed web/PostgreSQL checks in run 37169116417 while managed tests are live.
No broad release, native, live-wait or complete ticket claim follows from this.

## Actual handle command session lifetime during database waits

The exact-image identity transaction fixture now records an original no-op
handle receipt without changing the existing account/event fixture. Using the
actual cookie and restricted API login, it admits a request, observes its real
PostgreSQL account-row lock wait, then revokes or expires that original session
before releasing the lock. Current-setting GET, a fresh changed claim and
original receipt recovery must each return session_unavailable without handle
content or account identity. Exact before/after account/audit/event/profile-
receipt and handle/reservation/handle-receipt state must match. An independent
current session of the same active account remains readable.

A separate late-expiry case holds the handle row, gives the actual cookie a
finite 15-second future expiry and observes the real handle CAS wait after
initial actor checks. It asserts the session is still valid at that wait, then
waits for database time to cross expiry before releasing the row. The producer's
final admission must refuse the acknowledgment and roll back its tentative
alias/account/audit/event/receipt despite owning the account/session locks.
The fixture uses existing CI-only lock/cleanup helpers and restores a fresh
session for subsequent tests. Shell syntax passes; execution remains pending.

Corrected release run 37168895243 has now passed its actual PostgreSQL profile
step, which invokes the complete corrected handle HTTP contract and post-
deactivation protected-read denial. Its later runtime stages are still live;
the new wait cases and native UI scenarios require their own new exact-image run.

## Current Board participant handle metadata

A separate read-only Work port searches literal canonical handle prefixes with
20 results plus one lookahead and resolves at most 64 lexical targets. It
requires an owning Organization transaction and joins current handles through
active Board and Organization membership, active accounts and an explicit
verified-email policy. Returned metadata contains only stable user ID, current
handle and revision, and display name. Former reserved aliases never resolve.
Ordinal seek anchors must belong to the searched prefix; underscore is literal,
not a SQL wildcard. Duplicate exact targets collapse to one account.

The global current-handle table is trusted runtime metadata, not a forced
subject-RLS table. Account writes retain the owning identity lease and database
subject/revision trigger. This Work projection uses existing forced tenant RLS
membership joins; it adds no global HTTP lookup or database privilege. Caller
session, Card context, current parent lifecycle and COMMENT participation need
Application admission before and after this storage read. Recipient eligibility
must also be freshly checked at atomic notification publication.

Demo discovers eligible participants through the existing membership adapter
and only then reads their current handles. It traverses membership pages while
retaining at most 21 search rows or 64 exact targets. A real Demo adapter test
covers 60 participants across pages, tenant/Board mismatch, explicit shared
participation, current-only rename, email policy and removed/deactivated members.
The new restricted PostgreSQL contract separately seeds 30 participants and
checks prefix/seek ordering, current revisions, all eligibility filters, former
alias exclusion, shared-user isolation and input/output bounds. Both fixtures
use synthetic transaction admission and do not prove actual cookie authorization.

Full local compilation passes with zero warnings/errors. Linux execution for
this slice remains pending. On previous main 3aeabb2, source, restricted database,
immutable image build and security gates pass in run 37169585086. Its exact-image
identity transaction step has passed the actual session revocation/expiry and
late-expiry handle wait fixtures; browser stages are still pending. No complete
PRD requirement, native browser proof or release pass is credited here.

## Application admission for teammate options

CardMentionOptionsService now admits teammate discovery only within the owning
Work transaction, through current COMMENT editing rights, active Card/List/Board
and Organization context, explicit active Board membership and the shared
current-session checks before and after the read. The caller cannot use public
visibility or Organization administration as a substitute for participation.
Search inputs normalize before storage; bounded cursors bind exact Card ID,
Card revision, canonical prefix and current-handle seek anchor. A stale Card
revision requires fresh review. Results remain metadata hints and confer no
recipient authorization on a later comment command. No HTTP route is added by
this slice.

The Demo fixture adds actual Application calls for normalized lookahead/seek,
cross-prefix and malformed cursors, invalid prefixes, governance without Board
membership, initially unavailable and mid-read revoked synthetic session actor,
Card revision changes and archived Card refusal. These exercise the actual
Application and Demo adapter with a synthetic authorization probe; protected
cookie/DB wait/native options evidence remains required. Local full compilation
passes with zero warnings/errors; Linux execution of these additions is pending.

Storage commit fd23dac has passed actual restricted PostgreSQL integration in
run 37170517190. Its new current-member contract emitted its full passing summary
for active membership/account/email filtering, literal prefix bounds, seek,
current-only handles and shared-user isolation. Managed/web/release gates are
still being inspected. No complete ticket or mention notification is claimed.

Application run 37170679196 passed restricted PostgreSQL integration but failed
its new Demo test before Application admission because the fixture omitted the
IWorkCommandContext needed by the canonical Board authorization service. The
fixture now registers that actual interface with a null read-only retry key;
product admission and all assertions remain unchanged. Full local compilation
passes; corrected Linux execution must pass before crediting the service test.

## Protected HTTP teammate options

Authenticated GET /cards/{cardId}/mention-options now invokes the owning Card
Application boundary using only the cookie actor, with bounded prefix/after
query inputs and no-store responses. Repeated query values fail with stable
Problem codes; telemetry uses an approved operation/error label and retains no
handles or search text. Returned items contain exactly userId, handle,
displayName and handleVersion, alongside the admitted Card context/cursor.
This is an internal current-comment capability, not a global account directory.

The real-cookie Demo API fixture checks authentication, canonical current
handles and exact metadata keys, invalid/repeated/empty cursors and prefixes,
exact current prefix filtering, Board membership revocation, remaining eligible
participants and archived Card refusal. It uses the actual API session and
Application/adapter, but not a Production database or native selector. Local
warning-as-error compilation passes; Linux host/release execution is pending.
Mention UI, atomic recipient notifications, mass-mention controls and complete
activity acceptance remain outstanding. No ticket or full criterion is closed.

## Client teammate response admission and exact-image read fixtures

The teammate client contract now admits only the exact current Organization,
Board, Card and safe positive revision, canonical literal prefix, at most 20
current handle options and a continuation anchored to the final delivered
handle. It rejects unexpected/private fields, foreign or duplicate identities,
noncanonical/default handles belonging to another user, invalid revisions,
unsorted or repeated anchors, stale/cross-prefix/Card cursors and oversized
metadata. It copies and freezes the admitted page/items so later mutable
response objects cannot replace an already reviewed identity. Display names
are metadata, never handle authority. This is response admission for the coming
selector, not a mention notification producer or recipient authorization.
Approved prefix/cursor Problem codes pass through the shared bounded safe
normalizer; private error details remain excluded. Typecheck/lint and 16 selected
response/Problem cases pass locally.

The already registered exact-image assignment fixture now reads mention options
using the actual member cookie and restricted API, checks 20+20+12 prefix pages
against exact Card context, current default handles, exact metadata keys, unique
ordinal order and final-row cursors, excludes all removed/deactivated fixtures,
checks exact prefix filtering, and rejects malformed/repeated input/outsiders.
Its existing complete Work-state snapshot must match across those reads.
Additional controlled Board lock waits revoke Board membership, Organization
membership or actual sessions before release; responses must refuse without
items/handle/displayName/Card revision. Membership/cookie fixture state is
restored for subsequent checks. Shell syntax passes; new exact-image execution
remains required and is not inferred from registration.

Protected HTTP commit 7274e99 now passes all source gates in run 37170838129:
603 Domain and 286 API tests, zero failed/skipped, restricted PostgreSQL and web
quality. This includes the corrected current COMMENT options Application case
and the actual-cookie API case. Immutable image build is still running; complete
release/native/mass-mention/notification/activity/performance acceptance remains
open.

## Bounded recipient identity and edit delta

CommentMentionRecipients captures immutable normalized-body UTF-16 references,
current distinct stable recipient IDs and newly added non-self IDs. The prior
committed revision's recipient snapshot must be unique, nonempty IDs and at
most 20 accounts; current resolution retains the same 20-account bound. Editing
without adding a stable recipient does not repeat a notification, even if that
recipient's handle changes. Removing and later readding an account after an
intervening snapshot without it creates a new delta. Self references retain
identity in the snapshot while never creating a notification. Repeated tokens
retain their own offsets but collapse to one recipient/delta. Unresolved handles,
former aliases and lexical email/URL text remain literal.

CardCommentMentionPlanning uses the owning Work current-handle adapter and the
actual verified-email policy to prepare this delta. It exposes @card/@board as
explicit declarations needing separate mass authorization/confirmation/fanout
and rate policy; they do not become username recipients. The planner has no
public route and does not publish notifications or grant later authorization.
The existing comment command still treats mention-like text literally until
snapshot persistence, current-target revalidation and notifications are wired
atomically with the comment/event/audit/receipt. The durable snapshot must not
retain comment body and must remain available as the preceding revision's
identity comparison, independently of mutable usernames or deactivated profiles.

Domain tests cover edit/rename/duplicate/self/removal/readdition semantics,
immutable copies, bounds/history refusal and nonauthoritative declarations.
The actual Demo adapter fixture invokes the Application planner inside its
owning transaction, checks current-only renamed/removed/deactivated recipients,
identity deltas, mass flags, repeated original recipients and the 21-recipient
refusal. The restricted PostgreSQL contract invokes the same planner with actual
tenant storage, checking all membership/account/email/former-alias exclusions,
exact identities/offset count, edit/self delta and oversized fanout refusal.
Admission in these fixture units is synthetic; this is not Production cookie
publication or native selector proof. Full local warning-as-error compilation
passes. Linux execution for these new additions remains pending. No complete
PRD acceptance or mention notification publication is claimed.

## Durable body-free recipient snapshot schema

Migration 058 adds tenant-scoped comment_mention_snapshots and relational
comment_mention_recipients. An immutable header records exact comment revision,
Card, finite comment-update timestamp and declared recipient count (0 through
20); children retain stable nonempty recipient IDs without comment text or
mutable handles/profile data. Composite foreign keys bind the comment/Card/
Organization, snapshot revision and recipient Organization membership. Both
tables force tenant RLS. Normal trigger functions retain the same RLS context;
none grants global lookup or security-definer elevation.

Snapshot insertion requires the actual current comment revision and timestamp
under a comment-row share lock. A deleted comment can retain only an empty
snapshot. Deferred constraints require the declared child cardinality to match
at commit, allowing the owning command to insert header and children together
while refusing incomplete/oversized effects. Prior complete snapshots are not
replaced when another revision is saved. Runtime receives SELECT/INSERT only;
UPDATE is additionally fenced by immutable triggers and runtime DELETE is
unavailable. Worker receives no snapshot access. Future governed purge needs
its own authority; these grants do not provide one.

Readiness requires all 58 migrations. Exact-image missing-ledger refusal and
restore fixtures include 058; migration repeat/forward upgrade and serialized/
failure fixtures were advanced consistently. CI now executes a restricted-role
snapshot SQL fixture for tenant reads/writes, revision/time fences, immutable
history, duplicate/foreign recipients, incomplete command rollback, retained
prior IDs alongside an empty current snapshot and no-context/no-delete denial.
Actual runtime grant assertions separately check API SELECT/INSERT only and no
Worker access. Shell syntax and full local warning-as-error compilation pass.
Actual Linux SQL/release execution of this migration is pending. Storage
adapters, current-target locking and atomic comment/notification integration
remain required; existing comments still do not publish mention notifications.
No complete acceptance criterion or ticket is closed by the schema alone.

Planner commit 311bf72 has now passed its actual restricted PostgreSQL integration
in run 37171614912, including current eligibility and exact stable edit/self
recipient delta. Managed tests are still live; this does not prove durable
publication or complete native/release acceptance.

## Owning snapshot persistence adapters

ICommentMentionSnapshotStore now exposes only an owning-transaction exact
Organization/Card/comment/revision lookup and immutable append. Its typed
snapshot copies/sorts at most 20 unique nonempty recipient IDs, validates parent
identity and UTC microsecond timestamp, and contains no body/profile/handle.
The PostgreSQL adapter reads a bounded 21-row corruption sentinel and checks
actual header cardinality; complete metadata retries match the retained header
and recipients exactly. A differing Card/time/target set cannot replace that
revision. New header and child inserts share the owning restricted tenant
transaction and existing database revision/cardinality/FK guards. Exact
historical retries perform no write after a later comment revision.

Demo implements the same scope/current comment/time/recipient membership and
immutable matching rules, including historical retries and empty snapshots.
Its dictionary is now included in canonical Work rollback alongside comments.
It does not add current-recipient admission: retained membership exists even
after removal; fresh eligibility and target locks remain producer obligations.

Four actual Demo storage/owning-unit cases cover failed result, exception,
cancellation and late synthetic actor revocation restoring comment and snapshot
together, exact duplicate retry, immutable copied metadata, body-free serialized
snapshot, empty current revision retaining prior IDs, different revision target
refusal, malformed/bounded IDs and wrong-Card reads. A new restricted C# contract
checks owning scope, real FK failure rolling back a tentative header, comment/
snapshot command refusal rollback, exact/historical retry, immutable mismatch,
empty snapshot history and tenant/Card isolation. Its administrative cleanup is
one explicit transaction so deferred cardinality checks see the completed
fixture purge. These fixture actor predicates are synthetic, not HTTP cookie
or actual recipient publication proof.

Full local warning-as-error compilation passes. Linux execution of the new
adapters remains pending. Schema commit 62e26ac has passed source gates and its
immutable image build in run 37171932768; security/container stages are live.
No mention notification, complete release/native pass or ticket closure is
claimed. Current-target locking, atomic notification effects, mass-mention
policy and native teammate controls remain necessary before full acceptance.

## Current recipient publication revalidation

The internal mention-member port now has a separate producer-only lock operation
bounded to 20 exact current username targets. PostgreSQL takes share row locks
on the selected account, current handle, Board membership and Organization
membership, with the same active-account/membership/verified-email policy and
forced tenant joins. Locks stay in the owning mutation transaction. Competing
updates must wait or fail; a rename/removal that wins before selection produces
fresh eligibility, not an obsolete metadata promise. This does not establish
caller/session/Card authorization or mass mention permission.

The plan retains an immutable copy of reviewed user IDs, current handles and
handle revisions. Revalidation requires the exact eligible identity/handle/
revision set after locking; a missing/removed account or changed handle yields
mention_targets_changed, including rename-away/reclaim of the same spelling.
Display-name changes do not grant recipient authority. Producer callers must
perform initial/final Card/actor admission, retain snapshots and publish all
notification/event effects atomically. Unknown handles remain literal rather
than selecting arbitrary global accounts.

Demo rechecks current eligibility under its owning Work gate; global identity
commands do not share that gate, so this slice does not claim PostgreSQL row-lock
semantics for Demo. Its fixture covers fresh validation, rename refusal,
rename/reclaim revision refusal and fresh explicit review. The actual restricted
PostgreSQL C# contract now validates the plan, opens independent administrative
transactions and verifies lock_timeout refusal for account, handle and both
membership updates while the owning scope holds its locks; it then renames a
recipient and requires original plan refusal. Full local warning-as-error
compilation passes; new Linux lock/revalidation execution remains pending.

Snapshot adapter commit 610f68b now passes source gates, immutable image build
and security in run 37172378145. Its actual restricted C# snapshot contract passed
owning/exact retry, real FK rollback, comment/snapshot rollback, retained prior
identity, empty current revision and tenant/Card isolation. Later container/
native release acceptance remains live. No full requirement or ticket is closed.

The authoritative PRD-15 and PRD-17 issues were reread before integration.
MENTION_CREATED must be a real relevant domain event; simply relabeling the
existing COMMENT event notification is not complete event coverage. Mention
notifications remain in-app; external email/push is outside PRD-17 scope.
Mass mention rate control/fanout and its recipient history need separate full
implementation beyond the direct username window; the 20-username bound cannot
be used as proof of arbitrary Board mass mention support.

### Username mention command publication

Comment create/edit/delete now retain the current recipient snapshot in their
owning Work receipt transaction. Current eligible username targets are locked
before effects and revalidated after publication; edits compare stable user IDs
with the previous revision. New references produce a separate content-free
MENTION_CREATED Card event at the same revision as the COMMENT event. Only newly
added non-self recipients receive durable in-app notifications. Removal and
redaction append empty snapshots while retaining earlier recipient history;
unchanged bodies and exact receipts produce no duplicate event or notification.
Self references remain in history and can produce the source event, without an
inbox item. Unknown/former aliases remain literal. Existing pre-feature comments
have no delivered-recipient snapshot; their first changed revision starts history.

Migration 059 admits the mention notification type while retaining the original
source-event FK, exact source metadata verification, tenant isolation and reminder
self-action exception. API/Worker readiness requires the complete 59-migration
ledger. Migration repeat/upgrade/failure and missing-ledger checks include it.
The MUI inbox labels and validates the new type under its existing recipient,
canonical Card link and self-action checks; Board synchronization uses the generic
content-free contiguous event envelope and needs no event-type exception.

Local warning-as-error build passes with zero warnings/errors; web typecheck,
lint and all 32 inbox tests pass. Added real-cookie Demo HTTP coverage for exact
retry, private recipient inbox, no body disclosure/self inbox, and fresh Board
revocation; whole-command Demo tests cover recipient delta/removal/readdition,
redaction and late actor refusal after actual notification insertion followed by
same-key recovery. Linux execution for these new cases is pending. Production
whole-command lock waits, exact-image HTTP/native execution and capacity evidence
are still required. Demo identity writes do not share PostgreSQL recipient row
locks; its fresh rechecks are not proof of Production concurrency semantics.
Mass @card/@board tokens remain literal until separate policy/confirmation,
rate-control, full-fanout history and disclosure are implemented. Activity feeds,
native mention selection and full PRD-15/PRD-17 acceptance remain open.

The prior lock commit 554550d has now passed web, .NET, actual restricted
PostgreSQL recipient row-lock tests and immutable image build in run 37173017212;
container/security stages remain live. No full ticket is closed by this slice.
