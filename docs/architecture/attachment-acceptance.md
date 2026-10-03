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

Migration 041 adds metadata-only `attachments` with forced tenant RLS, composite
Card/Organization and uploader-membership FKs, mutually exclusive file/URL shape,
quarantine/terminal timestamp shape, positive size/version and bounded names,
MIME, URLs and keys. A globally unique generated object key prevents two metadata
rows sharing binary ownership; active Card reads have a timestamp/ID cursor index.
Card identity avoids stale duplicated Board identity after a move. SQL shape
checks complement the stricter Domain URL validation; they do not admit uploads,
authorize delivery or verify bytes. Scan transition CAS and atomic events remain
Application/store work, and no cover reference is added yet.

Runtime connection admission now requires all 41 ordered migrations. API metadata
access has SELECT/INSERT/UPDATE without hard DELETE; Worker still has no attachment
table grants until the scoped scanning command path is implemented. CI adds actual
migrated PostgreSQL checks under a nonsuperuser/NOBYPASSRLS role: both tenant reads,
foreign update rejection, tenant write rejection, foreign Card/uploader rejection,
39 malformed file/URL shapes including nullable bypasses, unique binary keys,
valid scan/tombstone history and missing-context refusal. Upgrade/repeat checks
and missing-ledger exact-image readiness probes include migration 041. Local
warning-as-error build, shell syntax and diff checks pass. PostgreSQL execution
is pending Linux CI; no SQL/runtime proof is inferred from compilation.

Linux run 37110102227 at 0ab0c97 passed the actual PostgreSQL attachment fixture,
ordered upgrade/repeat/serialization and tenant catalog guards. Its unfiltered
.NET host suites passed 280 Domain and 257 API cases, including all 34 new
attachment Domain cases. This is source/database evidence, not upload/scanner or
complete immutable-image acceptance.

`IAttachmentMetadataStore` now has matching Demo/PostgreSQL URL creation,
Card/Organization-scoped lookup and newest-first timestamp/ID reads capped at 51
(the eventual API returns 50 plus a continuation). Timestamps are normalized to
PostgreSQL microseconds before storage so ties have stable identities. Domain URL
validation is reused; SQL inserts enforce Card/uploader composite FKs. Production
calls reject a missing owning tenant transaction before database access or
metadata validation. Read records exclude object keys and scanner/provider detail.
New cases exercise 63 tied-timestamp rows across pages, both foreign scopes,
identity reuse refusal, foreign parents/uploaders, metadata separation, malformed
cursor pairs and unscoped production calls. They compile; execution is pending
Linux CI. These primitives are deliberately not exposed through HTTP yet: current
permission/lifecycle admission, Card CAS, replay receipts and atomic audit/outbox
must wrap them before a user-facing URL attachment command or read is enabled.

Run 37110453074 at 396b24f failed two new Demo store cases before assertions:
the isolated fixture omitted identity services required by the Organization
store constructor. Its production scope-refusal case and 280 prior Domain cases
passed; PostgreSQL and web jobs passed. The fixture now registers Demo identity
and its clock using the existing registrations. Pagination/scope/identity
assertions are unchanged; repaired execution remains pending.
