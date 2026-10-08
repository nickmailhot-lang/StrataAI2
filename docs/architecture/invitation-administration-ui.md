# Administrator invitation creation

Board issuance browser acceptance now waits for the initial live permission
refresh before composing or retrying its retained request. The qualified
Board-read tracker excludes reads from the previous canvas and retains its
five-second deadline. This fixes keyboard activation while background admission
temporarily disables the control; it does not retry a failed test action.
The companion history view admits a protected read after a validated pending
stream head, with independent fresh administration and account checks. See
[validated pending Board head](../invitation-history.md#validated-pending-board-head)
for scoped evidence and remaining exact-image verification.

Current internal Owners and Admins can open `/app/{organizationId}/invite` from the member administration screen. Admission reads the current account and authorized exact-member review; no saved grant details or email are restored before current actor/Organization administration is confirmed. Direct Member, Portal-only, inactive Organization and revoked-session access is denied by the server.

The MUI form selects canonical internal or Portal roles. Internal Member is the default; only current Owners can select an internal Owner grant. Portal choices match the canonical service contract and do not create or downgrade internal membership. The server independently verifies every grant.

Before sending, the form retains a UUID and the exact normalized input in tab-scoped session storage, keyed by current account ID and Organization ID. No bearer token, password, provider secret or acknowledgment is stored. Storage failure prevents the first mutation. An unreadable retained request fails closed rather than silently replacing an unknown intent. Saved input is restored only after current authorized admission for that same scope. Confirmed sign-out and deactivation purge this namespace while preserving unrelated site data. A transient session/permission refusal clears the UI but keeps the scoped pending intent available for later freshly authorized recovery.

A missing acknowledgment, transport/body timeout, malformed success or throttling freezes email/surface/role and offers an explicit retry with the identical key and body. Reload and navigation preserve the retained intent. Duplicate pending activation is refused, request time is bounded to 15 seconds even when transport ignores abort, and route/unmount fences suppress late acknowledgments. Known validation rejection permits corrections; expired/reused keys stay blocked and reserved until existing invitations are reviewed. Full administrator invitation history/revoke UI remains required to support those reviews.

Only HTTP 201 with matching Organization, canonical input, valid invitation ID, token-free acknowledgment and explicit-offset expiry confirms creation. Expiry is shown using the current account's locale/timezone. The screen distinguishes historical creation acknowledgment from delivery and current recipient access; it does not claim email was sent, an invitation remains pending, or membership was granted. After acknowledgment, explicitly choosing “Create another invitation” clears the prior confirmed intent and starts new input with a new UUID.

Local validation includes the full web suite, typecheck/zero-warning lint, and actual desktop/mobile keyboard browser workflows. The browser tests commit creation while dropping its response, reload, retry the original key/body, verify exactly one pending invitation, accept it, create a separate Portal invitation and verify its acceptance leaves internal membership unchanged. Component cases cover storage boundaries, permission denial, key expiry/conflict, validation, throttling, malformed acknowledgments, timeout/duplicate/unmount behavior and draft cleanup on confirmed lifecycle commands. Mandatory exact-image execution remains pending for this UI commit.

This implements the creation controls; Worker invitation email delivery, invitation-driven registration with self-registration disabled, administrator history/revocation, invitation events and remaining PRD-03/60 acceptance are still outstanding. No broader issue completion is claimed.

## Native account and deadline recovery admission

The creation account/deadline browser fixtures now observe the actual scoped
SignalR Watch invocation, its first head and a fresh protected scope read started
after that head. Board heads use the Board stream page; Organization heads retain
the Organization-and-actor envelope. Handshake, foreign scope/actor, malformed
head, previous-screen requests and late reads from an older head cannot establish
readiness. The observer is passive: it changes no stream, request or application
state and retains only fixture counters/known scope IDs in memory.

Account substitution/unavailable-account recovery starts a new watcher. The
fixture waits for its actual head and protected read before keyboard retry.
Aggregate-deadline recovery likewise waits for the freshly admitted read; a
withdrawn account also requires a new head. The existing five-second readiness
observation, 15-second whole-operation deadline, original command key/body,
private-display withdrawal, one canonical invitation and accessibility assertions
remain intact. The fixture does not automatically retry a failed mutation or
accept an unconfirmed result.

The baseline current-main run reproduces eight failures and four passes across
the twelve native creation account/deadline cases. Saved original intent and
fresh permission recovery are intact, but a retry activated before bootstrap
settles can have its displayed acknowledgment fenced by that real invalidation.
The fixed observer tests include previous-screen exclusion, stale-head reads,
foreign Organization/actor envelopes and failed reads. All ten combined
Board-read/admission observer cases pass; browser TypeScript passes. CI's mandatory
source readiness gate now runs both observer suites before the build-once image
pipeline. Workflow changes are inspected statically; no YAML-parser result is
claimed.

An intermediate observer run used the unwrapped Board page shape for Organization
heads, so it stopped at readiness instead of exercising those workflows. That
known-invalid runner was stopped after verifying its process identity; the
Organization wrapper was corrected and given a negative scope/actor regression.
It is not reported as a passing complete invocation or a product failure.

The final complete invocation passes all **12 native desktop/phone creation
account/deadline cases in 5.9 minutes**, against the same frozen Production web,
compiled Production API and separate Worker, restricted schema-110 PostgreSQL
and Nginx. Organization metadata and recipient/issuer authority routing are
enabled on the Worker; provider email sending and scoped work-event delivery are
outside this fixture. Actual commits, cookie substitutions/unavailable account
reads, full-operation expiry, private-display withdrawal, original-key/body
recovery and one canonical invitation are verified. The four deadline cases also
retain unchanged no-reload and WCAG checks. All three disposable containers are
removed after the terminal run, preserving the original services/images/volumes.
This compiled-source evidence does not prove current immutable-image acceptance.

This remains browser-fixture verification, not a change to production authority,
command receipts or automatic retry behavior. Current full exact-image CI and
the other historical browser/performance failures remain required before closure.

## Complete-operation deadlines and original-request recovery

### Open acknowledgment preference recovery

Confirmed Organization and Board acknowledgments now follow identity preference
delivery plus visible ten-second, focus, online and visibility recovery. Each
refresh uses normal bounded account/scope/account admission and final preferences.
Signals defer while an unsent draft or uncertain command is unresolved; no
background preference check submits a mutation or replaces the captured key/body.
Signals during a read coalesce into one subsequent check. Denial and unmount stop
the listener and fallback. Owned action focus survives a quiet read; focus moved
to another control stays there.

All 61 focused creation/identity cases pass, including both scope regressions,
draft/original-command deferral, queued recovery, periodic fallback, focus ownership
and denial cleanup. Web/browser typechecking and lint pass. Both complete native
desktop/phone administrator workflows pass against the local Production API,
restricted schema-110 PostgreSQL and current Vite source. The first real creation
commits before a separate author session changes Honolulu to Tokyo and its reply
is dropped. Reload and original retry require identical key/body, one invitation
and the final Tokyo caption. A further change to UTC updates the already confirmed
acknowledgment automatically without reload or another POST, retains Create another
invitation focus and leaves actual invitation history unchanged. Existing recipient
acceptance, canonical metadata delivery and independent Portal grants also pass.
The invocation exits 0 with two cases in 30.5 seconds; timing is not a benchmark.

The first native run retried before the reload's initial metadata refresh finished:
HTTP 201 was correctly fenced by the newer admission epoch. The fixture now waits
for the actual checked-permissions notice before retrying, retaining the original
assertions. Two disposable Workers process only the two newly created test
Organizations with global discovery disabled, then retire. API/Worker outputs are
immutable local builds, not the current retained release images; backend source
is unchanged from the local API's `6044227e` build. Complete current release and
remaining preference-consumer acceptance are still required before closure.

### Final confirmed expiry preferences

Both Organization and Board creation now validate and use locale/timezone from
the final account check after publication. Protected permission admission also
uses its final confirmed preferences. A timezone changed while the POST is in
flight cannot leave an acknowledged expiry formatted with pre-command settings.
The stored expiry and retained command remain unchanged; no preference update is
added to the invitation payload. Invalid final preferences withdraw display
authority through the existing account-uncertainty path and preserve the stored
original intent rather than falsely confirming creation.

Both new scope regressions fail before the fix. All 52 creation component cases
pass after it, including two original lost-response retries requiring identical
key/body and the newly confirmed Tokyo expiry display. Web typechecking/lint and
browser typechecking pass. The mandatory desktop/phone administrator native
fixture now changes the author's timezone from a separate authenticated session
after the actual first creation commits and before its reply is dropped. After
reload and original retry it requires the Tokyo caption for the actual stored
expiry and one pending invitation. Its original acceptance, metadata delivery and
Portal isolation assertions remain. Strengthened native execution remains pending
the Worker-backed release fixture; this source result does not prove full
two-client recovery of every preference consumer or current release acceptance.

Organization and Board invitation admission now share one 15-second deadline
across the initial account read, protected scope read, final account read and JSON
parsing. Creation/retry has its own single 15-second deadline across pre-command
account verification, POST and post-command account verification. Requests check
the child signal before transport and after body parsing, so an adapter that
ignores abort cannot publish a late permission result or acknowledgment.

At the deadline the page withdraws private fields and current submission authority.
The tab/account/Organization/Board-specific stored intent remains reserved. A fresh
permission check must admit the same account and scope before restoring the
original request; explicit recovery reuses its exact key and body. A prompt
transport failure retains the already reviewed locked request, while uncertain
account verification withdraws it. Neither path replaces an unknown command.

Component coverage includes aggregate admission and create deadlines at both
Organization and Board scope, late JSON suppression, and exact original recovery.
The native `invitation-creation-deadline.spec.ts` adds four desktop/mobile cases:
keyboard creation, an eight-second pre-command account delay plus a held real
committed POST response, deadline withdrawal, explicit same-key recovery, one
persisted invitation, no document reload and WCAG 2.2 AA checks. Browser-clock
advancement tests the client deadline; it does not measure server latency or
advance server cookie expiry. Exact-image native execution remains pending.

The focused invitation creation component suite passed all 48 cases after this
change, including the existing transport-loss, account replacement, live
invalidation, storage, permission, validation and exact-Board recovery cases.


## Native history account and deadline admission

The history account/deadline fixtures now require the actual scope/actor-bound
stream head and a protected invitation-history read started after that head
before beginning keyboard or account-failure actions. The passive observer accepts
an explicit protected collection path; unrelated successful membership/Board
reads cannot admit history. The existing five-second readiness assertion remains.
No production code, authority, mutation, timeout, rate limit or receipt changes.

The current-source baseline completed with seven passes and one Organization
desktop account case failing its private-heading withdrawal assertion. Its trace
shows the injected account-read 503 followed by a successful protected history
refresh: the initial watcher invalidation raced the uncertainty scenario. Waiting
for the actual bootstrap history read fixes that fixture race without weakening
withdrawal or permitting an unconfirmed result.

The complete corrected eight-case native invocation passes in 4.1 minutes:
Organization/Board desktop/phone account uncertainty before and after actual
revocation, plus both account checks inside one fifteen-second deadline. Existing
no-command-before-confirmation, one actual committed revocation, private-display
withdrawal, explicit current-state recovery, exact canonical history, no full
reload, keyboard and accessibility assertions remain. Eleven combined passive
observer tests and browser TypeScript pass, including rejection of unrelated or
failed collection reads. CI already requires both observer suites.

Runtime evidence uses the frozen Production web/API assemblies, Nginx, real
restricted schema-110 PostgreSQL and a separate discovery Worker with email
sending disabled. It does not prove current retained images, provider delivery or
full two-client event delivery. Temporary API/web/Worker containers were removed
after the terminal run, preserving the original services, images and volumes.


## Production Organization administration verification

Both unchanged desktop/phone Organization administration cases pass in the same
complete eight-case invocation as the six corrected history expiry cases (four
minutes total). Actual first creation commits while its reply is dropped; reload
and explicit retry use the identical key/body and preserve one pending invitation.
A separate owner session changes Honolulu to Tokyo before retry and then UTC;
the displayed real expiry recovers without replacing the acknowledgment, issuing
another command or stealing focus from Create another invitation.

Actual recipient acceptance is observed as an Organization-member-added stream
source, followed by a protected scope refresh. The historical acknowledgment
remains and Retry stays disabled. A new Portal invitation uses a different key
and the OWNER role, and actual Portal acceptance leaves the existing internal
membership unchanged. This is native Production-mode proof with a separate
metadata discovery Worker and real restricted PostgreSQL; provider sending is
disabled and strict verified-account policy/current immutable images are separate
requirements. No administration test or product source changed for these cases.
See [Production expiry admission](../invitation-history.md#production-expiry-consent-admission)
for the reproduced expiry fixture race and correction. Full PRD-wide release
acceptance still prevents closure.


## Board creation and consent admission verification

The [four final Board administration/history cases](../board-invitation-release-evidence.md#board-creation-and-history-admission-after-real-source-frames)
pass together in 2.6 minutes. A two-read bootstrap assumption allowed a later
permission refresh to clear the entered email, sending no POST; the partial
strengthening reproduced that failure at 390px. Creation now waits for its actual
scoped head and subsequent protected Board read before composition and after
reload. History reviews require actual issuance frames and protected history reads
started after them. Enabled focus precedes one activation. Original identity,
request/body/key, revocation count and canonical rows remain, alongside actual
acceptance, preference changes, reconnect, cancellation focus and tagged Axe.
Eleven passive observer tests and browser TypeScript pass. Local compiled
Production API/MUI, restricted PostgreSQL and scoped Worker evidence uses the
full-browser phase's optional verification policy; strict/provider and full current
immutable acceptance remain separate. PRD-05 stays open at **15%**, and PRD-60 at
**18% estimated work remaining** (planning estimates).

## Organization invitation admission with automatic metadata delivery

The browser release phase now enables actual automatic Organization metadata
routing. Composition and reload require a matching actor/Organization Watch head
and protected membership read after it. Enabled focus precedes each keyboard
activation and the deliberate preference change. The intermediate phone trace
shows focus attempted on a disabled acknowledgment action; the final test still
requires focus preservation after the actual preference update.

Both widths pass in the final six-case native invocation, retaining committed
lost-response creation, identical original key/body across reload, canonical
single invitation, actual member-added frame/protected refresh, exact expiry
formatting, no extra writes and separately accepted Portal access with internal
membership unchanged. See the
[baseline, final execution and release limits](browser-recovery-ci.md#automatic-organization-metadata-routing-in-native-browser-acceptance).
PRD-03 stays open at **8%**, and PRD-60 at **18% estimated work remaining**
(planning estimates). Current immutable CI and full acceptance remain required.
