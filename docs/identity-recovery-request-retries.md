# Recovery request retries

`POST /auth/password/forgot` and `POST /auth/verification/resend` accept an optional nonempty UUID `Idempotency-Key`, scoped to the current account and operation. Accounts are locked before eligibility and receipt checks. Unknown, suspended and deactivated accounts return the same accepted response without receipt or delivery creation. Verification resend also requires a pending, unverified account.

A new keyed intent atomically publishes its original token, delivery job, audit and typed receipt. Concurrent duplicates acknowledge that original publication. Receipts contain a keyed email/operation fingerprint, signing versions and same-user token coordinates, never a bearer, password, response JSON or profile. Reset and verification have distinct HMAC purposes, separate from sign-in and registration. Production acknowledgments always contain `accepted: true` and a null bearer; conflicts, expiry and retired keys do not disclose account existence.

A matching eligible retry derives and checks the original proof. It never publishes a replacement if that proof is used, revoked, expired or no longer derivable, or if the receipt has expired. These cases remain neutral accepted acknowledgments. A new recovery request uses a new key. Receipts last at most 24 hours; an expired key remains reserved until cleanup. Reusing a purged key starts a new independently evaluated request, so retention is not permanent reservation.

The API-only retry ring supplies recovery intent and Demo token derivation. Production token derivation uses the original email-delivery signing version. Retain both relevant versions while receipts/proofs are live. Neither ring is persisted in receipts or exposed to the cleanup Worker. Demo secrets are ephemeral and provide no restart durability promise.

Migration 019 forces subject RLS and composite token/user foreign keys for exactly one reset or verification purpose. The API can read/insert but not update/delete. The Worker can read only expired subject/key/operation/expiry metadata and delete at most 100 recovery receipts in the existing cleanup transaction. Across five retry tables, a pass is bounded to 500 rows. Token, user, audit, delivery and event history survive receipt cleanup.

The recovery and resend screens retain the exact body and UUID after stalled, lost or malformed acknowledgments. Editing the email creates a new intent; a validated accepted acknowledgment clears it. The 15-second deadline includes JSON consumption, and closing the screen cancels its request. Only known safe errors are mapped. Success requires the expected status and acknowledgment fields.

Local validation covers concurrent original-proof acknowledgment, consumed/expired proof denial without replacement, retained/retired API keys, cryptographic purpose separation, web regressions and mobile keyboard retries. Required CI fixtures add restricted-login RLS/column/foreign-key/retention checks and production token/job/audit/receipt rollback, concurrency, restart, actual Worker delivery and cleanup. Release checks are pending verification.

This increment covers request publication. Retry acknowledgments for consuming reset/verification tokens, invitation-driven registration, invitation side effects, ownership continuity, remaining telemetry and full PRD acceptance criteria remain outstanding.
