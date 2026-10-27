# PRD-28 / PRD-34: property foundation, first increment

User-authorized work: October 26, 2026. Source baseline is the green PR #126 head `ee0e42ec8b5bbe2df5932a1a45c284b04138705a`. This increment is stacked on that PR and does not merge it, change CI selection or claim either feature PRD complete. Existing UX polish remains the integration priority; this increment adds no navigation or frontend route.

## Reconciliation and implemented boundary

The live canonical requirements are issue #29 (PRD-28), issue #35 (PRD-34) and issue #36 (PRD-35 confidentiality). The inspected source already provides Organization classification/configuration, Board/Card permission admission, assignment, dates, lifecycle, activity and transaction/retry infrastructure. No existing StrataLot directory, CardStrataLot relationship or authoritative strata Card metadata implementation was found. Preserve all original FR/AC/test IDs and dependency cycles; these are missing-work increments, not a replacement backlog.

`PRD-28-VS-01-T-02` delivers the first administrative unit directory API. `PRD-34-VS-01-T-02` delivers validation contracts for a later persisted Card metadata command. The original audit tasks remain separate, In Progress and unaccepted.

### Unit directory consumer

An active internal Organization Owner/Admin can call:

- `GET /organizations/{organizationId}/strata-lots?after={uuid}`: 50 rows plus an exclusive next cursor; inactive units remain available for historical context.
- `GET /organizations/{organizationId}/strata-lots/{lotId}`: canonical current revision.
- `PATCH /organizations/{organizationId}/strata-lots/{lotId}`: create with version 0 or correct/deactivate the current expected version. A nonempty UUID `Idempotency-Key` is required.
- `GET /organizations/{organizationId}/strata-lots/{lotId}/history?beforeVersion={version}`: 50 newest revisions plus an exclusive continuation boundary.

PATCH accepts `version` and a `data` object with required `unitIdentifier`, `lotIdentifier`, nullable `civicAddress`, `building`, `floor`, `type`, and `status` (`ACTIVE`/`INACTIVE`). Every field must be present, including nullable fields. Unknown/duplicate fields, oversized bodies and malformed values fail validation after admission. Identifiers are exact, case-sensitive labels unique within their Organization. Correcting a label retains the UUID, prior labels, timestamps and history. Reactivation uses the same versioned command; there is no delete operation or current-owner field.

The production adapter borrows the existing admitted Organization transaction. Migration 151 supplies composite tenant identities/FKs, forced RLS, uniqueness, append-only history/receipts and a deferred current-history relationship. Startup requires the migration; provisioning grants the API only necessary table operations and grants the Worker none. Demo has its own in-memory adapter and participates in cross-store rollback; it is not a Production fallback. The source consumer tests include a 55-unit directory so a 50-row page cannot silently omit the final five units.

Current active session/Organization and Owner/Admin authority are required before and after IO, including original receipt recovery. Internal Member, foreign user and Portal grants do not authorize directory disclosure. Lost final authority rolls back the current row, history, receipt and audit together. Exact original intent returns its original revision; changed intent under the same key conflicts; expired receipts cannot silently replay. These are administrative capabilities, not an invented Council/Manager role model.

### Card metadata contract

The pure domain contract defines fixed priority identities with configurable order, validated Organization category options/defaults, the PRD source types and explicit `UNIT_SPECIFIC`, `MULTIPLE_UNITS`, `COMMON_PROPERTY`, `ALL_UNITS` and `ORGANIZATION_WIDE` scopes. Unit-specific work needs exactly one ID; multiple-unit work needs at least two distinct IDs; the other scopes carry no synthetic unit IDs. Relationship validation refuses foreign, missing, duplicate and inactive unit records. Actor/object authorization remains a separate required consumer check.

This does **not** yet persist options or Card metadata, configure priority display labels, edit existing Cards, filter Cards, publish realtime changes or review AI suggestions. List state and assignees are deliberately absent from this contract. Common property is represented by explicit scope, not an automatically created fake unit.

## Validation and preserved failures

The evidence JSON records exact source revision, commands and local results. Release solution compilation, focused domain rules and actual TestServer HTTP/session/transaction consumers are distinct from Production persistence. The latter has a new restricted-role contract wired into the existing mandatory Organization configuration persistence execution, plus installed-schema privilege/RLS checks. It has not run locally because Docker/PostgreSQL are unavailable here. Browser, responsive, accessibility, full security and immutable-image qualification remain not run for this candidate.

An initial optional architecture-contract run failed because the isolated worktree lacked eslint; reusing the existing installed dependencies allowed all 12 contracts to pass. A strengthened receipt-source check initially returned a storage error for changed intent; the original HTTP test caught it. Conflict ordering was corrected and the complete affected HTTP suite passes. Neither failure is hidden by retries, skips, changed deadlines or weakened comparisons.

## Remaining original obligations and next increments

1. Qualify this exact candidate against real PostgreSQL/restricted roles and review schema, role scope, retention and historical integrity. No acceptance or Done transition yet.
2. Add the unit directory UI under the approved existing Organization composition, with authorized entry, create/correct/deactivate, canonical refresh and return. Apply UXA-01–08, supported widths/zoom, keyboard/Axe, empty/error/conflict and live withdrawal evidence.
3. Persist Organization priority display labels/order/categories and versioned Card metadata; preserve Card versions, original-command receipts, audit/activity and authorized realtime delivery. Review PRD-35 confidentiality before exposing new Card metadata through shared/public surfaces.
4. Add typed CardStrataLot links and affected-scope picker, separate from Assigned to; link only currently authorized endpoints within the same Organization. Show first two units plus count on Boards and all authorized units in detail.
5. Add indexed, server-side unit/scope/metadata filtering across pages, retaining Card identity, filters and return context. Do not approximate complete filters from a partially loaded client page.
6. Continue PRD-28 Portal temporal unit relationships with PRD-29/61 and property/asset dependencies with PRD-62. Do not derive ownership or Portal access from Card assignments or unit selection.
7. Continue PRD-34 optional related operational records and distinct AI-inferred/human-confirmed provenance as those producers become available. These remain open original obligations, not silently deferred acceptance.

Draft publication targets PR #126's branch so the feature diff contains only this increment. It must be retargeted to main after that prerequisite is integrated; current workflow branch filters are unchanged. This stacked draft does not yet have its own hosted required-ci qualification. Passing PR #126 does not qualify this new feature revision. Neither PRD-28, PRD-34 nor the MVP is closed or accepted.
