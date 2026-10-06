# Identity profile and deactivation transactions

The Identity service facade wraps authenticated profile changes and self-deactivation in an explicit global identity command transaction. It locks the actor user row for update, checks the current account/session through the shared command actor verifier after waits, then executes validation, persistence and audit insertion. Profile updates, deactivation, revoking every user session and their audit records commit together. Audit/database failure returns masked `identity_storage_unavailable` HTTP 503, and deactivation failure does not expire the browser cookie. Failed session admission returns HTTP 401 before any state change.

Identity account state is global. The factory uses a separate identity transaction context, borrows it for identity reads/writes/audits, prohibits nested command roots and refuses acquiring an Organization session inside an identity command. It does not fabricate an Organization ID or disable RLS. Existing identity operations outside this boundary retain their existing owning connection/transaction behavior. Demo serialization is process-local and is not durable audit/rollback evidence.

The required exact-image fixture denies audit insertion during profile change and deactivation, compares the complete user/session state plus audit count, checks no cookie deletion on rejection, commits a valid profile edit, and rejects a stale version without changes. Controlled user locks observe logout committed during both profile and deactivation admission waits, then verify masked 401, unchanged profile/audit state and another session still working. With the API limited to one database connection, profile change and deactivation must finish; the final account remains in history, every session is revoked and exactly one deactivation audit exists. The API host test checks two independent sessions both lose access after deactivation.

Profile and restricted logout/deactivation [durable retries](identity-command-retries.md) are implemented. Credential/token retry protocols, invitation delivery, remaining identity UI and lifecycle/ownership interactions still require acceptance work. These changes do not declare all PRD-02/60 acceptance criteria complete.

## Sign-in and logout

Sign-in runs password verification, optional hash upgrade, session creation and its audit in one global identity transaction. The email lookup locks the current user before verification; a concurrent password reset or deactivation therefore precedes credential verification or follows session issuance. No existing HTTP session is fabricated for this public credential operation. Database failures return the masked `identity_storage_unavailable` envelope without setting a session cookie.

Logout uses the authenticated identity command boundary. Session revocation and its audit commit together, and the API clears the browser cookie only after confirmed success. Audit failure preserves both the session and cookie so the user can retry. Restricted release-image fixtures cover audit denial for login/logout, account deactivation while sign-in waits, and both commands with a one-connection pool.

The demo store serializes identity commands but does not claim durable rollback.

## Registration

Self-registration validates the configured onboarding policy inside its global identity command. Account creation, the hashed verification token, durable delivery publication and `USER_REGISTERED` audit commit together. The store borrows the root transaction and retains owning-transaction behavior for direct store callers. Case-insensitive uniqueness uses `ON CONFLICT DO NOTHING`; a rejected duplicate does not produce another token, delivery or audit. Registration database errors use the same masked 503 response.

The exact-image mail fixture limits the API to one database connection, stops the Worker, denies audit insertion, verifies all four record counts remain unchanged, then retries and checks exactly one verification token, queued delivery and registration audit. An uppercase-email duplicate leaves those counts unchanged. Publication rejection also returns the masked storage error without committing an account.

## Password reset and verification

These public commands prove possession of a hashed, expiring, single-use token inside a global identity transaction. Lookup locks the account before the token, consistently with sign-in and deactivation. Account state is checked under that lock; the existing verification policy preserves suspension while reset rejects suspended accounts. No session or Organization is fabricated.

Token consumption, account changes, reset session revocation and the corresponding audit commit together. A failed audit or failed token consumption rolls everything back. Token lookup and consumption use `clock_timestamp()` so expiry during a wait cannot succeed using an earlier request timestamp. Direct store callers retain an owned transaction and the same account-before-token lock order.

The one-connection exact-image mail fixture compares complete user/token/session state and audit counts after denying each command's audit. It also holds a token row, observes the API waiting on that row, allows the token to expire without changing its row, then releases the lock and expects `invalid_or_expired_token` with unchanged state. Successful retries and single-use checks follow.

## Recovery requests

Password-forgotten and verification-resend requests lock a matching account before checking current eligibility. Their hashed token, durable delivery publication and audit share a global identity transaction. Direct store callers still own their token/publication transaction. A storage or audit failure rolls back all effects.

Public requests retain the same generic acknowledgment for unknown, ineligible and failed-storage accounts, preventing an account-existence distinction caused by a conditional database failure. This acknowledgment confirms receipt of a request, never delivery. A failed transaction logs a masked database code without an email or token; operators must treat that warning as a failed publication. Transport-disabled mode still returns its uniform configuration error before lookup. Eligible clients can retry the request; durable deduplication of such retries remains outstanding.

The one-connection release fixture denies audit insertion for both requests, tests known and unknown emails receive the same generic 202 with no tokens, and checks account/token/delivery/audit counts remain unchanged. Existing success checks then exercise Worker delivery.

## Demo sign-in and token consumption rollback

Demo sign-in and password-reset/email-verification consumption now use the owning
Identity snapshot boundary rather than only the account/Organization semaphore.
The operation supplies its credential or security-token proof; it does not require
an already authenticated session. Success commits only after a cancellation check;
failure results, exceptions and cancellation restore registered Identity stores
and retry receipts before releasing the shared gate. No Work gate is acquired by
these credential commands, and production PostgreSQL behavior is unchanged.

Nine new API-host cases use actual sign-in, reset and verification operations
inside the owning boundary, then introduce a failure result, exception or
cancellation after real state/receipt writes. Sign-in cases require its newly
created session and login receipt to disappear. Token cases require exact account,
password/status, original unused token proof, Identity event and consumption
receipt restoration; password-reset cases also require the original session to
remain active. Fresh same-key commands must succeed and matching acknowledgments
must retain their original result without repeating the token-consumption event.

These cases compile with warnings as errors; native API-host execution is pending
CI. They exercise the process-local snapshot boundary and do not prove durable
audit or email publication, PostgreSQL waits, or complete PRD-02 acceptance.
Recovery requests use the same Identity snapshots through their generic result
boundary; their focused rollback cases are described below. The Demo audit store remains a no-op.

## Demo recovery-request rollback

Password-forgotten and verification-resend commands now capture registered
Identity participants under the shared account/Organization gate. A normal
result, including a neutral null result for an unknown or ineligible account,
commits only after the final cancellation check. Exceptions and cancellation
restore new recovery tokens and retry receipts before releasing the gate.
An operation/storage `InvalidOperationException` now returns the supplied neutral
result only after owning rollback completes, with a content-free warning.
Cancellation and unrelated exception types still propagate. Both Demo and
PostgreSQL recovery adapters recheck cancellation before masking a storage
failure, so a concurrent cancellation cannot become a neutral success response.
The PostgreSQL adapter otherwise retains its masked database-failure policy.
A request acknowledgment still does not prove email delivery.

Six API-host cases run actual password-reset requests or verification resends,
observe real token and recovery-receipt writes, then introduce an exception or
cancellation. They require the failed token and receipt to disappear. The
non-cancellation failure returns a neutral result; cancellation still propagates.
The account, Identity event stream and pre-existing registration verification proof
remain unchanged. Fresh same-key requests must create usable proof; matching
replay must retain the token and receipt. An unknown-email request must retain
its neutral null result without altering the known account.

Native execution of these cases remains required in CI. Demo audit remains a
no-op and Demo delivery does not prove durable Worker publication. This repair
does not close PRD-02 or establish full public-request failure indistinguishability.
