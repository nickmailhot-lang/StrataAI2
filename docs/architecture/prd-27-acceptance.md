# Organization Types and configuration acceptance — PRD-27

[PRD-27](https://github.com/nickmailhot-lang/StrataAI2/issues/28) remains open. Estimated work remaining: **90%**. This map preserves the complete ticket scope; classification at creation is an increment, not acceptance of tenant configuration.

## Functional requirements and acceptance criteria

| Requirement | Current evidence | Work still required |
| --- | --- | --- |
| ORGCFG-FR-001 / AC-ORGCFG-27-01 | Existing Organization tenant transactions, membership admission, forced PostgreSQL RLS and independent portal admission remain authoritative. Classification does not grant access. | Apply and verify those boundaries for every new configuration entity and command, including lifecycle, version, audit and events. |
| ORGCFG-FR-002 / AC-ORGCFG-27-02 | Six supported creation types, STRATA default, GENERIC backfill, immutable reviewed intent, canonical reads and MUI display. API, migration and desktop/phone checks are recorded in the [classification guide](organization-types.md). | Full current suites and immutable-image acceptance; any later classification change must use historically significant configuration revision/audit semantics. No complete AC claim is made. |
| ORGCFG-FR-003 / AC-ORGCFG-27-03 | Organization name and existing operational lifecycle are prerequisites. | Legal/display-name distinction, corporation/registration identifier, civic address, jurisdiction, Organization timezone and the complete required lifecycle. A Board timezone is not an Organization timezone. |
| ORGCFG-FR-004 / AC-ORGCFG-27-04 | No strata configuration implementation is claimed. | Lot count, fiscal year end, AGM/depreciation cycles, insurance renewal, emergency contacts, default categories/priorities and tenant-safe intake Board/List relationships. |
| ORGCFG-FR-005 | Existing metadata versions, audits and creation receipts provide transaction foundations. Direct classification overwrites are refused. | Authoritative configuration revisions/history, stale-write protection, audited changes, original command recovery and rollback of every related effect. |
| ORGCFG-FR-006 | Classification contains no hard-coded jurisdiction law. | Persisted jurisdiction policies with source/notes, validation, explicit permissions and version/audit semantics. Configuration must not claim legal compliance. |
| ORGCFG-FR-007 | Existing deletion/retirement gates are prerequisites. | Suspension/deactivation commands, rejection of new operational activity, preserved historical records and lifecycle-aware authorized reads. Deletion evidence does not prove suspension. |
| ORGCFG-FR-008 | Creation/read tests deny an unrelated account; PostgreSQL migration retains existing tenant RLS. | Configuration-specific guessed-ID, cross-Organization relationship substitution, permission withdrawal, portal separation and lifecycle tests on server and browser surfaces. |

## API, data and event obligations

`POST /organizations` and canonical Organization reads expose the classification increment. `GET/PATCH /organizations/{id}/configuration` remain unimplemented. Only authorized Organization administrators may change configuration; governance titles or management-company classification must never imply that authority.

The remaining `OrganizationConfiguration` and `JurisdictionPolicy` data definitions must specify tenant ownership, keys/foreign keys, cardinality, confidentiality, required/optional fields, indexes/uniqueness, lifecycle, versions/history, retention and audit as required by PRD-20. Intake references must belong to the owning Organization and preserve Board/List relationship meaning.

Configuration mutations must schema-validate input, use authoritative version preconditions and retry-safe idempotency, and commit state, history, audit, required Activity/domain events and receipt atomically. Safe event metadata must not expose address, emergency contacts or other private configuration. The remaining `ORGANIZATION_CONFIGURATION_CHANGED` and `ORGANIZATION_SUSPENDED` events require authorized realtime publication and recovery, authenticated actor/capacity where applicable, entity/version/correlation and actual source time.

## Linked test scenarios

Creation/classification evidence supports portions of TC-01, TC-03, TC-04, TC-05, TC-07, TC-11 and TC-12. It does not complete those scenarios for configuration. TC-02 empty-state behavior, TC-06 permission withdrawal, TC-08 concurrent configuration writes, TC-09 realtime recovery and TC-10 archive/retention interactions require the configuration implementation. All critical loading, validation, conflict, denial, uncertainty/retry and lifecycle states still require accessible desktop/phone verification.

TC-13 management-company changes mid-year, TC-14 jurisdiction/timezone changes after historical records exist, and TC-15 conflicting corporation identifiers remain open. These changes must preserve historical meaning and return a non-disclosing conflict when a protected Organization already owns a requested identifier.

## Closure gate

No requirement has an approved scope deferral. Closure requires the complete functional/API/data/event contract, all linked scenarios, accessible keyboard/mobile flows, private telemetry and operator guidance, complete current automated suites, rigorous build-once image CI and resolution of material cross-PRD contradictions. Local classification results and earlier immutable-image results support only their recorded scopes and revisions.

Related: [classification behavior and executed evidence](organization-types.md), [Organization acceptance](prd-03-acceptance.md), [dependency audit](../ticket-dependencies.md), [documentation index](../README.md).
