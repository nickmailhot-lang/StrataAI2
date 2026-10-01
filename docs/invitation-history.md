# Administrator invitation history

`GET /organizations/{organizationId}/invitations?after={uuid}` returns at most
50 issued invitations with an optional next cursor. Only a currently active
Organization Owner/Admin may read it. The command rechecks the actor after
parent/membership lock waits and after the read, using the existing account and
session authorization boundary. Foreign, ordinary member and portal accounts
receive the same safe Organization-not-found response. Migration 024 adds the
tenant/cursor index and required schema readiness advances through 024.

Each row exposes ID, recipient email, surface, target role, creation/expiry and
accepted/revoked timestamps. It never exposes bearer/hash, signing keys, provider
receipt/account, job metadata, or provider error text. Delivery state is separate
from invitation lifecycle: PENDING, SENT, CANCELLED, FAILED, or RETRY_EXHAUSTED
when the generic job exhausted retries while its ledger is still pending. A
null delivery state means no mail intent was recorded (including Demo and
feature-disabled creation), not a delivered message. SENT means the configured
provider acknowledged the message; it does not prove inbox receipt or grant
access. A SENT invitation can subsequently be revoked or expire.

The endpoint is read-only and paginates all lifecycle states, so historical
revoked/accepted invitations remain reviewable. The administrator history UI and
revocation controls still need to consume this endpoint. No issue is complete
on the strength of this backend increment. Host tests cover paging, lifecycle,
secret exclusion and current/removed administrative access; exact-image mail
tests check actual SENT reads and denied recipient access. New CI evidence is
required before claiming these tests passed.
