# Organization Types (PRD-27, implementation in progress)

[PRD-27](https://github.com/nickmailhot-lang/StrataAI2/issues/28) defines Organization Types and tenant configuration. This guide describes the creation/classification increment. Configuration, jurisdiction policy and suspension requirements remain open.

The [complete acceptance map](prd-27-acceptance.md) tracks all eight functional requirements, four acceptance criteria and fifteen linked scenarios without reducing scope.

## Creating and reading a classification

`POST /organizations` accepts an optional `type`: `STRATA`, `HOA`, `CONDOMINIUM`, `COOPERATIVE`, `PROPERTY_MANAGEMENT_COMPANY` or `GENERIC`. Omitted values default to `STRATA`. Unsupported values return `400 invalid_organization_type` without creating an Organization. Classification grants no additional permissions or legal compliance.

The MUI creation dialog defaults to Strata and offers all six choices. The original immutable creation intent includes the reviewed type. An uncertain acknowledgment preserves the original type, body and key for retry. A receipt with a missing, unknown or different type cannot admit the destination. Successful creation still requires a separate current account and membership read. The Organization screen displays the classification from its canonical read, including after reload.

Organization creation retains the existing account admission, tenant transaction, Owner membership, creation audit and receipt. Canonical Organization reads and directory entries return `organization.type`; general metadata edits preserve it. Other accounts cannot discover the tenant by guessing its identifier.

## Receipt and upgrade compatibility

Default Strata creation preserves the previous name/description fingerprint. Other classifications also bind the requested type into the fingerprint. Reusing a creation key with a different classification returns `idempotency_conflict`. Historic receipts that did not contain a type decode as `GENERIC`; a retry must not retroactively assert that an unclassified tenant was Strata. Existing receipt expiry and current authorization checks still apply.

Migration `136_organization_types` classifies existing tenants as `GENERIC`, preserving their other fields, timestamps and versions. Future inserts default to `STRATA`. The supported vocabulary has a database constraint. Direct changes to a persisted classification are refused; a future historically significant configuration change requires an owning versioned and audited command. A no-op assignment preserves the record. API and Worker readiness require the migration ledger entry.

## Verification scope

- `OrganizationTypeTests.cs` covers all supported creation types, omitted default, invalid input, canonical reads, cross-account denial, metadata preservation, original receipt recovery and changed-type conflicts.
- `OrganizationTypeReceiptCompatibilityTests.cs` exercises both omitted/explicit default retry directions and deserializes the historical stored JSON format without Type before an actual HTTP retry. Creation dialog tests cover the selector and immutable retries, including missing, unknown and mismatched acknowledgments. Organization screen tests display each canonical classification.
- `organization-types.spec.ts` exercises all six classifications through the real MUI creation dialog and PostgreSQL API at desktop and phone sizes. It checks reload persistence, directory classifications, membership, cross-account denial, focus, overflow and accessibility. Execution results must be established separately; source coverage alone does not prove acceptance.
- The required migration runner includes `organization-types-before-upgrade.sql` and `organization-types-after-upgrade.sql`. It compares the entire prior tenant payload, checks generic backfill and the new default, and verifies refused changes and unchanged no-ops alongside the original migration refusal, serialization and fault checks.

## Remaining PRD-27 work

Local working-tree execution on 2026-10-10 passed the complete migration runner, 14 focused API/receipt compatibility cases, all 787 original Domain cases in the Linux runtime, and both desktop/phone browser cases with all six classifications. A subsequent first-attempt browser execution selected every classification with the keyboard and also passed both cases without skips or retries. The existing restricted API/Worker readiness contract individually refused and recovered every one of the 136 migration entries; its original identity lifecycle contract also passed. Owned browser databases, containers and credential files were independently confirmed absent after execution. The unfiltered API run passes all 751 cases, retaining all 740 original source cases. The 14 focused cases include three additional receipt cases, yielding a 754-case passing source union across two runs; this is not a single unfiltered 754-case execution. The complete frontend rerun passes 2,088 assertion occurrences in 142 files, retaining all 2,077 original occurrences and adding 11. Integration/shard guards pass 163 checks, and collection verifies 349 browser cases in 131 intact files. Current immutable-image CI remains pending, and no PRD closure is implied.

The increment does not complete PRD-27. Legal name/registration identifier, address, jurisdiction, timezone, strata operating configuration, sourced jurisdiction policies, historically significant configuration revisions and audit, tenant suspension/deactivation, corresponding events/realtime, configuration UI and full acceptance remain to be implemented and verified. The management-company, historic timezone/jurisdiction and corporation-number conflict scenarios also remain open. Existing lifecycle and authorization foundations must be applied to those commands; creation classification cannot substitute for them.

Related: [creation receipts](organization-creation-retries.md), [Organization settings](organization-settings.md), [database roles](runtime-database-roles.md), [schema upgrades](schema-upgrades.md), [documentation index](../README.md).
