# Durable identity email (PRD-02 / ARCH-06 / ARCH-07 / PRD-24)

Production verification/reset email uses the separate Worker and a PostgreSQL
identity outbox. An account exists before Organization membership, so this queue
has an explicit `GLOBAL_IDENTITY_MAIL` service scope and no invented tenant. It
does not grant access to Organization/Board records. Organization jobs retain
their separate forced tenant RLS boundary.

Registration commits the user, verification token hash and email job atomically.
Reset and verification-resend requests commit token hashes with their email jobs.
Publication failure rolls back those records. Normal known/unknown requests
return the same accepted envelope without a production token. Resend only queues
unverified pending accounts. Disabled production delivery returns the same 503
`identity_delivery_unavailable` before looking up any email; verified registration
also rejects creating an undeliverable pending account when self-registration is
enabled. Demo keeps its explicit in-memory token behavior.

## Token recovery and keys

Email jobs contain token references/purpose/key version, never plaintext bearer
tokens, message bodies, credentials or attachments. Token tables contain only
SHA-256 hashes. API and Worker derive the same 256-bit HMAC-SHA-256 bearer token
from a random token UUID with domain-separated version, purpose and key ID.
Purpose/ID/key substitution changes the token. The Worker compares its hash to
the authoritative, unexpired, unconsumed token before sending. This scheme's
runtime signing keys must be protected separately from database snapshots:
possession of a database snapshot alone cannot reconstruct delivery tokens;
compromise of both keys and token references can reconstruct unexpired tokens.

API and Worker share `STRATAAI_IDENTITY_TOKEN_KEYS`, a runtime JSON object of key
IDs to base64-encoded **32 random bytes per key**, and
`STRATAAI_IDENTITY_TOKEN_CURRENT_KEY`. Never use examples/static development keys
in Production or commit a real ring. IDs are bounded ASCII labels. Missing,
malformed, duplicate or incorrectly sized configured keys fail startup without
printing their contents. Rotation adds a new key/current ID while retaining old
keys until their tokens and queued work have expired. Do not remove old versions
prematurely; missing versions fail jobs closed. Session and invitation tokens
remain independently random and are not derived using this key ring.

## Provider and runtime configuration

The initial adapter uses [Resend's email API and idempotency contract](https://resend.com/changelog/idempotency-keys).
It remains behind `IIdentityEmailProvider`; no vendor SDK reaches Domain or
Application. Resend retains idempotency keys for 24 hours. A job's ID/purpose is
its stable key, and its immutable sender/recipient/origin snapshot keeps retries'
payload identical. Delivery expires by the token deadline or **23 hours from
database publication**, whichever comes first. Never manually retry a job beyond
that window or switch provider accounts for a queued job: the provider's
idempotency retention cannot protect such a resend.

Enable only after migrations, provider domain verification, keys and the Worker
database role are provisioned. Release/production compose files pass runtime
settings to the already-built images:

- `STRATAAI_IDENTITY_EMAIL_ENABLED=true` (default false).
- `STRATAAI_IDENTITY_EMAIL_FROM`: verified plain sender address.
- `STRATAAI_PUBLIC_ORIGIN`: trusted HTTPS origin with no path/query/credentials.
- `STRATAAI_IDENTITY_EMAIL_ACCOUNT`: stable non-secret account label shared by
  API/Worker; changing the Worker label rejects old jobs. Operators must keep its
  provider API key within the same Resend account when rotating credentials.
- Signing key ring/current ID described above, on API and Worker.
- `STRATAAI_IDENTITY_EMAIL_API_KEY`: provider credential, **Worker only**.
- `STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING`: separate restricted Worker
  PostgreSQL credential, mapped to `ConnectionStrings:IdentityDeliveryPostgres`.

The identity Worker role needs schema usage; EXECUTE on
`public.runtime_database_role_is_safe()`; SELECT on `schema_migrations`; SELECT/UPDATE on
`identity_delivery_jobs`; SELECT on users' `id,email,status,email_verified`; and
SELECT on token tables' `id,user_id,token_hash,used_at,revoked_at,expires_at`. It
must not have password-hash or Organization/Board grants, SUPERUSER/BYPASSRLS,
or ownership of domain tables. Provision a distinct runtime login with these
column grants, not the migration/owner credential. Jobs use forced service-scope
RLS and SECURITY INVOKER functions. The API's identity writer needs token/user
writes and outbox INSERT in the same transaction.

## Execution, privacy and recovery

Claims commit a two-minute lease before provider calls. Current worker/lease
identity fences completion; provider receipts are required for SENT. Five total
claims, bounded exponential backoff, expiry cancellation and FAILED state prevent
unlimited retries. Provider rejection stops retries; transient outages/ambiguous
acknowledgements retry with the same key. Shutdown leaves leases recoverable.
Workers cancel calls before token/lease expiry and check account status, email and
token usability before sending. Deactivated/suspended accounts and used/expired
tokens cancel pending delivery. Provider idempotency handles the unavoidable gap
between an external send and database acknowledgement.

HTTP calls time out after 30 seconds, bound buffered response size, forbid
redirects and redact headers. Provider error bodies, addresses, message/link
bodies, signing keys and credentials are excluded from Worker logs. Logs correlate
job/subject/worker IDs, purpose, attempt and request correlation ID. An accepted
request means queued work, not guaranteed inbox delivery; operators must monitor
FAILED/CANCELLED jobs and provider/domain reputation without bypassing expiry or
idempotency rules. Provider inbox acceptance/deliverability cannot be established
by the transport fixture.

Verification and reset links use URL fragments, removed from browser history
immediately. The MUI verification screen requires explicit confirmation (GET/link
preview does not consume a token) and offers generic resend after expiry. Query
tokens are rejected. Account creation explains required verification. Reset
continues to consume tokens once and revoke existing sessions.

## Evidence and remaining scope

CI uses exact API/Worker/web images, real PostgreSQL and a role unable to read
password hashes or Organization data. The isolated test provider accepts an effect
then returns 503, proving the Worker retries without a duplicate effect. Tests
cover atomic rollback, queued recovery under rotated keys, token-purpose/replay
denial, verification/reset, old-session revocation, permanent rejection,
deactivation cancellation and verification resend. A mobile keyboard browser test
consumes actual Worker-delivered links under release CSP. Unit tests check signing,
rotation, safe transport failures, payload snapshots and dispatch decisions.

`STRATAAI_IDENTITY_EMAIL_TEST_ENDPOINT` is permitted only in the isolated
`IntegrationTest` host environment. Its fake transport, fixture credentials,
signing keys and compose overlay are excluded from the release bundle. Production
uses the fixed HTTPS Resend endpoint.

PRD-02/ARCH-07 remain broader than this increment: realtime/reconnect behavior,
date/time consumers, mailbox/AI/correspondence/storage integrations, broader
accessibility and the other acceptance criteria still require implementation.
