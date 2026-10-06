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
