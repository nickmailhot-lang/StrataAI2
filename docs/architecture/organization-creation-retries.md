# Organization creation acknowledgments

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-001,
AC-WS-03-01 and TC-05/06/07/08 require creation recovery without duplicate
Organizations. This document records the server contract; browser recovery
and current native execution remain incomplete.

## Request and original acknowledgment

`POST /organizations?expectedActorId=<reviewed account UUID>` accepts an
optional `Idempotency-Key` header containing one nonzero canonical UUID.
The account query refuses a changed cookie account with `session_unavailable`
before creating an Organization. Existing callers without a key retain their
one-request creation behavior; they cannot recover a lost acknowledgment.

A keyed request binds the exact name and description, including differences
between null and empty values. The first successful response is a 201 with
Location and the original Organization summary. A matching retry returns the
same original summary and does not recreate the Organization, owner membership
or creation audit. Changed input returns `idempotency_conflict`. Expired receipts
return `idempotency_expired`; their keys remain reserved.

A replay requires the original authenticated account, a current active
Organization and its current active membership. It never restores a departed
or removed member, promotes a later member back to Owner, or replaces later
metadata. Its historical Owner role and metadata acknowledge creation; use a
current canonical read for present roles and state. Refused recovery returns
no private Organization name or identifier. Responses use private/no-store.

## Transaction and tenant isolation

Before creation there is no existing tenant to read. The server derives a
128-bit transaction/Organization ID from SHA-256 of the versioned creation
namespace, account UUID and random intent UUID, using .NET Guid byte ordering.
The request body cannot choose a tenant. The key is intent identity, not an
access grant, and current membership remains required for disclosure.

The restricted PostgreSQL Organization unit of work acquires an advisory
transaction lock for that creation scope, then locks an existing Organization
parent if present, before actor/session admission. Thus simultaneous retries
serialize even when the parent does not yet exist. Organization, creator Owner
membership, audit and receipt all use the same owning tenant transaction.
Final session admission and cancellation must pass before commit.

Migration `086_organization_creation_replays` adds immutable receipts with
Organization/account foreign keys, a fingerprint, original result and a
24-hour expiry. Forced RLS binds them to `app.tenant_id`. API receives SELECT
and INSERT only; Worker receives no receipt access. Both hosts require the
migration ledger entry for readiness. Demo uses the existing account/Work
transaction gates and includes receipts in rollback snapshots.

## Verification and remaining work

API-host scenarios cover concurrent identical creation, changed input, later
metadata, account mismatch, departure/rejoin without role restoration and
actual final-session expiry after observing receipt/owner publication. The
expiry fixture requires rollback of the parent, owner membership and receipt,
then a fresh successful same-key retry. Compilation is source evidence only.

The native restricted-runtime fixture requires receipt-insertion failure to
leave no parent/member/audit/receipt, observes both identical requests waiting
on the absent-parent creation gate, then requires one creation audit, Owner
membership and identical 201 responses. It also checks later-edit preservation,
withdrawn membership, cross-tenant visibility, immutable grants and reserved
expired keys. The ordered migration runner repeats application and checks
forced RLS. These scenarios await CI execution.

The browser creation form still sends an unkeyed request. Remaining work is
account-bound immutable browser intent, bounded timeout, explicit same-key
recovery, current-state admission after an original acknowledgment, keyboard
and phone acceptance, and native session-expiry proof at receipt publication.
Full PRD-03 acceptance also requires completed deletion and Organization
metadata/lifecycle realtime delivery; see the [acceptance map](prd-03-acceptance.md).

Related contracts: [Organization transactions](organization-command-transactions.md),
[discovery](organization-discovery.md), [metadata recovery](organization-settings.md),
[departure](organization-departure.md), [schema upgrades](schema-upgrades.md).
