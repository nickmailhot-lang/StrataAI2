# Sign-in retry protocol

`POST /auth/login` accepts an optional nonempty UUID `Idempotency-Key`. Each keyed attempt checks the current adaptive password hash, account status and verification policy before reading its receipt. Concurrent duplicates serialize under the existing identity command transaction. Session creation, its audit and the typed receipt commit together; a publication failure rolls them back.

A receipt contains the original session ID, signing-key version, keyed intent fingerprint and expiry. It contains no password, raw cookie, response body or cached profile. A matching retry derives the original opaque token with the retained key version and checks that the original session remains active and unexpired. It returns that session's original expiry and the current authorized profile. Logout, reset or session expiry prevents replay from reopening authentication. A new attempt requires a fresh credential check.

Production requires API-only runtime secrets `STRATAAI_AUTH_RETRY_CURRENT_KEY` and `STRATAAI_AUTH_RETRY_KEYS`. The latter is a JSON object mapping unique ASCII key versions to base64-encoded 32-byte keys. Supply it through the deployment secret mechanism, never source control. Startup rejects malformed, duplicate or missing current keys without reporting secret values. Session derivation and intent fingerprints use distinct HMAC purposes. The Worker receives neither key ring nor session token derivation access.

Rotate by adding a new key and selecting it as current while retaining every version referenced by unexpired receipts. Receipts last at most 24 hours. Removing a required retained key makes retries fail closed with a masked service-unavailable response. Demo uses an ephemeral in-memory key and makes no restart durability promise.

The existing Worker deletes at most 100 expired sign-in receipts per cleanup transaction, alongside at most 100 profile and 100 revocation receipts. Restricted PostgreSQL policies deny receipt updates and prevent the Worker from reading fingerprints, key versions or session IDs. Expired receipts reserve their UUID until cleanup; after removal, reuse is a new independently credential-verified attempt. This is bounded retention, not permanent key reservation.

The MUI sign-in form preserves a UUID for unchanged submitted details after an uncertain response, owns a 15-second transport/body deadline and cancels on unmount. It masks untrusted server errors and validates the returned identity, email and session expiry before navigation. An expired attempt offers an explicit fresh attempt.

Validation includes API concurrency/logout replay and signing-key rotation tests, browser component retry/deadline/unmount tests, and an exact-image PostgreSQL fixture for publication rollback, duplicate session prevention, restart replay and revoked-session denial. These checks verify the increment, not the complete authentication or onboarding tickets.

The first release run exposed a first-response/replay timestamp precision mismatch. Session creation now uses PostgreSQL microsecond precision before returning the first acknowledgment. The fixture retains exact response comparison and adds retained/retired key rotation, a one-connection API pool, actual Worker cleanup and expiry during an observed session-row wait. A non-microsecond test clock protects the timestamp regression. These follow-up release checks passed in the run linked below.

[Commit 6464e69 CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36824621457) passed all nine jobs with three non-expired exact-SHA artifacts. The release fixture passed the original-session, rotation, one-connection, restart, rollback, Worker cleanup and post-wait expiry checks. Eleven release browser tests and the separate mobile verification/recovery test passed. This verifies the sign-in increment; the broader authentication and onboarding acceptance criteria remain outstanding.

## Incorrect-password lifecycle privacy

Unknown addresses and Active, PendingVerification, Suspended and Deactivated
accounts return the same `401 invalid_credentials` response for an incorrect
password. Adaptive password verification precedes account lifecycle disclosure;
an unknown address uses a process-local dummy hash without persisting a dummy
identity. The registered identity service is a singleton, so creating this hash
does not add hashing work to each ordinary authenticated request.

Ten API-host cases passed locally on 2026-10-07, covering those five account
conditions under both required and optional email-verification policies. Each
case sends two actual HTTP login requests with the same UUID and checks adaptive
verification on both attempts, no additional hash creation, no session cookie,
no retry receipt, no protected fields or lifecycle-specific error in the refusal,
and unchanged account/event state. Fixture accounts are seeded through the real
Demo identity store; the probe delegates verification to ASP.NET's password
hasher. This is correctness evidence, not a statistical timing comparison or
restricted PostgreSQL/release-image acceptance. The locked Release build passed
with zero warnings and errors; the required CI API-host suite includes these cases.

The required `test-identity-login-privacy.sh` release fixture additionally sends
ten actual HTTP refusals against the Production API: two identical-key attempts
for Active, PendingVerification, Suspended, Deactivated and unknown accounts.
It compares the public Problem fields exactly (excluding per-request correlation),
refuses cookies/private fields and compares complete persisted user, session,
audit, stream, event and login-receipt snapshots after every request. Lifecycle
setup affects only its disposable account; cleanup restores its original status
and verification flag even after failure. No runtime grants or policy are changed.

