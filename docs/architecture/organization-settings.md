# Organization metadata settings

PRD-03 WS-FR-002/003 now has an accessible MUI settings screen at `/app/{organizationId}/settings`, linked from an active Organization's board directory for internal Owners and Admins. Ordinary members, Portal-only accounts and inactive Organizations do not receive an editing form. The existing server membership, lifecycle, current-session and version checks remain authoritative for every save.

The screen edits name, description and optional logo URL. HTTPS absolute URLs without embedded credentials, up to 2,048 characters, are accepted by the server; empty values clear the logo. Invalid URL validation follows permission admission, so unrelated accounts receive the usual protected not-found response. This stores URL metadata only; binary files still require the separate object-storage implementation. It introduces no new schema or authorization surface.

Metadata PATCH carries the last reviewed Organization version. Only a valid 200 response naming the same Organization, matching the submitted metadata and advancing the version exactly once produces a saved acknowledgment. A conflict preserves the draft and disables further saves until current authorized settings load. The user can discard the draft or explicitly retain it after reviewing the current saved metadata. Permission/session loss clears draft and saved metadata; expired sessions return to sign-in.

A lost, timed-out or malformed save acknowledgment preserves the original metadata, version and retry key. The screen offers **Retry original save** and prevents editing or a replacement save while recovery is unresolved. The retry submits the same command to recover its durable acknowledgment. After recovery, a current authorized read is required before editing again; an old acknowledgment cannot replace another administrator's later settings. A definite conflict or expired acknowledgment requires current-state review before a new save. Creation/editing events now use the [canonical metadata journal, Worker readiness and protected live replay](organization-metadata-replay.md); required member/invitation event integration remains unfinished.

Each complete read/save operation has one 15-second deadline covering both account checks, the Organization request and response-body decoding. Duplicate in-flight submission is refused, unmount aborts pending work, and late responses cannot replace a different route or restore private data. Reads and saves check the account before and after their operation. The settings read targets one Organization directly rather than loading the whole directory. Account replacement or confirmed loss of administration clears the draft, current metadata and original-save intent. The settings route remounts on Organization change. Server/edge error text is not displayed as trusted product content.

If a temporary account-check failure happens before a fresh PATCH, no original-save intent is created: the screen says no save was sent, preserves the draft and requires a current read and explicit review before another save. A failure after actual submission preserves the exact body/version/key until the original acknowledgment is recovered. Failed refreshes withdraw stale saved-review controls and block saving until fresh admission. Definite profile refusals clear private state. Settings reads and PATCH now send `X-StrataAI-Expected-Actor`; the API rejects a mismatched, malformed, empty or repeated value with neutral 401 `session_unavailable` before service or receipt admission. Existing API clients may omit it. Each original save retains its reviewed actor alongside body/version/key; a replacement account cannot receive its acknowledgment or apply its edit using that intent. Current server session, membership, lifecycle and final transaction admission still apply.

`organization-settings-account.spec.ts` adds four mandatory native cases at
desktop and phone widths. A normal registered administrator reviews actual
Organization data; a controlled 503 before PATCH requires zero writes and an
unchanged stored record, while a 503 after a real committed PATCH withholds its
acknowledgment and recovers the identical body/key without a second version
advance. Both paths preserve the draft, require current review, use keyboard
controls and check WCAG 2.2 AA without a document reload. Browser TypeScript
checking and collection of all four cases pass. Runtime execution against exact
release images remains required; test collection is not a native pass.

Verification includes web components and typecheck/lint, API-host tests, restricted PostgreSQL contracts and mandatory desktop/phone keyboard scenarios against exact release images. Local .NET test execution is available. The exact-image Organization fixture checks rejected URLs and unauthorized callers leave Organization/member/invitation/Portal/audit state unchanged. Current release execution remains required; compilation alone is not runtime evidence.

Full PRD-03 remains open. Use the [current acceptance map](prd-03-acceptance.md) for functional coverage and outstanding work; individual settings checks do not establish complete Organization acceptance.

## Live changes and preserved drafts

Settings in both runtime modes subscribe to canonical metadata replay. Reset, change and
interruption callbacks queue an authorized scoped refresh. A background read
keeps draft fields editable and compares the returned snapshot with the latest
draft, including text typed while the read was pending. A different saved version
appears in a review panel and blocks a replacement save until explicit review.
The read does not move focus or discard unsaved text.

Live reads may verify admission and display later saved settings while an original
save is unresolved. They preserve its body, version and key; review/discard controls
remain disabled until that acknowledgment is recovered. Events during an in-flight
request queue another read after the request settles. A matching live snapshot
does not fabricate a save acknowledgment. Demo simulates source delivery in its
owning API transaction; Production retains its separate durable Worker.

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

## Reviewed account binding verification

