# PRD-18 acceptance audit

Audit of main after fdf2ade. PRD-18 remains open. This is a requirement map, not a claim that the complete runtime gate passed.

| Requirement | Current evidence | Required completion evidence |
| --- | --- | --- |
| LIFE-FR-001–003 explicit lifecycle, reversible archive, hidden canvas | WorkManagement lifecycle enums; migration 006 persists archive/deletion timestamps; archive interfaces and lifecycle command fixtures | Fresh complete server, PostgreSQL and native lifecycle execution |
| LIFE-FR-004 archive browser | ArchivedCardsPage and ArchivedListsPage; 59 focused component tests pass, including current scope denial and reconnect recovery | Native desktop/mobile archive cases on the immutable images |
| LIFE-FR-005 parent-safe restoration | ListArchivedListsAsync rejects non-active Boards; transactional archive reads verify the actor; reviewed restore commands and parent-lifecycle fixtures exist | Full Card/List/Board/Organization transition matrix and concurrent parent changes |
| LIFE-FR-006–009 archived-only elevated deletion, confirmation, irreversibility and List impact | card-deletion-consent.md and list-deletion-consent.md document the implemented command/receipt contracts and MUI reviews | Executed native consent, lost-response/retry and cascading-impact cases |
| LIFE-FR-010 audit integrity | Immutable audit storage fixture and lifecycle event/receipt contracts | Exact execution after archive, deletion and account removal; retained actor interpretation |
| LIFE-FR-011 deleted content absent from search/notifications | Separate search and notification admission implementations and fixtures | Cross-surface deletion matrix, including historical notification and moved-entity scope |
| LIFE-FR-012 product deletion versus backup retention | Attachment lifecycle documentation distinguishes product tombstones from provider/backup cleanup | Complete documented retention/purge authority and implementation across lifecycle entities; operational backup windows remain separately defined |

The explicit data requirement also names deletedBy across Board/List/Card. Migration/source searches find archive/deletion timestamps and retained event actors, but no dedicated deletion-actor field or equivalent deletion-actor property in WorkManagement storage. Retained audit attribution alone is insufficient to mark this mutable-record requirement complete. Implement the deletion actor contract, safe historical backfill, tenant/account integrity, command/rollback/replay behavior and runtime checks before closure.

Client archive telemetry still needs a complete open/use, outcome/latency, retry/conflict/exception and reconnect map. Server BoardSharing telemetry already identifies archive operations; it does not establish those client observations. Performance completion must cover the actual lifecycle operations and documented large-data fixture, rather than infer lifecycle capacity from the notification consumer benchmark.

CI is queued at this audit. Source presence, compilation, component tests and browser discovery are distinct from executed Linux/native evidence. Preserve all acceptance criteria and the separate retention/backup distinction when completing this ticket.

Contract continuation: BoardRecord, BoardListRecord and CardRecord now carry nullable ArchivedAt, DeletedAt and DeletedBy properties. Null values are omitted from JSON, preserving existing active-record and historical-receipt payloads. This only establishes the shared data contract. Store projection/write integration, deleting-actor command plumbing, PostgreSQL migration/backfill and demo parity remain unfinished; no actor persistence is claimed by the contract change.
