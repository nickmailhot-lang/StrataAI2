# PRD-18 acceptance audit

Current source audit after deletion attribution and archive observation integration. PRD-18 remains open. Compilation, component tests, source fixtures and queued CI do not prove the complete runtime gate.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| LIFE-FR-001–003 explicit lifecycle, reversible archive, hidden canvas | Canonical enums; archive/deletion timestamps persisted and projected; reviewed Board/Card/List archive controls and discovery pages; active Board discovery excludes archives | Fresh complete server, PostgreSQL and native lifecycle execution |
| LIFE-FR-004 archive browser | ArchivedCardsPage, ArchivedListsPage and ArchivedBoardsPage; current scope/account denial and coalesced recovery reads | Native desktop/mobile archive cases on the immutable images; Board archive directory SignalR invalidation |
| LIFE-FR-005 parent-safe restoration | ListArchivedListsAsync rejects non-active Boards; transactional reads verify the actor; reviewed restore commands and parent-lifecycle fixtures exist | Full Card/List/Board/Organization transition matrix and concurrent parent changes |
| LIFE-FR-006–009 archived-only elevated deletion, confirmation, irreversibility and List impact | Card/List/Board command and receipt contracts; MUI reviews with explicit consent, including Board archive directory reviews | Executed native consent, lost-response/retry and cascading-impact cases |
| LIFE-FR-010 audit integrity | Immutable audit storage fixture; lifecycle events, canonical receipts and retained deleting actor | Exact execution after archive, deletion and account removal; retained actor interpretation |
| LIFE-FR-011 deleted content absent from search/notifications | Separate search and notification admission implementations and fixtures | Cross-surface deletion matrix, including historical notification and moved-entity scope |
| LIFE-FR-012 product deletion versus backup retention | Attachment lifecycle documentation distinguishes product tombstones from provider/backup cleanup | Complete retention/purge authority and implementation across lifecycle entities; separately documented operational backup windows |

## Record attribution

BoardRecord, BoardListRecord and CardRecord expose nullable ArchivedAt, DeletedAt and DeletedBy, omitting null JSON fields. Demo and PostgreSQL archives record the archive clock, restores clear it, and deletion preserves that clock while recording its own clock. All PostgreSQL canonical reads and mutation projections carry these fields, including parent List attribution in archived Card discovery.

Authorized lifecycle service commands pass the deleting actor into both stores. New store deletions require a nonempty actor. Migration 069 adds retained account references and prevents non-null deletion attribution on non-deleted records; readiness requires this migration. Historical unknown actors remain null. Copying a List resets an archived Card's clock to its new creation time and clears deletion attribution.

Source fixtures cover missing-actor rejection, unconfirmed and confirmed deletion, archive/restore/rearchive, rollback/unchanged command replay and copied Cards without deletion attribution. Full solution compilation passes; database/API runtime evidence remains pending Linux CI.

## Client observations and permission withdrawal

Archived List/Card discovery reports page and review opens, read and command use, retry, success/failure duration, conflict, exception and online/live recovery. Coalesced reads preserve reconnect/retry classification ahead of ordinary invalidation. Active-canvas Card/List archive commands report review open, use/retry, success/failure duration, conflict and exception.

Client/server allowlists carry only fixed categories, counts and bounded durations. They retain no scope IDs, names, positions, revisions, command keys, impact counts or diagnostics. Component tests inspect actual report payloads, same-key retries after canonical removal, denial, conflicts and live recovery coalescing. API parser source cases reject private extras atomically.

Both Card and List archive controls withdraw a pending request/review and unresolved intent when fresh Board admission removes permission. A late acknowledgment cannot recreate success or recovery after that withdrawal.

The active-canvas archive/observation suite has 42 passing component tests; web type checking, lint and full solution compilation pass. These results do not substitute for executed API/PostgreSQL/native acceptance.

Board archive directory restore/delete reviews now have component coverage for consent, canonical acknowledgment, lost-response recovery, account change and pending-command permission withdrawal. Directory observations report opens, use/retry/reconnect, duration/outcome, conflicts and exceptions through four fixed allowlisted actions. Actual payload tests cover same-key recovery after canonical removal, conflict, foreground denial and coalesced reconnect classification; API parser source cases reject private extras. The combined archive observation/command suites have 27 passing tests; type checking, lint, production build and warning-free solution compilation pass.

The active-Board archive control now reviews scope/version/impact, fences other Board commands while its original request is unresolved, and retains receipt recovery after authoritative read-only state while current administrative admission remains. Fresh permission/scope withdrawal aborts pending work and ignores late acknowledgment. Its fixed board_archive observation action retains no scope/content/request material. Organization discovery excludes archived Boards in both stores and re-exposes restored Boards; source API and exact-image PostgreSQL fixtures cover this behavior. See board-archive-control.md. Native and runtime evidence remains pending.

Lifecycle capacity must measure the actual lifecycle operations and documented large-data fixture, rather than infer capacity from the notification consumer benchmark. Full API/PostgreSQL/native execution and immutable-image acceptance remain pending rigorous StrataAI2 CI.

Board deletion now requires explicit confirmation at HTTP and Application boundaries after current archived-state/administration admission. Its transactional fingerprint includes consent. See board-deletion-consent.md for the source regression and remaining Board tombstone-receipt recovery/UI requirements. Full solution compilation passes; runtime consent execution remains pending CI.

Board tombstone receipt recovery now has a dedicated fresh-admission path for current Organization/Board administrators with active Organization membership. Normal Board/member lookup excludes deleted Boards in both stores. Source API and PostgreSQL container cases cover identical replay, changed consent, new keys, normal-read non-disclosure, membership revocation and atomic audit rollback. Their runtime execution remains pending. The archive directory retains an unresolved original deletion for recovery after its Board disappears from discovery.

Board archive discovery now has a bounded, current-admin-filtered Organization API with minimal item fields and private/no-store responses. Current grants filter before 50-item pagination, and the read transaction preserves Organization/account admission and Board gates. Its MUI directory validates scope, paging and current identity and offers reviewed restore/delete commands. See board-archive-discovery.md for coverage and remaining lifecycle acceptance. Compilation/script syntax and focused web checks pass; API/PostgreSQL/native runtime execution remains pending.
