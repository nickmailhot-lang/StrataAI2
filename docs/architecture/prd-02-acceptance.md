# Authentication acceptance map — PRD-02

[PRD-02](https://github.com/nickmailhot-lang/StrataAI2/issues/2) remains open.
This map identifies verification paths; it does not certify release readiness.
Current changes must pass the complete pipeline against their exact revision.

## Functional requirements

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
The full container job remains live: passed steps establish their scoped
revision evidence, not complete current-main release readiness.

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

Older green CI runs linked from feature documents establish their recorded
revision only. Queued or live runs, successful image builds/security jobs, source
compilation and narrow tests cannot establish full current-release acceptance.
Inspect final required CI, native assertions, browser evidence and retained
immutable images before closure. The current PRD estimate is **25% work
remaining**; it is a planning estimate, not a count of unchecked functional rows.