The complete fixture passed locally on 2026-10-07 against the frozen compiled API
at `79753c03`, Production with optional email verification, and real schema-110
PostgreSQL. The restricted API role has neither superuser nor RLS-bypass privileges.
After cleanup, the one fixture account is Active, verified and still version 1,
with no session or login receipt. The disposable API container was removed,
preserving the original three services. Bash syntax and diff checks passed.
Required-verification policy coverage remains the ten API-host cases above;
this local execution does not replace current retained-image or full PRD evidence.

## Final session admission after publication

Fresh sign-in now reads its actual active session after session/audit/receipt
writes and before disclosing a cookie. It requires the same session and account,
current account/verification eligibility and unexpired session. A refusal returns
`session_unavailable`; the owning transaction rolls back its writes. The existing
credential-checked replay path retains its current-session admission.

An API-host regression uses the real Demo session and receipt stores. Its clock
advances to the session expiry only after it observes the actual saved receipt;
refusal must leave no session or receipt, preserve the account, and permit a
fresh same-key request followed by stable replay. It compiles but needs native CI
execution.

The required exact-image sign-in fixture adds an ephemeral invoker trigger that
expires only its fixture session after actual receipt insertion. The restricted
runtime must return 401 with no cookie or account disclosure and unchanged full
user/session/audit/receipt state. The trigger is removed, and the same key then
runs the existing concurrent success/replay checks. This fault injection tests
post-publication admission, not elapsed real-time expiry during a database wait.
It is guarded by CI-only execution and cleaned up on exit; no migration or
production runtime policy is changed. Bash syntax passes; execution against the
exact release API remains pending CI. Full PRD-02 acceptance remains open.

## Executed current and legacy adaptive password-hash recovery

The mandatory release sign-in fixture now runs independently for `current` and
`legacy` hashes. Both create their disposable account through actual registration.
The registered hash is decoded and independently verified from the fixture's
known password. Only the legacy case replaces that new account's hash with the
old ASP.NET Identity encoding to exercise upgrade admission. The
[private hash fixture](../scripts/ci/identity-password-hash-fixture.py) follows the
[.NET 10 PasswordHasher format](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs):
it requires the current SHA-512 PBKDF2 format, a 128-bit salt, a 256-bit subkey and
at least the current 100,000-iteration default. It recomputes the stored subkey;
format inspection or a successful login alone does not satisfy that check.
The independent verifier must also refuse the wrong fixture password against
the actual registered hash, so a vacuous successful oracle fails the CI step.
Legacy encoding is fixture input only. Passwords, hashes, cookies, key rings and
complete private snapshots are not console diagnostics or release artifacts.

Both complete modes pass locally against the frozen current compiled Production
API, restricted PostgreSQL 17/pgvector schema 113 and a separate real Worker.
They retain malformed-hash and known/unknown wrong-password privacy checks.
Revoked audit/receipt publication grants and actual post-receipt session-expiry
fault injection each require the complete user/session/audit/identity-stream/
event/receipt snapshot to remain identical. In the legacy case this includes the
original old hash and account version, so a refused sign-in cannot retain an
upgrade. The same original key then runs three concurrent real HTTP requests:
one session, one session audit, one receipt and identical original cookie/body.

Current hashing preserves the original hash and profile version one. Legacy
hashing changes only the password hash, updated clock and profile version, with
exactly one revision advance to version two. Other complete account fields remain
identical. The upgraded hash must independently verify under current parameters;
later wrong-password refusals, same-key recovery, retained/retired key rotation
and API restart preserve the complete stored snapshot. A real profile edit must
advance the expected version once in each mode. Logout denies old-session reuse,
the Worker removes 101 actual expired receipts, and an observed real session-row
wait still refuses expired original-session disclosure. The existing single-API-
connection and rollback checks remain; no session, audit or receipt is seeded as
successful delivery.

A final API-host regression uses the real hash/session/replay providers with a
clock that advances on every observation and retains sub-microsecond ticks. The
first upgraded profile must equal the stored profile, its same-key retry must
return the exact same acknowledgment and the stored hash/version must not change
again. All five focused legacy upgrade, post-publication expiry and original-session
recovery cases pass; the locked Release solution build has zero warnings/errors.
A preliminary Demo-only microsecond assertion was discarded: provider precision
is not the requirement, and the existing final session read already returns the
authoritative stored profile. No product logic change follows from that hypothesis.

The initial local harness stops before registration at an API replacement
readiness mistake; it is retained separately. The corrected harness waits for
actual API health and passes both complete modes, then removes only its owned
containers/database and private environment files. Original services/data remain.
This is current compiled runtime evidence with the existing optional-verification
sign-in fixture policy, not current immutable/full CI, timing equivalence, native
browser or email-provider delivery. Current exact-image execution and complete
PRD-02 acceptance remain required. Estimated PRD-02 work remaining is **16%**
(planning estimate); the issue remains open.
