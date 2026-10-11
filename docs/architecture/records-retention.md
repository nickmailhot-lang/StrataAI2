# Records retention contracts — PRD-70

[PRD-70](https://github.com/nickmailhot-lang/StrataAI2/issues/71) remains open.
Estimated work remaining: **86%**. Domain contracts, private policy storage,
transactional Demo parity and authorized policy HTTP commands/reads are implemented
and locally tested. MUI policy administration, runtime holds, redaction, export
and disposition workflows remain incomplete. This shared boundary is also required by
[PRD-20](https://github.com/nickmailhot-lang/StrataAI2/issues/21) and the remaining
[PRD-27 acceptance](prd-27-acceptance.md). Retained configuration history alone
cannot establish legal-hold behavior.

## Policy and decision semantics

[RecordRetention.cs](../../src/StrataAI.Domain/Records/RecordRetention.cs) defines
Organization-specific policy values for operational work, governance, audit,
correspondence, document and AI records. Each policy has an explicit record
class/category, optional jurisdiction, retention anchor, elapsed UTC days,
disposition action and source/notes. Category keys are 1–64 ASCII letters,
digits, underscores, dots or hyphens; matching is ordinal and case insensitive.
Jurisdiction strings are explicit user-supplied values, matched ordinally without
case. Null jurisdiction means applicability within the owning Organization
without a jurisdiction restriction. It does not mean cross-Organization scope.
No record-class or jurisdiction retention duration is built into the code.

Anchors are creation, archive, closure or approval. A missing configured anchor
preserves the record instead of borrowing creation time. Retention days are
explicit integers from zero to 3,652,500; they represent elapsed UTC days rather
than calendar years/months or a legal rule. Null days require Preserve and mean
indefinite preservation. Explicit Preserve always denies disposition. Delete
and Anonymize remain separate actions; their downstream storage/privacy effects
are unimplemented. Unrepresentable deadlines fail closed. Eligibility starts
at the exact explicit deadline, after the entire configured period.

An active hold can cover the whole Organization, one record class, one category
within a class, or one record ID within a class. Category/record scope requires
a class to prevent cross-kind ID ambiguity. A hold has no automatic expiration.
Release requires a positive new revision, release actor and UTC release clock
at or after application; original application actor/reason/time stay present.
The eventual command must persist immutable transitions and audit evidence.
The Domain model alone does not make those writes auditable.

The disposition decision refuses invalid or foreign policy/record/hold sources,
incomplete hold lookup, immutable records, matching active holds, missing
anchors and pending/overflowing deadlines. It accepts a server-owned
`holdScopeComplete` flag: the future owning reader must establish completeness;
HTTP callers must never supply that assertion. Same-Organization unrelated
holds have no effect. Governance records cannot match an operational Card
policy. An explicit immutable flag preserves records even with a due destructive
policy. That flag must come from the authoritative record's current domain
state, never from a request to dispose it.

## Canonical entity definitions and persistence

Migration [142](../../db/migrations/142_record_retention_policies.sql) implements
private policy headers, immutable revisions and retry receipts. Hold persistence
and owning hold adapters remain pending. The table below describes the broader
contracts; planned hold references must be implemented before hold enforcement.

| Contract | Identity and ownership | Version/lifecycle and retained meaning |
| --- | --- | --- |
| RecordRetentionPolicy | Nonempty policy ID and Organization ID; one Organization has many policies, one policy has many immutable revisions. Persisted revision key `(tenant_id, policy_id, version)` and Organization FK with restrictive deletion. | Positive version, UTC creation/update clocks, update at or after creation. Changes append revisions; the header selects the current revision. Disposition must bind the exact policy revision. Policy retirement must preserve referenced revisions. |
| RetentionHold | Nonempty hold ID and Organization ID; one Organization has many holds. Planned header key `(tenant_id, hold_id)` and immutable transition key `(tenant_id, hold_id, version)`, with restrictive Organization/reference deletion. | Positive version; Active has no release metadata, Released requires version at least two and complete release provenance. Hold transitions require compare-and-swap versioning, idempotent receipts and append-only audit. Release cannot remove prior application evidence. |
| RetentionRecord | Read-only source projection of an existing record ID, Organization, version, class/category, jurisdiction, domain clocks and immutability. | No new independent persisted record lifecycle. Owning record adapters retain object confidentiality, current lifecycle, approved-governance immutability and archive/history references. |
| RecordDispositionDecision | Ephemeral derived result from a policy revision, record snapshot and complete current holds. | Not an audit or authorization source. The eventual transaction must recheck authority, record/policy revisions and holds through disposition and persist its own authoritative audit/result. |

Policy required values are class/category, anchor, action, source and positive
version. Jurisdiction, retention days and notes are optional as defined above.
Source is nonblank trimmed text up to 1,000 UTF-16 units; notes allow up to 2,000
and jurisdiction 120. Source/notes allow internal line breaks and tabs but no
other controls. Optional text is null or nonblank. Hold reason is required,
nonblank trimmed multiline text up to 2,000 units. Application actor/time are
required; release actor/time are nullable only for Active holds. Each supplied
actor ID must be nonempty. The future schema must retain restrictive actor FKs
and immutable event attribution after account/membership deactivation; labels
and acting capacity belong to the historical audit/publication snapshot rather
than a current membership join.

The persisted current policy namespace enforces one retained identity per
Organization/class/normalized category/jurisdiction scope, including the null
jurisdiction scope. A policy cannot move namespaces. The future owning selector must resolve a specific jurisdiction
and Organization-wide applicability deterministically; it may not silently
choose the shortest retention period. Explicit selection and stale-selection
recovery still require implementation and tests. Planned indexes support that
namespace and hold admission by `(tenant_id, status, record_class, category,
record_id)`; immutable version histories need descending version pagination.
Record-specific holds require tenant-safe typed owning-record references,
retained after operational archive or supersession. Broad scopes use nullable
class/category/record selectors as documented above. Entity records and sources
are private; forced RLS, tenant-safe FKs and current confidentiality guards are
required at persistence and HTTP boundaries.

## Policy storage and integrity

`record_retention_policies` has primary key `(tenant_id, id)` and selects its
exact current immutable history revision. `record_retention_policy_history`
has primary key `(tenant_id, policy_id, version)`; revisions advance contiguously
and preserve creation time. Deferred foreign keys and final-state triggers require
the header, revision, authenticated actor's retry receipt and audit source to
commit together. Orphan revisions and missing receipts are rejected atomically.
Category keys use ASCII case normalization; jurisdiction keys use the existing
configuration namespace normalization. Null jurisdiction has its own reserved
scope; blank normalized jurisdictions are rejected.

History insertion validates an active owning Organization and an active Owner
or Admin membership under locks, with exact membership version, actor label and
acting capacity snapshots. Later profile changes preserve those historical
values. The generated `RECORD_RETENTION_POLICY_CHANGED` audit contains only
version, record class, action and acting capacity; source and notes stay private.
Receipts bind the exact revision, actor, key, fingerprint and source clock, with
a 24-hour expiry boundary. Reserved retry keys and revisions cannot be deleted.

All three tables use forced tenant RLS and restrictive tenant-safe references.
Canonical API provisioning now grants SELECT/INSERT on all three policy tables
and UPDATE on the current header only. DELETE, history/receipt UPDATE, TRUNCATE,
REFERENCES and TRIGGER remain denied. The Worker has no policy access; neither
role may call internal trigger routines directly. API/Worker readiness requires
migration 142. No default policy is fabricated during upgrade. The service and
HTTP boundary establish current authority independently of storage permissions.

## Owning policy command contract

[RetentionPolicyService](../../src/StrataAI.Application/Records/RetentionPolicyService.cs)
implements the Application admission and command boundary. It enters the existing
Organization unit of work, checks active account/parent and current Owner/Admin
membership before validation or private store reads, and checks authority again
before returning a successful result. It validates policy fields and explicit
expected version, freezes scalar record input, preserves creation time and
original applicability, and rejects namespace moves, stale versions and backward
clocks. A separately reviewed identity is required for a different applicability.

The actor-bound retry fingerprint includes Organization, policy ID, expected
version and reviewed input. Valid retries return the original revision even after
a later policy edit; changed intent, expired keys and inconsistent receipt
provenance are refused. Correlation IDs are bounded and excluded from intent
identity. Store writes receive current membership identity/version/capacity and
an event ID, and must atomically persist the full audited source graph.

The service and both adapters are now registered. The Demo store participates in
Organization rollback; the Production store borrows its owning transaction.
The current HTTP surface and verified scope are documented below.
The test probe is not an adapter and cannot establish persistence, audit or rollback
semantics. Four focused Linux command tests passed after fresh whole-solution
compilation (zero warnings/errors), with all 822 backend/test hashes matching.
They verify admission before disclosure, original acknowledgment recovery,
changed/stale/expired retries, immutable applicability, substituted private
sources and result withdrawal after authority loss during IO. A current full API
regression is running; no result is claimed before it terminates.

[PostgresRetentionPolicyStore](../../src/StrataAI.Infrastructure/Records/PostgresRetentionPolicyStore.cs)
now borrows the owning transaction for typed current/history and original receipt
reads. It validates current account, membership and source/receipt provenance
again before writing, snapshots the actor's current label, and appends the complete
header/revision/receipt graph under a savepoint. The schema produces the audit.
Namespace, version and retry conflicts return fixed errors; a final authority
failure rolls the owning transaction back. It cannot open an independent command.
Command timestamps are canonicalized to PostgreSQL microseconds before responses
and receipts are formed, preserving exact refresh/replay equality.

Fresh compilation and **five focused Linux cases** passed with **823 backend/test
hashes matching**, including refusal of unowned/cancelled adapter operations before
database access. A separate local compiled native PostgreSQL harness also passed
using real restricted API-role connections and the actual owning unit/store/service:
first/later committed refresh, original retries, changed/stale intent refusal,
normalized namespace collision, two concurrent writers with one stale conflict,
and final authority failure rolling back the header and receipt. Independent SQL
confirmed exactly **one header, three revisions, three receipts and three audit
events**, with four safe metadata fields and no private source/notes. All **823
captured source hashes** matched; owned fixtures and credentials were removed.
This harness used **fixture-local prospective INSERT/UPDATE grants** after checking
then-canonical read-only provisioning. That capture predates the runtime command
activation below. The Worker still has no policy access; this earlier local
compiled harness is not maintained release-image or complete HTTP acceptance.

## Internal MUI policy browsing

The internal route `/app/{organizationId}/retention-policies` now reads private
current policies and retained history. Active Organization Owners/Admins receive
the navigation link; the separate Owner Portal has no new retention route.
The screen shows explicit class/category/jurisdiction, anchor, elapsed days,
action and source/notes, plus original actor label/capacity and UTC revision time.
It clearly states that recorded policies do not yet enable retention enforcement
or automatic disposition. Creation/edit/review controls remain pending.

The browser client validates exact response shapes, owning identities, bounded
pages and seek boundaries, safe versions, class/action/period rules, retained
attribution, contiguous history, stable applicability and clocks at full fractional
UTC precision. A maximum-UUID empty directory seek rechecks the same current
policy authority before private read completion; account identity is checked on
both sides. Protected failure details never enter visible text. Reads are bounded
and cancellable; changing context or superseding a request aborts and fences late
responses. Metadata invalidation and window focus recheck private access.

MUI controls provide source-aware history names, bounded next-page navigation,
loading/empty/degraded/refused states and fixed safe retry text. Opening history
moves focus to its heading; closing returns focus to the originating connected
control. Authority refusal withdraws private policy/history values. No policy
source is written to browser storage. The initial 17 client/screen tests and the
final 74-case targeted client/screen/Organization-home regression passed; web
typecheck, complete lint and production build passed. The build retains the
existing bundle-size advisory. These checks do not establish desktop/mobile
browser, screen-reader, full frontend, live policy publication or mutation-review
acceptance; those remain required before closure.

## Reviewed browser submission and recovery contract

[RetentionPolicyChangeIntent](../../apps/web/src/features/records/retentionPolicyClient.ts)
freezes the reviewed Organization, account, policy identity, expected revision,
input and retry key. Review validates the supported fields without introducing
jurisdiction defaults. Submission rechecks account and owning private policy
authority before sending, uses the existing same-origin CSRF boundary and
15-second bound, and validates the acknowledgment against the original identity,
next revision and exact reviewed input. A historical original receipt remains an
acknowledgment of that submission, not evidence of today's current policy.
Authority/account are checked again before returning private values.

[Recovery helpers](../../apps/web/src/features/records/retentionPolicyRecovery.ts)
retain at most one unresolved submission per account/Organization, with a
32-context capacity. They preserve exact canonical request bytes and refuse
replacement, corrupt/foreign records and failed storage writes instead of
discarding unresolved work. No later read acknowledges an unresolved submission.
The helpers accept browser storage explicitly; the policy browsing screen still
does not persist private read results. Sign-out and confirmed account deactivation
clear retained policy submissions. A future editor must retain its original
before sending, restore only after current owning admission, clear foreign
account/withdrawn authority scopes, and clear completion only for the matching
acknowledgment or explicitly reviewed definitive refusal.

Known retention Problem codes are admitted through a status-bound allowlist;
raw diagnostic bodies and unsafe correlation references are excluded. Maintained
client/recovery cases cover lost acknowledgment, stalled delivery, exact retry
bytes, replacement accounts, authority withdrawal, substituted receipts,
cancellation, capacity, storage failure, corrupt records and account cleanup.
The focused records/Profile/Problem regression passed **79/79 cases**; web
typecheck, full source lint and production build passed. The build retains the
existing bundle-size advisory. These are maintained client/component checks,
not browser or complete frontend acceptance.
The create/edit form, confirmation dialog, conflict reconciliation and browser
acceptance remain pending; this client contract alone does not satisfy them.

## Transactional Demo storage evidence

[InMemoryRetentionPolicyStore](../../src/StrataAI.Infrastructure/Records/InMemoryRetentionPolicyStore.cs)
participates in the existing account/Organization and Work gates and rollback
snapshots. It retains immutable policy revisions, original receipts, actor label,
membership version and acting capacity, reserves per-Organization normalized
class/category/jurisdiction namespaces, and requires an owning Organization
command for reads and writes. Expired keys remain reserved rather than silently
reused. No default policy is seeded. Historical snapshots do not join current
membership values when replayed.

The existing Demo audit store now admits retention events only with typed fixed
metadata: positive version, record class, disposition action and acting capacity.
Source, notes, category and jurisdiction never enter that metadata. Those audit
facts share the Work rollback with policy history, receipts, namespace claims and
event identities. Cancellation or a failed final authority fence restores all of
them, allowing a genuinely rolled-back key/namespace to be used again.

Fresh whole-solution compilation passed without warnings/errors. All **eight
focused Linux retention service/adapter tests** passed, with **825 backend/test
source hashes matching**. The three maintained Demo scenarios cover persisted
refresh and original retry after competing versioned edits, normalized namespace
collision, unchanged historical attribution after a real membership-version
change, 256-character correlation IDs, fixed body-free audit metadata, complete
final-authority rollback with key/namespace reclamation, and cancellation after
write with audit rollback. A fresh full API regression captures this Demo source;
its result remains pending. The earlier running regression captures the preceding
Application service and cannot prove these later Demo or PostgreSQL changes.

## Acceptance evidence and complete remaining scope

### Authorized HTTP policy administration

All routes belong to `/organizations/{organizationId}/retention-policies` and
require current active account, active Organization and Owner/Admin membership.
Collection GET accepts one nonempty UUID `after`; GET `/{policyId}` returns the
current revision; GET `/{policyId}/history` accepts one positive `beforeVersion`.
PATCH `/{policyId}` creates at version zero or changes the reviewed current
revision, with a nonempty UUID `Idempotency-Key`. The request has required `version`
and `policy`; policy requires recordClass, category, anchor, retentionDays (explicit
null is allowed), action and source, with optional jurisdiction/notes. Record class,
anchor and action are named camel-case enum values. Applicability remains fixed
for a retained identity. No DELETE or automatic disposition operation is exposed.

Input rejects duplicate/unknown/missing fields and numeric enum values and bounds
both chunked and fixed-length bodies to 32,768 bytes. Current admission precedes
body, key and cursor validation, then the owning command/read freshly rechecks
authority. Responses use `private, no-store`; expected-actor mismatch returns 401.
Stable envelopes distinguish validation (400), version/namespace/retry conflicts
(409), authorized missing policy (404), denied Organization (404 without protected
source facts), and unavailable private storage/source (503).

Fresh whole-solution compilation passed without warnings/errors. **17/17 focused
Linux cases** passed with **828 backend/test hashes matching**, including Demo HTTP
refresh/history/original retry, private headers, foreign/anonymous and expected-actor
denial, and strict malformed input. The unchanged maintained
[HTTP fixture](../../scripts/ci/test-retention-policy-http.sh) passed against a
fresh compiled Production API with canonical provisioned API/Worker roles, fresh
schema and installed capability assertions: persisted equality, first/later changes,
original and changed/stale retries, namespace conflicts, retained attribution,
history seeks, history/receipt/audit counters and four body-free metadata fields,
foreign/anonymous refusal, and suspended/deactivated denial with unchanged restored
revision. All **1,908 repository source hashes** matched. Owned fixtures and
credential files were removed. The fixture is now mandatory in build-once release
integration CI, but this local run used compiled mounts, not the immutable release
images. A fresh full API regression is running; exact-head CI remains pending.

The owning Application service now supplies bounded policy-directory and retained
history pages. Both adapters read at most 51 sources and return 50 items with a
seek boundary: ascending policy UUID for the directory, descending retained
revision for history. PostgreSQL uses the existing tenant/policy primary keys;
Demo uses the same ordering and boundaries. History carries immutable actor label,
actor/membership identities, membership version, acting capacity and event ID;
it never joins today's profile or membership to rewrite the revision attribution.
Retry fingerprints and keys are excluded from these read models.

The service admits current Owner/Admin authority before cursor validation/private
IO and rechecks it before disclosure. It rejects excess rows, duplicate or foreign
directory identities, incomplete/noncontiguous revision pages, inconsistent current
source, changed applicability, nonmonotonic clocks and invalid historical
attribution. A missing policy returns a fixed not-found result; seeking below
revision one returns an authorized empty page. These are Application read models;
The current HTTP surface uses these seek boundaries and private headers. The
internal MUI browsing route is documented above; mutation review remains pending.

Fresh whole-solution compilation passed with zero warnings/errors; **11/11 focused
Linux cases** passed with **826 backend/test hashes matching**. New maintained
cases cross the 50-item boundary with 52 policies and 53 retained revisions,
exclude another Organization's sources, check original attribution and exhausted
boundaries, and refuse duplicate/foreign/incomplete/substituted sources and
withdraw pages after authority loss during IO. A separate fresh local native
PostgreSQL harness executed the actual directory/history queries and seek
boundaries with retained attribution, alongside prior write/retry/concurrency/
rollback checks. Its **826 source hashes matched** and independent committed
graph/audit counters remained one header, three revisions, three receipts and
three fixed body-free audit events. Its prospective write grants were fixture-local;
canonical runtime provisioning and feature activation remain unchanged. Owned
fixtures and credentials were removed. Neither running full API regression nor
queued release CI is claimed as proof of these later source changes.

[RecordRetentionTests.cs](../../tests/StrataAI.Domain.Tests/RecordRetentionTests.cs)
passes **28/28 focused Linux cases**. The complete current Linux Domain suite
passes **971/971**, zero failures, skipped, aborted or timed-out cases. Both
captures match all **820 backend/test source hashes**. Fresh compilation passes
with zero warnings/errors; owned terminal test containers were removed.

The final disposable PostgreSQL check passed with **1,899 captured source files**
and zero mismatches. It exercised policy namespace and version refusal,
atomic source/receipt integrity, historical attribution, immutable history,
cross-tenant and unscoped disclosure refusal, and canonical API/Worker capability
checks. The unchanged full maintained migration runner also passed, including
repeat execution and its synthetic failure/serialization cases. These checks are
now required by the build-once integration workflow. Fresh whole-solution
compilation passed without warnings or errors. A compiled Production API returned
503 `runtime_database_schema_incompatible` after ledger 142 was removed and
recovered after it was restored. Owned fixtures were removed. These are local
schema/compiled-runtime checks; exact release-image CI remains pending.

This supports policy storage integrity, validation and disposition/hold decision primitives
for RECORDS-FR-001/002/003. It does not close any numbered acceptance criterion
or linked scenario. Required remaining work includes:

- Hold schema/RLS, typed tenant-safe record references and restricted policy/hold
  runtime adapters, with Demo parity and policy selector acceptance.
- Owning transaction admission, explicit elevated authorization, review/concurrency,
  complete hold lookup and current lock ordering across every disposition path.
- Applied/released hold commands, policy selector/retirement rules, MUI policy review,
  cross-session domain publication and authorized replay after membership changes.
- Independent governance preservation; controlled redaction and authorized original
  access; privacy deletion/anonymization versus retained governance/audit sources.
- Scoped exports, confidentiality, asynchronous expiring object-storage downloads;
  idempotent/recoverable disposition jobs; offboarding steps and verification.
- MUI workflows, disclosure withdrawal, degraded/empty/conflict states, keyboard,
  mobile and screen-reader acceptance; fixed-content telemetry and runbooks.
- All PRD-70 linked scenarios, cross-PRD consistency and actual build-once release
  image integration/security/CI acceptance. No new service or datastore is introduced.

The earlier API regression captures predate these two added Domain/test files;
they remain evidence for their captured API source, not proof of retention
runtime enforcement. PRD-27 stays open at 24% estimated work remaining.

The later `retcmd3-full` API capture has now completed **846/846 cases**, without
failed, skipped, aborted or timed-out cases. It captured 822 backend/test files;
six captured files changed by completion. It predates later adapter, pagination
and HTTP activation changes, so it is historical regression evidence only.
The current backend `rethttp1-full` capture remains running. Exact-head release
CI remains queued and no release acceptance is claimed.
