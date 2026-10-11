# Organization lifecycle support — PRD-27

Use this guide for suspension/deactivation review, uncertain acknowledgments,
delayed realtime delivery and historical-access questions. The
[acceptance map](prd-27-acceptance.md) records which checks have executed and
which release obligations remain open. This guide does not establish telemetry
ingestion, latency targets or retention-hold enforcement.

## Identify the affected workflow

Record the deployed image/commit, runtime mode, UTC observation time, fixed
HTTP status/error code and whether the failure concerns review, mutation,
replay or delivery. Keep correlation/event references in access-controlled
incident records where needed. Do not copy cookies, authorization headers,
retry keys, private Organization names, configuration values, Activity captions
or raw response bodies into shared logs or analytics.

An authorized current internal Owner/Admin can review
`GET /organizations/{id}/operational-lifecycle`. This supports ACTIVE,
SUSPENDED and DEACTIVATED parents. The independent Portal grant does not grant
this authority. A safe 404 may reflect a missing parent, withdrawn membership,
wrong surface or DELETING/DELETED lifecycle; it does not establish which private
record exists. A current session failure returns 401.

## Resolve an uncertain change

| Result | Next permitted action |
| --- | --- |
| No acknowledgment or 503 | Keep the original reviewed intent and key; use the UI's original-request retry. Do not create a second intent to discover whether the first committed. |
| `version_conflict` (409) | Reload authorized current state and review the changed revision before approving a new intent. |
| `idempotency_key_conflict` (409) | Stop reusing that key with different values. Retain the original intent; review a genuinely new change separately. |
| `idempotency_key_expired` (409) | Review current lifecycle. An expired receipt cannot prove the original operation failed. |
| `organization_lifecycle_unchanged` (409) | Review the already-current state before proposing another transition. |
| `session_unavailable` (401) | Sign in again as the intended account and obtain fresh admission before retrying. |
| `organization_not_found` (404) | Verify the account, intended Organization context and current internal administrative grant through authorized navigation. Do not probe guessed identifiers. |

These codes come from the [HTTP endpoint](../../src/StrataAI.Api/Organizations/OrganizationOperationalLifecycleEndpoints.cs).
An original receipt is historical evidence, not current admission or current
lifecycle state. Never edit a receipt, revision or audit source to repair a UI
disagreement. The server remains authoritative after timeout or reconnect.

## Investigate delayed publication

In Production, the separate Worker discovers eligible scopes when
`STRATAAI_ORGANIZATION_OPERATIONAL_LIFECYCLE_DISCOVERY_ENABLED` is unset; an
explicit false disables this loop. An explicit invalid value fails startup,
and enabling it in Demo mode fails startup. Check the effective runtime mode,
setting and actual Worker process/image before attributing a delay to transport.
Demo publication is a simulation and cannot prove Production Worker health.

The [discovery loop](../../src/StrataAI.Worker/OrganizationOperationalLifecycleDiscoveryWorker.cs)
seeks pages of 100 Organizations, processes at most 32 jobs or 250 milliseconds
per scope, wraps after the last page and pauses one second between passes.
These are scheduling bounds, not a delivery SLA. Its fixed warnings distinguish
an individual scope pass from a failed discovery pass; raw caught exception
messages are intentionally omitted. Scope-specific warnings contain an
Organization identifier and require appropriately restricted log access.

The typed job is `ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY`, with service
identity `organization-operational-lifecycle-delivery`. Delivery binds the live
lease to the owning immutable source. Worker shutdown leaves the committed
lease for later recovery; immediate restart does not imply immediate reclaim.
The [processor](../../src/StrataAI.Application/BackgroundJobs/BackgroundJobProcessor.cs)
records fixed completion/retry/lease-loss outcomes and stable handler failure
codes. Investigate repeated failures using existing restricted job diagnostics,
the configured database role and ordered migration state. Restoring a correctly
configured Worker allows normal recovery. Do not forge readiness clocks, erase
leases, mutate source history or bypass tenant RLS to force a green status.

Replay at `GET /organizations/{id}/operational-lifecycle/events?afterVersion=N`
requires fresh current authorization and returns only the ready source prefix.
A parent can be newer than the delivered prefix. Compare those versions only
inside the authorized incident scope; never infer missing publication from
parent version alone. The browser uses its retained source cursor after a
disconnect and obtains fresh authority. Repeated reconnect failure requires
checking the release edge's WebSocket/origin configuration and current session,
as well as Worker delivery. An ordinary page refresh does not prove missed-source
recovery.

## Interpret historical and terminal states

Suspension/deactivation withdraws ordinary operational access while preserving
records. Reactivation requires fresh administrative review. It must not create
new Portal/internal grants or rewrite archived records. After accepted deletion
the parent is DELETING; after actual Worker completion it is DELETED. Both deny
this lifecycle review/replay and reactivation, even for an otherwise active
Owner. Retained configuration/history and graph tombstones are not an access
grant or a legal-hold implementation.

For regression investigation, use the maintained
[history](../../scripts/ci/test-organization-operational-lifecycle-history.sh),
[session ordering](../../scripts/ci/test-organization-operational-lifecycle-session.sh),
[Portal isolation](../../scripts/ci/test-organization-operational-lifecycle-portal.sh)
and [Worker delivery](../../scripts/ci/test-organization-operational-lifecycle-delivery.sh)
fixtures in their CI environment. They create and clean owned test resources;
they are not production repair commands. A local pass, collected browser test
or queued Actions run cannot replace successful exact-image release execution.
