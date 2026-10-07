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

The first exact-image run (`cba365ad`, Actions run `37575126042`) stopped
before delivery: its disposable recipient email embedded a lowercase UUID and
violated the recipient-proof normalization constraint. The fixture now
uppercases the complete address. Its corrected 205-row insertion and all 205
recipient proofs pass against PostgreSQL schema 106 in a rolled-back verification
transaction. Error tracing also propagates into shell functions. Full exact-image
delivery/restart evidence remains pending a fresh CI run; this repair does not
weaken the database constraint or the delivery assertions.

## Production Board authority sources

[Migration 107](../../db/migrations/107_invitation_recipient_board_authority.sql)
adds a private immutable reference with an exclusive foreign key to either the
owning Organization metadata event or Work event. Existing metadata checkpoints
are repointed to their already-proven references. Historical Work events are not
backfilled, and no event, actor, correlation or subject version is invented.
Runtime roles cannot read or write this reference table or its private source
view directly; both remain inside the existing leased/discovery capabilities.

Future canonical Board `BOARD_UPDATED`, `BOARD_VISIBILITY_CHANGED`,
`BOARD_ARCHIVED`, `BOARD_RESTORED`, `BOARD_DELETED`, `BOARD_MEMBER_UPDATED` and
`BOARD_MEMBER_REMOVED` Work events publish the same bounded authority queue.
The canonical event must have Board entity type and matching Board/entity IDs.
Board creation, stars, Card/List/Watch/Reminder changes are excluded. Membership
events retain their existing canonical Board subject/version; they are not
compared with a membership revision. Publication scans no recipient candidates
and rolls back with the owning Work command. Delivery and automatic scope
discovery consume the private source view while retaining exact job, source,
actor, correlation, worker/lease and fixed 100-candidate limits. Broad tenant
invalidation also withdraws cached accepted/revoked disclosure; present grant
admission still belongs to protected discovery.

Executed local evidence against PostgreSQL schema 107:

- Ordered migration clean/repeat/forward upgrade, serialized runners and failed
  migration rollback pass. The required-migration diagnostic verifies API and
  Worker readiness, refusal of missing required versions including 107, and
  recovery after restoration. Worker and persistence-contract Release builds
  pass with zero warnings/errors; tenant schema isolation inspection passes.
- Existing Organization authority and discovery SQL cases pass unchanged.
  The mandatory Board SQL case proves 205 candidates in 100/100/5 pages, two
  deduplicated revisions, source-time cutoff, owning Work rollback, wrong
  capabilities, replacement-lease replay, expired lease refusal, immutable
  references and API/Worker private-table/view read refusal. Authority delivery
  does not mark the Work source ready or invent invitation transitions.
- An ordinary authenticated Production HTTP Board rename publishes the actual
  Work source. A separate local Production Worker, with no configured
  Organization IDs and only automatic authority discovery active, acknowledges
  all three pages on their first attempt with original actor/correlation. After
  Worker restart, two recipient revisions remain one; unrelated jobs stay
  pending with zero attempts and Work readiness remains unset. The local runtime
  uses compiled source with the framework image, not retained CI release images.

CI now requires the direct Board SQL case and a second exact-image authority
delivery/restart gate using a real HTTP Board rename. The existing image/cover
persistence fixture drains the new genuine authority jobs through their handler
and acknowledgment, retaining its exact Work-event counts. That full fixture
and retained-image gates require fresh CI execution. Demo Board source parity
and native local Board-target recovery are verified below. Production native
Board-target recovery, concurrency and latency remain unfinished; these local results do not close PRD-03/04/60 or ARCH-11.

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
native two-client acceptance remains.

## Demo canonical source proof

Demo captures tentative Organization-update and membership-removal proofs only
inside the existing owning Organization command. Proofs bind a private command
identity; a later command cannot publish an earlier unaudited fixture mutation.
Raw fixture writes acquire no history. The audit projection requires the actual subject proof, an active
source actor, valid correlation and the applicable administration/self-departure
rule. A self-removing administrator retains its proven pre-removal role and
must match the resulting inactive membership revision/timestamp; current
membership retirement cannot erase that actual source. Organization and
membership revisions remain separate. The private source
retains the original audit identity/actor/correlation and immutable subject proof;
publishing the same subject revision twice is refused.

Proofs, source records and publication deduplication participate in the existing
cross-store Demo rollback. Late audit refusal or command failure cannot retain
tentative history or block retry of the actual revision. Source publication
records references only. Demo then follows its existing immediate Work-event
simulation: an indexed `(created_at, id)` seek reads at most 100 candidates per
page, bounded by the original source timestamp. Private page checkpoints,
per-source/email deduplication and recipient revision effects participate in
the same command rollback. The deterministic simulation completes its pages
before the Demo command acknowledges; Production retains separate leased
Worker transactions. Both readers bind the current private revision and reset
stale cursors without inventing invitation transitions. These process-local records
reset on API restart and are separate from sample-catalog reset endpoints.