The API-host guard case uses two authenticated accounts with legitimate Owner
and Admin access. Wrong, malformed, empty and repeated identities must refuse
both private reads and new edits without changing the Organization or creating
a receipt. Correct identity can then use the same key once; changed identity
cannot disclose that committed acknowledgment, and correct replay advances no
additional revision. This case and all three metadata receipt/replay API cases
passed locally. API and API-test builds completed with zero warnings/errors
using the already-built shared libraries; the independent original persistence
run and its inputs remained unchanged. All 30 focused settings component cases,
web/browser TypeScript checks and lint passed.

Six mandatory `organization-settings-actor.spec.ts` cases replace actual browser
cookies before submission, after the before-profile check but before API admission,
or after actual commit, at desktop and phone widths. The replacement account is
an accepted Organization Admin. The middle case forwards the actual replacement
cookie with the original reviewed actor and requires API 401 plus unchanged
settings. Before/after cases respectively require zero commands or one legitimate
commit, and all retire private draft/acknowledgment/retry state without a document
reload. Keyboard and WCAG 2.2 AA checks remain required. All six collect and
type-check; exact-image runtime execution is pending. The mandatory restricted
Organization command fixture also checks neutral actor refusals before private
read/new edit/committed receipt and compares actual metadata, audit, receipt and
event-count state. Its shell syntax and snapshot SQL passed; full exact-image
execution remains pending.

## Executed local metadata and reviewed-account evidence

On 2026-10-07, both complete desktop/phone live settings scenarios passed against
the local Production API, restricted PostgreSQL and newly scoped Workers. They
preserved unsaved drafts through other-client edits and recovered the original
same-key/body save after a later administrator version. Both complete metadata
stream scenarios also passed: reconnect recovered the exact canonical event ID,
actor, version, timestamp and empty metadata; logout closed the stream and
refused replay. All four disposable Workers were retired.

The subsequent six reviewed-account scenarios exposed an Organization creation
acknowledgment precision defect in the four denied-edit cases. Creation now
returns the stored PostgreSQL row. The new mandatory persistence regression
fails before repair and passes three precision cases afterward. All six complete
native scenarios pass against a separate immutable repaired API, without changing
any fixture assertion. The first repaired-run attempt used 127.0.0.1 and failed
secure-cookie profile admission; the successful complete run uses localhost,
matching the existing Production sign-in fixtures. The temporary API/web process
and private configuration were removed. See [creation acknowledgment evidence](organization-creation-retries.md#canonical-persisted-creation-acknowledgment).

These are scoped local results. They do not establish current retained images,
all Organization lifecycle/Demo parity/performance or complete ticket acceptance.


## Unknown save warning during protected recovery

A current native desktop scenario exposed a real warning defect: the PATCH
committed and its reply was lost, but a queued live settings read cleared the
unknown-save error while the original retry intent remained. A current protected
read can reveal later settings; it cannot recover that command's acknowledgment.
Background load now retains the original uncertainty warning while an intent is
reserved. Explicit original-key retry still performs its own account/command/
account checks; definite refusal still withdraws private state and retry authority.
No command, receipt, API policy or automatic retry behavior changes.

The three strengthened invalidate/reset/unavailable regressions fail on the
missing warning before the fix. All 36 settings/telemetry component cases pass
after it, including exact original key/body recovery and permission/account
withdrawal. Web/browser TypeScript, targeted zero-warning lint and production
build pass (the existing bundle-size advisory remains).

The native fixture also waits for real actor/scope-bound ORGANIZATION_UPDATED
versions two and four and protected settings reads started after those sources.
Earlier/failed reads cannot satisfy readiness. Review choices must be enabled
and keyboard-focused before activation. A final desktop/phone invocation passes
both cases in 1.1 minutes against the new frozen web bundle, Production API,
restricted schema-110 PostgreSQL, Nginx and separate metadata discovery Worker.
Actual committed lost response, identical original key/body/version, later saved
version preservation, explicit draft discard, reload and WCAG checks remain.

Earlier native runs are not reported as passing: the original six-case baseline
had three passes/three failures; a deletion/settings-only attempt had three
passes/one settings failure; the first warning-fix browser invocation had one
pass/one late-review failure. The final two-case run verifies both settings cases
with the warning fix and event/read readiness together. The local runtime uses
explicitly unverified-account browser policy and disabled provider sending.
Current retained-image/full-PRD acceptance is still required.

## Native browser phase automatic metadata routing

Both existing desktop/phone settings cases fail at actual source/read settlement
when the release fixture leaves metadata discovery disabled. Both pass unchanged
with automatic discovery enabled, including versions two/four, committed lost
response, exact original key/body/version retry, later saved state, draft discard,
reload and WCAG checks. The final six-case Organization invocation also passes.
The phase now enables, persists and verifies the real Worker flag; product settings
behavior and interaction deadlines are unchanged. See the
[routing and invocation evidence](browser-recovery-ci.md#automatic-organization-metadata-routing-in-native-browser-acceptance)
for baseline/partial failures and local-versus-immutable verification limits.
