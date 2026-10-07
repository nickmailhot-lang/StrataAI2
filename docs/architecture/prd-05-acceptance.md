# PRD-05 acceptance and closure audit

This audit covers the complete [PRD-05 issue](https://github.com/nickmailhot-lang/StrataAI2/issues/6), including all ten functional requirements, three acceptance criteria, thirteen test scenarios and the definition of done. PRD-05 remains open. Estimated work remaining is **20%**, an initial planning estimate rather than a measured completion fraction.

## Functional traceability

| Requirement | Current implementation or source evidence | Evidence or work still required |
| --- | --- | --- |
| PERM-FR-001 three visibility values | WorkManagementService validates canonical visibility; BoardVisibilityPage reviews the current Board revision and explains public exposure. | Complete current-image native consent/conflict/recovery and rejected-write persistence assertions. |
| PERM-FR-002 visibility does not grant edits | Server access separates visibility from administration/edit/move capabilities; anonymous native assertions inspect the actual access object and reject mutation. | Full Organization/private/public role and operation matrix, including content, comments, files and cross-Board movement. |
| PERM-FR-003 ADMIN/MEMBER roles | BoardMemberRecord stores role, active state, timestamps and independent version; member commands enforce eligible active Organization membership and reviewed If-Match. | Full invalid/unauthorized/stale/replay matrix with unchanged protected state, restricted persistence and current release images. |
| PERM-FR-004 eligible invitations | BoardInvitationService checks current Board administrator and eligible recipient policy; separate invitation acceptance preserves Organization roles and grants Board membership. | Current retained-image issuance/acceptance, token expiry/revocation, account/issuer/recipient authority changes, replay, registration and private mail/link delivery acceptance. |
| PERM-FR-005 removal safeguards | WorkManagementService enforces last-Board-admin continuity with an explicit Organization-admin override and atomically removes Card assignments. BoardAdminContinuityTests and the reviewed member UI cover these contracts. | Full concurrent/rollback/current-admission checks, native permission withdrawal and current-image acceptance. |
| PERM-FR-006 promotion/demotion | Canonical eligibility, independent member version, continuity safeguards, MUI person-bound consent and read-only uncertain-command recovery. | Direct administrative role changes currently emit BOARD_MEMBER_UPDATED; reconcile public event producers with the required BOARD_MEMBER_ROLE_CHANGED contract and verify replay/consumers. |
| PERM-FR-007 anonymous public read-only | Public Board snapshots expose read-only access; controls omit editing and administration. Native visibility and persisted-workflow cases use a separate anonymous browser. | Current retained-image cross-surface read-only acceptance, anonymous cover/background admission and current parent/account/visibility withdrawal. |
| PERM-FR-008 private admission | Current Organization/account admission and explicit private grant checks; inaccessible Boards return neutral non-disclosure. | Complete role/lifecycle/reconnect matrix, including authority loss during reads and commands. |
| PERM-FR-009 Organization discovery | Active directory uses current reader admission, bounded filtering and protected Organization Board replay; ordinary directory native cases were executed locally. | Current-image role/visibility transition, paging, reconnect and large-data acceptance. |
| PERM-FR-010 server authorization | Protected commands use Application authorization inside their owning transaction; PostgreSQL tenant RLS and restricted roles retain database boundaries. | Complete endpoint/operation inventory, refusal-before-disclosure, late rollback and exact-image concurrency proof. |

Paths above are under `src/StrataAI.Application/WorkManagement`, `src/StrataAI.Application/Onboarding`, `apps/web/src/features/kanban` and `tests/StrataAI.Api.Tests`. Relevant behavior guides are [visibility](../board-visibility-ui.md), [member administration](../board-members-ui.md), [public sharing](../public-board-sharing.md), [member directory](../board-member-directory.md) and [live directories](../organization-board-realtime.md).

## Acceptance criteria and scenarios

- **AC-PERM-05-01:** prove authenticated authorized visibility selection, reviewed version and consent, persisted canonical state, and client reflection. An HTTP response or component assertion alone is insufficient.
- **AC-PERM-05-02:** prove role validation and stable unauthorized/invalid/stale refusals without private disclosure or changes to membership, Board state, assignments, audit or receipts.
- **AC-PERM-05-03:** prove genuine separate Worker delivery to another authorized client, missed-event reconnect recovery, canonical source identity and immediate withdrawal after current authority loss, without a manual document reload.

| Test scenarios | Required acceptance scope |
| --- | --- |
| TC-01 happy path; TC-02 empty state; TC-03 invalid input; TC-04 unauthorized user | Visibility, membership and invitations through the actual API, restricted persistence and browser. |
| TC-05 mid-session permission loss; TC-10 archived/deleted parents | Withhold private Board/person data and retire old consent; late responses cannot restore withdrawn admission. |
| TC-06 timeout/retry; TC-07 duplicate request | Original receipts/intent or explicitly read-only uncertain-write recovery; no automatic repeated destructive effects. |
| TC-08 concurrent update; TC-09 disconnect/reconnect | Actual competing revisions and original Worker events; source/event identity must survive reconnect without disclosing ineligible Boards. |
| TC-11 keyboard; TC-12 mobile | Desktop/phone controls, cancellation and submission focus, named person-bound reviews, announcements, overflow and WCAG 2.2 AA checks. |
| TC-13 large data | Documented fixture and actual measured operation/viewport budgets; test duration including rate pacing is not a benchmark. |

## Events, data and cross-cutting completion

The required public event names are BOARD_MEMBER_INVITED, BOARD_MEMBER_ADDED, BOARD_MEMBER_ROLE_CHANGED, BOARD_MEMBER_REMOVED and BOARD_VISIBILITY_CHANGED. Invitation acceptance already distinguishes added membership from role change. Direct member administration currently journals BOARD_MEMBER_UPDATED instead. Its separate private Demo authority-proof source also uses that name; preserve historical proof/replay compatibility while correcting public semantics. Do not rename a private authority capability merely to satisfy public event naming. Audit, durable Work journal and consumers require executed transactional/replay tests after any correction.

Canonical memberships retain created/updated clocks and versions; the API directory pages at 50 qualifying rows, filters current profiles and withholds former-member private profiles. Review join-clock semantics, tenant-safe foreign keys/indexes, migration upgrade/rollback and restricted-role execution as part of the data requirements. Review every specified member/invitation/visibility endpoint and its schema, Problem envelope, pagination, idempotency and authoritative revision.

Completion also requires documented telemetry for feature open/use, success/failure latency, permission codes, reconnect/conflict, exceptions and explicit retry, without sensitive content or misuse as business audit. See [sharing telemetry](../board-sharing-telemetry.md).

The four latency targets remain: initial usable Board below 1.5 seconds, movement feedback below 100 milliseconds, server acknowledgment p95 below 500 milliseconds, and cached Card detail below 200 milliseconds. Required supported capacity remains 200 Lists/Board, 5,000 Cards/Board and 100,000 archived Cards/Organization. General readiness, a small native fixture or a different consumer benchmark cannot establish these budgets.

All functional requirements, current server authorization tests, schema/migration review, emitted/consumed domain events, unit/integration/end-to-end execution, accessibility/keyboard, error/loading/empty behavior, telemetry, no known P0/P1 defects and complete product documentation must pass before closure. A whole-ticket green-release and requirement-by-requirement audit remain unproven. Dependencies remain PRD-02, PRD-03, PRD-04 and PRD-24; MUI, the modular monolith, separate Worker, PostgreSQL/RLS and build-once release architecture remain unchanged.

## Current local execution

The initial six-case invocation against the Production API, restricted schema-110 PostgreSQL, fresh production web bundle behind current Nginx/CSP and scoped separate Workers passed both member cases, desktop visibility and both persisted Board workflows. Phone visibility reopened consent after an HTTP refresh but before the competing original Worker event reached the browser; the later invalidation correctly retired that consent. The corrected fixture observes the genuine upstream event ID, matches the canonical visibility event/revision, and waits for its subsequent Board read before opening a new review. It does not insert events, change product consent rules or relax any assertion, deadline, retry or production limiter. The corrected full invocation exited 0 with all six cases passing in 3.5 minutes, including intentional pacing; this duration is not a performance measurement.

Both member cases verify keyboard consent/conflict and lost demotion/removal read-only recovery with unchanged Organization membership and immediate denied recipient access. Both visibility cases verify cancellation/focus, stale-version refusal, renewed public consent, lost-response canonical recovery and anonymous read-only admission. Both persisted-workflow cases use the UI to create an Organization, Board, List and Card, recover a lost Card creation exactly once, preserve a conflicting Card draft, retain saved state after reload, refuse wrong Organization scope and show the public Card read-only to a separate anonymous browser. See the [member evidence](../board-members-ui.md#executed-desktop-and-phone-member-recovery) and [visibility evidence](../board-visibility-ui.md#executed-desktop-and-phone-visibility-recovery). All six scoped Workers were removed after execution; the reusable API/web/database runtime and data volumes remain available.

## Remaining implementation order

1. Correct direct member-command public event classification and test actual journal/audit identity, replay, no-op behavior and transaction rollback while preserving private authority-proof compatibility.
2. Complete current-image invitation/member/visibility administration, actor and parent authority withdrawal, continuity and reconnect acceptance, including the full operation matrix.
3. Finish WCAG 2.2 AA, documented large-data and latency execution, then audit every definition-of-done requirement against the retained build-once release before closing the ticket.

Local Workers have only their newly created fixture Organization scopes, with all four global discovery loops and identity mail disabled. Frozen API/Worker assemblies mounted read-only in cached runtime images and the ordinary unverified-account browser policy do not establish retained-current-image release proof or strict verified-account acceptance.
