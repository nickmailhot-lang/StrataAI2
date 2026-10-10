# Organization Types and configuration acceptance — PRD-27

[PRD-27](https://github.com/nickmailhot-lang/StrataAI2/issues/28) remains open. Estimated work remaining: **75%**. This map preserves the complete ticket scope; classification and configuration storage/HTTP are increments, not complete tenant configuration acceptance.

The [configuration foundation and HTTP contract](organization-configuration.md) include Domain validation, Application commands/history, Demo/PostgreSQL stores and configuration read/change/history routes. The focused Linux run passed 34 PRD-27 cases, including 10 HTTP cases; a new local Production HTTP scenario passed on its first attempt with real verified sessions and PostgreSQL. Restricted C# PostgreSQL persistence, foreign-intake scope regression, invariant namespace case/whitespace parity, complete migration upgrade/isolation/atomicity checks and all 137 API/Worker readiness entries also passed. The latest unfiltered API run passed 774/774 against the current API source; its newer namespace SQL/persistence-contract changes have separate restricted-runtime evidence. Complete frontend source coverage passed 2,089/2,089 across 142 files with unchanged assertions/deadlines and two workers. MUI/realtime, remaining data/lifecycle review, complete deployed workflow and immutable-image/full acceptance remain unfinished. This supports portions of the requirements and scenarios below; it does not complete them or establish a released configuration workflow.

## Functional requirements and acceptance criteria

| Requirement | Current evidence | Work still required |
| --- | --- | --- |
| ORGCFG-FR-001 / AC-ORGCFG-27-01 | Existing Organization tenant transactions, membership admission, forced PostgreSQL RLS and independent portal admission remain authoritative. Classification does not grant access. | Apply and verify those boundaries for every new configuration entity and command, including lifecycle, version, audit and events. |
| ORGCFG-FR-002 / AC-ORGCFG-27-02 | Six supported creation types, STRATA default, GENERIC backfill, immutable reviewed intent, canonical reads and MUI display. API, migration and desktop/phone checks are recorded in the [classification guide](organization-types.md). | Full current suites and immutable-image acceptance; any later classification change must use historically significant configuration revision/audit semantics. No complete AC claim is made. |
| ORGCFG-FR-003 / AC-ORGCFG-27-03 | Reviewed legal name, identifier, civic address, jurisdiction and IANA timezone are modeled separately from canonical display name and persisted with server parent snapshots; focused configuration HTTP checks pass. | Remaining schema/data review, deployed HTTP and MUI behavior, complete required lifecycle and full acceptance. A Board timezone is not an Organization timezone. |
| ORGCFG-FR-004 / AC-ORGCFG-27-04 | Domain model and private stores include all named strata fields; Demo and PostgreSQL owning transactions preserve revisions and source identity. | HTTP/MUI workflow, Production intake edge-case coverage, complete field/schema contract and linked scenarios. |
| ORGCFG-FR-005 | Revision history, required versions, original receipts, reserved expired keys, PostgreSQL audit/source and final-fence atomic rollback have focused evidence. Direct classification overwrites are refused. | Complete HTTP recovery, Demo authoritative audit integration, Activity/Worker/realtime delivery, retention interactions and full suites. |
| ORGCFG-FR-006 | Sourced/annotated jurisdiction policies are validated and stored in private revisions without hard-coded jurisdiction law or compliance claims. | Full schema/HTTP/MUI review, permission/lifecycle coverage and published audit/realtime acceptance. |
| ORGCFG-FR-007 | Existing deletion/retirement gates are prerequisites. | Suspension/deactivation commands, rejection of new operational activity, preserved historical records and lifecycle-aware authorized reads. Deletion evidence does not prove suspension. |
| ORGCFG-FR-008 | Creation denies an unrelated account; configuration admission/withdrawal and forced PostgreSQL RLS have focused tests. Demo intake substitution, restricted SQL isolation, foreign-intake native regression and HTTP guessed-target/account-binding checks pass. | Additional Production intake combinations, separated portal behavior, lifecycle and browser tests. |

## API, data and event obligations

`POST /organizations` and canonical Organization reads expose the classification increment. `GET/PATCH /organizations/{id}/configuration` and bounded configuration history are implemented with focused source-host HTTP evidence; deployed acceptance remains outstanding. Only authorized Organization administrators may change configuration; governance titles or management-company classification must never imply that authority.

The remaining `OrganizationConfiguration` and `JurisdictionPolicy` data definitions must specify tenant ownership, keys/foreign keys, cardinality, confidentiality, required/optional fields, indexes/uniqueness, lifecycle, versions/history, retention and audit as required by PRD-20. Intake references must belong to the owning Organization and preserve Board/List relationship meaning.

Configuration mutations must schema-validate input, use authoritative version preconditions and retry-safe idempotency, and commit state, history, audit, required Activity/domain events and receipt atomically. Safe event metadata must not expose address, emergency contacts or other private configuration. The remaining `ORGANIZATION_CONFIGURATION_CHANGED` and `ORGANIZATION_SUSPENDED` events require authorized realtime publication and recovery, authenticated actor/capacity where applicable, entity/version/correlation and actual source time.

## Linked test scenarios

Creation/classification evidence supports portions of TC-01, TC-03, TC-04, TC-05, TC-07, TC-11 and TC-12. It does not complete those scenarios for configuration. TC-02 empty-state behavior, TC-06 permission withdrawal, TC-08 concurrent configuration writes, TC-09 realtime recovery and TC-10 archive/retention interactions require the configuration implementation. All critical loading, validation, conflict, denial, uncertainty/retry and lifecycle states still require accessible desktop/phone verification.

TC-13 management-company changes mid-year, TC-14 jurisdiction/timezone changes after historical records exist, and TC-15 conflicting corporation identifiers have focused Application/Demo/restricted PostgreSQL evidence for preserved history and non-disclosing registration conflicts. They remain open for the complete HTTP, accessible UI, lifecycle, publication and immutable-image workflow.

## Closure gate

No requirement has an approved scope deferral. Closure requires the complete functional/API/data/event contract, all linked scenarios, accessible keyboard/mobile flows, private telemetry and operator guidance, complete current automated suites, rigorous build-once image CI and resolution of material cross-PRD contradictions. Local classification results and earlier immutable-image results support only their recorded scopes and revisions.

Related: [classification behavior and executed evidence](organization-types.md), [Organization acceptance](prd-03-acceptance.md), [dependency audit](../ticket-dependencies.md), [documentation index](../README.md).
