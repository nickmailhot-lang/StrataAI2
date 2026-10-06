# Organization metadata settings

PRD-03 WS-FR-002/003 now has an accessible MUI settings screen at `/app/{organizationId}/settings`, linked from an active Organization's board directory for internal Owners and Admins. Ordinary members, Portal-only accounts and inactive Organizations do not receive an editing form. The existing server membership, lifecycle, current-session and version checks remain authoritative for every save.

The screen edits name, description and optional logo URL. HTTPS absolute URLs without embedded credentials, up to 2,048 characters, are accepted by the server; empty values clear the logo. Invalid URL validation follows permission admission, so unrelated accounts receive the usual protected not-found response. This stores URL metadata only; binary files still require the separate object-storage implementation. It introduces no new schema or authorization surface.

Metadata PATCH carries the last reviewed Organization version. Only a valid 200 response naming the same Organization, matching the submitted metadata and advancing the version exactly once produces a saved acknowledgment. A conflict preserves the draft and disables further saves until current authorized settings load. The user can discard the draft or explicitly retain it after reviewing the current saved metadata. Permission/session loss clears draft and saved metadata; expired sessions return to sign-in.

A lost, timed-out or malformed save acknowledgment preserves the original metadata, version and retry key. The screen offers **Retry original save** and prevents editing or a replacement save while recovery is unresolved. The retry submits the same command to recover its durable acknowledgment. After recovery, a current authorized read is required before editing again; an old acknowledgment cannot replace another administrator's later settings. A definite conflict or expired acknowledgment requires current-state review before a new save. Creation/editing events now use the [canonical metadata journal, Worker readiness and protected live replay](organization-metadata-replay.md); required member/invitation event integration remains unfinished.

Each HTTP read/write has a 15-second deadline, duplicate in-flight submission is refused, unmount aborts pending work, and late responses cannot replace a different route or restore private data. Reads and saves check the account before and after their operation. The settings read targets one Organization directly rather than loading the whole directory. Account replacement or confirmed loss of administration clears the draft, current metadata and original-save intent. The settings route remounts on Organization change. Server/edge error text is not displayed as trusted product content.

Verification includes web components and typecheck/lint, API-host tests, restricted PostgreSQL contracts and mandatory desktop/phone keyboard scenarios against exact release images. Local .NET test execution is available. The exact-image Organization fixture checks rejected URLs and unauthorized callers leave Organization/member/invitation/Portal/audit state unchanged. Current release execution remains required; compilation alone is not runtime evidence.

Full PRD-03 remains open. Use the [current acceptance map](prd-03-acceptance.md) for functional coverage and outstanding work; individual settings checks do not establish complete Organization acceptance.

## Live changes and preserved drafts

Production settings subscribe to canonical metadata replay. Reset, change and
interruption callbacks queue an authorized scoped refresh. A background read
keeps draft fields editable and compares the returned snapshot with the latest
draft, including text typed while the read was pending. A different saved version
appears in a review panel and blocks a replacement save until explicit review.
The read does not move focus or discard unsaved text.

Live reads may verify admission and display later saved settings while an original
save is unresolved. They preserve its body, version and key; review/discard controls
remain disabled until that acknowledgment is recovered. Events during an in-flight
request queue another read after the request settles. A matching live snapshot
does not fabricate a save acknowledgment. Demo skips this unavailable channel.

All 24 focused settings tests, web type checking and lint passed locally. The
new release-image scenario covers genuine Worker-delivered versions in two tabs,
draft review, lost acknowledgment followed by a later edit, exact original
key/body recovery, keyboard operation, phone width and automated accessibility.
Browser TypeScript checks passed; runtime execution remains pending CI.

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

The metadata API-host cases now include concurrent same-key requests and actual
receipt-triggered session expiry. The expiry case observes the stored original
acknowledgment before the final actor check, advances the injected clock to the
actual session expiry, and requires 401 without metadata disclosure. The original
Organization/session must remain unchanged and the failed receipt must disappear;
same-key retry/replay then advances the Organization once. These cases compile
with zero warnings/errors; runtime execution remains pending CI.

The mandatory PostgreSQL fixture now holds the Organization parent lock until
two same-key requests are observed waiting. On release, both must return identical
acknowledgments with exactly one metadata audit, one receipt and one version
advance. This checks real concurrent contention rather than sequential replay.
Fixture syntax passes; exact-image execution remains pending.

The native fixture also delays the actual metadata receipt INSERT using a
disposable invoker trigger and observes the restricted API in PostgreSQL PgSleep.
The original cookie session expires during that wait. Final refusal must return
401 without a cookie or private acknowledgment, preserve the complete
Organization/audit/receipt and account/session snapshots, and leave the same key
available for the concurrent successful retry after the fixture restores the
original expiry. Cleanup removes the trigger and restores session expiry if the
check fails. Bash syntax passes; this coverage awaits exact-image execution.

The full web run at browser revision `107180d` completed with 1,497 passing tests
and two 5-second timeouts in unrelated Board metadata/URL-attachment recovery
cases. Both passed together in a focused run. Their polling assertions now reuse
the already identified controls, require them to remain attached, and preserve
the original enabled/focus checks and timeout limits. A full recheck is running;
the failed run is not release acceptance evidence.
