# Reset and verification completion retries

`POST /auth/password/reset` and `POST /auth/verify-email` accept an optional nonempty UUID `Idempotency-Key`, scoped to the account and operation. Omitting the key retains ordinary single-use token behavior. Keyed first use atomically commits the original mutation, session revocation where required, audit, identity event and typed consumption receipt.

The original bearer must still identify an unrevoked token within its original expiry. PostgreSQL freezes the current user before the token and rereads wall-clock expiry after any token-row wait. Matching retries must prove that this exact token was consumed at the receipt's recorded time. Password-reset retries also verify the submitted new password against the account's current adaptive hash before reading the receipt. Verification replay requires a currently active, verified account. Deactivation, suspension, changed credentials, expiry, revocation and missing original signing versions prevent replay from disclosing the profile.

A matching retry returns the current authorized profile and performs no mutation. It never consumes the token twice, emits another audit/event, creates authentication or revokes sessions created after the original reset. A different key cannot fabricate an acknowledgment for an already consumed token. First verification keeps existing lifecycle semantics: verification does not reactivate a suspended account.

Receipts contain the same-user token identifier, key version, purpose-separated HMAC intent fingerprint, original consumption timestamp and expiry. They contain no bearer, password, password hash, session cookie, response JSON or cached profile. Serialized credential-bearing fingerprint input is cleared after computing the HMAC. The existing API-only retry ring supplies the retained signing version. Keep required versions until their live receipts expire; removing one produces a masked retry-key-unavailable response.

Receipt expiry is bounded by both the original token lifetime and 24 hours. Migration 020 forces subject RLS, immutable API insert/read grants, and operation-specific same-user token foreign keys. The cleanup Worker can read only expired subject/key/operation/expiry metadata and delete at most 100 consumption receipts per transaction. Across the six identity retry tables the existing pass is bounded to 600 rows, preserving token, account, delivery, audit and event history. Purged receipts cannot make consumed or expired proofs reusable.

The recovery screens preserve the exact body and UUID after lost, stalled or malformed completion responses. The reset password stays in memory until a validated acknowledgment, and closing a screen clears its pending request/intent. The existing response-body deadline, safe error mapping and fragment scrubbing still apply. Editing submitted details creates a different intent.

Local evidence: the 101-test API suite, seven focused consumption/credential/lifecycle/expiry/rotation/cryptographic cases, 140 web tests, typecheck/lint and a mobile keyboard browser check all pass. The browser deliberately loses committed verification and reset responses, retries each unchanged intent and signs in afterward. Required release fixtures add real-role RLS/column/foreign-key/batching checks, rollback at audit/event/receipt publication, concurrent acknowledgments, restart, original/newer session behavior, expiry during an observed token wait, lifecycle denial and actual Worker cleanup. The separate production mobile mail test loses and retries both Worker-delivered token completion responses. Release verification is pending CI.

These increments do not complete authentication/onboarding: invitation-driven registration and invitation side effects, deactivation ownership continuity, remaining telemetry, full accessibility/performance evidence and other FR/AC/DoD requirements remain outstanding.

## Expiry after receipt publication

Fresh keyed password-reset and email-verification consumption now checks token
and receipt expiry again after the consumption receipt has been saved. A wait in
publication cannot turn already expired proof into a successful profile
acknowledgment. Refusal returns `invalid_or_expired_token` before disclosure and
rolls back the owning Identity transaction, including password/status/token/event
and receipt writes. Existing credential-checked acknowledgment behavior remains.

Two API-host regressions use actual Demo token and receipt stores. Their clock
advances to the original token expiry only after the real consumption receipt is
visible. They require a failed result with no profile, exact account and unused
token restoration, no failed receipt or extra event, then successful same-key
consumption and matching replay with only one new event. Restoring the test clock
before rollback assertions prevents natural expiry from hiding a residual record.
These cases require native API-host CI execution; compilation alone is not
acceptance evidence. Required exact-image post-publication expiry coverage is described below; its
execution and full PRD-02 acceptance remain outstanding.

The required Identity mail fixture now covers both token purposes during an actual
receipt-publication wait. An ephemeral invoker trigger pauses receipt insertion
for twelve seconds after consumption and event writes. The fixture gives the
canonical token a ten-second remaining lifetime and observes the restricted API
connection in `PgSleep` on the consumption receipt insert. An early rejection
before publication cannot satisfy that observation. After the wait, the request
must return 400/`invalid_or_expired_token`, no cookie/account disclosure, and
unchanged complete account/token/session/audit/event/receipt state. It removes
the trigger, restores token lifetime, and runs the existing concurrent same-key
success/replay checks. Exit cleanup also removes the trigger/function. The fault
injection requires CI; it changes no migration or production runtime setting.
Bash syntax passes; exact-image execution remains pending CI.
