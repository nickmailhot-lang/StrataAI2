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

`OrganizationMetadataEventContract` checks canonical version/time, late
projection failure rollback, gap-free retry, duplicate-version refusal, tenant
isolation, restricted capabilities and immutable history. The exact-image
Organization command fixture includes event and counter rows in its unchanged
state comparisons during receipt failures and session expiry, and requires one
event for concurrent same-key creation and editing. Real PostgreSQL execution
for this migration is pending CI; compilation does not establish those results.

This is a durable source foundation. Worker readiness, bounded authorized
replay, SignalR invalidation/reconnect and browser consumption are still
required. Invitation/member events and the existing terminal deletion event
also need integration into the Organization delivery contract. Journal
insertion alone is not realtime delivery and does not satisfy PRD-03 closure.
