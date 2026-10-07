# Invitation recipient authority invalidation

Return to the [documentation index](../README.md),
[recipient journal and replay](invitation-recipient-events.md) or
[protected discovery](invitation-discovery.md).

An invitation creation source proves a committed invitation, but it does not
prove continuing issuer rights, active parent state or current names. Those
dependencies can change without an invitation acceptance/revocation event.
The recipient cannot subscribe to protected Internal Organization/Board streams
merely because an invitation or Portal relationship exists.

## Application dispatch boundary implemented

`InvitationRecipientAuthorityDeliveryHandler` defines a distinct
`INVITATION_RECIPIENT_AUTHORITY_PAGE` job with service identity
`invitation-recipient-authority`. Dispatch preserves the original claimed job,
lease and exactly one canonical `eventId` reference. The existing strict source
reference parser rejects extra/private fields, duplicate properties, malformed
references and zero IDs. One call to the private delivery store is bounded to
100 scanned candidates. No recipient addresses, parent names, raw tokens or
grant details are returned by this capability.

The store contract requires one leased transaction to verify the original
canonical source and its private page checkpoint, deduplicate recipient effects
and persist any next-page job before acknowledging the current page. A successful
page is not a claim that all recipients have been processed. The handler refuses
an unavailable source/lease and observes cancellation after storage returns;
the existing dispatcher therefore cannot acknowledge that cancelled page. A
later lease must be able to recover already committed effects/continuation.

Eleven application/dispatcher cases pass for exact source/lease forwarding and
the fixed page limit, malformed/private references, expired/unavailable leases,
wrong service and cancellation after a page return. The full Release solution
build passes with zero warnings/errors. Test doubles exercise dispatch policy;
they do not prove database provenance, atomicity, deduplication or persistence.

The production separate Worker registers the handler and storage adapter. Its
existing explicitly configured Organization scope can deliver these jobs.
Automatic authority scope discovery and protected reader integration are still
required. The API does not run this handler.

## PostgreSQL source and bounded delivery implemented

Forward migration `105_invitation_recipient_authority` attaches only to future
actual `ORGANIZATION_UPDATED` and `ORGANIZATION_MEMBER_REMOVED` metadata sources.
It publishes one reference-only job and private initial checkpoint in the owning
command transaction. Historical rows acquire no fabricated jobs or revisions.
The original audit event, actor, correlation and subject revision remain the
source. Membership sources validate membership revisions, not Organization
revision numbers. Historical source delivery survives later actor departure.

The lease-bound capability scans at most 100 invitation candidates using an
indexed `(created_at, id)` seek and the original source-time cutoff. It includes
accepted/revoked rows conservatively because recipients can retain cached names
or links. Each source affects each recipient once, even across multiple pages or
replacement leases. Private revision increments, immutable effects, checkpoint
completion and any continuation job commit together. A completed page replay
reuses its persisted result. Tenant/job/service/source/metadata/worker/lease are
validated; expiry after effects raises an error and rolls the transaction back.
Recipient revisions are separate from canonical invitation transition sequences.

The API can read only the explicitly routed recipient's private revision. The
Worker can execute delivery but cannot read private checkpoints, effects or
revision rows directly. All three tables use forced RLS; runtime roles receive
no direct writes. A PostgreSQL adapter wraps delivery in one tenant transaction.
The production Worker registers the adapter and handler for its existing
explicit Organization job scope. Automatic delivery without that configuration
still needs typed authority claim/discovery; revisions do not yet affect
protected cursors. This increment does not claim live authority invalidation.

The mandatory SQL fixture exercises 205 candidates in pages of 100/100/5,
per-source recipient deduplication, committed-page replacement-lease replay,
future-candidate exclusion, owning membership-command rollback, late page
failure, post-effect lease expiry, and restricted role capabilities. This is
storage evidence; actual Worker restart, concurrent delivery and native browser
withdrawal evidence remain required.

## Runtime work required next

The first producer should attach to future actual Organization metadata sources
for current parent names/state and issuer membership changes. Existing metadata
events retain original audit identity, actor, correlation, subject and subject
revision. Their current delivery capability already validates the revision
against the correct Organization, membership or invitation subject; these
different revision sequences must not be compared with one another.

The typed separate-Worker claim/discovery path for automatic scope delivery must
be implemented next. API observation must read the private revision inside its
existing owning identity boundary and include it in protected cursor binding.
A revision change should cause an empty reset followed by fresh protected
discovery, without inventing invitation transitions or pending-list hashes.
Demo needs equivalent transaction rollback and current-recipient behavior.
Actual adapter/Worker restart and concurrent-page execution evidence must be
added alongside the existing SQL capability tests.

After the Organization path, the remaining dependencies include target Board
name/archive/member authority, Organization deletion/lifecycle and issuer
account withdrawal. Each needs its actual canonical source and appropriate
bounded scope discovery. Historical committed sources remain deliverable after
later actor departure; current recipient discovery still owns present grant
admission. Legacy changes without a proven source must not acquire fabricated
actors or history.

Automatic scope discovery, cursor/reader integration, Demo behavior and actual
concurrent delivery/restart/large-population evidence remain unfinished. Native nonmember/Portal two-client authority withdrawal,
disconnect recovery, disclosure clearing, keyboard/mobile and latency results
must also be verified against the retained exact release images before closure.
