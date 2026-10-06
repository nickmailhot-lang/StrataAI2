# Identity command retries

PRD-02 and PRD-60 require retry safety for identity mutations. Identity is global; tenant Work replay storage and fabricated Organization IDs are unsuitable. A saved acknowledgment must never bypass current authorization, token expiry, account lifecycle or revocation. Raw passwords, session cookies and recovery/verification bearer tokens must not be persisted in replay responses or ordinary request fingerprints.

The first implemented boundary is authenticated `PATCH /me`. An optional `Idempotency-Key` is a nonempty canonical UUID. The browser generates one per exact submitted profile body and retains it for an unchanged retry after a deadline or transport failure. Editing the body or receiving a confirmed acknowledgment starts a new intent. Keys are scoped to the actual authenticated account, not to a session or Organization. Omitting the key retains ordinary version-precondition behavior for existing clients.

Migration 013 stores only a typed profile acknowledgment and a SHA-256 fingerprint of the profile fields and expected version, including an operation discriminator. This profile-only fingerprint contains no password or bearer-token input. The JSON schema permits exactly the profile fields; credential-bearing identity outcomes cannot be passed to this store. Both database RLS and application scope checks protect the subject. The API has SELECT/INSERT/UPDATE grants. Expired entries cease to replay after 24 hours and can be replaced by a successful newly authorized command.

Migration 014 introduces a separate global retry-maintenance capability in the existing Worker. Its column grants permit only expired key/subject/expiry metadata and deletion; cached profile JSON and fingerprints remain inaccessible. Restrictive RLS prevents an identity-subject GUC from granting maintenance access to live entries. A transaction-local GLOBAL_IDENTITY_RETRY_CLEANUP scope is required. The API cannot use that scope to disclose other subjects or invoke global purging. The purge function is SECURITY INVOKER, has no public execution grant, and deletes at most 100 expired entries atomically. It rechecks expiry when deleting so a concurrently refreshed acknowledgment remains protected. It does not delete accounts, audit records, events or stream positions and does not repurpose the identity-mail capability.

The production Worker runs a pass every 30 seconds and records only deletion counts or a generic failure message. Its existing runtime-role/schema guard remains in force. Failed or interrupted transactions can be retried; multiple Workers may encounter the same expired candidates without duplicating effects. A database lock wait remains subject to the configured command timeout. Demo does not start this durable maintenance service. Required CI checks cover expired-only visibility/deletion, forbidden content/fingerprint reads, spoofed subject denial, no-scope denial, rollback, the 100-row batch limit, preserved live entries and history, and actual release Worker cleanup. These checks must pass before claiming release verification of this increment.

The global identity command first locks the account and verifies its current live session. It then checks the saved key. Matching input returns the original successful profile acknowledgment, even if a later edit advanced the profile. Different input returns `409 idempotency_key_reused` without state changes. A new or expired key must pass normal validation and the version precondition. Mutation, audit, event and retry acknowledgment commit together. Database publication failure rolls all of them back. Concurrent duplicate commands serialize under the account lock. A restart preserves the stored result. The browser still validates acknowledgment subject/version and uses authoritative sync to reconcile newer state.

[Commit 409cdff CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36816150593) verified profile retries and migration 013 across all nine jobs, with matching retained tested images, security evidence and release bundle. The exact-image browser suite passed the lost-acknowledgment retry, and PostgreSQL fixtures passed publication rollback, concurrent duplicates, collision, restart/one-connection replay, lock-wait session revocation and expired-key replacement. Migration 014 maintenance requires its subsequent release verification.

[Commit e32b97d CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36817125388) verified migration 014 maintenance across all nine jobs with matching retained artifacts. Runtime-role fixtures passed content/fingerprint denial, spoofed-subject and live-record protection, no-scope denial, rollback and 100/50/0 batching. The actual release Worker removed 101 expired keys while preserving the live acknowledgment and account/audit/event/stream history.

Migration 015 adds a distinct protocol for `POST /auth/logout` and `POST /me/deactivate`. A keyed first request proves a live session and active account inside the global identity transaction, locking the account before the session. Its effect, audit, content-free event and receipt commit together. A retry proves possession of the same original opaque cookie and matches the same account, session, key and operation. It returns only an empty 204 acknowledgment after revocation; it does not authenticate that session, disclose a profile, or authorize any other endpoint. Different session/operation returns 409; missing, unknown or expired proof returns 401. Unkeyed calls retain their ordinary authenticated behavior. Receipt expiry is the earlier of the original session expiry and 24 hours. No password, token, hash or response JSON is stored in the receipt. The browser retains the logout UUID after an uncertain outcome.

