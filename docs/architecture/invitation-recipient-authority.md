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
Automatic authority scope discovery and production protected reader integration
are implemented below. The API does not run this handler.

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
explicit Organization job scope and the independent automatic discovery loop.
Private revisions bind production protected cursors as described below. Full
live authority invalidation acceptance remains incomplete.

The mandatory SQL fixture exercises 205 candidates in pages of 100/100/5,
per-source recipient deduplication, committed-page replacement-lease replay,
future-candidate exclusion, owning membership-command rollback, late page
failure, post-effect lease expiry, and restricted role capabilities. This is
storage evidence; actual Worker restart, concurrent delivery and native browser
withdrawal evidence remain required.

## Automatic production authority discovery

Forward migration `106_invitation_recipient_authority_discovery` exposes a
Worker-only routing capability returning at most 100 ordered Organization IDs.
It joins each private checkpoint to its original canonical metadata source and
exact job, actor, correlation, event reference and checkpoint idempotency key.
Delayed jobs, live leases and malformed source bindings are excluded. Discovery
does not mutate the queue, scan recipients or expose private recipient fields.
The independent Worker seeks through UUID pages and wraps to revisit newly
published lower IDs and recovered leases. Each Organization pass runs at most
32 jobs or 250 ms before yielding; each delivery retains its fixed 100-candidate
limit. The routing loop waits one second between passes.

The typed claim capability remains a tenant-scoped invoker under forced RLS.
Concurrent claimers use `SKIP LOCKED`; expired leases can be recovered and expired
final attempts are retired. Metadata, deletion, mail and provider queues are
outside this claim path. A committed checkpoint remains routable until its job
is acknowledged, so process death after delivery does not strand its replay.

The actual restricted C# contract passes 110-Organization seek/wrap coverage,
delay/live-lease exclusions, immutable routing, API/private-table refusal,
concurrent claims, committed replay and expired-lease recovery. Mandatory direct
SQL checks reject null/out-of-range limits and missing Worker identities, and
deny both capabilities to the API role. Migration clean/repeat/upgrade,
serialization and failure rollback checks pass through migration 106. The
isolated Linux Release builds pass with zero warnings/errors; all 738 Domain
tests pass.

A separate production Worker using the restricted Worker login, all discovery
defaults and no configured Organization IDs processed a future canonical
Organization update with 205 invitation candidates. Its persisted checkpoints
were 100/100/5; all three jobs reached `SUCCEEDED` on attempt one and the two
recipient revisions each incremented once. This was a local build snapshot,
not retained release-image or browser acceptance evidence. Startup refusal was
also verified for enabled Demo discovery and an invalid boolean setting.

The release Compose definition forwards the authority discovery flag, and its
example environment documents the default. The mandatory exact-image integration
gate `scripts/ci/test-invitation-recipient-authority-delivery.sh` submits a real
HTTP Organization update after populating 205 disposable invitation candidates.
It requires 100/100/5 acknowledged pages, two deduplicated recipient revisions,
restart stability and unchanged unrelated metadata/Work queues. It toggles only
the independent Worker discovery settings and restores the release Worker on
exit. Shell syntax is verified; execution of this new gate against retained
release images is pending CI and is not yet passing evidence.

## Production protected cursor binding

The recipient reader requires its existing owning account observation, derives
the active verified account's normalized email, and reads only that email's
private authority revision in the same transaction. An absent counter is zero.
The protected cursor binds actor, email, account version, authority revision and
position. Revision values remain private; the outbound event shape is unchanged.
The cursor uses Data Protection purpose/payload version 2, so version-1 cursors
reset after deployment rather than omit the new authority binding.

An old authority binding produces an empty reset at the current invitation
transition head. Consumers reload protected invitation discovery; no synthetic
invitation transition or list hash is introduced. The final scope comparison
discards a page if its revision changes during observation, and the existing
SignalR final cursor check rebinds after session I/O before yielding a page.

The actual restricted persistence contract creates a canonical Organization
update, delivers it through the restricted Worker adapter/typed claim, and
proves the old cursor is no longer current. The owning API reader returns an
empty reset bound to revision one; the fresh cursor is current and subsequent
replay contains no fabricated transition. Existing email-change, unverified
account and revoked persisted-session refusal checks still pass. All 739 Linux
Domain tests pass, including authority binding mismatch and revision withdrawal
during source I/O. This proves production storage/reader integration; Demo
authority parity and retained exact-image two-client native acceptance remain.

## Runtime work required next

The implemented first producer attaches to future actual Organization metadata
sources for current parent names and issuer membership changes. Existing metadata
events retain original audit identity, actor, correlation, subject and subject
revision. Their current delivery capability already validates the revision
against the correct Organization, membership or invitation subject; these
different revision sequences must not be compared with one another.

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

Demo behavior and actual
concurrent delivery/restart/large-population evidence remain unfinished. Native nonmember/Portal two-client authority withdrawal,
disconnect recovery, disclosure clearing, keyboard/mobile and latency results
must also be verified against the retained exact release images before closure.
