# PRD-18 acceptance audit

Current source audit after deletion attribution and archive observation integration. PRD-18 remains open. Compilation, component tests, source fixtures and queued CI do not prove the complete runtime gate.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| LIFE-FR-001–003 explicit lifecycle, reversible archive, hidden canvas | Canonical enums; archive/deletion timestamps persisted and projected; reviewed Card/List archive controls and discovery pages | Fresh complete server, PostgreSQL and native lifecycle execution |
| LIFE-FR-004 archive browser | ArchivedCardsPage and ArchivedListsPage; current scope denial, online/live recovery and coalesced reads | Native desktop/mobile archive cases on the immutable images; Board archive discovery and controls |
| LIFE-FR-005 parent-safe restoration | ListArchivedListsAsync rejects non-active Boards; transactional reads verify the actor; reviewed restore commands and parent-lifecycle fixtures exist | Full Card/List/Board/Organization transition matrix and concurrent parent changes |
| LIFE-FR-006–009 archived-only elevated deletion, confirmation, irreversibility and List impact | card-deletion-consent.md and list-deletion-consent.md document command/receipt contracts; MUI reviews with explicit consent | Executed native consent, lost-response/retry and cascading-impact cases; full Board review flow |
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

Board lifecycle UI/observations remain unfinished. Server BoardSharing telemetry does not establish complete client lifecycle observations. Lifecycle capacity must measure the actual lifecycle operations and documented large-data fixture, rather than infer capacity from the notification consumer benchmark. Full API/PostgreSQL/native execution and immutable-image acceptance remain pending rigorous StrataAI2 CI.

Board deletion now requires explicit confirmation at HTTP and Application boundaries after current archived-state/administration admission. Its transactional fingerprint includes consent. See board-deletion-consent.md for the source regression and remaining Board tombstone-receipt recovery/UI requirements. Full solution compilation passes; runtime consent execution remains pending CI.