The receipt table has forced subject RLS, a composite account/session foreign key and no runtime UPDATE grant. The API may delete only its own expired receipts. Maintenance cannot read session binding or operation and must use its separate transaction-local cleanup scope; live records remain protected even with a spoofed subject. Each Worker pass removes at most 100 expired profile acknowledgments and 100 expired revocation receipts in one transaction. Required release checks exercise publication rollback, concurrent duplicates, session/operation conflicts, protected-read denial, restart, expiry, role isolation, batching and actual Worker cleanup. [Commit cef32ea CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36818817389) verified migration 015 across all nine jobs and three exact-SHA artifacts, including expiry during the observed account-lock wait and the lost-logout acknowledgment browser retry.

Migration 017 adds credential-checked sign-in retries with a separate API-only key ring and typed session coordinates. See [sign-in retry protocol](../identity-login-retries.md) for configuration, retention and verified release evidence.

Migration 018 adds credential-checked registration retries and typed original verification coordinates with separate HMAC purposes. See [registration retry protocol](../identity-registration-retries.md) for concurrency, production delivery, rotation, lifecycle and verified release evidence.

Migration 019 adds neutral recovery-request acknowledgments without duplicate token or email publication. See [recovery request protocol](../identity-recovery-request-retries.md) for eligibility, privacy, original-proof retention and pending release validation.

Migration 020 adds original-proof-bound reset and verification completion acknowledgments without repeating consumption, events or revocation of newer sessions. See [token consumption protocol](../identity-token-consumption-retries.md) for current credential/lifecycle proof, expiry, rotation and pending release validation. The Worker now removes at most 100 expired receipts from each of six retry tables in one transaction.

Recovery and verification screens own a 15-second deadline covering both transport and JSON consumption, cancel when closed, reject malformed success responses and display only known safe error messages. Input and exact UUID intents are preserved after uncertain publication and consumption acknowledgments. Expired or inaccessible proofs still offer sign-in or replacement-link recovery. [Commit f778720 CI](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/36860761965) verified the client deadline/acknowledgment increment across all nine jobs and three exact-SHA artifacts, including 14 release browser scenarios and the separate mobile mail flow. Later consumption retry changes require their own release verification.

This boundary does not complete identity idempotency. Invitation side effects still need appropriate fresh-proof and secret-safe retry protocols, including retention for their eventual retry records. The existing email-token key ring is registered only for enabled production delivery and supports reset/verification purposes; sign-in, registration, recovery requests and token consumption use a separate API-only ring with distinct purposes. [Account-deactivation ownership admission](account-owner-continuity.md) now precedes a first lifecycle mutation; a previously completed receipt remains acknowledgment-only. Its verification, confirmation UI and further telemetry remain required. No full PRD or architecture issue closure follows from these retry increments alone.

## Final revocation expiry after receipt publication

Fresh keyed logout and deactivation now retain the original session proof's
expiry fence through receipt publication. After saving the receipt, the executor
checks both original session and receipt expiry before acknowledging success.
Refusal returns `session_unavailable`; the owning transaction restores session,
account/event and receipt changes, plus assignment cleanup for deactivation.
It does not require the intentionally revoked session to become active again.

Two API-host regressions observe actual Demo receipts before advancing the clock
to the original session expiry. They require failure, exact account and session
restoration, absent failed receipt, unchanged Identity and Work events, and
restored Card assignment/version. Fresh same-key revocation must then succeed,
and a matching acknowledgment must not repeat its Identity event. The test clock
uses a guarded synchronous observation because the Demo receipt reader checks
expiry through that same clock. Production clock/provider behavior is unchanged.
Native API-host execution and the required exact-image publication-wait checks
described below remain pending; compilation is not runtime acceptance. Full PRD-02 remains open.

The required revocation release fixture now covers both logout and deactivation
expiry during an actual receipt-publication wait. It shortens the primary
session's remaining lifetime to ten seconds and installs an ephemeral invoker
trigger that pauses receipt insertion for twelve seconds. It requires an observed
restricted API connection in `PgSleep` on the receipt INSERT, then 401 with
`session_unavailable`, no cookie/account disclosure and unchanged complete
user/session/audit/Identity-event/receipt state. It removes the trigger, restores
the exact original session expiry, checks the original snapshot again, and runs
existing concurrent same-key success/replay checks with the original cookie jar.
These accounts have no Organization assignment state; cross-module assignment
rollback is covered separately by the source regressions and awaits native proof.
Exit cleanup removes the trigger/function. This CI-only fault injection changes no
production migration or runtime policy. Bash syntax passes; execution remains
pending against the exact release images.
