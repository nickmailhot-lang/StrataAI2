# Organization metadata event source

Return to the [documentation index](../README.md) or the
[PRD-03 acceptance map](prd-03-acceptance.md).

Migration `094_organization_metadata_events` journals future
`ORGANIZATION_CREATED` and `ORGANIZATION_UPDATED` commands from their actual
canonical audit inserts. The Organization mutation, audit, event and stream
sequence commit together in the existing owning command transaction. A later
receipt or final actor/session refusal rolls all four back. Replaying a durable
command receipt does not append another audit or event.

The private projection validates the actual active parent and active Owner/Admin
account and membership, then records its committed version and update timestamp.
Creation also requires the actual owner and version one. Each Organization
version can produce at most one metadata event. A failed insertion rolls back
the stream increment, preserving contiguous committed sequences.

Events retain the source audit ID as `event_id`, actor, Organization/entity
references, event type, version, correlation ID and timestamp. Metadata is
exactly `{}`; names, descriptions, email addresses and logo URLs are absent.
Both journal and counter use forced tenant RLS. The restricted API can read its
explicit tenant but cannot write either table or invoke the private projection.
Event history is immutable, including through administrative SQL.

There is no historical backfill: older audits do not contain the canonical
entity version, and reconstructing it would invent evidence. A future live
reader must begin from an authoritative snapshot and bind subsequent replay to
the current authorized account and Organization.

## Verification and remaining work

Migration `095_organization_metadata_delivery` publishes one reference-only
`ORGANIZATION_METADATA_EVENT_READY` job per source event in the same transaction.
Existing real source rows from migration 094 receive reference jobs during the
upgrade. No earlier audit history is reconstructed. The separate production
Worker registers `organization-metadata-delivery` and uses its existing queue
claims, retry limits, service checks and completion acknowledgments.

The Worker-only delivery capability requires the actual tenant, job, actor,
worker, lease, source event, correlation, reference key and exact one-field
metadata. It checks the lease before and after readiness publication. An expired
late fence rolls readiness back. Delivery retries and reclaimed jobs retain the
first readiness timestamp; runtime roles cannot directly update source history
or readiness. Historical committed events can be delivered after later edits or
the actor's departure, without authorizing that former actor to read them.

This handler currently runs through existing explicit general-job scopes, or
when automatic deletion processing drains that Organization's queued jobs.
Automatic routing for metadata events in other Organizations is still required.
Readers must withhold later events behind any earlier unready sequence and
freshly authorize current account, membership and surface before returning data.

`OrganizationMetadataEventContract` checks canonical version/time, late
projection failure rollback, gap-free retry, duplicate-version refusal, tenant
isolation, restricted capabilities and immutable history. The exact-image
Organization command fixture includes event and counter rows in its unchanged
state comparisons during receipt failures and session expiry, and requires one
event for concurrent same-key creation and editing. Restricted PostgreSQL CI
passed for `010324c` in [run 37514298212](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37514298212):
the migration clean/repeat/upgrade checks, schema readiness and metadata source
contract all passed. Follow-up `bf20c67` adds explicit source-audit rollback
and direct-insert privilege assertions; its restricted PostgreSQL runtime
checks passed in run 37514533790.
Exact-image command and browser integration are also pending; the database
contract does not establish those results.

The metadata delivery handler passed 17 focused local tests, and the full
solution built with warnings treated as errors. New restricted PostgreSQL
fixtures cover publication rollback, valid/stale/expired/superseded claims,
late readiness rollback, unchanged duplicate readiness and actor departure;
the upgrade fixture checks reference-job backfill exactly once. Restricted
PostgreSQL CI for migration 095 passed at `ab9a389` in
[run 37515672088](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37515672088).
Logs confirm source and delivery contracts, migration upgrade/repeat,
failure rollback and runtime schema readiness. Exact-image command and browser
integration still await their runtime results.

Automatic metadata routing, bounded authorized replay, SignalR
invalidation/reconnect and browser consumption are still required.
Invitation/member events and the existing terminal deletion event
also need integration into the Organization delivery contract. Journal
insertion alone is not realtime delivery and does not satisfy PRD-03 closure.
