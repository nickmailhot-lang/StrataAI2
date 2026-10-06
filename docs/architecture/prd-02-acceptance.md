# Authentication acceptance map — PRD-02

[PRD-02](https://github.com/nickmailhot-lang/StrataAI2/issues/2) remains open.
This map identifies verification paths; it does not certify release readiness.
Current changes must pass the complete pipeline against their exact revision.

## Functional requirements

| Requirement | Implementation and verification path | Outstanding acceptance evidence |
| --- | --- | --- |
| AUTH-FR-001 registration | [Invitation registration](../invitation-registration.md), [registration retries](../identity-registration-retries.md), API registration replay/rollback cases, native registration fixtures | Current production-policy, invitation-backed and browser release checks |
| AUTH-FR-002 case-insensitive email uniqueness | Canonical identity storage and [identity schema](../../db/migrations/003_identity.sql); registration fixtures | Current real PostgreSQL uniqueness/concurrency evidence |
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

The complete web suite at unchanged browser revision `28f9355` passed with exit
0: 1,494 tests in 122 files (491.71 seconds). Strict API-test project compilation
also passed for the recent profile/sync admission and date-projection increments.
Windows Application Control prevents local .NET test execution; native API-host,
restricted PostgreSQL and exact-image results must be inspected in CI.

Older green CI runs linked from feature documents establish their recorded
revision only. Queued or live runs, successful image builds/security jobs, source
compilation and narrow tests cannot establish full current-release acceptance.
Inspect final required CI, native assertions, browser evidence and retained
immutable images before closure. The current PRD estimate remains **29% work
remaining**; it is a planning estimate, not a count of unchecked functional rows.
