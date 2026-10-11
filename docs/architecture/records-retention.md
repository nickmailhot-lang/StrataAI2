# Records retention contracts — PRD-70

[PRD-70](https://github.com/nickmailhot-lang/StrataAI2/issues/71) remains open.
Estimated work remaining: **92%**. Domain policy, hold and disposition contracts
and private policy persistence are implemented and tested. Commands are not registered in API/Worker
flows: no runtime hold, policy administration, redaction, export or disposition
workflow is complete. This shared boundary is also required by
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
and owning runtime adapters remain pending. The table below describes the broader
contracts; planned hold references must be implemented before hold enforcement.

| Contract | Identity and ownership | Version/lifecycle and retained meaning |
| --- | --- | --- |
| RecordRetentionPolicy | Nonempty policy ID and Organization ID; one Organization has many policies, one policy has many immutable revisions. Planned revision key `(tenant_id, policy_id, version)` and Organization FK with restrictive deletion. | Positive version, UTC creation/update clocks, update at or after creation. Changes append revisions; a future current-policy reference selects the admitted revision. Disposition must bind the exact policy revision. Policy retirement must preserve referenced revisions. |
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
The API has SELECT only; the Worker has no access. Neither has write grants or
permission to call internal trigger routines. API/Worker readiness requires
migration 142. No default policy is fabricated during upgrade. Storage permission
does not establish HTTP authorization or enable a policy command.

## Acceptance evidence and complete remaining scope

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
- Applied/released hold and policy administration commands, historical actor/capacity
  attribution, audit/domain publication and authorized replay after membership changes.
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
