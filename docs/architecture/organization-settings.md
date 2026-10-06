# Organization metadata settings

PRD-03 WS-FR-002/003 now has an accessible MUI settings screen at `/app/{organizationId}/settings`, linked from an active Organization's board directory for internal Owners and Admins. Ordinary members, Portal-only accounts and inactive Organizations do not receive an editing form. The existing server membership, lifecycle, current-session and version checks remain authoritative for every save.

The screen edits name, description and optional logo URL. HTTPS absolute URLs without embedded credentials, up to 2,048 characters, are accepted by the server; empty values clear the logo. Invalid URL validation follows permission admission, so unrelated accounts receive the usual protected not-found response. This stores URL metadata only; binary files still require the separate object-storage implementation. It introduces no new schema or authorization surface.

Metadata PATCH carries the last reviewed Organization version. Only a valid 200 response naming the same Organization, matching the submitted metadata and advancing the version exactly once produces a saved acknowledgment. A conflict preserves the draft and disables further saves until current authorized settings load. The user can discard the draft or explicitly retain it after reviewing the current saved metadata. Permission/session loss clears draft and saved metadata; expired sessions return to sign-in.

A lost, timed-out or malformed save acknowledgment preserves the original metadata, version and retry key. The screen offers **Retry original save** and prevents editing or a replacement save while recovery is unresolved. The retry submits the same command to recover its durable acknowledgment. After recovery, a current authorized read is required before editing again; an old acknowledgment cannot replace another administrator's later settings. A definite conflict or expired acknowledgment requires current-state review before a new save. Organization domain-event publication remains outstanding.

Reads and writes have a 15-second deadline, duplicate in-flight submission is refused, unmount aborts pending work, and late responses cannot replace a different route or restore private data. The settings route remounts on Organization change. Server/edge error text is not displayed as trusted product content.

Local execution: full web component suite and typecheck/lint, plus real desktop/mobile keyboard browser scenarios for URL validation, stale-version review, lost committed-save acknowledgment and persisted reload. The new API host tests compile in the warnings-as-errors solution build; execution proof comes from required Linux CI because the local Windows host has blocked the test runner with Application Control. The existing mandatory exact-image Organization fixture also checks rejected URLs and unauthorized callers leave all Organization/member/invitation/Portal/audit state unchanged. The new browser scenarios run in the mandatory exact-image suite.

Full PRD-03 remains open. Follow-up commits provide member administration, bounded member paging and invitation creation controls. Invitation delivery/history, ownership governance, remaining durable Organization retry receipts, Organization discovery pagination, realtime domain-event delivery, deletion processing/retention and remaining acceptance evidence are still outstanding.

## Keyed metadata API recovery

Metadata PATCH accepts an optional nonempty UUID `Idempotency-Key`; the settings
screen supplies one for each new reviewed edit. Unkeyed API callers retain
version-based reconciliation. A keyed edit binds
the Organization, actor, submitted metadata and expected version to an immutable
acknowledgment. Its receipt, edit and audit commit together under the owning
Organization transaction. Receipt failure rolls back the edit and audit.

Matching replay returns the original record without applying it over later
edits. Current active Organization/admin membership and current session admission
are required before disclosure; a removed administrator cannot recover private
metadata. Different input returns 409 `idempotency_conflict`. After 24 hours,
matching replay returns 409 `idempotency_expired`; the key remains reserved and
cannot create another mutation. Receipt retention/cleanup remains future work.

Migration 082 forces tenant RLS and gives the API only SELECT/INSERT on receipts;
the Worker receives no receipt access. Both runtime hosts require its ledger
entry. Demo receipts participate in owning snapshot rollback. The mandatory
native Organization fixture covers denied receipt INSERT, unchanged edit/audit
state, original replay after a later edit, mismatched input and expiry. The
API-host scenario also checks withdrawal of admin membership. Strict compilation
and fixture syntax pass; runtime execution remains pending CI. Browser same-key
recovery now preserves input/key across repeated uncertainty, clears private
state on access withdrawal, and requires current settings after original replay.
The desktop/phone keyboard fixture drops a committed response, applies a later
edit, recovers the original receipt and verifies the later persisted revision
survives reload. It includes WCAG 2.2 AA automated checks. Native browser execution,
concurrency and post-receipt final-admission coverage remain to verify.
