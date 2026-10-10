# Organization creation acknowledgments

[PRD-03](https://github.com/nickmailhot-lang/StrataAI2/issues/3) WS-FR-001,
AC-WS-03-01 and TC-05/06/07/08 require creation recovery without duplicate
Organizations. This document records the server and browser recovery contracts; current
native execution and complete PRD acceptance remain pending.

## Request and original acknowledgment

`POST /organizations?expectedActorId=<reviewed account UUID>` accepts an
optional `Idempotency-Key` header containing one nonzero canonical UUID.
The account query refuses a changed cookie account with `session_unavailable`
before creating an Organization. Existing callers without a key retain their
one-request creation behavior; they cannot recover a lost acknowledgment.

A keyed request binds the exact name and description, including differences
between null and empty values, and the reviewed [Organization Type](organization-types.md).
Default Strata retains the prior name/description fingerprint for compatibility;
other classifications also bind the type. The first successful response is a 201 with
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

## Browser recovery

The MUI creation dialog captures the account, UUID intent key and exact body
on the first submission. It checks `/me` before POST and sends the expected
account query. Unknown responses or transport failure retain that same intent;
name and description stay read-only and only explicit **Retry original
creation** resubmits it. Return/cancel, Escape and directory paging/refresh
cannot replace an unresolved creation with a fresh intent. Each request has a
15-second deadline; route departure aborts pending reads and fences late results.
The pending intent lives in the mounted dialog and is not persisted across
navigation or a browser reload.

A valid original 201 is checked for creator, Owner role, version one and a
canonical Organization UUID. The dialog then separately reads current active
Organization membership/state and checks `/me` again before navigation. It
never displays historical receipt metadata as current access or overwrites
later metadata. A failed current read retains the same creation key for recovery.
Current refusal clears private form values and stops submission; a changed
account sends the browser to sign-in. Definitive input/conflict/expiry/rate-limit
refusals require return to a freshly checked directory before another creation.

The uncertain result moves keyboard focus to the recovery control. Cancellation
before submission refreshes the directory and restores focus to its current
creation control. Unit scenarios cover immutable repeated retries, switched
accounts before/during recovery, current access withdrawal, malformed foreign
receipts, definitive refusals, current-read failure, deadline and route/late
response fences. All 45 creation/discovery cases pass; web type checking, lint
and browser TypeScript checks pass.

Desktop/phone native scenarios commit a real creation, edit its metadata through
the actual API, lose the original response, then recover with exactly the same
account/path/body/key. They require the later canonical heading, one directory
Organization, one Owner membership and unchanged later Organization state.
Cancellation must produce no Organization and restore keyboard focus; dialog
WCAG 2.2 AA checks are included. These native checks await CI execution.

The restricted PostgreSQL creation fixture additionally observes a real runtime
receipt INSERT sleeping in an AFTER INSERT trigger while the original cookie
session expires. It requires a private-detail-free 401 without Set-Cookie,
unchanged user/session records and no parent/Owner/audit/receipt publication.
It restores the original expiry and reuses the same creation key for the
observed concurrent successful retry. Trigger/session cleanup also runs on
failure. Bash syntax passes; actual native execution remains pending.

Remaining work includes current native keyboard/phone and receipt-expiry proof.
Full PRD-03 acceptance also requires completed deletion and Organization
metadata/lifecycle realtime delivery; see the [acceptance map](prd-03-acceptance.md).

Related contracts: [Organization transactions](organization-command-transactions.md),
[discovery](organization-discovery.md), [metadata recovery](organization-settings.md),
[departure](organization-departure.md), [schema upgrades](schema-upgrades.md).

## Complete-attempt deadline

Creation and explicit original recovery now each have one 15-second deadline
covering initial account verification, POST, current Organization admission and
final account verification, including response-body decoding. Requests check the
child signal before transport and after decoding. Per-request timers no longer
multiply the total wait, and abort-ignoring late bodies cannot navigate or clear
the original intent after the deadline or a later attempt.

An unsent preflight failure explicitly says no creation was sent. A submitted
uncertain result preserves the original account/key/body, locks fields and moves
focus to explicit original recovery. Neither path creates a replacement command;
fresh account/current-parent admission still gates navigation.

The focused creation component suite passed all 16 cases, including an
eight-second account check followed by stalled canonical JSON, complete deadline
expiry, late-result suppression and identical-key/body recovery, plus an unsent
stalled preflight. Desktop/phone native cases hold an actual committed POST
response for eight browser-clock seconds, then hold its protected current-read
response through the complete deadline. They require recovery of the real
original request, one directory Organization/revision, keyboard focus, no document
reload and WCAG 2.2 AA. Browser clock advancement is client deadline evidence,
not server latency or cookie-expiry proof. Native exact-image execution remains
pending; broader telemetry and PRD-03 acceptance remain unfinished.

## Canonical persisted creation acknowledgment

Organization creation now returns the actual PostgreSQL INSERT row, including
its stored timestamps, before committing the original Organization/Owner
membership transaction. It no longer returns the higher-precision input clock
value when PostgreSQL stored a different microsecond value. No schema, RLS,
authorization or receipt policy changes are required.

The native settings actor fixture exposed this mismatch in four denied-edit
cases: business fields stayed unchanged, but the creation acknowledgment's
createdAt/updatedAt differed from the subsequent protected read. A new mandatory
restricted persistence contract fails before the fix and passes all three
sub-microsecond cases afterward. It compares the entire acknowledged/stored
Organization and initial Owner membership. Run the same diagnostic with
`--organization-creation-timestamps-only`; the full CI executable includes it.

The repaired contract and API Release builds pass with zero warnings/errors.
All six complete desktop/phone reviewed-actor settings scenarios pass against
an isolated immutable repaired API build and restricted PostgreSQL, retaining
exact whole-record unchanged-state assertions, real administrator substitution,
zero/one command counts, disclosure retirement, keyboard and accessibility checks.
The disposable API/web fixture and private configuration were removed afterward.
This is actual local adapter/HTTP/browser evidence, not retained-image acceptance.