The isolated Linux Release API/test build passes with zero warnings/errors.
Three source cases pass for actual update/removal publication, unproven and
duplicate audit refusal, earlier-command proof refusal, late correlation failure,
and failure after publication followed by retry of the same subject revision.
All nine existing Demo recipient cases and six Demo Organization transaction
cases also pass. These are source/rollback regressions, not delivery or native
authority-withdrawal acceptance.

The delivery increment passes all 19 targeted Linux API-host cases (four source
and delivery cases, nine existing recipient cases and six Organization
transaction cases), with zero Release build warnings/errors. Its 205-candidate
fixture proves recipient deduplication across pages, delivery to the recipient
after the first two pages, exclusion of a later-created candidate, rollback of
completed simulation effects and retry, and empty authority-bound resets without
new invitation events. The second recipient receives no Internal membership.
This delivery regression is in-process Demo evidence; it does not itself prove
native two-client acceptance. The native results are recorded below.
The existing self-removal receipt regression also passes after retaining the
pre-removal administrative role in its proof: 20 targeted API cases pass in all.
The web subscription fixture now waits for its actual live registration before
triggering account replacement; all 1,830 web tests pass locally. The prior
`94b79707` CI run failed that fixture and self-removal guard, while PostgreSQL
passed. These repairs require fresh exact-commit CI and do not create a green
release claim.

### Native Organization authority recovery

`tests/browser/recipient-invitation-authority.spec.ts` passes against the local
Demo API at 1280px and 390px. Two real signed-in clients issue a Portal invitation,
rename its Organization, and observe the recipient's stale name and acceptance
control disappear while the actual protected recovery read is held. Releasing
that read restores the current name. A later rename during transport interruption
is recovered without a document reload; keyboard acceptance creates Portal
access while Internal Organization reads remain 404. Both viewports pass Axe
WCAG 2.2 AA checks. Actual server envelopes contain no fabricated invitation
transitions and expose no email, parent/invitation IDs, or names.

Chromium's offline emulation preserves established WebSockets. The scenario
therefore closes its forwarded real transport explicitly, rejects reconnects
while offline, and reconnects to the actual server afterward; it does not create
source rows or substitute frames. The test defaults to asserting Production
runtime in release CI. Local Demo verification explicitly sets
`STRATAAI_E2E_RUNTIME_MODE=demo` and is not retained-image Production evidence.

This native check exposed stale Demo creation names in non-Board discovery.
All bounded discovery candidates now use the existing canonical review after
the account routing scope closes, including current Organization name/status
and issuer account/membership authority. Board targets retain their additional
Board review. Two API regressions pass for Internal and Portal names and for
withdrawal after actual issuer membership removal. All 61 API cases selected
by `*invitation*` pass, including those two new regressions. The Release build
and browser typecheck pass with no errors. Candidate pagination and final
account/session reauthorization remain in place.

`tests/browser/recipient-invitation-issuer-authority.spec.ts` also passes in
local Demo at 1280px and 390px. An Owner establishes an administrator through
actual Internal invitation acceptance; that administrator issues the Portal
invitation. The Owner then removes the administrator through the actual member
command. The nonmember recipient's old parent name and acceptance control clear
before the held protected recovery read returns. Focus moves from the withdrawn
acceptance control to Refresh. The authoritative recovery page is empty, and
a stale direct acceptance returns `invalid_or_expired_invitation` without granting
Internal or Portal access. Actual socket envelopes contain only empty authority
resets, no invented invitation transitions, and no parent/issuer/invitation IDs,
email or parent name. Both widths pass Axe WCAG 2.2 AA and require only the
original document navigation. This uses ordinary HTTP and the real Demo socket;
Production retained-image execution is still pending CI.

## Runtime work required next

The implemented first producer attaches to future actual Organization metadata
sources for current parent names and issuer membership changes. Existing metadata
events retain original audit identity, actor, correlation, subject and subject
revision. Their current delivery capability already validates the revision
against the correct Organization, membership or invitation subject; these
different revision sequences must not be compared with one another.

Local adapter concurrency is verified below. Separate-process concurrent Worker
execution and retained-image confirmation remain required alongside these tests.

