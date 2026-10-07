# Authentication acceptance map — PRD-02

[PRD-02](https://github.com/nickmailhot-lang/StrataAI2/issues/2) remains open.
This map identifies verification paths; it does not certify release readiness.
Current changes must pass the complete pipeline against their exact revision.

## Incorrect-password lifecycle privacy evidence

Ten API-host cases pass for unknown, Active, PendingVerification, Suspended and
Deactivated accounts under both email-verification policies. Repeated real HTTP
requests retain the same key and generic credential refusal, perform adaptive
verification, issue no cookie or receipt, disclose no private account fields,
and preserve account/event state. See [execution and limits](../identity-login-retries.md#incorrect-password-lifecycle-privacy).
The locked Release build passes with zero warnings/errors. This local Demo-store
evidence does not establish timing equivalence, restricted PostgreSQL or current
immutable release acceptance. Estimated PRD-02 work remaining stays **17%**,
a planning estimate; the issue remains open.

## Strict supplied authentication policy

API startup now rejects supplied malformed/empty Boolean policies and malformed,
empty or out-of-range password/session/security-token integer settings in Demo
and Production. Previously, invalid values silently selected defaults or were
clamped to the nearest boundary. Omitted settings retain their existing mode
defaults; valid settings retain their exact values. Configuration errors name
only the setting and supported form, without reflecting the supplied value.
See [policy configuration and upgrade guidance](configuration.md#authentication-policy-validation).

The regression first failed all thirteen invalid-setting cases against the
original implementation; the four default/boundary cases passed. After the
repair, all seventeen cases pass, covering both modes. Six actual compiled-API
startup runs also refuse invalid Boolean, excessive session lifetime and weak
password minimum settings across both modes, without echoing the supplied value.
These isolated containers had no external network and are retired.

The full solution builds with zero warnings/errors. All 756 domain cases pass
in Linux using the current compiled test assembly in a cached runtime image.
The Windows full-domain invocation has 740 passes and sixteen failures in
platform-dependent socket, symbolic-link and date cases; that invocation is not
claimed green. The seventeen policy cases pass independently on Windows.
All 29 selected existing identity API cases and all three documented Demo
account cases pass with the repaired application. These local checks do not
establish current retained-release image, browser, mail or complete PRD acceptance.
Estimated PRD-02 work remaining stays **18%**; the ticket stays open.

## Current desktop/phone Worker mail evidence

Both complete native mail scenarios pass in one 2.5-minute invocation using a
fresh isolated restricted PostgreSQL schema, current compiled API/separate Worker
and production web bundle behind current Nginx/CSP. Real Worker-delivered
verification/reset links, lost-success keyboard retry with identical body/key,
fragment removal, new-password sign-in and ten WCAG 2.2 AA/overflow checks per
viewport pass. Four durable jobs are SENT and the provider retains exactly four
effects with at least two attempts each. Actual mail-role logins are denied
password-hash and Organization reads. See [full execution and scope](identity-email.md#executed-desktop-and-phone-mail-recovery).

Browser types, Python syntax, workflow YAML and 142 embedded Bash checks pass.
CI now requires both desktop and phone scenarios against retained images. This
local result does not establish external inbox delivery or current immutable
release/full PRD acceptance. Estimated PRD-02 work remaining is now **17%**; the
issue remains open. Earlier estimates/evidence below are historical records.

## Executed history preference recovery and star focus repair

At source baseline `62d589ee`, both complete desktop/phone activity scenarios
pass against the frozen compiled Production API, real restricted schema-110
PostgreSQL, a separate Organization-scoped Worker and a production web bundle
behind current Nginx/CSP. They verify an independent client's saved timezone
change reaches the already open activity history without document reload,
unchanged original UTC timestamps, keyboard paging, actual two-client/reconnect
updates, revoked disclosure, archived/deleted history, historical attribution
after rename/deactivation, WCAG-tagged scanning and viewport overflow.

The initial local invocation used a mismatched realtime origin and failed four
cases; correcting only that fixture setting produces three passes and a desktop
star retry failure. Trace inspection confirms no second retry request was sent.
The star fixture now observes current parent admission and enabled keyboard focus
before activating the original retry. A subsequent desktop closing interruption
exposes a real background-read focus defect: the removed star action returns
while focus stays on the dialog container. Its controlled component regression
fails before the repair. Star reads now park owned action/Done/check/retry focus
before disabling/removing controls and restore it only after admission recovers
and focus remains owned. Existing intent, key and authorization rules are preserved.

All 58 focused star-control/history/live/sync cases pass, as do web/browser
typechecking, scoped lint, production build and Nginx configuration validation.
Both complete star browser scenarios pass against the repaired production bundle
in a final invocation (exit 0, 1.5 minutes), including actual private WebSocket
delivery, original-key recovery after a later unstar, open mirror timezone recovery,
unchanged source timestamps and Board/List data, keyboard close focus and reload.
The two activity passes and two final star passes are separate invocation evidence;
the intermediate failing runs are not claimed green.

These are compiled hosts/bundles in cached runtime images, not current retained
release images or performance measurements. The disposable API/web/Worker are
removed after execution. Full CI, other preference consumers and complete PRD
acceptance remain required. PRD-02 stays open at **17% estimated work remaining**,
a planning estimate.

## Functional requirements

The [expected-account command correction](profile-recovery.md#expected-account-during-commands)
prevents a stale profile draft, sign-out/deactivation confirmation or handle intent
from addressing a different account after a shared cookie is replaced. Server
admission occurs before the identity command service. Three baseline regressions
fail with successful operations; the final eight expected-account cases and two
existing replay cases pass with zero build warnings/errors. All 37 selected web
cases and seven actual PostgreSQL/browser scenarios pass, retaining original-session
deactivation/logout and profile-save receipt recovery. The account-switch cases
preserve both profiles/events and all four accounts remain at version 1. This
does not establish full current retained-image or PRD acceptance. Estimated work
remaining stays **17%**, a planning estimate; the issue remains open.

The [expanded native cookie-switch matrix](profile-recovery.md#executed-four-command-cookie-switch-matrix)
also passes all four commands at desktop and phone sizes in one eight-case
invocation against the compiled Production API and restricted PostgreSQL. Handle
claims test a cookie change after an actual successful preflight read. Both account
profile/event snapshots and both handle settings remain exact after refusal, with
no cookie deletion or false deactivation confirmation; all sixteen accounts remain
at version 1. This extends executed TC-05/08 coverage without asserting complete
current-release acceptance. PRD-02 remains open at **17% estimated work remaining**,
a planning estimate.

| Requirement | Implementation and verification path | Outstanding acceptance evidence |
| --- | --- | --- |
| AUTH-FR-001 registration | [Invitation registration](../invitation-registration.md), [registration retries](../identity-registration-retries.md), API registration replay/rollback cases, native registration fixtures | Current production-policy, invitation-backed and browser release checks |
| AUTH-FR-002 case-insensitive email uniqueness | Canonical identity storage and [identity schema](../../db/migrations/003_identity.sql); executed restricted PostgreSQL registration races below | Complete current-release registration policy/browser evidence |
| AUTH-FR-003 secure sign-in/out | [Sign-in retries](../identity-login-retries.md), [revocation retries](identity-command-retries.md), API login/revocation cases | Current release-image credential/session and browser checks |
| AUTH-FR-004 expiring single-use reset tokens | [Token consumption](../identity-token-consumption-retries.md), [email delivery](identity-email.md), token replay/final-admission cases | Current Worker delivery, elapsed-expiry, single-use and rollback evidence |
| AUTH-FR-005 adaptive password hashes | Identity password-hash provider and sign-in/reset verification | Current hash policy and persisted-secret protection checks; passing compilation is insufficient |
| AUTH-FR-006 session expiry/revocation | Sign-in/token/revocation/profile final-admission cases; native identity command fixtures | Current post-wait expiry, original-session withdrawal and durable transaction results |
| AUTH-FR-007 complete profile fields | [Profile management](profile-management.md), profile concurrency/replay fixtures and account browser scenario | Current persisted-field, concurrent save, recovery and browser evidence |
| AUTH-FR-008 deactivation with attribution | [Owner continuity](account-owner-continuity.md), account deactivation and assignment rollback cases | Current usable-owner floor, historical attribution and exact-image rollback/replay checks |
| AUTH-FR-009 failure privacy | [Recovery requests](../identity-recovery-request-retries.md), public auth/recovery contracts and abuse fixtures | Known/unknown and storage-failure disclosure checks plus current production limiter evidence |
| AUTH-FR-010 viewing preferences | [Profile date-display coverage](profile-management.md), Card date controls/badges, search, comments, notifications and activity | Full consumer review, two-client preference recovery, mobile/keyboard and current native release checks |

## Acceptance criteria and definition of done

- **AC-AUTH-02-01:** prove registration validation, durable authoritative state and browser confirmation under adopted production policies. Demo success alone is insufficient.
- **AC-AUTH-02-02:** prove stable refusal, unchanged protected state and no unauthorized disclosure for invalid sign-in/session operations, including waits and retry publication failures.
- **AC-AUTH-02-03:** prove preference changes reach or recover in other admitted clients without a full manual reload. [Identity realtime](identity-realtime.md) and [profile recovery](profile-recovery.md) supply the recovery contracts; individual display cases do not prove all consumers.
- **TC-01 through TC-13:** retain happy/empty/invalid/unauthorized, mid-session withdrawal, timeout/retry, duplicate/concurrent, reconnect, parent lifecycle, keyboard, mobile and applicable capacity evidence. No scenario becomes complete merely because a similarly named test exists.
- **Definition of done:** review authorization, schema, atomic audit/events, unit/integration/end-to-end execution, accessibility, error states, telemetry, performance, documentation and known defect severity against the current release revision. Record exclusions only when the PRD genuinely makes a scenario inapplicable.

## Evidence boundaries

### Executed native search preference recovery

Both current desktop/phone search deadline fixtures pass against the local
Production API and restricted schema-110 PostgreSQL role, with current Vite
source. An independent second session changes the saved timezone; the already
open results recover automatically while retaining an unsent filter draft and
the applied query. Existing Board-policy precedence/change/clearing, unchanged
UTC deadlines, date-only display, keyboard focus and automated WCAG-tagged
checks also pass. The completed invocation exits 0 with two cases in 37.1 seconds;
scenario duration is not a performance benchmark. Browser TypeScript and the
isolated production web build pass.

The first run failed before date rendering because the Vite API-root expression
missed `/search?...` and returned SPA HTML. The corrected query boundary passes
the actual browser flow; Nginx's existing path-based routing is unchanged. The
API remains the immutable local build at `6044227e`; backend `src` files are
unchanged from that revision through `f5fc2efb`. Current retained images, all
other preference consumers and the remaining PRD acceptance still need their
own evidence. See the [profile guide](profile-management.md#activity-and-personal-star-history-date-display).

### Current preference and profile retry checks

The current-source isolated Release API-test build passes with zero warnings or
errors. Six focused HTTP cases pass: one search date-policy projection/privacy
case and five profile retry/concurrency/invalid-key cases. They exercise the
composed framework host with Demo persistence; they do not certify PostgreSQL or
the complete release images. The [profile guide](profile-management.md#activity-and-personal-star-history-date-display)
records their scope and the executed 21-case history/formatter web check.

Activity and personal star history now share the account date formatter, with
an explicit zone label and unchanged original UTC timestamp. Their regression
cases check final confirmed preferences and Honolulu-to-Tokyo history refresh.
Desktop and phone release fixtures include corresponding actual-source date
assertions; new native execution remains pending. Full two-client preference
recovery for every consumer and complete current release CI are still required.

Open Board/Card activity additionally recovers account changes via the identity
stream, ten-second visible-page checks and focus/online/visibility recovery,
preserving its selected history cursor. Its completed 19-case activity/identity
suite passes queued-signal handling, periodic fallback, close/denial retirement
and keyboard focus ownership. Web/browser TypeScript and lint pass. The native
peer-preference update assertion is registered but not yet executed against
release images. This proves the scoped component recovery, not every consumer's
complete two-client acceptance. Estimated PRD-02 work remaining stays 23%.

Personal star history now also uses identity delivery and periodic/focus/online/
visibility recovery while preserving the selected revision cursor. Complete
combined activity/star/identity checks pass: 33 cases, including single queued
follow-up, denial retirement until fresh parent admission, periodic fallback,
close cleanup and MUI Dialog focus ownership. Web/browser TypeScript and lint
pass. Its new desktop/phone other-client preference-change assertions remain
pending native release execution; the other date consumers still require their
own recovery audit. This scoped increment leaves the estimate at 23% remaining.

### Executed security and final-admission checks

On 2026-10-07, an isolated Release build of source revision `be7b1616` passed
with zero warnings/errors. Seven focused API-host invocations then passed with
19 cases and no skips or failures:

| Executed fixture | Cases | Scope |
| --- | --- | --- |
| Unknown-account adaptive verification | 1 | Real framework hash verification, reusable dummy hash, no dummy identity or account/event mutation |
| Malformed password hash | 4 | Actual HTTP credential refusal, no cookie/disclosure, unchanged account/events and absent receipt |
| Sign-in final expiry | 2 | Real session/receipt publication, ordinary and legacy hashes, complete rollback and same-key retry |
| Recovery rollback | 6 | Reset/verification token and receipt writes, failure/cancellation races, preservation of original proofs, retry/replay |
| Token consumption final expiry | 2 | Reset/verification consumption rollback after receipt publication and same-key retry |
| Revocation final expiry | 2 | Logout/deactivation rollback after receipt publication, preserving sessions/accounts/assignments/events |
| Profile final expiry | 2 | Keyed/unkeyed profile publication rollback and authoritative retry |

These use the actual composed framework host with Demo persistence. The
final-admission fixtures advance an injected clock at observed publication;
they do not measure wall-clock expiry or establish PostgreSQL cancellation-race
behavior, external mail delivery, limiter capacity or full release acceptance.

The mandatory real-PostgreSQL persistence executable now includes
`IdentityRecoveryRollbackContract`. All six local Production combinations pass
under the restricted API login: reset/verification crossed with a real server
constraint failure, cancellation at final commit, and cancellation already
pending when the server constraint fails. Each case observes an actual token
proof and retry receipt before failure. The recovery adapters also publish the
production mail outbox and audit in that same transaction. After failure, a
complete database snapshot of the user, both token tables, receipts, mail jobs,
audits, sessions, event stream and events equals its original state. Earlier
proofs remain usable, failed proofs are absent, and same-key retry/replay leaves
exactly two token/mail/audit/receipt publications: the original and recovered
intent. Database failures return the neutral result; cancellation propagates.
This strengthens the PostgreSQL cancellation-race evidence. Failure timing is
injected after real publication through a duplicate receipt primary key; it is
not external mail transport, process-crash or HTTP response evidence. The focused
diagnostic is `--identity-recovery-rollback-only`; mandatory CI executes the same
contract as part of the full persistence suite.

That contract also passes two real elapsed-expiry cases for password reset and
email verification. An admin fixture shortens only the selected disposable
token's lifetime to eight seconds and installs an actor-scoped twelve-second
wait after actual consumption-receipt insertion. Observation requires this
contract's unique restricted API connection application name and PostgreSQL's
`PgSleep` state during that insertion; an early expired-token refusal cannot
pass. Final admission refuses with `invalid_or_expired_token` and no profile.
The complete original persisted snapshot, including consumption receipts, is
restored. After removing the temporary trigger and restoring only the selected
token's lifetime, same-key consumption succeeds, acknowledgment replay preserves
exact state and a new intent cannot consume the token again. Both the six
recovery rollback cases and these two elapsed cases pass in the final local
Production diagnostic, with zero build warnings/errors. The wait and shortened
lifetime are injected fixtures; actual wall-clock passage, server constraints,
Application final admission and PostgreSQL transaction rollback are real.

Separately, retained-image CI run
[37593384106](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37593384106)
at `f62785a6` reports successful container steps for production profile
concurrency, identity audit/logout rollback, session-bound logout/deactivation
receipts, credential-checked sign-in retries and active-owner continuity.
The successful sign-in script includes malformed stored-hash refusal and
known/unknown public-response/state privacy assertions. That script and the
seven focused fixture sources are unchanged between `f62785a6` and `be7b1616`.
The full container job subsequently failed in the large-Board desktop pointer
fixture; see [the retained trace and verification boundary](../kanban-release-evidence.md#retained-source-pointer-fixture-at-f62785a6).
Passed authentication steps establish their scoped revision evidence, not
complete current-main release readiness.

### Executed durable registration concurrency

`IdentityRegistrationConcurrencyContract` is now part of mandatory persistence
CI. Its focused local Production diagnostic passes two eight-client races under
the restricted API login. Eight independent service compositions/connections
start together against case and whitespace variants of one normalized email.
Distinct keys produce exactly one successful registration and seven
`email_unavailable` refusals without a profile. A shared key produces eight
identical authoritative account/token acknowledgments. Each race persists one
account, verification token, mail job, registration audit, canonical event and
receipt. The event retains the original actor/entity, version 1 and empty
metadata; the receipt belongs to the winning intent. The account remains pending
verification with its requested locale/timezone. Its configured adaptive hash
verifies without rehash, and raw passwords/verification tokens are absent from
the persisted snapshot. Case-normalized winner replay preserves exact state;
wrong-password and changed-display-name retries refuse without changing it.

The isolated Release build passes with zero warnings/errors. Public signup is
explicitly enabled only in this fixture, while verified-email and production
mail-outbox policy remain enabled. This is actual adapter/transaction concurrency
with a start barrier, not eight deployed API processes, an HTTP/browser flow,
external mail transport or a performance benchmark. Production's closed-signup
and invitation policies and complete current release CI remain required. The
focused diagnostic is `--identity-registration-concurrency-only`.

### Native browser recovery

Four existing native account scenarios now pass locally against an actual
schema-110 Production API and restricted database role: desktop/phone keyboard
deactivation with lost acknowledgment, original-session logout retry and
two-browser persisted profile conflict recovery. The profile case includes
canonical event recovery, saved UTC timestamp display, preservation of stale
drafts, explicit latest-state adoption, merged save and logout propagation.
See [executed profile recovery](profile-recovery.md#executed-local-production-recovery).
This compiled-source/Vite evidence strengthens the corresponding AUTH-FR-003,
006, 007, 008 and 010 paths; it does not establish all preference consumers,
current retained-image identity, complete provider/expiry/privacy acceptance or
full performance budgets.

Valid unknown-address sign-in attempts now perform adaptive verification against
a process-local dummy hash created once with the configured password provider.
The result never authorizes an account/session and the credential is never stored
or returned. A probe using the actual framework hasher checks unknown/known/unknown
wrong-password attempts all invoke verification, reuse the dummy hash, return the
same credential error, preserve the real account/events and create no dummy user.
The focused API-host case passes as recorded above. This removes the missing
verification-work distinction, not every possible timing difference (database
work and legacy hash costs may differ); production abuse limits remain required.

The mandatory native sign-in fixture compares known-address wrong-password and
unknown-address refusals through the actual API. Public status/title/type/code/
detail must match; request correlation identifiers are not compared. Neither
attempt may issue a cookie, disclose the account/email or change the complete
user/session/audit/event-stream/events/receipt snapshot. The unknown and dummy
subjects must remain absent from canonical users. The retained-image sign-in
step passes at `f62785a6`, as recorded above. These assertions verify public response/state privacy, not
exact request timing or complete abuse resistance.

The framework password-hash adapter treats malformed persisted Base64 encoding
as failed verification, preserving the ordinary `invalid_credentials` response.
It catches encoding `FormatException` only; unrelated infrastructure errors keep
their existing handling. Four API-host cases cover malformed and truncated hash
values, require no cookie or protected account/storage details, preserve account
and event state, and require no sign-in retry receipt. All four API-host cases
pass; retained-image PostgreSQL sign-in assertions pass at `f62785a6`. This repair
does not introduce a password algorithm or change valid-hash verification policy.

The mandatory exact-image sign-in fixture also substitutes malformed/truncated
hashes only on its disposable account, requiring 401 `invalid_credentials`, no
cookie/protected hash/storage details, and exact unchanged user/session/audit/
receipt state for every refusal. It restores the original encoded hash before
the existing audit/receipt rollback, same-key concurrency and session checks.
Cleanup restores the hash if interrupted. The retained-image sign-in step passes
at `f62785a6`; complete current-main CI remains required. Production schema and runtime privileges are unchanged.

The sign-in final-admission API-host theory now also seeds a real framework
Identity V2 password hash and requires the configured hash provider to request
rehashing. After observing the actual upgraded hash/version and stored sign-in
receipt, the injected clock reaches the new session's expiry. Refusal must restore
the original legacy hash/account version and remove the failed session/receipt.
A fresh same-key retry must upgrade exactly once to a hash that verifies without
requesting another upgrade; acknowledgment replay must preserve that version.
Both focused API-host cases pass as recorded above. This is scoped
rollback/upgrade coverage, not complete password-policy or release acceptance.

The complete web suite at unchanged browser revision `28f9355` passed with exit
0: 1,494 tests in 122 files (491.71 seconds). Strict API-test project compilation
also passed for the recent profile/sync admission and date-projection increments.
Windows Application Control blocked local .NET execution during those earlier
increments. Subsequent focused API-host and restricted PostgreSQL checks have
executed successfully, including the local browser scenarios above. Each earlier
pending case still needs its own execution evidence, and complete current
exact-image results must be inspected in CI.

### Elapsed profile session expiry in restricted PostgreSQL

`IdentityProfileExpiryContract` is now mandatory in the complete persistence
executable, with `--identity-profile-expiry-only` for bounded diagnosis. Both
local Production cases pass under the actual restricted API login: keyed and
unkeyed profile updates. They use SystemClock and a canonical verified account
and session, with the normal CommandActorAuthorization and transactional service.
The context models an admitted request; this is not an HTTP authentication test.

An actor-scoped temporary AFTER INSERT trigger verifies that the real updated
profile, audit and USER_PROFILE_UPDATED source exist in the transaction before
sleeping for twelve seconds. Keyed commands pause after actual receipt insertion;
unkeyed commands pause after actual event insertion. The disposable session's
eight-second expiry elapses during that pause. A bounded admin probe requires
the exact restricted connection/application to be executing the publication SQL
in PgSleep; an early refusal cannot satisfy the case. The temporary verification
function uses its fixture administrator's read authority with a fixed search
path, not additional runtime grants, and the trigger/function retire in finally.

Final admission returns session_unavailable with no profile value. Exact complete
user/session/profile-receipt/audit/event-stream/event snapshots match the state
before the command. Restoring only the disposable session's lifetime permits the
original intent to update all requested profile fields once, publishing exactly
one audit/source with the canonical actor/entity/version, empty metadata and
correlation, while preserving its session. Keyed replay returns the identical
acknowledgment without writes; unkeyed stale-version resubmission is refused.
Changed intent is refused with idempotency_key_reused or version_conflict and
unchanged state. The final isolated Release build passes with zero warnings/errors;
both strengthened real-PostgreSQL cases exit successfully. This adds elapsed
storage proof for AUTH-FR-006/007, not full HTTP/provider or release acceptance.

### Elapsed logout and deactivation session expiry in restricted PostgreSQL

`IdentityRevocationExpiryContract` runs in the mandatory persistence executable;
`--identity-revocation-expiry-only` selects its two bounded diagnostic cases.
Both local Production cases pass under the restricted API login with SystemClock,
the normal CommandActorAuthorization and transactional identity service. Accounts,
Organization/Board membership and a Card assignment are seeded fixtures; the
context models an admitted request rather than proving HTTP authentication.

An actor-scoped temporary AFTER INSERT receipt trigger first requires the actual
account status/version, revoked session, canonical identity source and expected
Card assignment state inside the transaction. It then sleeps twelve seconds
while the original session's eight-second lifetime elapses. A bounded admin probe
requires the restricted connection to reach that publication SQL in PgSleep.
The fixture verifier uses SECURITY DEFINER with a fixed search path and retires
in finally; runtime privileges are unchanged.

Final admission refuses with session_unavailable and no success value. Complete
snapshots of accounts, sessions, receipts, audit/source streams, issuer-authority
proofs/routing/effects, Organization/Board membership, Card assignments and tenant
jobs match the pre-command state. Restoring only the disposable session lifetime
allows the original intent to recover: logout revokes its single session and
preserves the assignment; deactivation revokes both sessions, removes the
assignment with a Card version/event and publishes one issuer-authority source
and routing job. Each produces one attributed identity source and receipt.
Same-key replay changes no state, an opposite revocation kind returns
idempotency_key_reused, and a fresh key cannot reuse withdrawn session authority.
The isolated Release build passes with zero warnings/errors and both cases exit
successfully. This strengthens AUTH-FR-003/006/008 storage acceptance; current
exact-image, HTTP, two-client and full release acceptance remain required.

### Open comment preference recovery

Clean comment dialogs now follow identity preference delivery with visible
periodic/focus/online recovery, protected account/page/account reads and retained
continuation. Dirty drafts and uncertain commands defer the signal while retaining
the exact original intent. Owned action focus is recovered after row replacement;
401/403/404 retires background disclosure and reads. All 30 focused comment and
identity checks pass, as do web typechecking/lint and browser typechecking. The
mandatory desktop/phone native fixture requires both dialogs to adopt a timezone
changed by another session without manual Review, reload, history mutation or a
new comment write. Strengthened native execution and current release CI remain
pending. Estimated PRD-02 work remaining stays **22%**.

### Invitation history preference recovery

Open issued Organization/Portal and Board invitation history now follows identity
delivery and visible periodic/focus/online checks using its normal protected reads.
Continuation and unchanged consent/focus survive quiet checks; authority sources
take precedence, changed history retires consent, queued signals coalesce and
denial/unmount retire recovery. All 75 combined invitation-history/comment/identity
cases pass, including 45 invitation-history cases. Web typechecking/lint and browser
typechecking pass. Both desktop/phone Organization history scenarios pass against
the local Production API and restricted PostgreSQL, covering independent-session
timezone changes, preserved keyboard consent, immutable history and the original
single-write lost-response recovery. Board native assertions are registered but
remain pending execution. See [invitation history](../invitation-history.md#account-preference-recovery)
for the initial-read readiness correction and exact local evidence boundary.

Predecessor CI `37613124227` failed one comment test because admitted rows appeared
before the passive identity listener was registered; 1,855 other web cases passed.
The fixture now waits for the actual listener before delivering a simulated event;
its original display and unchanged-history assertions remain. The complete affected
suite passes locally. Full current release CI and remaining consumers still need
verification. The estimate stays **22% work remaining**; no closure is justified.

### Invitation creation final preferences

Organization and Board creation now use validated locale/timezone from the final
account check after POST publication, and permission admission uses its final
confirmed preferences. Two publication-race regressions fail before the fix;
all 52 creation component cases pass afterward, including unchanged-key/body
lost-response recovery and final Tokyo display. Web typechecking/lint and browser
typechecking pass. The mandatory desktop/phone administrator fixture changes the
author's timezone from a separate session after actual creation commits and before
its first reply is lost, then requires the actual stored expiry in Tokyo after
reload and original retry. Its complete Worker-backed native execution remains
pending. See [administrator invitation creation](invitation-administration-ui.md#final-confirmed-expiry-preferences).

Predecessor `05bf10e7` CI run `37614051459` completed the web-quality job
successfully, including all 134 test files and the production web build. This
confirms the prior comment/invitation listener-registration fixture fixes at that
revision; it does not prove the newly changed creation source or pending container
and full release gates. Estimated PRD-02 work remaining stays **22%**.

### Executed creation acknowledgment recovery

Clean Organization and Board acknowledgments now recover account preferences via
identity delivery and visible periodic/focus/online checks using normal protected
scope/account admission. Drafts and uncertain commands defer recovery without
replacing key/body; queued signals coalesce, denial/unmount retire fallback and
owned action focus survives quiet reads. All 61 focused creation/identity cases
pass; web/browser typechecking and lint pass.

Both complete desktop/phone administrator native workflows pass against the local
Production API, restricted schema-110 PostgreSQL and current Vite source with
disposable Workers restricted to each new fixture Organization. They exercise a
timezone changed by another session after actual publication but before a lost
reply, identical original recovery after reload, automatic preference recovery in
the confirmed acknowledgment, unchanged actual history and keyboard focus. Existing
recipient acceptance, canonical metadata delivery and separate Portal grants pass.
The full invocation exits 0 with two cases in 30.5 seconds. The initial retry now
waits for actual metadata readmission; an earlier HTTP 201 was correctly fenced
when that epoch changed. See [creation recovery evidence](invitation-administration-ui.md#open-acknowledgment-preference-recovery).

These local immutable API/Worker builds and Vite source are not current retained
release-image evidence. Full release CI and the remaining date consumers still
require acceptance. Estimated PRD-02 work remaining is now **21%**.

### Executed local comments, activity and personal stars

All six complete desktop/phone native scenarios now pass against the local
Production API, restricted schema-110 PostgreSQL and current Vite source, using
disposable Workers scoped only to the new fixture Organizations. Comments prove
both-client automatic timezone recovery, immutable history, same-key lost-response
recovery, offline edit delivery, redaction, former-body refusal, exact source events
and automated accessibility checks. Activity proves actual paging, unchanged UTC
source display, automatic preference recovery, reconnect, access loss and historical
attribution through rename/deactivation. Personal stars prove account privacy,
private WebSocket delivery, original retry after a later change, automatic mirror
preference recovery, unchanged Board/List data, closing focus and reload.

The complete runs exposed and now verify two repairs: comment recovery must perform
fresh protected admission after a transient read cleared the displayed version,
retaining a cursor only at its original current version; star-history closing focus
must retain its Dialog reference after the activated Close button is removed.
The new regressions fail before their fixes. All 63 combined component cases and
web typechecking/lint pass. See [executed local evidence](profile-management.md#executed-local-comment-activity-and-star-recovery)
for scope and timings. Current retained release images, full CI and remaining
consumer acceptance are still required. Estimated PRD-02 work remaining: **20%**.

### Executed local notification preference recovery

The complete desktop/phone notification scenario passes local Production API,
restricted schema-110 PostgreSQL and current Vite source with a disposable Worker
scoped only to its new Organization. An independent recipient session changes
locale/timezone to en-US/Asia/Tokyo and then UTC; both inboxes recover automatically
without changing stored notification datetime/read history. Original live delivery,
same-key/body uncertain read recovery, bulk read, offline retained-cursor replay,
keyboard focus, access filtering and automated accessibility assertions pass.
The initial reconnect failure exposed a transient shell remount that discarded
the feature's cursor; the repair retains a hidden tree only through failed
transport admission, with actual denial still unmounting protected content.
Inbox denial also retires queued/background reads until explicit fresh admission.
Both defects have regressions that fail before the fix. All 166 focused source
cases, web/browser typechecking and lint pass. Installed MUI dialogs are included
in the hidden surface boundary, explicit retry preserves their draft and hidden
focus enforcement stops. Both complete comment native cases also pass after
repairing the original-receipt preflight race without weakening server admission
or the existing native assertions. See [notification evidence](notification-inbox.md#current-local-preference-and-reconnect-evidence)
and [final scoped source/native evidence](profile-management.md#executed-local-notification-preference-recovery).

This is scoped local runtime evidence, not current retained-image release
acceptance. Full CI and remaining consumers remain outstanding. Estimated PRD-02
work remaining is now **19%**.

Older green CI runs linked from feature documents establish their recorded
revision only. Queued or live runs, successful image builds/security jobs, source
compilation and narrow tests cannot establish full current-release acceptance.
Inspect final required CI, native assertions, browser evidence and retained
immutable images before closure. The current PRD estimate is **19% work
remaining**; it is a planning estimate, not a count of unchecked functional rows.

### Executed local date preference recovery

Card captions and Board due badges now recover account preferences through
identity events and visible periodic/focus/online/visibility reads. Signals
coalesce during admission, denial retires background checks, and account changes
require fresh parent admission. Board policy and canonical UTC values are preserved.

All 101 focused date/Board source cases passed, alongside four complete local
desktop/phone native cases. Independent-session timezone changes reached open
views automatically with canonical Board responses unchanged. A fail-first
StrictMode regression and the full policy native rerun verify the initial
admission cleanup repair. See [date recovery evidence](card-dates.md#open-date-preference-recovery).

This is local Production-runtime evidence, not retained-image acceptance. Full
current CI and PRD-wide acceptance remain required. Estimated PRD-02 work
remaining is now **18%**; the ticket remains open.

### Executed account browser and current-source checks

The complete web suite at `8c6d2b9a` passed locally: **134 files, 1,896 cases**,
exit 0 in 643.07 seconds with two workers. Exact-commit CI run 37625889421 also
passed all 1,896 web cases, .NET quality, restricted PostgreSQL integration and
the source gate. Its release-image/container gates were still live when recorded.
No application/backend source changed during or after these checks in this increment.

The account and mention-handle native invocation completed with 11 passes and
three failures. Two recovery cases needed email-enabled Production configuration;
the local API correctly refused with its configured 503. A separate disposable
API with an ephemeral token key then passed both full cases, including malformed
acknowledgment retry, generic confirmation and invalid reset-link recovery.
These cases use unknown accounts/invalid tokens; they do not prove real email
transport or Worker delivery. The disposable API/web process and private key
configuration were removed afterward.

The third failure was a keyboard fixture race: live recovery disabled the retry
control between separate focus and page-level Enter actions, so Enter refreshed
instead of submitting the original acceptance. The fixture now presses Enter on
the intended control. The complete invitation scenario passed after this change,
retaining the exact two original-ID commands, lost acknowledgment, empty discovery,
protected-label withdrawal and canonical membership assertions. No application
admission, denial or command guard was weakened.

The profile scenario also passed with added WCAG 2.2 AA tagged axe checks on
both desktop/phone views and the phone conflict state. Canonical event/timestamp
assertions, dirty-edit preservation, latest-version merge, live preference recovery
and logout in both views remain enforced. All 14 distinct account/handle scenarios
therefore have scoped local passes across the initial invocation and targeted
reruns; this is not a single green complete invocation or retained-image proof.
Browser typechecking passed after the fixture changes.

Seven focused API-host cases passed from the isolated Release test output:
four malformed persisted hashes refuse without identity/event/receipt/session
mutation; unknown-account requests perform adaptive verification without dummy
identity persistence; two post-publication sign-in expiry cases roll back and
recover with the original key, including legacy hash upgrade. These use Demo
persistence and do not replace restricted PostgreSQL or release-image evidence.

Estimated PRD-02 work remaining stays **18%**. Current full release acceptance,
mail delivery and PRD-wide audit remain outstanding; the ticket stays open.
