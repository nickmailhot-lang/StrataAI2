# PRD-18 acceptance audit

Current status (2026-10-08): open, **16% estimated work remaining**. This is a planning estimate. Earlier estimates below record the scope and evidence available at those points; current immutable-image acceptance and the remaining integrated matrix still govern closure.

## Deleted List child-receipt admission

The [executed parent receipt correction](prd-05-acceptance.md#deleted-list-child-receipt-admission-correction)
fixes ordinary Card edit/restore acknowledgments being returned beneath a deleted
List. List deletion retains child records; the transactional command guard now
requires a surviving List before retrieving any Card command acknowledgment.
Archived-parent original recovery remains read-only and unchanged.

All three new visibility API cases fail before the correction and pass afterward.
The existing archived-parent lifecycle and concurrent retry/revocation cases also
pass; the final Release build has zero warnings/errors. The expanded restricted
PostgreSQL script reproduces the old defect and then passes its complete three-
visibility invocation against the corrected compiled Production API. It preserves
the deleted List and retained active Card, refuses original edit/restore receipts
and fresh commands, and leaves protected state/audit/events/jobs/receipts unchanged.

This is local compiled/cached-runtime evidence, not retained-current-image,
separate Worker/browser or full concurrent parent-withdrawal proof. The mandatory
build-once CI script includes the regression; PRD-18 remains open at **22%**
estimated work remaining, a planning estimate.

## Existing lifecycle evidence

### Deleted-parent comment, checklist and URL receipts

The [sibling receipt coverage](prd-05-acceptance.md#deleted-parent-sibling-receipt-coverage)
passes the complete real PostgreSQL permission-recovery invocation across all three
Board visibilities. Original child creation receipts recover unchanged before List
deletion; original receipts and fresh current-version commands are withheld after
deletion, without private content or persistence effects. The script compares
retained child rows as well as parent, stream, audit/event/job and receipt state.
The disposable API is removed after execution; existing services and volumes are
preserved. This extends TC-07/10 coverage without changing lifecycle policy or
claiming full current-image/concurrency/browser acceptance. PRD-18 stays open at
**22% estimated work remaining**, a planning estimate.

Current source audit after deletion attribution and archive observation integration. PRD-18 remains open. Compilation, component tests, source fixtures and queued CI do not prove the complete runtime gate.

Both complete desktop/phone Board lifecycle native cases now pass locally after
repairing delayed archive-directory focus recovery that prevented the phone's
deletion retry. The regression fails before repair; all 24 archive component
cases and 16 related focus/archive/observation cases pass. The executed cases
retain original-key archive/restore/delete recovery, real two-client Worker
updates, read-only reconciliation, unchanged child states, explicit consent,
keyboard/focus and deleted-parent denial, with ten WCAG-tagged Axe scans. See
[executed lifecycle evidence](../board-archive-control.md#executed-lifecycle-recovery-and-focus-repair).
Local Production execution with frozen assemblies and unverified-account
browser policy does not establish current retained-image acceptance or the
complete lifecycle/retention/performance matrix. Estimated work remaining
stays 22%; the ticket remains open.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| LIFE-FR-001–003 explicit lifecycle, reversible archive, hidden canvas | Canonical enums; archive/deletion timestamps persisted and projected; reviewed Board/Card/List archive controls and discovery pages; active Board discovery excludes archives | Fresh complete server, PostgreSQL and native lifecycle execution |
| LIFE-FR-004 archive browser | ArchivedCardsPage, ArchivedListsPage and ArchivedBoardsPage; current scope/account denial and coalesced recovery reads | Native desktop/mobile archive cases on the immutable images; Board archive directory SignalR invalidation |
| LIFE-FR-005 parent-safe restoration | ListArchivedListsAsync rejects non-active Boards; transactional actor verification; 12 parent-deletion and 156 Organization/account/session/membership/administration lock-wait cases refuse fresh restores and old receipts using the production verifier | Remaining integrated transitions, HTTP cookie/middleware and native recovery scenarios; current immutable-image acceptance |
| LIFE-FR-006–009 archived-only elevated deletion, confirmation, irreversibility and List impact | Card/List/Board command and receipt contracts; MUI reviews with explicit consent, including Board archive directory reviews | Executed native consent, lost-response/retry and cascading-impact cases |
| LIFE-FR-010 audit integrity | Immutable audit storage fixture; lifecycle events, canonical receipts and retained deleting actor | Exact execution after archive, deletion and account removal; retained actor interpretation |
| LIFE-FR-011 deleted content absent from search/notifications | Separate search and notification admission implementations; mandatory 21-case real deletion matrix across private/Organization/public visibility covers direct and moved-Card search, inbox, historical sync and original read/move receipts | Current immutable-image execution, Organization terminal deletion and integrated permission/native recovery scenarios |
| LIFE-FR-012 product deletion versus backup retention | Attachment and Organization lifecycle documentation distinguishes irreversible product tombstones from retained provider evidence and operational backups | Verify irreversible product behavior across lifecycle entities and document actual operational backup windows; provider removal requires its own explicit reconciliation/retention authority, rather than being inferred from this requirement |

## Record attribution

### Deleted content across search and notification surfaces

The mandatory `scripts/ci/test-deleted-content-surfaces.sh` passed nine complete HTTP workflows against the current compiled Production API, restricted PostgreSQL 17/pgvector and Nginx: Card/List/Board deletion on PRIVATE/ORGANIZATION/PUBLIC Boards. Real registration/login, invitation acceptance, Board membership, Card assignment, notification read, archive and confirmed deletion requests establish each case. Each archived Card remains searchable before deletion, whether its own state or a parent supplies archived context.

After deletion, fresh active/archived search and the original archived search continuation return no results; the recipient inbox and historical sync return no notification entries. The sync cursor still advances over the two retained creation/read sources, rather than fabricating erasure or disclosing their identities. Original-key and new single/bulk read commands return `notification_not_found`; restoration returns the entity's stable not-found code. Original deletion recovery returns the identical acknowledgment. Complete Board/List/Card and notification/journal rows, plus aggregate audit/event/Work-job/receipt counts, remain unchanged throughout these post-deletion checks. Deletion preserves its archive clock and deleting actor.

The final complete invocation exited 0 with verified-email admission enabled. Account activation/verification was controlled setup when configured provider delivery kept tokens private; the script uses the real verification endpoint when registration exposes its test token. This is not proof of provider email delivery, separate Worker transport or native browser rendering. Search still appends its allowed content-free identity observation; the unchanged-state assertion concerns the protected Organization work/notification graph and listed aggregate effects. Invocation-owned containers and the cloned database were removed; original services and data were preserved. Shell syntax and diff validation passed. Earlier setup attempts are not acceptance evidence; the confirmed deletion query uses the existing `confirmed=true` contract without weakening its guard.

The required container-integration job now executes this script using the images built once for that revision. Current immutable-image execution, Organization terminal deletion, moved-entity admission, browser withdrawal/recovery and the remaining integrated Definition of Done still govern closure. Estimated PRD-18 work remaining is **16%**; PRD-17's estimate remains **22%**, since producer/watch/reminder and integrated release requirements remain independently open. This covers LIFE-FR-011 and scoped SEARCH-FR-007/notification-consumer lifecycle acceptance, not complete acceptance of those PRDs.

### Moved Card deletion surfaces

The same mandatory script expands the matrix to 21 workflows: the nine direct deletion cases and twelve cases after actual cross-Board Card movement. Source and destination use matched PRIVATE/ORGANIZATION/PUBLIC visibility with current explicit recipient membership. The original assignment notification retains its source Board, while its entity link follows the current destination. The expected admission contract is:

| Deleted context after movement | Current Card search | Original notification/incremental history | Original read/move receipts |
| --- | --- | --- | --- |
| Old source List, now empty | Surviving destination Card remains active | Remains admitted through the surviving source Board and current destination | Original single/bulk read and move receipts recover unchanged |
| Old source Board | Surviving destination Card remains active | Hidden because the original Board no longer survives | Refused with stable not-found codes |
| Destination List | Hidden through the deleted current parent | Hidden | Refused with stable not-found codes |
| Destination Board | Hidden through the deleted current parent | Hidden | Refused with stable not-found codes |

Survivor cases compare the complete Card-row fingerprint before/after deletion and assert its current Board/List/version through real search. All cases require irreversible restore refusal, exact original deletion acknowledgment recovery and unchanged protected records/listed aggregate effects during the post-deletion checks. This guards against hiding surviving moved work as well as disclosing deleted context.

The 21-Organization fixture can legitimately return an empty search page with a continuation under the existing 20-Organization/Board traversal budgets. The first expanded invocation rejected that valid page; the diagnostic retained empty items and a nonempty continuation. The helper now follows bounded empty continuations, rejects repeated cursors, validates every page's private/no-store response and fails beyond its fixed fixture page bound. Server budgets and authorization remain unchanged.

The corrected complete local invocation exited 0 with all 21 workflows, against the current compiled Production API, restricted PostgreSQL 17/pgvector and Nginx with verified-email admission enabled. All six survivor cases preserved the complete destination Card row. The three old-List cases recovered original single/bulk read and move receipts unchanged; the nine other moved cases refused those receipts. All 21 original deletion recoveries and irreversible restore refusals passed without changing the protected records/listed aggregate effects. Invocation-owned containers/database were removed and original services/data preserved. Shell syntax, diff and README navigation-target checks passed. Account activation/verification remains fixture setup when provider delivery keeps tokens private; this proves neither provider delivery nor separate Worker/native browser behavior. Current immutable-image, Organization terminal deletion and integrated permission/native recovery acceptance remain required. Estimated work remaining stays **16%**.

The four existing archived-Board account uncertainty native cases passed in one
local Production invocation at desktop and phone widths. Both restore and
permanent deletion prove no submission before uncertain admission and original
key/body recovery after actual commitment with withheld account confirmation.
Keyboard, consent, focus, Axe and no-reload checks passed. See
[executed archive recovery](../board-archive-discovery.md#executed-account-uncertainty-recovery)
for runtime and policy limits. These results do not complete Card/List/Board/
Organization transitions, retention/purge, capacity or retained-image acceptance.

The three archive-directory live cases also passed alongside two ordinary
directory cases in one five-case invocation. Desktop and phone clients matched
genuine Worker archive/restore events to canonical sync and recovered an
interrupted connection. The phone case withheld an inaccessible Board, retired
destructive consent on role and membership withdrawal, and proved unchanged
archived state after denied restoration. See [live directory evidence](../organization-board-realtime.md#executed-native-directory-delivery-and-withdrawal).
PRD-18 remains open at 22% estimated work remaining; this is local scoped
acceptance, not the complete lifecycle or retained-image release gate.

BoardRecord, BoardListRecord and CardRecord expose nullable ArchivedAt, DeletedAt and DeletedBy, omitting null JSON fields. Demo and PostgreSQL archives record the latest archive clock, restores retain it, and deletion preserves that clock while recording its own clock. Lifecycle state determines active visibility; an active restored record can carry archive history. All PostgreSQL canonical reads and mutation projections carry these fields, including parent List attribution in archived Card discovery.

### Archive history retained through restoration (2026-10-08)

PRD-18 requires retained archive timestamps. A regression against the previous Demo implementation failed because restoration cleared the known timestamp. Board, List and Card restoration now preserve it in both stores; another archive records the new archive clock, and deletion retains that latest clock with deletion time and actor.

Migration `112_work_archive_history` guards known timestamps against clearing, ordinary rewrites and backward re-archiving. Its invoker triggers preserve the existing restricted-role and RLS boundaries. Historical null timestamps remain unknown; the migration invents no historical evidence. Copies continue to establish their own lifecycle history. Both API and Worker refuse an incomplete required migration ledger.

Executed local evidence: strict full-solution Release compilation passed with zero warnings/errors; five selected API-host/store cases passed, including real authenticated HTTP archive/restore responses and fresh active Board reads for Boards, Lists and Cards. The full PostgreSQL 17/pgvector migration runner passed clean/repeated/forward upgrades, serialized runners, failed-migration rollback and unrecorded-migration rejection. Its restricted SQL fixture proved history rejection, complete-statement rollback and cross-tenant non-disclosure. The production PostgreSQL store contract passed all three archive/restore/re-archive/delete cycles and original archive receipt replay without changing canonical records or aggregate audit/event/job/receipt counts. Required-ledger refusal/recovery passed for both runtime roles.

These checks used isolated local test databases and compiled contract output, rather than the current immutable release images. Full CI, lifecycle capacity measurements and the remaining PRD-18 acceptance/Definition of Done requirements still govern issue closure.

### Restore/receipt admission after an observed parent-deletion wait

The mandatory `WorkArchiveHistoryContract` now covers 12 restricted PostgreSQL cases: private/Organization/public Board visibility, Card restoration under a deleted List or List restoration under a deleted Board, and a fresh command or an original restore receipt. Receipt cases first execute real restore and re-archive commands with distinct keys. Each attempted restore is then held behind an administrator Board row lock. The test observes the actual pending command blocked by that exact connection through `pg_blocking_pids`; it fails if no database wait occurs. A controlled competing transaction archives/deletes the parent, preserves its archive history, and commits before releasing the pending command.

All 12 local cases passed with stable `card_not_found`/`list_not_found` outcomes, no returned canonical acknowledgment, unchanged target/child records and unchanged aggregate audit/event/job/receipt counts. Required-ledger refusal/recovery and the existing complete archive-history contract passed in the same invocation; strict solution compilation had zero warnings/errors. The isolated test database and containers were removed.

Parent lifecycle commits, initial rows and request context are controlled fixtures; the pending restore/replay goes through the actual restricted production services, stores and actor verifier. The initial version used an always-admit actor fixture; the expanded authority matrix replaced it with production verification and reran these 12 cases successfully. HTTP cookie/middleware behavior, parent deletion consent, live Worker delivery and native browser recovery remain independent requirements. Ordinary full CI includes these cases; `--work-archive-history-only` selects them explicitly for diagnosis. Complete current immutable-image and integrated acceptance remain required.

### Organization, account and original-session authority after waits

The expanded mandatory contract passed 90 additional observed database waits: private/Organization/public Board visibility; Card/List/Board restore; fresh command/original restore receipt; and Organization archival, Organization deletion acceptance (`DELETING`), account deactivation, original-session revocation or original-session expiry. Each case uses independent verified-active accounts, persisted session hashes and lifecycle records. Real restore/re-archive commands establish original receipt cases. Organization changes hold the Organization row gate; account/session changes hold the Board row gate. Every pending command must be observed blocked by the exact controlled connection before the competing state change commits.

The contract now uses `CommandActorAuthorization` with actual restricted identity/session stores and the production verified-account policy. It validates the current persisted account and original session after the wait. Organization withdrawals return the entity's stable not-found code; deactivation and session revocation/expiry return `session_unavailable`. No canonical value is returned, and complete Board/List/Card rows plus aggregate audit/event/job/receipt state remain unchanged.

Strict complete-solution compilation passed with zero warnings/errors. The full expanded invocation passed all 90 authority cases, the earlier 12 parent-deletion cases, existing archive-history cycles/replays and both roles' migration readiness checks in isolated PostgreSQL 17/pgvector. Temporary databases and containers were removed. Initial records, authenticated request-context binding and competing state transitions are fixtures; this proves the production verifier/store admission boundary, while actual login/cookie middleware, user-facing deactivation/Organization deletion consent, terminal deletion jobs, realtime recovery and current immutable-image/browser release acceptance still require their respective evidence.

### Membership and required administration withdrawal after waits

The mandatory contract additionally passed 66 observed waits: three Board visibilities, fresh restores/original receipts, and Organization membership removal/suspension or Board membership removal for Cards, Lists and Boards; Board Admin downgrade to Member for Lists and Boards. These actors are Organization Members with explicit Board Admin grants and independent Organization Owners, so an Owner override cannot mask grant withdrawal. Under the adopted permission policy, Board Members retain Card editing/restoration rights; administrator downgrade is therefore a denial case only for List/Board restoration.

Each pending restricted production command is observed waiting on the exact competing connection's Organization-membership or Board-membership row lock. The competing controlled grant update commits before the command proceeds. Both fresh restores and old receipts return the entity's stable not-found code with no canonical value. Complete Board/List/Card records and aggregate audit/event/job/receipt counts remain unchanged. This proves current membership/role admission after a real database wait; grant changes themselves are fixtures, not proof of their HTTP endpoints or browser withdrawal behavior.

Strict full-solution compilation passed with zero warnings/errors. One isolated PostgreSQL 17/pgvector invocation passed all 156 authority cases, the prior 12 parent-deletion cases, complete archive-history cycles/replays and both roles' required-ledger refusal/recovery. Invocation-owned containers were removed and original services/data preserved. Ordinary full CI includes these cases. Current immutable-image, login/cookie, native recovery and integrated acceptance still govern closure; estimated PRD-18 work remaining stays **17%**.

### Attachment-preview fixture repair after archive-history enforcement

The full PostgreSQL contract job on `af754166` and `be9b6eb2` rejected an older attachment-preview fixture restoring a Card with `archived_at=NULL` (`23514`). The production invariant remains enforced. Preview intent/publication/backfill fixtures now retain history on restoration; repeated archive setup uses a consistent statement clock for archive/update fields. The Card-cover row-lock fixture now clones its authenticated administrator connection through Npgsql's `ICloneable` implementation instead of rebuilding a connection from its redacted opened connection string. No database authentication requirement was relaxed.

Strict solution compilation passed with zero warnings/errors. An isolated PostgreSQL 17/pgvector invocation of `--attachment-preview-lifecycle-only` passed real restricted API/Worker readiness and the complete existing attachment Worker contract chain: preview intent/publication/backfill/activation, Card covers/commands, Board background images, preview reads, scan recovery and attachment lifecycle. The selector skips unrelated contract groups only when explicitly requested; ordinary CI continues to run the complete suite. Storage/scanner/provider fixture behavior in this contract is not proof of live provider integration. Full corrected current CI remains required.

The later full contract job on `9ba0c6d6` reached file publication and found another older fixture clearing Board archive history through a parameterized `archived_at=@archived` update (`23514`). Its moved-receipt Board-state helper now preserves history on restoration and uses matching archive/update clocks on archival. A broader source audit covered parameterized archive assignments as well as literal null clearing. Strict complete-solution compilation passed with zero warnings/errors; the complete restricted `AttachmentPublicationContract` chain passed, including moved original/destination receipt admission, file uploads and attachment lifecycle commands. The explicit `--attachment-publication-only` diagnostic selector includes required-ledger refusal/recovery and leaves ordinary CI's full suite intact. Local provider fixtures and compiled assemblies remain distinct from current immutable-image/live-provider release acceptance.

Authorized lifecycle service commands pass the deleting actor into both stores. New store deletions require a nonempty actor. Migration 069 adds retained account references and prevents non-null deletion attribution on non-deleted records; readiness requires this migration. Historical unknown actors remain null. Copying a List resets an archived Card's clock to its new creation time and clears deletion attribution.

Source fixtures cover missing-actor rejection, unconfirmed and confirmed deletion, archive/restore/rearchive, rollback/unchanged command replay and copied Cards without deletion attribution. Full solution compilation passes; database/API runtime evidence remains pending Linux CI.

## Client observations and permission withdrawal

Archived List/Card discovery reports page and review opens, read and command use, retry, success/failure duration, conflict, exception and online/live recovery. Coalesced reads preserve reconnect/retry classification ahead of ordinary invalidation. Active-canvas Card/List archive commands report review open, use/retry, success/failure duration, conflict and exception.

Client/server allowlists carry only fixed categories, counts and bounded durations. They retain no scope IDs, names, positions, revisions, command keys, impact counts or diagnostics. Component tests inspect actual report payloads, same-key retries after canonical removal, denial, conflicts and live recovery coalescing. API parser source cases reject private extras atomically.

Both Card and List archive controls withdraw a pending request/review and unresolved intent when fresh Board admission removes permission. A late acknowledgment cannot recreate success or recovery after that withdrawal.

The active-canvas archive/observation suite has 42 passing component tests; web type checking, lint and full solution compilation pass. These results do not substitute for executed API/PostgreSQL/native acceptance.

Board archive directory restore/delete reviews now have component coverage for consent, canonical acknowledgment, lost-response recovery, account change and pending-command permission withdrawal. Directory observations report opens, use/retry/reconnect, duration/outcome, conflicts and exceptions through four fixed allowlisted actions. Actual payload tests cover same-key recovery after canonical removal, conflict, foreground denial and coalesced reconnect classification; API parser source cases reject private extras. The combined archive observation/command suites have 27 passing tests; type checking, lint, production build and warning-free solution compilation pass.

The active-Board archive control now reviews scope/version/impact, fences other Board commands while its original request is unresolved, and retains receipt recovery after authoritative read-only state while current administrative admission remains. Fresh permission/scope withdrawal aborts pending work and ignores late acknowledgment. Its fixed board_archive observation action retains no scope/content/request material. Organization discovery excludes archived Boards in both stores and re-exposes restored Boards; source API and exact-image PostgreSQL fixtures cover this behavior. The serial immutable-image browser suite includes desktop/mobile keyboard Board lifecycle scenarios with a real Worker, two live clients, deliberately lost successful archive/restore/delete responses, consent/focus checks, child-state preservation and deleted-parent denial. Browser type checking/discovery pass; native execution is unproven until the queued rigorous CI completes. See board-archive-control.md.

Lifecycle capacity must measure the actual lifecycle operations and documented large-data fixture, rather than infer capacity from the notification consumer benchmark. Full API/PostgreSQL/native execution and immutable-image acceptance remain pending rigorous StrataAI2 CI.

### Archive/restore capacity measurements (2026-10-08)

`scripts/ci/test-work-lifecycle-capacity.sh` now runs in the mandatory supported-capacity chain through the release API/Nginx. It checks the fixture has 200 Lists, 5,000 active Cards and 100,000 archived Cards before and after 120 real commands: 20 archive/restore cycles each for a Card, its List and its Board. Each of the six entity/action groups retains all 20 curl-total timings and independently gates nearest-rank p95 below 500 ms. A failed timing gate retains the failed report before rejecting CI.

Canonical SQL checks validate every acknowledged version, lifecycle state and retained timestamp. Fresh HTTP Board reads prove active recovery. Original archive receipt replay after restoration must return the original acknowledgment without changing canonical records or aggregate effect counts. All neighboring List/Card records, including the 100,000 archived Cards, must retain their complete-record fingerprints. Audit, Work event, corresponding Work job and command-receipt deltas must each match the 120 commands. The retained report contains fixed scope/conditions, counts, six timing series and revision/topology; it excludes identities, content, keys and cookies.

The isolated local compiled Production API on PostgreSQL 17/pgvector passed the complete invocation through Nginx. One serial client, no intentional network delay, source runtime `af754166`:

| Entity | Archive p95 (ms) | Restore p95 (ms) |
| --- | ---: | ---: |
| Card | 303.099 | 293.805 |
| List | 282.872 | 279.816 |
| Board | 277.188 | 282.191 |

All six p95 values were independently recomputed from the retained samples. The test database and invocation-owned containers were removed afterward. This is local compiled-runtime evidence, not current immutable-image or browser interaction evidence. The CI artifact is `work-lifecycle-capacity-<revision>` / `work-lifecycle.json`. Permanent-deletion capacity/performance, concurrent lifecycle admission and full integrated release acceptance remain separate requirements; this archive/restore measurement does not prove them.

### Permanent-deletion capacity measurements (2026-10-08)

`scripts/ci/test-work-deletion-capacity.sh` runs last in the mandatory supported-capacity chain, after other cases finish using the original selected List. It verifies the original 200-List/5,000-active-Card/100,000-archived-Card fixture, then seeds 20 independent Boards, each with 200 Lists and 5,000 active child records. Each has an archived List and an additional archived Card. Synthetic setup is not proof of audited creation/archive behavior. The timed commands perform 20 distinct Card deletions, 20 distinct List deletions and 20 distinct Board deletions; none of the 60 samples is a replay. Actual preparatory API commands archive 20 Boards and the original selected List.

One List deletion reviews the original List containing all 100,000 archived Cards plus its active children. The report retains that command's latency and impact separately, as well as every per-entity sample, p95 and maximum. Missing confirmation is rejected without canonical/effect changes. Every successful deletion must persist its next version, archive history, deletion clock and actor. List/Board children retain complete-record fingerprints. Deleted Cards lose archived-detail admission, deleted Lists disappear from the active canvas, and deleted Boards deny ordinary reads. Every original deletion acknowledgment is recovered with the same key and unchanged aggregate effects. Deletion-tagged audit/event/Work job deltas match 60, and receipt deltas include the 21 preparatory archives. Failed timing reports remain available before the mandatory p95 gate rejects CI.

The complete isolated local compiled Production API/PostgreSQL 17/pgvector/Nginx invocation passed, using one serial client with no intentional network delay:

| Deleted entity | Samples | p95 (ms) | Maximum (ms) |
| --- | ---: | ---: | ---: |
| Card | 20 | 290.151 | 308.798 |
| List | 20 | 343.478 | 365.055 |
| Board | 20 | 292.759 | 305.025 |

The largest reviewed List retained 100,025 child Cards; its deletion acknowledgment took 321.834 ms. All three nearest-rank p95 values were independently recomputed from the retained samples. Temporary databases and invocation-owned containers were removed. This is compiled-runtime evidence; current immutable-image release acceptance, concurrent lifecycle scenarios and native browser interaction remain separate requirements. CI retains only fixed conditions/counts/timings/revision/topology in `work-deletion-capacity-<revision>` / `work-deletion.json`, without identities, content, command keys or cookies.

Board deletion now requires explicit confirmation at HTTP and Application boundaries after current archived-state/administration admission. Its transactional fingerprint includes consent. See board-deletion-consent.md for the source regression and remaining Board tombstone-receipt recovery/UI requirements. Full solution compilation passes; runtime consent execution remains pending CI.

Board tombstone receipt recovery now has a dedicated fresh-admission path for current Organization/Board administrators with active Organization membership. Normal Board/member lookup excludes deleted Boards in both stores. Source API and PostgreSQL container cases cover identical replay, changed consent, new keys, normal-read non-disclosure, membership revocation and atomic audit rollback. Their runtime execution remains pending. The archive directory retains an unresolved original deletion for recovery after its Board disappears from discovery.

Board archive discovery now has a bounded, current-admin-filtered Organization API with minimal item fields and private/no-store responses. Current grants filter before 50-item pagination, and the read transaction preserves Organization/account admission and Board gates. Its MUI directory validates scope, paging and current identity and offers reviewed restore/delete commands. See board-archive-discovery.md for coverage and remaining lifecycle acceptance. Compilation/script syntax and focused web checks pass; API/PostgreSQL/native runtime execution remains pending.

## Automatic Demo Organization deletion

Demo now processes immutable accepted requests automatically inside the API,
traversing actual attachments, archived/active Cards, Lists and Boards before
committing original attribution, terminal audit and canonical completion source.
Late failure or cancellation restores the graph and source together. Accepted
work can finish after the requesting actor retires; protected reads retain
current account/session/membership fences. Five API-host cases and six actual
desktop/phone browser cases pass, including lost acknowledgment, connected and
disconnected member recovery, content withdrawal and logout. See the
[implementation and verification scope](organization-deletion-lifecycle.md#demo-bounded-graph-simulation).

This removes a Demo lifecycle integration gap. It does not establish supported
scale, retained release-image acceptance, physical object erasure, backup expiry
or the entire restoration/search/notification matrix. Estimated PRD-18 work
remaining is **22%**, a planning estimate; the ticket stays open.