Production Board source publication and bounded delivery are implemented above.
Remaining dependencies include native Production Board-target
name/archive/member authority acceptance, Organization deletion/lifecycle and
issuer account withdrawal. Each remaining producer needs its actual canonical
source and appropriate bounded scope discovery. Historical committed sources remain deliverable after
later actor departure; current recipient discovery still owns present grant
admission. Legacy changes without a proven source must not acquire fabricated
actors or history.

Actual concurrent delivery/restart/large-population evidence remains unfinished.
Native Demo and Production nonmember/Portal two-client authority withdrawal,
disconnect recovery, disclosure clearing, keyboard/mobile and latency results
must also be verified against the retained exact release images before closure.

## Demo Board source and native recipient recovery

Demo now captures the seven supported Board authority transitions from actual
Board or Board-member mutations inside their owning command. Publication binds
that private command identity, current Board revision, original transition time,
subject revision and canonical Work event. Board and membership revisions are
validated separately. Administrator self-removal retains the prior role proof
while validating the resulting inactive membership. Raw legacy sources outside
commands create no authority history; owning unproven sources are refused.

The shared private journal participates in Work rollback as well as Organization
rollback. Actual Work source references, consumed proofs, recipient effects and
indexed 100-candidate page checkpoints all roll back with a late command failure.
An identical Work source is idempotent; a different event cannot consume the same
mutation proof, and another command cannot publish an earlier mutation.

All nine Board API-host cases pass: seven event types, actual administrator
self-removal, and rollback/proof refusal. Each HTTP case seeds 205 eligible
invitation records plus a future record, verifies cross-page recipient
invalidation and per-source recipient deduplication, excludes the future record,
and recovers an empty authority-bound reset without invitation transitions.
The rollback case additionally proves duplicate and earlier-command refusal.
The 69 invitation-filtered API cases and both existing directory replay cases
pass; Release builds have zero warnings/errors. Legacy replay fixtures explicitly
stay outside commands, and the owning rollback fixture uses a real Board update.

`tests/browser/recipient-invitation-board-authority.spec.ts` passes against the
local Demo API at 1280px and 390px. Three ordinary accounts establish membership
through real invitations and acceptance. A Board administrator issues an
invitation to a current Organization member without private Board access;
Board administrators cannot onboard outsiders. Actual rename, archive, restore
and issuer demotion drive protected recipient recovery. A held real recovery
read proves cached names and acceptance consent clear before the new response.
Restoration recovers the current invitation; issuer demotion removes it and
stale acceptance is rejected. Private Board access remains 404, Organization
membership remains valid, focus returns to Refresh, no document reload occurs,
wire envelopes contain no private fields or invented invitation transitions,
and accessibility/overflow checks pass. The scenario defaults to Production
in release CI; local evidence explicitly selects Demo.

These are actual local Demo HTTP/browser results, not retained-image Production
acceptance. The mandatory exact-image gates, concurrent authority delivery,
Organization lifecycle sources and issuer account withdrawal remain required.
PRD-04 and PRD-60 remain open.

## Concurrent restricted Board authority delivery

`InvitationBoardAuthorityConcurrencyContract` is part of the mandatory full
C# persistence executable. It executes two real Board updates through the
production Work store and owning unit of work, publishing each canonical source
through the production Work-event adapter. Actual account and initial invitation
records are disposable fixtures; the trusted transaction admission does not
claim HTTP cookie/session verification.

Two restricted Worker identities independently lease and deliver the source roots
concurrently, replay each committed root, and acknowledge through the normal
job store. Four identities then compete for the bounded continuation pages.
Under schema 107 and actual restricted API/Worker logins, the contract passes:
exactly six first-attempt successful jobs, 100/100/5 candidates per original
source, four source/email effects, exactly two increments for each of the two
recipients, no future-recipient revision, original actor/correlation/source
references, and unchanged unrelated queue/Work readiness. The optional
`--invitation-board-authority-concurrency-only` diagnostic runs this same
contract without reusing the fixed-ID discovery fixtures; mandatory CI retains
all existing source, graph, scale and provider contracts.

Release build passes with zero warnings/errors. This proves real concurrent
adapter/capability delivery against PostgreSQL, distinct from separate deployed
Worker processes, retained-image replay/restart and a latency benchmark. Those
release-level results and remaining canonical producers are still required.

## Retained activity fixture references

Repair-only CI run 37582973909 passed deletion graph and full scale processing,
then failed when the old activity fixture attempted to delete synthetic Work
history now referenced by authority sources. The forward FK correctly refused
that deletion. The fixture now owns a dedicated disposable Organization and
retains its sources, referenced graph, authority pages and jobs until database
teardown. Its shared actor profile is restored without deleting history or
disabling any production history trigger. Existing source, paging, audience,
privacy and rollback assertions remain unchanged.

