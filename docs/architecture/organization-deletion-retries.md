# Organization deletion request acknowledgments

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-009/010
and TC-05/06/07/08 require safe owner deletion and recovery. This contract
acknowledges the deletion request; terminal deletion remains incomplete.

## Request and recovery

`DELETE /organizations/{id}?version=<reviewed version>&expectedActorId=<account>`
accepts an optional single canonical nonzero UUID `Idempotency-Key`. Account
mismatch returns a private-detail-free `session_unavailable` before mutation.
The first success returns 202 after committing the DELETING transition,
reminder suspension, audit and receipt. It does not report completed deletion.

A matching key binds the original Organization and reviewed version. The same
currently authenticated active Owner can recover the original 202 after
DELETING has withdrawn ordinary reads. Recovery does not increment the
Organization or reminder version/generation again, resuspend reminders, append
another event/audit or restore membership/access. Changed reviewed version
returns `idempotency_conflict`. Expired receipts return `idempotency_expired`;
keys stay reserved. A fresh key cannot restart deletion on DELETING.

Current Owner membership and final session admission are required even for
receipt recovery. An Admin, departed/removed Owner or changed cookie account
cannot use a receipt as a grant. Response bodies contain no historical private
Organization data, and responses use private/no-store. Unkeyed callers retain
existing one-request 202 behavior and cannot recover lost acknowledgments.

## Owning transaction

Deletion alone opts into DELETING parent admission for recovery. Ordinary
Organization reads, metadata/member/invitation commands remain ACTIVE-only.
Both runtimes still lock/admit the parent before membership and final actor
checks. The service checks current Owner membership before receipt disclosure,
and a new deletion requires ACTIVE inside that same owning scope.

PostgreSQL borrows the Organization transaction for receipt reads/insertion.
The parent lock serializes identical retries; Organization, reminders, immutable
Work events/stream, audit and receipt roll back together on storage failure,
final session expiry or cancellation. Demo retains its account/Work gates and
captures deletion receipts with the other rollback participants.

Migration `087_organization_deletion_replays` adds immutable, forced-RLS tenant
receipts with Organization/user foreign keys and a 24-hour expiry. API receives
SELECT/INSERT only, Worker no receipt privileges. Both hosts require the ledger
entry, and the migration-runner fixture applies it repeatedly and checks RLS.

## Verification and remaining acceptance

API fixtures cover concurrent identical 202 acknowledgments after parent
access withdrawal, changed versions, fresh keys on DELETING, switched accounts,
Owner demotion, normal-write refusal and observed receipt publication followed
by session expiry. The existing Demo reminder/event rollback scenario now uses
a deletion key and requires receipt rollback and a successful same-key retry.
API and persistence projects compile with zero warnings/errors; local Windows
policy prevents test execution. Compilation is not runtime proof.

The exact-image restricted fixture creates an actual dated Card/reminder through
the API. Receipt insertion denial and observed cookie expiry during AFTER INSERT
receipt publication must restore the parent, membership, Card/assignments,
reminder, pending reminder jobs, immutable Work events/stream, audit and receipt.
The expiry response must be 401 without private data or Set-Cookie and leave
user/session records unchanged. It restores original expiry and observes two
same-key requests waiting on the parent before requiring two 202s, one audit,
one receipt and one reminder/event transition. Later replay, changed version,
fresh key, Owner demotion, cross-tenant read denial, immutable grants and
reserved expiry checks must not change the snapshot. Bash syntax passes;
these native scenarios await CI execution.

## Browser confirmation and recovery

Current Owners can open **Request Organization deletion** from Organization
home. `/app/{id}/delete` is an independent operation route: it performs its own
canonical ACTIVE Organization and Owner review with `/me` before and after,
rather than inheriting normal surface polling. Normal access is withdrawn by
the deletion request; polling must not unmount an unresolved original intent.
This route exposes no internal app navigation/data without its own admission.
The separate Owner Portal and ordinary surface checks retain their boundaries.

The MUI confirmation names the currently reviewed Organization and explains
that normal Organization/Board access will stop and reminders will suspend.
Cancel receives keyboard focus and produces no write. Confirm captures an
immutable original account, reviewed version and UUID key. Unknown outcomes
clear private review metadata, disable new reviews and offer only explicit
**Retry original deletion request** with the same path/account/version/key.
Every request has a 15-second deadline and late route responses are fenced.
The pending intent lives in the mounted operation, not across a browser reload.

The account is checked before each write and also bound by the API query.
Current permission/version refusals require a new canonical review and renewed
explicit confirmation before a fresh key. Denied recovery clears the original
intent and metadata; switched accounts navigate to sign-in. A 202 displays
**Deletion request acknowledged. Deletion has not been confirmed complete.**
No ordinary read absence is presented as proof of terminal deletion.

Confirmation exit focuses the recovery control or acknowledgment status;
cancellation returns focus to the review action. Routing tests require recovery
to remain mounted past the normal access polling interval without bypassing
other surfaces. Browser checks cover cancellation, Owner-only controls,
review/account fences, repeated immutable recovery, fresh reviewed version/key
after conflict, denied recovery, deadlines and route/late response fencing.
Focused source checks pass; current full/native verification remains pending.

Desktop/phone native scenarios use actual Owner/Admin accounts and invitation
acceptance, require Admin API/UI denial, keyboard cancel with unchanged state,
WCAG 2.2 AA, actual committed DELETE followed by a lost response, withdrawn
Organization/Board access for both accounts, same-key 202 recovery and honest
request-only status/focus. These scenarios are implemented but await CI.

Completed Organization graph/file deletion, audit retention, terminal
ORGANIZATION_DELETED publication and two-client lifecycle recovery remain
outstanding. A 202 receipt cannot satisfy those requirements.
See the [acceptance map](prd-03-acceptance.md),
[Organization transactions](organization-command-transactions.md),
[creation recovery](organization-creation-retries.md), and
[departure](organization-departure.md).
