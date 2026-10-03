# PRD-14 Attachments and Card Covers acceptance audit

The authoritative ticket is GitHub #15. Attachment source implementation has
begun after the Checklist command/read foundation. Neither feature is closed:
Checklist/operator image acceptance is pending, and the full attachment ticket
requires storage, scanning, API, UI, events, permissions and lifecycle evidence.

`Attachment` is a framework/provider-free Organization-scoped Domain entity.
It retains stable Card/uploader identity, display name, kind, timestamps/version,
server-owned binary MIME/byte count/key or URL metadata, scan status/time and
soft-deletion time. File factories always start in Pending quarantine. Clean,
Rejected and Failed are terminal scan outcomes; duplicates do not invent versions,
and a conflicting result cannot overwrite a settled outcome. Failed processing
has an explicit retry transition back to Pending; malware rejection cannot be
silently retried/cleared. Invalid or backwards transitions cannot partially mutate
state. Deletion preserves metadata and disables delivery/image/cover eligibility.

Only clean active PNG/JPEG/WebP file metadata is image/cover eligible, and cover
eligibility requires the same Organization and Card. This flag is a Domain
invariant, never an authorization decision or permission to issue a URL. URL
attachments are untrusted absolute HTTP(S) metadata with no embedded credentials,
no binary storage/MIME/size and no scan/download/cover status. No URL is fetched.
Bounds are 255 characters for display name, 2048 for the canonical encoded URL,
127 for verified MIME and 512 for a server-owned object key. Configured upload
size/type policy still belongs to future server admission; a positive Domain
byte count is not upload admission. Filenames/client MIME never establish trust.
The binary factory must receive actual server-verified metadata and a generated
key; HTTP DTOs must not directly bind these fields. Future public metadata DTOs
must not disclose storage keys, scanner/provider detail or protected uploader data.

| Requirement | Current evidence | Remaining work |
| --- | --- | --- |
| FR-001 upload size/type | Positive binary size and canonical verified-MIME Domain invariants compile | Configurable server policy, byte inspection, actual authorized upload and client confirmation |
| FR-002 URL | Domain metadata validation, URL-kind separation and no-fetch behavior compile | Authorized/idempotent URL command, persistence, safe external-link UI |
| FR-003 metadata | Scoped stable identity/metadata/time/version and variant separation compile | Migration/FKs/RLS, paginated store/DTOs, authorization-before-disclosure and stable API errors |
| FR-004 object storage | Server-owned object key is separate from URL metadata; no BLOB introduced | Adopted object-storage adapter, credential/readiness validation, compensating cleanup and exact-image proof |
| FR-005 controlled downloads | Pending/failed/rejected/deleted files have no delivery eligibility | Current-rights/lifecycle revalidation and bounded signed URL or controlled streaming |
| FR-006 scanning/quarantine | Pending-first Domain state machine with explicit failure retry compiles | Production scanner/quarantine interface/provider, separate Worker job/outbox, idempotent CAS verdict and scan events |
| FR-007 previews | Clean active supported-image eligibility compiles | Verified byte/image processing, safe bounded preview delivery and accessible MUI viewer |
| FR-008 cover | Eligibility rejects URL/unsafe/deleted/foreign-Card/foreign-Organization metadata | Card cover reference, atomic versioned command/FK, authorization, UI/realtime |
| FR-009 deletion | Domain tombstone disables eligibility and retains history | Explicit administrative consent, parent-version checks, atomic cover clearing, audit/event and object retention/cleanup policy |
| FR-010 untrusted MIME/name | Domain APIs explicitly require server-verified MIME/size/key; validation rejects malformed fields | Actual inspection/scanning/provider pipeline and forged-client-metadata integration tests |

New Domain cases cover scope/identity, quarantine, image/non-image cover rules,
terminal/duplicate/invalid scan states, failed scan retry, malware refusal,
tombstones, invalid/backwards updates, unsafe URLs and canonical encoded length,
key traversal, MIME/byte invariants and control/direction-spoofed display names.
They compile; local .NET execution is blocked by Windows Application Control.
Linux execution is pending. No persisted attachment, authorized upload/download,
cover mutation, scanner execution or user-facing attachment capability is claimed.

All AC-ATTACH-14-01/02/03 and TC-01 through TC-13 remain incomplete until their
full production/state/client scopes are executed. Audit/events/notifications,
realtime reconnect/conflicts, parent archive/delete/move/copy and retention,
original-intent retry, disclosure fencing, public/Owner Portal separation,
keyboard/mobile/scroll context and the unchanged performance/capacity budgets
must be implemented and proved before closure. This audit preserves all ten FRs
and shared dependency scope rather than treating the Domain foundation as done.