The complete activity adapter/feed/Organization reader contract passes locally
under the restricted API login on schema 107. It additionally requires all 67
synthetic Board authority/source/page references and pending zero-attempt jobs
to remain, with all four history protection triggers enabled. These are explicitly
synthetic source fixtures for storage/query boundaries, not actual Board mutation
or recipient-delivery evidence. `--activity-source-only` is an optional diagnostic;
the mandatory full persistence executable still runs this contract and later
shared-scope contracts. Release build has zero warnings/errors; full current CI
must confirm the combined sequence before any release acceptance claim.

## Organization request and terminal authority sources

Migration 108 projects the actual `ORGANIZATION_DELETION_REQUESTED` audit and
canonical `ORGANIZATION_DELETED` lifecycle event into private, immutable,
forced-RLS source history. Each source references exactly one original record.
It publishes the existing typed authority root in the owning transaction;
the production Worker retains the existing 100-candidate leased page capability.
No HTTP recipient scan or historical backfill is introduced.

A private transition proof binds status, version, timestamp and transaction ID
to the actual ACTIVE → DELETING or DELETING → DELETED update. Unproven audits,
sources appended in a later transaction, invalid correlations and late failures
are refused or rolled back. Request attribution requires a current active Owner.
Terminal attribution retains the accepted actor even if their account has since
retired; the existing leased finalizer still proves request and descendant
completion. Restricted API and Worker logins cannot read either private table.

The mandatory `InvitationOrganizationLifecycleAuthorityContract` passes locally
against real PostgreSQL with restricted runtime logins. Actual production
adapters publish the request and process terminal completion. Each source drains
100/100/5 candidates, excluding a future invitation; duplicate delivery retains
exactly four source/email effects and two recipient revisions. Original actors,
correlations, canonical sources and lifecycle readiness remain intact. Initial
accounts/invitations and transaction admission are disposable fixtures, so this
is adapter evidence rather than HTTP session or retained-image acceptance.
`--invitation-organization-lifecycle-only` runs the same diagnostic separately.

Demo captures the real deletion-request mutation in its existing owning command
proof and journal. Both API-host cases pass: publication failure and final actor
refusal restore parent and recipient cursors; retry and duplicate acceptance
invalidate each eligible recipient once, exclude future invitations and recover
an empty reset without granting membership. Demo terminal processing remains
unfinished. Release-image lifecycle browser acceptance is still required.

Existing terminal delivery and deletion graph diagnostics pass with schema 108;
the tenant catalog audit and clean/repeat/forward/serialized/failure-rollback
migration checks also pass. Full CI for revision 89ba069a passed PostgreSQL,
including scale and retained activity history, but failed four Demo API cases.
The repair captures date-policy mutation proofs, admits ordinary Board editors
for `BOARD_UPDATED`, and checks a deletion administrator's retained membership
only after proving that the same actor deleted the Board. These checks preserve
the existing operation-specific access rules. Fresh complete CI is required.

All four formerly failing scenarios now pass in targeted checks: two date-policy
cases, both deletion-receipt variants, and the actual Linux Board image workflow.
The nine existing Demo Board authority cases also pass; builds have zero warnings
and errors. The documented Demo account's three startup/login checks pass.
These local repairs do not establish a complete green release pipeline.

## Native deletion-request recipient recovery

`recipient-invitation-lifecycle-authority.spec.ts` passes all four local Demo
browser cases: 1280px and 390px, connected and actual transport interruption.
An ordinary nonmember receives a genuine Portal invitation. An actual Owner
issues a deletion request and retries the same key. Holding the protected
recovery read demonstrates withdrawal of the cached Organization name and
acceptance controls before the response arrives. Reconnect recovers an empty
page; stale acceptance is rejected without creating Organization access. Focus
returns to Refresh, account access remains valid, no document reload occurs,
wire envelopes exclude private fields and invented invitation transitions, and
accessibility/overflow checks pass. The test defaults to Production in release
CI. This local Demo evidence covers request acceptance, not Demo terminal work.

The mandatory retained-image gate now invokes the lifecycle variant of
`test-invitation-recipient-authority-delivery.sh`: actual HTTP request, isolated
100/100/5 request delivery and restart, then automatic deletion discovery without
configured Organization IDs, real graph traversal, canonical Board and terminal
sources, nine first-attempt authority jobs, six deduplicated source/email effects,
three recipient revisions, original attribution and ready terminal completion.
A second restart must preserve these results. Shell syntax and browser fixture
typechecks pass; this new complete exact-image gate remains unexecuted until CI
reaches container integration. Existing architecture and release gates remain.
