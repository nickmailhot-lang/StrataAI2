# Identity profile and deactivation transactions

The Identity service facade wraps authenticated profile changes and self-deactivation in an explicit global identity command transaction. It locks the actor user row for update, checks the current account/session through the shared command actor verifier after waits, then executes validation, persistence and audit insertion. Profile updates, deactivation, revoking every user session and their audit records commit together. Audit/database failure returns masked `identity_storage_unavailable` HTTP 503, and deactivation failure does not expire the browser cookie. Failed session admission returns HTTP 401 before any state change.

Identity account state is global. The factory uses a separate identity transaction context, borrows it for identity reads/writes/audits, prohibits nested command roots and refuses acquiring an Organization session inside an identity command. It does not fabricate an Organization ID or disable RLS. Existing identity operations outside this boundary retain their existing owning connection/transaction behavior. Demo serialization is process-local and is not durable audit/rollback evidence.

The required exact-image fixture denies audit insertion during profile change and deactivation, compares the complete user/session state plus audit count, checks no cookie deletion on rejection, commits a valid profile edit, and rejects a stale version without changes. Controlled user locks observe logout committed during both profile and deactivation admission waits, then verify masked 401, unchanged profile/audit state and another session still working. With the API limited to one database connection, profile change and deactivation must finish; the final account remains in history, every session is revoked and exactly one deactivation audit exists. The API host test checks two independent sessions both lose access after deactivation.

Verification/reset, recovery, durable retry keys, invitation delivery, identity UI and lifecycle/ownership interactions still require acceptance work. These changes do not declare all PRD-02/60 acceptance criteria complete.

## Sign-in and logout

Sign-in runs password verification, optional hash upgrade, session creation and its audit in one global identity transaction. The email lookup locks the current user before verification; a concurrent password reset or deactivation therefore precedes credential verification or follows session issuance. No existing HTTP session is fabricated for this public credential operation. Database failures return the masked `identity_storage_unavailable` envelope without setting a session cookie.

Logout uses the authenticated identity command boundary. Session revocation and its audit commit together, and the API clears the browser cookie only after confirmed success. Audit failure preserves both the session and cookie so the user can retry. Restricted release-image fixtures cover audit denial for login/logout, account deactivation while sign-in waits, and both commands with a one-connection pool.

Recovery and verification still need their audit writes incorporated into their command boundaries. The demo store serializes identity commands but does not claim durable rollback.

## Registration

Self-registration validates the configured onboarding policy inside its global identity command. Account creation, the hashed verification token, durable delivery publication and `USER_REGISTERED` audit commit together. The store borrows the root transaction and retains owning-transaction behavior for direct store callers. Case-insensitive uniqueness uses `ON CONFLICT DO NOTHING`; a rejected duplicate does not produce another token, delivery or audit. Registration database errors use the same masked 503 response.

The exact-image mail fixture limits the API to one database connection, stops the Worker, denies audit insertion, verifies all four record counts remain unchanged, then retries and checks exactly one verification token, queued delivery and registration audit. An uppercase-email duplicate leaves those counts unchanged. Publication rejection also returns the masked storage error without committing an account.
