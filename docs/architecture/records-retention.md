# Records retention contracts — PRD-70

[PRD-70](https://github.com/nickmailhot-lang/StrataAI2/issues/71) remains open.
Estimated work remaining: **82%**. Domain contracts, private policy storage,
transactional Demo parity and authorized policy HTTP commands/reads are implemented
and locally tested. The internal MUI policy create/edit/review and retry workflow
is implemented with component and scoped desktop/mobile browser coverage; remaining browser/release acceptance, runtime
holds, redaction, export and disposition workflows remain incomplete. This shared boundary is also required by
[PRD-20](https://github.com/nickmailhot-lang/StrataAI2/issues/21) and the remaining
[PRD-27 acceptance](prd-27-acceptance.md). Retained configuration history alone
cannot establish legal-hold behavior.

## Persistent producer identity foundation

[Migration 143](../../db/migrations/143_record_retention_sources.sql) introduces
`record_retention_sources` as an immutable, Organization-owned classification
identity. Its key is `(tenant_id, record_class, record_id)`; classification version
is one. Category and optional jurisdiction are supplied explicitly, without
automatic upgrade backfill, inferred jurisdiction or a legal retention default.
An indexed normalized category supports eventual class/category scope lookup.

The first supported producers are actual Cards and Card Comments in operational
work, Attachments in documents, and tenant-owned AuditEvents in audit. Generated
typed IDs and composite foreign keys require the real owning source and, for a
Comment or Attachment, its exact parent Card. A caller cannot substitute an
arbitrary UUID, another Organization's source, a system AuditEvent with no tenant,
or a generic Card for a Governance record. Physical deletion of a referenced
source is restricted; classification identity and provenance cannot be updated
or deleted. This does not prevent existing soft-delete paths or establish runtime
hold enforcement. Future disposition must retain referential meaning while
implementing its explicitly authorized deletion/anonymization behavior.

Registration locks the Organization, membership, account and actual producer,
checks active Owner/Admin provenance, rejects registration clocks predating the
current Organization/membership/account snapshot, and atomically appends a fixed audit fact.
The audit metadata contains only record class, producer kind and acting capacity;
category, jurisdiction, record content and original actor label are excluded from
that metadata. Forced RLS rejects foreign and unscoped reads. Neither API nor
Worker receives table or internal-function capabilities at this stage.

This schema is **unactivated foundation**, not a public registration API or a
confidentiality authorizer. Owning Application commands must authorize the
specific object before private source IO and retain/recheck that authority under
the shared transaction before activation. Demo classification parity, trustworthy
live record projections, hold schema/adapters, and all disposition paths remain
required. Governance, external correspondence and AI need their own real producer
identities and foreign keys; they are not replaced with ordinary Cards or Comments.
The six-class PRD scope remains unchanged.

The maintained [source contract check](../../scripts/ci/record-retention-source-after-upgrade.sql)
runs through the existing mandatory retained-image retention schema phase and the
ordered migration upgrade/repeat runner. Local disposable PostgreSQL executed all
143 migrations and the four real source registrations, immutable provenance,
substituted/missing/foreign/system sources, future clocks, physical source deletion
refusal, content-free audit rollback and forced tenant RLS. Canonical restricted
runtime role provisioning confirmed no source-table or routine capability escaped.
All 144 captured migration/contract file hashes matched. The full maintained
migration runner also passed clean installation, each incremental/repeat upgrade,
serialized execution, synthetic failure rollback and missing-ledger refusal, with
all 373 captured database/script hashes matching. Owned fixtures were removed.
This is local database evidence; exact-head hosted release evidence is still
required before acceptance.

## Production hold storage foundation

[Migration 144](../../db/migrations/144_record_retention_holds.sql) adds private
current holds, immutable hold history and immutable actor-bound command receipts.
Forced RLS applies to all three tables. Organization, class, category and exact
record scopes are represented explicitly; record scopes reference the typed
producer registry, and a supplied category must match that registered source.
The current-scope index retains Released rows as well as Active rows.

The storage transition is Active version one to Released version two, once.
Identity, scope, original reason, applying actor and application clock cannot be
rewritten. The release history retains that original evidence and the current
releasing actor's membership/version/capacity and attribution snapshot. Parent,
membership, account and source clocks are checked under current owning locks.
Deferred constraints require a complete current/history/receipt/audit graph;
missing history or receipt refuses the entire transaction. The original Apply
receipt continues to reference Active revision one after the head becomes
Released. Receipts have a fixed 24-hour expiry and retain their immutable
fingerprint and original result identity.

Audit facts use `RETENTION_HOLD_APPLIED` / `RETENTION_HOLD_RELEASED` and four safe
metadata values matching Demo: version, status, scope kind and acting capacity.
Reasons, category, record content and actor labels are not audit metadata. No API
or Worker table/routine grants are activated. Production adapters, real object
confidentiality authorization, owning HTTP/MUI workflows, realtime publication and
disposition enforcement remain outstanding; SQL graph proof does not establish
any of those runtime behaviors.

The [maintained hold storage contract](../../scripts/ci/record-retention-hold-after-upgrade.sql)
passed on disposable PostgreSQL after all 144 migrations. It exercises all four
scope shapes, persisted apply/release and original receipt identity, refused
scope/reason rewrites and reactivation, unknown/category-substituted records,
incomplete graph rollback, immutable history/receipts, safe audit facts, forced
tenant RLS and unactivated restricted capabilities. All captured SQL sources
matched. The full ordered migration runner passed with all 375 captured database
and script hashes matching, including upgrade/repeat, serialization, failure
rollback and missing-ledger refusal. Both maintained checks are required by the
existing build-once schema phase.

Hosted CI exposed a missing readiness requirement for migration 143. The compiled
connection factory now requires both 143 and 144 alongside every prior migration.
Fresh solution compilation passed with zero warnings/errors. A separate actual
compiled readiness contract passed using canonical restricted API and Worker
logins: all 144 ledger entries were individually hidden/refused, restored and
successfully recovered for both factories. All 1,933 captured repository hashes
matched at completion. Owned containers and ephemeral credential files were
removed. Full hosted CI for the resulting commit remains required; no ticket or
acceptance criterion is closed by this storage/readiness foundation.

## Owning PostgreSQL hold adapter

[PostgresRetentionHoldStore.cs](../../src/StrataAI.Infrastructure/Records/PostgresRetentionHoldStore.cs)
implements the owning hold store without application-wide registration. Every
read/write requires the existing Organization command scope and borrows that
transaction. Current reads join the current revision, whereas actor/key receipts
join their original immutable revision. Applicability reads include Released
heads, normalize ASCII categories consistently with Demo, and query at most 1,001
rows: exactly 1,000 can be complete; overflow stays incomplete and cannot authorize
disposition. Larger-scale complete processing remains required.

Writes validate the exact Domain transition, current parent/account/elevated
membership provenance, scope authorization, timestamps, receipt identity/expiry
and immutable source. A savepoint contains current/history/receipt/audit changes;
CAS/key conflicts and source constraint failures roll back that attempted graph.
No account, Organization, record scope, original application or audit privilege is
inferred from a supplied hold body. The owning Application service still performs
its admission, scope and final disclosure/write fences.

The maintained [native persistence contract](../../tests/StrataAI.Persistence.Contracts/RetentionHoldPersistenceContract.cs)
uses the actual adapter, existing PostgreSQL unit and Application commands against
a restricted API login. Its admission and scope probes are explicit fixture
contracts, not session or object confidentiality proof. Prospective table grants
exist only in that disposable fixture and are revoked in `finally`. PostgreSQL's
source row lock needs `UPDATE(version)` in addition to SELECT; a negative control
proves SELECT alone refuses the lock, while the immutable trigger still refuses
an actual update with that narrow privilege. Canonical provisioning and runtime
registration remain unchanged.

Fresh solution compilation passed with zero warnings/errors. The actual native
contract passed persisted apply/refresh/release, original Apply recovery after
release without reactivation, stale release, unknown typed source, foreign
Organization/actor receipt refusal, late scope withdrawal restoring the full
graph and original key, and unowned IO refusal. Independent committed counters
were two current heads, three history rows, three receipts and three hold audit
facts before the capacity fixture. Actual PostgreSQL lookup then passed exactly
1,000 matching heads as complete and 1,003 matching heads as a capped 1,001-row
incomplete result. The same executable passed all 144 individual ledger
refusal/restoration cases for API and Worker. All 1,935 captured repository hashes
matched at completion; owned containers and credential files were removed.

The contract is a mandatory source PostgreSQL CI phase. Coverage checks reject
omission, success substitution, skipping and ignored failures; all 207 maintained
integration/release-startup/browser-shard contract tests passed locally. Exact-head
hosted CI, actual record classification/confidentiality authorization and Demo
source parity, runtime activation, HTTP/MUI, domain delivery and disposition
integration remain outstanding. This adapter does not complete hold enforcement
or any PRD-70 acceptance criterion.

## Record authorization required before hold activation

The current hold scope contains only Organization, class, category and record ID.
Migration 145 adds trusted historical Board context to the producer registry and
hold storage, alongside the exact child Card relationship. These identify a record;
they do not prove
permission to its historical private classification, hold reason or acknowledgment
after a Card moves. The native store's scope probes do not resolve this gap.

The existing [attachment receipt admission](../../src/StrataAI.Application/WorkManagement/AttachmentAdmission.cs)
provides the relevant owning pattern: plan original and current Board gates,
acquire them in stable order, and re-read authoritative relationships before
disclosing the immutable acknowledgment. Hold/source authorization must preserve
that boundary rather than treating access to the current destination as permission
to all original private history.

Before registering a real resolver and enabling either runtime, implement:

- Trusted, persisted authorization context for private classification and record
  hold application. Capture the relevant original source/parent Board identities
  from the admitted producer, not request fields or free-form audit metadata.
  Retain their tenant-safe referential meaning across moves and archive.
- Minimal private reference lookup before reading classification details, hold
  reasons or receipt result bodies. Under the existing Organization transaction,
  authorize the original classification/application gates and the current producer
  gates, then re-resolve after waits and before disclosure/commit. Roles, governance
  titles, a UUID match and the scope completeness flag are not this permission.
- Explicit producer admission for Card, Comment and Attachment, including exact
  current child/parent identity and archive/deletion states. Audit records need
  the originating module's retained authorization context; an arbitrary AuditEvent
  or its safe metadata cannot grant access to a protected originating object.
  Governance, external Correspondence and AI require their real owning producers.
- An authoritative producer version/context for human review and a stable conflict
  result if that evidence changes. Original command recovery must retain its
  captured context while rechecking current actor/session and current source
  authority; it must not silently adopt a destination's permissions.
- Demo/Production parity and executed original/current Board move, archive,
  confidentiality, permission withdrawal, concurrent source change, historical
  attribution and lost-response retry cases. Preserve independent Portal admission
  and explicitly scoped system/offboarding permissions.

These are implementation prerequisites, not an approved scope reduction. Do not
fabricate historical gates for legacy rows from their current Board. Existing
unactivated foundation rows without sufficient trusted context need a defined,
authorized recovery/migration contract or must refuse disclosure. No permissive
scope resolver or new public capability is introduced by this review.

### Persisted historical authorization context

[Migration 145](../../db/migrations/145_record_retention_authorization_context.sql)
captures the actual classification Board and source revision for new Card,
Comment and Attachment classifications. For children, the Board comes from the
exact tenant-safe parent Card relationship. A record hold retains that original
classification Board plus the actual Board and source revision at application;
both history revisions copy the same application context. Subsequent moves and
release cannot rewrite those identities. Composite Board foreign keys preserve
Organization ownership. Callers cannot supply the captured fields.

Existing classification, hold and history rows remain explicitly unstamped.
Audit producers also remain unstamped until their originating module provides
trusted authorization evidence. The migration never guesses original permissions
from a record's current Board. All context fields must be absent together or form
the complete versioned shape; private Board identities are excluded from audit
metadata. Runtime grants and service registration remain unchanged.

The maintained migration runner seeds a pre-upgrade Card classification and hold,
moves the Card, then verifies that upgrade leaves missing evidence explicit. The
[post-upgrade contract](../../scripts/ci/retention-context-after-upgrade.sql)
checks server capture, moves before application and after application, immutable
history and context rewrite refusal. It also runs in the retained-image schema
phase. The fresh native executable passed all 145 individual ledger refusal and
restoration cases for both restricted hosts and the actual owning hold persistence
contract; all 1,938 captured repository files matched at completion. This verifies
storage compatibility, not original/current object authorization or review.

The full maintained migration runner passed clean/repeat/forward upgrade,
serialized execution and failure rollback with 378 captured database/script files
unchanged. A separate fully migrated PostgreSQL fixture passed the maintained
source, hold and authorization-context contracts, including both child producer
kinds and their exact parent Boards before and after moves; all 148 captured SQL
files matched. The 207 integration, release-startup and browser-shard coverage
tests also passed. Owned test containers were removed. PRD-70 remains open at
82% estimated work remaining; scope resolution, Demo source parity, review,
enforcement, redaction, exports, disposition and offboarding still require work.

### Private authorization reference lookup

[RetentionAuthorizationReferences.cs](../../src/StrataAI.Application/Records/RetentionAuthorizationReferences.cs)
defines owning-transaction reference hints for classification, current hold and
actor-bound original receipt history. The
[PostgreSQL reader](../../src/StrataAI.Infrastructure/Records/PostgresRetentionAuthorizationReferences.cs)
selects only typed identities, scope shape, version and retained Board context.
It excludes category values, jurisdiction, classification provenance, hold reasons,
actor labels, receipt fingerprints and result bodies. Receipt hints join the exact
actor/key/history version, so an original Apply hint remains version 1 after release
while the current hold hint is version 2. Missing context remains explicitly null.

Every query requires the existing owning Organization transaction and uses an
explicit tenant predicate under forced RLS. Unknown/wrong-class/foreign source IDs,
foreign hold IDs and another actor's receipt yield no reference. These hints grant
no permissions: the resolver must authorize original classification/application
and current producer gates, re-read relationships after waits, then authorize
private detail reads. The reader is unregistered and canonical runtime privileges
are unchanged; Demo parity and integration with command/detail reads remain pending.

The maintained native hold contract now executes the actual reference reader with
the restricted API login. It passed all four producer kinds, exact child parents,
original context after Card moves, fully stamped hold/application context,
explicit unresolved Audit context, current/original history separation, wrong-class,
unknown, cross-Organization, foreign-actor and unowned-IO refusals. Fresh solution
build passed with zero warnings/errors; the complete native hold and 145-entry
readiness contract passed for both restricted hosts with all 1,940 captured
repository files unchanged. Owned fixtures were removed. This is reference storage
evidence, not a completed confidentiality resolver or hold enforcement acceptance.

### Work producer authorization

[RetentionWorkAuthorization.cs](../../src/StrataAI.Application/Records/RetentionWorkAuthorization.cs)
adds owning authorization for the three existing work producers. It first checks
current actor admission and active Organization Owner/Admin membership. It resolves
only typed private references, plans classification/application/current Board gates
in distinct UUID order, acquires the existing Board read-scope locks, and uses the
existing Board permission implementation for view and administer decisions.
Retained archived Boards, Cards, Lists and Attachments remain available to their
current administrator; deleted or unsupported producer states refuse admission.
The reader selects only current parent/List/Board relationships, versions and
lifecycle states, without titles, comment content or Attachment URLs/object keys.
After the gates, the resolver re-reads both classification and current source and
refuses changed parent, route, version or lifecycle evidence, then rechecks actor
and Organization authority.

Record hold authorization retains the application Board even when a later move
returns the source to its classification Board. This work resolver does not grant
broad Organization/class/category retention administration or infer Audit origins
from safe metadata. Governance, Correspondence and AI still require real owning
modules; missing historical context remains a refusal. Callers must re-read the
exact current/original receipt reference before private body disclosure and commit.
The resolver remains unregistered. It now implements the hold command admission
interface, but does not establish Demo parity, session/Portal admission, human review or
disposition enforcement. Dedicated broad-scope admission and authorized recovery
for missing/deleted legacy evidence remain required; no scope waiver is implied.

The maintained native contract passed actual PostgreSQL source relationship reads,
existing Board permission code and owning locks for Card, Comment and Attachment,
sorted original/current gates, retained application gates after another move, and
archived source/Board access. Negative controls passed nonmember and unresolved
Audit refusal, separate historical/current/application gate denial, changed revision
refusal and rejection of broad-scope substitution. Gate denial and changed-version
controls are explicit decorators around the real adapters; session admission remains
a fixture probe. Fresh whole-solution build passed with zero warnings/errors. The
complete native hold contract and all 145 ledger refusal/restoration cases passed
for both restricted hosts with all 1,941 captured repository hashes unchanged.
Owned disposable fixtures were removed. PRD-70 remains open with approximately
82% of its full work remaining.

### Reference admission before private command reads

The hold command/store contracts now distinguish minimal current/original receipt
references from private bodies. Read, release and receipt recovery authorize the
retained reference before loading reasons, category values or acknowledgments.
The body must match its reference's Organization, hold identity, exact version,
class, record identity and category scope shape. The reference is re-read and
authorization rechecked before acknowledgment. Successful writes additionally
re-read the new authoritative reference/body under admission before the owning
transaction may commit; denial or a mismatched acknowledgment rolls back the graph.
Original Apply recovery still reads the original receipt version after release.

The Production store uses the persisted context reader. The actual work resolver
implements the required reference and source-scope admission methods, and the
maintained native contract now injects it into the actual hold commands and store.
Demo reference reads preserve exact history/version and explicitly absent context;
they never manufacture it from current routes. Demo work-source capture is still
required before the real resolver can authorize Demo record holds. Broad and
other producer admission, runtime registration, HTTP/MUI and enforcement remain
pending; fixture probes for these incomplete scopes grant no deployed capability.

Verification: fresh solution build passed with zero warnings/errors. The focused
Linux API Records suite passed 31/31, no failed or unexecuted cases, with 844 backend
and test hashes unchanged. New maintained cases prove reference denial precedes
private body reads, retries and stale release conflicts, and changed references
refuse acknowledgment. The complete actual native hold/readiness contract passed
all 145 migration refusal/restoration cases for both restricted hosts, with 1,941
repository hashes unchanged. Native real-admission commands passed archived-source
read/release and original Apply recovery; an application-Board denial control
produced safe not-found outcomes with zero private reads and zero writes for read,
stale release and retry. Owned terminal fixtures were removed. PRD-70 remains open
at 82% estimated work remaining; this is command ordering and owning persistence
evidence, not full HTTP/session/privacy or immutable-image release acceptance.

### Trusted Demo source capture

[InMemoryRetentionSourceStore.cs](../../src/StrataAI.Infrastructure/Records/InMemoryRetentionSourceStore.cs)
adds immutable Demo classification/reference storage for the three real work
producers. It reads minimal parent/Board/List identities, versions and lifecycle
from the actual Demo Card, Comment and Attachment dictionaries under the owning
Organization transaction. Category and optional jurisdiction are explicit values.
Original Board and source revision are captured from those admitted producers;
they are not accepted from a caller or reconstructed after a move. Captured
classification provenance retains the actor label and membership/version/capacity.

Capture requires current active account, active Organization Owner/Admin, exact
membership provenance, valid UTC source/registration clocks, reviewed source/Card
versions and existing Board view/administer permission. It re-reads producer
identity/version/lifecycle after the gate and rechecks actor admission. Unsupported
origin kinds, unknown/foreign sources, invalid provenance or stale revisions refuse
capture. Existing classification identities cannot be rewritten. The shared work
resolver now depends only on the source-reference reader, so it can use either
storage adapter without needing private hold-body access.

Classification and the typed three-field source audit fact participate in the
existing Demo Organization rollback. Audit metadata contains only record class,
source kind and acting capacity, excluding category, jurisdiction, Board IDs and
source content. The source store is unregistered; it is a storage primitive, not
the public review/idempotency workflow. Human review/idempotent classification
commands, broad and remaining producer admission, runtime activation and full
deployed acceptance still require work.

Verification: fresh solution compilation passed with zero warnings/errors. The
focused Linux Records API suite passed 32/32, no failed or unexecuted cases, with
847 backend/test hashes unchanged. Its maintained Demo source case uses actual
producer dictionaries and Board permissions for all three kinds; verifies original
classification context/current route after a move, exact parents, immutable
classification, forged membership/stale revision/unsupported origin refusal,
wrong-class/unknown reference refusal, safe metadata and atomic classification/audit
rollback. The complete native PostgreSQL hold/readiness contract also passed all
145 migration refusal/restoration cases for two restricted hosts with 1,944 captured
repository hashes unchanged. Owned fixtures were removed. PRD-70 stays open at
82% estimated work remaining; these checks do not establish full Demo/Production
workflow parity, HTTP/session acceptance or immutable-image release completion.

### Demo hold application and history context

The Demo hold store now requires its actual source catalog. New record holds must
reference a classified source of the exact class and ID; any supplied category must
match the retained classification, and application cannot precede registration.
There is no placeholder-record or guessed-current-Board fallback. Tests that formerly
used unbound document IDs now create and classify actual Attachment producers.

At application, the store captures the classification Board and the actual current
application Board/source revision. Each immutable history revision retains that
reference. Release copies the existing context while advancing only the hold
version, and actor/key receipt reference lookup selects the exact original history
revision. Moves and archive do not recapture the application Board or change an
original acknowledgment. Context rollback uses the same history/header/receipt
participants as the hold graph, so a failed final reference fence reclaims the key.
Broad holds have no invented producer context; their separate administration
contract and other module origins remain pending.

The maintained [Demo application-context test](../../tests/StrataAI.Api.Tests/RetentionHoldContextDemoTests.cs)
injects the actual work authorization, source catalog and hold store in fixture-local
DI. It applies holds to all three real producer kinds after a parent move, moves
again, archives retained sources/Boards, releases and recovers original receipts.
Both history versions retain classification/application Boards and the actual
application producer revision. It also proves category mismatch refusal and a final
reference denial restoring history/header/receipt context and the original key.
The denial is an explicit decorator; this is not HTTP/session withdrawal proof.

Verification: fresh solution build passed with zero warnings/errors. Focused Linux
Records API tests passed 33/33, no failed or unexecuted cases, with 848 backend/test
hashes unchanged. Complete native PostgreSQL hold/readiness verification passed all
145 ledger refusal/restoration cases for both restricted hosts with all 1,945
captured repository files unchanged. Owned fixtures were removed. These components
remain unregistered in deployed runtime. PRD-70 stays open at approximately 81%
estimated work remaining: review/idempotent classification, broad/other producer
admission, full permission/Portal/session matrices, HTTP/MUI, event and disposition
enforcement, controlled redaction, export/privacy and offboarding still require
implementation and acceptance evidence. Full immutable-image CI remains required.

### Evidence before first classification

The shared `IRetentionProducerReferences` contract reads minimal identities,
versions, lifecycle and creation time from actual Card, Comment and Attachment
producers before a classification exists. Demo uses its actual work dictionaries;
the unregistered PostgreSQL adapter uses typed tenant-safe parent/List joins inside
an owning transaction. Neither reader invents a classification, category,
jurisdiction or historical Board. This evidence is backend-private: review must
authorize the actual owning Board and re-read after acquiring its gates before
disclosing source evidence or accepting classification.

Maintained Demo and native PostgreSQL tests prove an actual unclassified Card can
be read while classified-source lookup remains empty; all three producer kinds
retain current routes and versions after a move. Wrong-kind, unknown, unsupported,
foreign-tenant and unowned-transaction lookups are refused. No runtime registration,
HTTP projection or new database grant is introduced by this reader.

Verification: fresh solution build passed with zero warnings/errors. Focused Linux
Records tests passed 33/33 with 849 backend/test hashes unchanged. Native PostgreSQL
hold and readiness checks passed all 145 ledger cases for both restricted hosts,
with 1,946 repository hashes unchanged. Owned fixtures were removed. PRD-70 remains
open at approximately 81% estimated work remaining; this is prerequisite evidence
for the outstanding review/idempotent classification workflow, not its acceptance.
Hosted immutable-image CI must still complete for the committed revision.

### Admission of first-classification review evidence

The unregistered `RetentionProducerAuthorization` checks current account admission,
active Organization Owner/Admin membership, exact supported producer identity and
the actual Board's view/administer permission. It acquires the existing Board read
scope, accepts retained active/archived sources, then re-reads the exact producer
tuple and actor admission. A move, revision, parent or lifecycle change requires a
new review instead of silently adopting a different Board. Callers still must retain
this evidence as their write precondition and repeat authorization before private
disclosure and commit; no HTTP or classification command is activated here.

Maintained Demo checks exercise all three actual producers before classification,
refuse nonmembers and malformed/revised evidence, then reject old review evidence
after a move and accept its new authorized route. Native PostgreSQL checks use the
actual restricted reader and existing Board permission service for an unclassified
Card and all three kinds, including archived evidence. Explicit denial/race
decorators prove Board refusal and a changed revision after admission remain
failures; they do not establish real HTTP session withdrawal acceptance.

Verification: fresh full solution build passed with zero warnings/errors; focused
Linux Records tests passed 33/33 with 850 backend/test hashes unchanged. Native
PostgreSQL hold/readiness verification passed all 145 ledger cases for both
restricted hosts with 1,947 repository hashes unchanged. Owned fixtures were
removed. PRD-70 remains open at approximately 81% estimated work remaining;
classification review/retry commands and full deployed acceptance remain pending.

### Shared reviewed classification storage

`IRetentionSourceStore` and `RetentionSourceWrite` now provide the same capture
contract to Demo and PostgreSQL. The write retains the full reviewed producer
tuple, rather than accepting only source/Card versions. Demo refuses a changed
Board, List revision or any other reviewed field before its existing atomic audit
capture. Category and optional jurisdiction remain explicit classifications.

The unregistered `PostgresRetentionSourceStore` requires an owning transaction,
the actual current Board admission, exact active membership/version/capacity,
active account and valid registration clocks. It inserts the typed source with
server-derived actor label and parent. Existing database triggers capture trusted
context and the content-free audit. The adapter checks the persisted reference
against the review and repeats admission after insertion. Its savepoint rolls back
both classification and audit if the final review fence fails; the outer owning
transaction also restores them on command failure. Existing classifications cannot
be rewritten. Canonical runtime grants, DI and HTTP activation remain unchanged.

Maintained native checks classify actual Card, Comment and Attachment producers
under the restricted API login, including retained archived Board context. They
refuse forged membership/route/kind, verify immutable identities, owning rollback,
post-insert review withdrawal, reused source/audit identities after rollback and
exact three-field audit privacy. The post-insert race is an explicit reader
decorator, not a deployed HTTP/session race. Prospective INSERT privileges exist
only in the disposable contract and are revoked on cleanup.

Verification: full solution build passed with zero warnings/errors; focused Linux
Records tests passed 33/33 with 852 backend/test hashes unchanged. Native hold/source
and readiness checks passed all 145 ledger cases for both restricted hosts with
1,949 repository hashes unchanged. Owned fixtures were removed. PRD-70 remains
open at approximately 80% estimated work remaining. Human review/retry commands,
classification receipts, broad and other producer admission, HTTP/MUI and full
permission/session/Portal matrices, events/disposition, controlled redaction,
exports/privacy/offboarding and exact-commit immutable-image CI remain pending.

### Owning first-classification review command

The unregistered `RetentionSourceReviewService` now opens the actual Organization
unit of work for first-classification review. It checks current actor and active
Owner/Admin membership before reading a supported real producer, admits the exact
Board through `RetentionProducerAuthorization`, and refuses an existing
classification. Before returning, it repeats producer, classification and account
admission checks. Its result is backend-private evidence for a later write
precondition, not a bearer grant or permission to disclose private bodies.
Unsupported Audit origins, wrong kinds, unknown sources and changed evidence
return a generic unavailable result. The command writes no classification or audit.

Maintained Demo and PostgreSQL checks review real Card, Comment and Attachment
sources, use the returned evidence for classification, and refuse a second
first-classification review afterward. An explicit reader decorator changes the
source after its initial Board gate and proves no review escapes that fence.
Demo additionally checks nonmember, wrong-kind and unsupported/unknown refusal,
and absence of classification audit before the actual capture.

Verification: the fresh full solution build passed with zero warnings/errors;
focused Linux Records API tests passed **34/34** with **854** captured hashes
unchanged. The complete native hold/source contract and all **145** migration
readiness checks passed for both restricted hosts with **1,951** repository hashes
unchanged. Owned fixtures were removed and original PostgreSQL preserved. This
supports the review prerequisite; durable classification receipts, retry commands,
HTTP/MUI human review, runtime grants/DI, broad and other producer admission and the
full PRD acceptance remain pending. PRD-70 stays open at approximately **80% work
remaining**. Current immutable-image CI must still complete.

## Policy and decision semantics

[RecordRetention.cs](../../src/StrataAI.Domain/Records/RecordRetention.cs) defines
Organization-specific policy values for operational work, governance, audit,
correspondence, document and AI records. Each policy has an explicit record
class/category, optional jurisdiction, retention anchor, elapsed UTC days,
disposition action and source/notes. Category keys are 1–64 ASCII letters,
digits, underscores, dots or hyphens; matching is ordinal and case insensitive.
Jurisdiction strings are explicit user-supplied values, matched ordinally without
case. Null jurisdiction means applicability within the owning Organization
without a jurisdiction restriction. It does not mean cross-Organization scope.
No record-class or jurisdiction retention duration is built into the code.

Anchors are creation, archive, closure or approval. A missing configured anchor
preserves the record instead of borrowing creation time. Retention days are
explicit integers from zero to 3,652,500; they represent elapsed UTC days rather
than calendar years/months or a legal rule. Null days require Preserve and mean
indefinite preservation. Explicit Preserve always denies disposition. Delete
and Anonymize remain separate actions; their downstream storage/privacy effects
are unimplemented. Unrepresentable deadlines fail closed. Eligibility starts
at the exact explicit deadline, after the entire configured period.

An active hold can cover the whole Organization, one record class, one category
within a class, or one record ID within a class. Category/record scope requires
a class to prevent cross-kind ID ambiguity. A hold has no automatic expiration.
Release requires a positive new revision, release actor and UTC release clock
at or after application; original application actor/reason/time stay present.
The eventual command must persist immutable transitions and audit evidence.
The Domain model alone does not make those writes auditable.

[RetentionHoldLifecycle.cs](../../src/StrataAI.Domain/Records/RetentionHoldLifecycle.cs)
now defines explicit application and release transitions. Application creates an
Active version-one hold with the supplied Organization/class/category/record scope,
reason and application actor/UTC clock. It does not infer a scope or expiration.
Release accepts only the current owning Organization and exact positive revision,
valid source and release actor/UTC clock at or after application. It increments
that revision while preserving identity, scope, original reason, application actor
and clock. Stale releases, foreign/invalid/future sources, revision overflow and
another release of an already released hold return no replacement state. A future
owning command must handle exact receipt recovery independently, rather than
creating a new release revision for a retried acknowledgment.

Disposition now refuses duplicate hold IDs, including two revisions of one hold,
before evaluating any action. A claimed complete scope cannot substitute a mixed
history for unique current holds. Owning hold persistence, scope authorization,
typed record references, locks, immutable audit/receipts, runtime enforcement and
human review remain pending; these Domain transitions provide no permissions.
Existing Cards have no retained category or jurisdiction classification contract,
so their owning retention projection must be designed and persisted before record
selection can rely on it. No generic Card lifecycle is used for governance records.

Fresh solution compilation passed with zero warnings/errors. Maintained
[hold lifecycle tests](../../tests/StrataAI.Domain.Tests/RetentionHoldLifecycleTests.cs)
cover four scope shapes, invalid identity/scope/reason/clock, immutable application
evidence, exact/stale/repeated release, foreign/future/overflow sources and duplicate
hold corruption. The focused Linux Records suite passed **49/49**, including eight
new cases; the complete Linux Domain suite passed **992/992**, zero failures or
unexecuted cases. Both captures matched **833 backend/test source hashes**, zero
mismatches, and owned terminal containers were removed. This is Domain verification,
not persistence, authorized runtime enforcement or immutable-image CI acceptance.
PRD-70 remains open at **83% estimated work remaining**.

### Owning hold command contract

[RetentionHoldService.cs](../../src/StrataAI.Application/Records/RetentionHoldService.cs)
defines apply, release and private read commands inside the existing owning
Organization unit of work. Parent/account/current Owner or Admin admission
precedes input validation or private store I/O. A required scope authorizer must
resolve the typed owning record and its confidentiality under that transaction;
an elevated Organization role cannot substitute for that decision. Apply checks
the scope before receipt lookup. Reads, release and receipt recovery verify the
retained scope; private scope denial also precedes release revision conflicts.
Source identity, clocks, state and original evidence are validated before use.

Actor-bound 24-hour receipts retain the exact original acknowledgment. Apply and
release have distinct canonical fingerprints; changing the reviewed request cannot
reuse its key. An apply acknowledgment can be recovered after release without
reactivating the hold. Release recovery also cross-checks original application
evidence and the exact retained release against the current head. Normal release
uses compare-and-swap and preserves original application data. Write intent carries
the authenticated actor, current membership/version/capacity, fresh event ID,
correlation and exact receipt. Scope authority is rechecked around writing, and
parent/account authority is checked before returning success. The owning unit must
roll back the entire immutable graph when a final fence refuses success.

This service is deliberately **unregistered** until Production/Demo atomic hold
adapters and actual typed scope authorization exist. The interfaces are contracts,
not proof of persistence or audit events. No permissive scope resolver, HTTP/Worker
capability, disposal side effect or usable hold enforcement is introduced. Runtime
adapters, database integrity, audit/publication, complete current hold lookup,
review/recovery UI and the rest of PRD-70 remain required before closure.

Verification: fresh whole-solution build passed with zero warnings/errors. The
focused Linux API Records suite passed **23/23**, including five new maintained
[hold command tests](../../tests/StrataAI.Api.Tests/RetentionHoldCommandTests.cs).
These use the actual owning Organization unit and contract probes for hold storage
and scope authorization. They establish original apply/release recovery, immutable
application evidence, distinct write/audit intent, request/key conflicts and expiry,
admission before private I/O, denial before protected revision conflicts, corrupt
or future source refusal and final scope/account fences. They do **not** prove
runtime scope resolution, adapter rollback, database persistence or audit emission.
The capture matched **835 backend/test source hashes**, zero mismatches, and its
owned terminal container was removed. Main CI/image acceptance remains pending.
Estimated PRD-70 work remaining remains **83%**.

### Demo hold persistence and rollback

[InMemoryRetentionHoldStore.cs](../../src/StrataAI.Infrastructure/Records/InMemoryRetentionHoldStore.cs)
implements current hold state, immutable transition history with retained actor
label/membership/capacity, actor-bound original receipts and event identities. All
calls require the owning Organization transaction. Writes recheck active account,
parent and current elevated membership, typed scope authorization and the exact
Domain transition. Release cannot rewrite original application evidence. Supplied
membership, event, correlation, receipt and microsecond clocks are validated before
the first change. The adapter participates in the same rollback snapshot as Demo
audit facts, so an unsuccessful final fence or cancellation must restore all four
parts of the graph and leave retry keys available.

Demo audit now supports typed hold metadata containing only version, status, broad
scope kind and acting capacity. Apply/release event types must match that status;
reason, category and record reference are excluded. Existing policy metadata stays
separate. These are internal immutable audit facts, not authorized realtime
publication or an API exposure of private holds.

The adapter is registered only in maintained test fixtures with a scope contract
probe. Application-wide registration, real typed owning record authorization,
PostgreSQL schema/adapter parity and runtime enforcement remain pending. This
fixture registration establishes actual Demo persistence and rollback, while its
scope probe does not establish confidentiality or typed record reference integrity
for production. PRD-70 remains open at **82% estimated work remaining**.

Fresh solution compilation passed with zero warnings/errors. The focused Linux API
Records suite passed **27/27**, including four new maintained
[Demo hold tests](../../tests/StrataAI.Api.Tests/RetentionHoldDemoTests.cs). Actual
storage and owning rollback participation establish apply/release refresh, original
receipt recovery, immutable history/attribution, safe audit facts, serialized
concurrent release, rejection of rewritten/forged source evidence, and cancellation
or scope withdrawal restoring current state/history/receipts/audit. The capture
matched **837 backend/test source hashes**, zero mismatches, and its owned terminal
container was removed. The full Linux API regression for the shared audit change
has also completed: **869/869** cases passed, zero failed, unexecuted, aborted or
timed-out cases. That capture matched the same **837 backend/test source hashes**
with zero mismatches, and its owned terminal container was removed. Both captures
used freshly compiled code in a pinned runtime container; they do not establish
immutable release image or rigorous CI acceptance. Production hold parity and
runtime activation remain incomplete.

### Current hold applicability and completeness

`IRetentionHoldStore.ReadApplicableAsync` now accepts a trusted owning record
projection and returns [RetentionHoldCandidates](../../src/StrataAI.Application/Records/RetentionHoldCandidates.cs).
The Demo adapter requires the existing owning Organization transaction before
validating that projection or reading private rows. It rejects invalid identities,
classification and future record clocks. Matching includes the same Organization's
whole-Organization, record class, case-insensitive category and record-ID scopes;
unrelated tenant/class/category/record holds are excluded. Current Released rows
remain in the result so their source evidence can be validated; historical Active
revisions are never substituted for a current release.

The result retains the full record snapshot and an immutable item collection. Its
evaluation helper refuses a changed record before applying selection-aware Domain
disposition checks. A synchronous scope with at most 1,000 matches is complete;
the 1,001st row proves overflow and keeps `ScopeComplete` false. Evaluation of that
result preserves the record with `IncompleteHoldScope`, never treating a truncated
page as permission to dispose. This is a synchronous safety bound, **not a hold
quota**: storage accepts additional holds. A scalable complete reader for larger
scopes and recoverable disposition processing still require implementation.

The input projection and completeness result are private storage contracts, not
HTTP input, record authorization or a grant to a Worker. The eventual owning
resolver must authorize the actual record/confidentiality, establish its current
classification, and re-read current record/policy/hold sources under locks through
disposition. PostgreSQL hold schema/adapter parity and runtime enforcement remain
pending. The earlier 869-case API capture predates this lookup change and is not
proof of its runtime integration. Estimated PRD-70 work remaining remains **82%**.

Verification: fresh solution compilation passed with zero warnings/errors. The
focused Linux API Records suite passed **29/29**, including two new maintained
[hold lookup tests](../../tests/StrataAI.Api.Tests/RetentionHoldLookupTests.cs).
They use actual Demo writes/transactions to verify all four matching scope shapes,
unrelated tenant/class/category/record exclusion, current release evidence,
unowned/foreign/invalid/future/canceled reads, record snapshot changes, empty scope
and the exact 1,000/1,001 completeness boundary. The overflow case persists 1,001
valid holds within one owning transaction and proves evaluation refuses disposition.
The capture matched **839 backend/test source hashes**, zero mismatches, and its
owned terminal fixture was removed. Typed scope authorization remains a contract
probe in these fixtures; this is not actual record authorization, Production
persistence or immutable release image acceptance.

The disposition decision refuses invalid or foreign policy/record/hold sources,
incomplete hold lookup, immutable records, matching active holds, missing
anchors and pending/overflowing deadlines. It accepts a server-owned
`holdScopeComplete` flag: the future owning reader must establish completeness;
HTTP callers must never supply that assertion. Same-Organization unrelated
holds have no effect. Governance records cannot match an operational Card
policy. An explicit immutable flag preserves records even with a due destructive
policy. That flag must come from the authoritative record's current domain
state, never from a request to dispose it.

## Canonical entity definitions and persistence

Migration [142](../../db/migrations/142_record_retention_policies.sql) implements
private policy headers, immutable revisions and retry receipts. Hold persistence
and owning hold adapters remain pending. The table below describes the broader
contracts; planned hold references must be implemented before hold enforcement.

| Contract | Identity and ownership | Version/lifecycle and retained meaning |
| --- | --- | --- |
| RecordRetentionPolicy | Nonempty policy ID and Organization ID; one Organization has many policies, one policy has many immutable revisions. Persisted revision key `(tenant_id, policy_id, version)` and Organization FK with restrictive deletion. | Positive version, UTC creation/update clocks, update at or after creation. Changes append revisions; the header selects the current revision. Disposition must bind the exact policy revision. Policy retirement must preserve referenced revisions. |
| RetentionHold | Nonempty hold ID and Organization ID; one Organization has many holds. Planned header key `(tenant_id, hold_id)` and immutable transition key `(tenant_id, hold_id, version)`, with restrictive Organization/reference deletion. | Positive version; Active has no release metadata, Released requires version at least two and complete release provenance. Hold transitions require compare-and-swap versioning, idempotent receipts and append-only audit. Release cannot remove prior application evidence. |
| RetentionRecord | Read-only source projection of an existing record ID, Organization, version, class/category, jurisdiction, domain clocks and immutability. | No new independent persisted record lifecycle. Owning record adapters retain object confidentiality, current lifecycle, approved-governance immutability and archive/history references. |
| RecordDispositionDecision | Ephemeral derived result from a policy revision, record snapshot and complete current holds. | Not an audit or authorization source. The eventual transaction must recheck authority, record/policy revisions and holds through disposition and persist its own authoritative audit/result. |

Policy required values are class/category, anchor, action, source and positive
version. Jurisdiction, retention days and notes are optional as defined above.
Source is nonblank trimmed text up to 1,000 UTF-16 units; notes allow up to 2,000
and jurisdiction 120. Source/notes allow internal line breaks and tabs but no
other controls. Optional text is null or nonblank. Hold reason is required,
nonblank trimmed multiline text up to 2,000 units. Application actor/time are
required; release actor/time are nullable only for Active holds. Each supplied
actor ID must be nonempty. The future schema must retain restrictive actor FKs
and immutable event attribution after account/membership deactivation; labels
and acting capacity belong to the historical audit/publication snapshot rather
than a current membership join.

The persisted current policy namespace enforces one retained identity per
Organization/class/normalized category/jurisdiction scope, including the null
jurisdiction scope. A policy cannot move namespaces. The future owning selector must resolve a specific jurisdiction
and Organization-wide applicability deterministically; it may not silently
choose the shortest retention period. The domain selection contract below now
defines explicit choice and stale-source refusal; owning record readers,
selection review/recovery and policy retirement still require implementation.
Planned indexes support that
namespace and hold admission by `(tenant_id, status, record_class, category,
record_id)`; immutable version histories need descending version pagination.
Record-specific holds require tenant-safe typed owning-record references,
retained after operational archive or supersession. Broad scopes use nullable
class/category/record selectors as documented above. Entity records and sources
are private; forced RLS, tenant-safe FKs and current confidentiality guards are
required at persistence and HTTP boundaries.

## Policy storage and integrity

### Complete policy selection and revalidation

[RetentionPolicySelection.cs](../../src/StrataAI.Domain/Records/RetentionPolicySelection.cs)
adds a domain selection result bound to the full owning record snapshot. A trusted
reader must establish the complete current class/category scope containing the
Organization-wide policy and, where applicable, the matching jurisdiction policy.
The existing unique namespace permits at most two matching identities. A paged
directory or a caller-supplied assertion is not proof of completeness. Invalid,
foreign, unrelated, duplicate or future sources are refused; no matching rule
preserves the record.

A single current applicable rule can be selected without inventing another rule.
When both Organization-wide and jurisdiction applicability match, selection
requires an explicit tenant/policy/version reference. It does not prefer the
specific jurisdiction, shorter period or destructive action. Foreign, missing,
nonpositive or stale references cannot silently rebind to today's revision.
The result retains whether the original choice was explicit or unambiguous.

The selection-aware disposition evaluator requires a fresh complete policy scope
and identical current record/rule snapshots. It repeats the original choice:
new ambiguity invalidates an automatic choice, while an explicit reviewed choice
can remain valid if that exact rule is still current. Changed versions, clocks,
applicability, immutability or same-version rule content refuse disposition.
Successful selection still passes through complete hold lookup, active hold,
governance immutability, anchor, period and overflow checks. No selection alone
authorizes deletion or anonymization.

Fresh whole-solution compilation passed with zero warnings/errors. Maintained
[selection tests](../../tests/StrataAI.Domain.Tests/RetentionPolicySelectionTests.cs)
cover order-independent ambiguity, explicit preservation, unique matching,
snapshot changes, incomplete scopes, foreign/future/duplicate/unrelated sources,
stale/substituted references, new ambiguity, holds and governance preservation.
The focused Records suite passed **41/41** and the complete Linux Domain suite
passed **984/984**, zero failed/not-executed/aborted/timed-out cases. Both captures
matched all **830 backend/test source hashes**; owned terminal fixtures were
removed. These are domain prerequisites, not runtime selection, lock acquisition,
retirement, hold persistence, disposition execution, audit/publication or release
acceptance. The previously verified 859-case API capture predates these new
domain sources; it is not evidence of their runtime integration. PRD-70 remains
open at **83% estimated work remaining**.

### Complete current policy lookup

`IRetentionPolicyStore.ReadApplicableAsync` now loads the current Organization,
record class, normalized category and Organization-wide or matching jurisdiction
namespace. It is separate from directory pagination. Both Demo and PostgreSQL
require an existing owning transaction before validating applicability or reading
private policy data. PostgreSQL borrows that transaction, filters the indexed
namespace and joins each header to its exact current immutable revision. Demo
uses the same invariant uppercase keys. Neither reads old revisions as candidates.
The three-row limit exposes an unexpected third match to the selector's corruption
check; the persisted unique namespace permits at most two applicable policies.

The existing Organization unit locks the active parent before admission, and
policy writes acquire that same parent. An eventual owning record reader must
keep that lock, authorize the actual record and its confidentiality, load its
current snapshot and complete holds, and revalidate selection through disposition.
The applicability object is a trusted storage input, not a public record or proof
of authorization. No HTTP completeness flag, disposition command or Worker grant
is introduced by this lookup. Selection review, policy retirement, actual record
adapters, hold persistence and all remaining PRD-70 workflows are still pending.

Maintained [applicability tests](../../tests/StrataAI.Api.Tests/RetentionPolicyApplicabilityTests.cs)
exercise the registered Demo adapter with real policy writes: complete global and
Unicode jurisdiction matching, current revisions, unrelated class/category and
jurisdiction exclusion, tenant isolation, owning scope, invalid input, cancellation
and explicit selection when two candidates match.

Verification for this lookup: a fresh solution build passed with zero warnings
and errors; the focused Linux API Records suite passed **18/18**, including the
new applicability case. A separate fresh PostgreSQL fixture applied the maintained
migration stream and canonical API/Worker roles, verified the schema, and executed
the compiled Production adapter against real current/history rows. It passed
normalized Unicode matching, complete dual applicability, current revision,
empty/global-only scopes, unrelated scope exclusion, owning tenant admission,
invalid input and cancellation alongside the existing persistence/retry/concurrency
and rollback checks. The committed graph contained five policy headers and seven
revisions, receipts and audit events; audit metadata retained only four safe fields.
Both fixtures matched **831 backend/test source hashes** with zero mismatches.
Owned fixture containers were removed. These compiled-runtime captures do not
establish immutable release image acceptance; the exact main CI still requires
execution and inspection. Estimated PRD-70 work remaining remains **83%**.

`record_retention_policies` has primary key `(tenant_id, id)` and selects its
exact current immutable history revision. `record_retention_policy_history`
has primary key `(tenant_id, policy_id, version)`; revisions advance contiguously
and preserve creation time. Deferred foreign keys and final-state triggers require
the header, revision, authenticated actor's retry receipt and audit source to
commit together. Orphan revisions and missing receipts are rejected atomically.
Category keys use ASCII case normalization; jurisdiction keys use the existing
configuration namespace normalization. Null jurisdiction has its own reserved
scope; blank normalized jurisdictions are rejected.

History insertion validates an active owning Organization and an active Owner
or Admin membership under locks, with exact membership version, actor label and
acting capacity snapshots. Later profile changes preserve those historical
values. The generated `RECORD_RETENTION_POLICY_CHANGED` audit contains only
version, record class, action and acting capacity; source and notes stay private.
Receipts bind the exact revision, actor, key, fingerprint and source clock, with
a 24-hour expiry boundary. Reserved retry keys and revisions cannot be deleted.

All three tables use forced tenant RLS and restrictive tenant-safe references.
Canonical API provisioning now grants SELECT/INSERT on all three policy tables
and UPDATE on the current header only. DELETE, history/receipt UPDATE, TRUNCATE,
REFERENCES and TRIGGER remain denied. The Worker has no policy access; neither
role may call internal trigger routines directly. API/Worker readiness requires
migration 142. No default policy is fabricated during upgrade. The service and
HTTP boundary establish current authority independently of storage permissions.

## Owning policy command contract

[RetentionPolicyService](../../src/StrataAI.Application/Records/RetentionPolicyService.cs)
implements the Application admission and command boundary. It enters the existing
Organization unit of work, checks active account/parent and current Owner/Admin
membership before validation or private store reads, and checks authority again
before returning a successful result. It validates policy fields and explicit
expected version, freezes scalar record input, preserves creation time and
original applicability, and rejects namespace moves, stale versions and backward
clocks. A separately reviewed identity is required for a different applicability.

The actor-bound retry fingerprint includes Organization, policy ID, expected
version and reviewed input. Valid retries return the original revision even after
a later policy edit; changed intent, expired keys and inconsistent receipt
provenance are refused. Correlation IDs are bounded and excluded from intent
identity. Store writes receive current membership identity/version/capacity and
an event ID, and must atomically persist the full audited source graph.

The service and both adapters are now registered. The Demo store participates in
Organization rollback; the Production store borrows its owning transaction.
The current HTTP surface and verified scope are documented below.
The test probe is not an adapter and cannot establish persistence, audit or rollback
semantics. Four focused Linux command tests passed after fresh whole-solution
compilation (zero warnings/errors), with all 822 backend/test hashes matching.
They verify admission before disclosure, original acknowledgment recovery,
changed/stale/expired retries, immutable applicability, substituted private
sources and result withdrawal after authority loss during IO. Full API results
and the boundaries of their captured source are documented below.

[PostgresRetentionPolicyStore](../../src/StrataAI.Infrastructure/Records/PostgresRetentionPolicyStore.cs)
now borrows the owning transaction for typed current/history and original receipt
reads. It validates current account, membership and source/receipt provenance
again before writing, snapshots the actor's current label, and appends the complete
header/revision/receipt graph under a savepoint. The schema produces the audit.
Namespace, version and retry conflicts return fixed errors; a final authority
failure rolls the owning transaction back. It cannot open an independent command.
Command timestamps are canonicalized to PostgreSQL microseconds before responses
and receipts are formed, preserving exact refresh/replay equality.

Fresh compilation and **five focused Linux cases** passed with **823 backend/test
hashes matching**, including refusal of unowned/cancelled adapter operations before
database access. A separate local compiled native PostgreSQL harness also passed
using real restricted API-role connections and the actual owning unit/store/service:
first/later committed refresh, original retries, changed/stale intent refusal,
normalized namespace collision, two concurrent writers with one stale conflict,
and final authority failure rolling back the header and receipt. Independent SQL
confirmed exactly **one header, three revisions, three receipts and three audit
events**, with four safe metadata fields and no private source/notes. All **823
captured source hashes** matched; owned fixtures and credentials were removed.
This harness used **fixture-local prospective INSERT/UPDATE grants** after checking
then-canonical read-only provisioning. That capture predates the runtime command
activation below. The Worker still has no policy access; this earlier local
compiled harness is not maintained release-image or complete HTTP acceptance.

## Internal MUI policy administration

The internal route `/app/{organizationId}/retention-policies` now reads private
current policies and retained history. Active Organization Owners/Admins receive
the navigation link; the separate Owner Portal has no new retention route.
The screen shows explicit class/category/jurisdiction, anchor, elapsed days,
action and source/notes, plus original actor label/capacity and UTC revision time.
It clearly states that recorded policies do not yet enable retention enforcement
or automatic disposition. Authorized users can create and edit policies through
an explicit draft and confirmation dialog.

The browser client validates exact response shapes, owning identities, bounded
pages and seek boundaries, safe versions, class/action/period rules, retained
attribution, contiguous history, stable applicability and clocks at full fractional
UTC precision. A maximum-UUID empty directory seek rechecks the same current
policy authority before private read completion; account identity is checked on
both sides. Protected failure details never enter visible text. Reads are bounded
and cancellable; changing context or superseding a request aborts and fences late
responses. Metadata invalidation and window focus recheck private access.

MUI controls provide source-aware history names, bounded next-page navigation,
loading/empty/degraded/refused states and fixed safe retry text. Opening history
moves focus to its heading; closing returns focus to the originating connected
control. Authority refusal withdraws private policy/history values. Private read
results are not written to browser storage. The initial 17 client/screen tests and the
final 74-case targeted client/screen/Organization-home regression passed; web
typecheck, complete lint and production build passed. The build retains the
existing bundle-size advisory. These checks do not establish desktop/mobile
browser, screen-reader, full frontend or live policy publication acceptance;
those remain required before closure.

[RetentionPolicyEditor](../../apps/web/src/features/records/RetentionPolicyEditor.tsx)
starts a new policy with explicit class, anchor, action and reviewed source; it
does not invent a jurisdiction or retention duration. Existing applicability is
fixed for the retained identity. Local field errors explain supported rules and
focus the first invalid control. The review dialog shows exact applicability,
rule, action and source/notes, reviewed/next revisions and the enforcement
limitation. Confirmation requires an explicit source/applicability acknowledgment;
returning to edit requires another review.

The editor retains unsent work in memory during degraded admission checks and
hides private dialog content until current access is confirmed. An explicit
Discard draft clears unsent work; Close and retain work keeps it on this page.
Another target cannot silently replace an existing draft. Confirmed submissions
are verified in tab-local session recovery before network delivery and are
restored only after owning admission. Uncertain delivery offers Retry original
submission with the original bytes and key, including after navigation/reload.
A definitive refusal offers Review current policy; a successful fresh admitted
read and matching recovery cleanup return to the preserved draft for explicit
comparison and a new approval/revision/key. Read/storage failures retain the
original and do not automatically rebase or acknowledge it.

Metadata/window-focus rechecks hide the open private editor while admission is
pending. Confirmed access withdrawal clears draft, selected source and retained
submission, aborts ongoing IO and fences late responses. Acknowledgment refreshes
the directory separately, so a later current revision replaces an older original
receipt in the screen. Successful refresh and denial focus the page heading;
MUI supplies dialog focus trapping/restoration and responsive wrapped actions.
Unsent drafts are not persisted across route unmounts; reviewed original
submissions have the tab-local recovery contract described below.

Maintained editor/page cases cover explicit creation, field validation and focus,
source review, edit revision guards, preserved unsent work, lost acknowledgment
across navigation, stale conflict with degraded reconciliation and a new reviewed
key, access withdrawal during in-flight IO, late-response fences, corrupt and
unwritable storage, metadata invalidation, later current state and page focus.
The records/Profile/Organization-home/Problem regression passed **147/147 cases**;
web typecheck, full lint and production build passed. These are component/client
checks, not actual desktop/mobile browser or screen-reader acceptance. Realtime
policy publication and the remaining PRD scope are still required.

### Maintained policy browser scenarios

[retention-policy-review.spec.ts](../../tests/browser/retention-policy-review.spec.ts)
executes four maintained cases at 1,280px and 390px with real authenticated
accounts and owning policy writes. It covers empty → explicit creation,
keyboard selection/review/confirmation and focus containment, revision guards,
stale conflict/current-source comparison with a preserved draft and new approval,
retained revision order/event identities/original actor attribution, and current
state after acknowledgment. Lost-response cases commit the actual request and
abort only delivery, reload tab-local recovery, make a later policy change, then
require the original request bytes/key and acknowledgment before admitting the
refreshed later current state. Another uncertain committed submission is followed
by administrator removal; private values and recovery disappear, subsequent reads
and retries are denied, and the Owner retains the original committed revision.

The scenarios observe the actual initial metadata stream before triggering a
recovered retry. Admission rechecks can cancel a preflight while preserving its
original; a hidden dialog or a newer read never proves acknowledgment. The test
requires the actual retry request and explicit acknowledgment notice. It does
not retry an unexpected failing assertion or weaken current authority checks.

All **4/4 Production and 4/4 Demo cases** passed without skipped, unexpected or
flaky cases. Axe found no violations for the tested WCAG 2/2.1/2.2 AA rule tags
in review/history states; layout overflow checks passed at both widths. These
checks do not establish manual screen-reader or full PRD accessibility coverage.
The Production fixture used fresh compiled API/Worker builds, a production web
build, canonical runtime roles, fresh migrations and installed policy capability
assertions, with an isolated database and local proxy. Its **1,918 captured
repository hashes** matched; the browser/code subset also matched all **1,335**
hashes. Demo used a separate compiled Demo API/provider with the same maintained
spec and **1,335 code/browser/web hashes matching**. Owned fixtures, database,
roles and credential files were removed; original PostgreSQL was preserved.
These local fixtures used compiled mounts and a local proxy, not immutable
release images or the release Nginx edge.

The spec now runs in the mandatory Demo release-image browser step and in the
complete Production browser shards. Actual collection/partition verification
covers **372 cases in 142 intact files**, each exactly once across four shards,
with retries disabled. Exact-head image integration/security/CI remains pending.
No numbered PRD-70 criterion or linked scenario is closed by this policy subset.

## Reviewed browser submission and recovery contract

[RetentionPolicyChangeIntent](../../apps/web/src/features/records/retentionPolicyClient.ts)
freezes the reviewed Organization, account, policy identity, expected revision,
input and retry key. Review validates the supported fields without introducing
jurisdiction defaults. Submission rechecks account and owning private policy
authority before sending, uses the existing same-origin CSRF boundary and
15-second bound, and validates the acknowledgment against the original identity,
next revision and exact reviewed input. A historical original receipt remains an
acknowledgment of that submission, not evidence of today's current policy.
Authority/account are checked again before returning private values.

[Recovery helpers](../../apps/web/src/features/records/retentionPolicyRecovery.ts)
retain at most one unresolved submission per account/Organization, with a
32-context capacity. They preserve exact canonical request bytes and refuse
replacement, corrupt/foreign records and failed storage writes instead of
discarding unresolved work. No later read acknowledges an unresolved submission.
The helpers accept browser storage explicitly; the policy browsing screen still
does not persist private read results. Sign-out and confirmed account deactivation
clear retained policy submissions. The editor retains its original before
sending, restores only after current owning admission, clears foreign account/
withdrawn authority scopes, and clears completion only for the matching
acknowledgment or a fresh read after an explicitly reviewed definitive refusal.

Known retention Problem codes are admitted through a status-bound allowlist;
raw diagnostic bodies and unsafe correlation references are excluded. Maintained
client/recovery cases cover lost acknowledgment, stalled delivery, exact retry
bytes, replacement accounts, authority withdrawal, substituted receipts,
cancellation, capacity, storage failure, corrupt records and account cleanup.
The focused records/Profile/Problem regression passed **79/79 cases**; web
typecheck, full source lint and production build passed. The build retains the
existing bundle-size advisory. These are maintained client/component checks,
not browser or complete frontend acceptance.
The editor now integrates this contract as described above. Actual browser and
complete release acceptance remain pending; client/component coverage alone
does not satisfy them.

## Transactional Demo storage evidence

[InMemoryRetentionPolicyStore](../../src/StrataAI.Infrastructure/Records/InMemoryRetentionPolicyStore.cs)
participates in the existing account/Organization and Work gates and rollback
snapshots. It retains immutable policy revisions, original receipts, actor label,
membership version and acting capacity, reserves per-Organization normalized
class/category/jurisdiction namespaces, and requires an owning Organization
command for reads and writes. Expired keys remain reserved rather than silently
reused. No default policy is seeded. Historical snapshots do not join current
membership values when replayed.

The existing Demo audit store now admits retention events only with typed fixed
metadata: positive version, record class, disposition action and acting capacity.
Source, notes, category and jurisdiction never enter that metadata. Those audit
facts share the Work rollback with policy history, receipts, namespace claims and
event identities. Cancellation or a failed final authority fence restores all of
them, allowing a genuinely rolled-back key/namespace to be used again.

Fresh whole-solution compilation passed without warnings/errors. All **eight
focused Linux retention service/adapter tests** passed, with **825 backend/test
source hashes matching**. The three maintained Demo scenarios cover persisted
refresh and original retry after competing versioned edits, normalized namespace
collision, unchanged historical attribution after a real membership-version
change, 256-character correlation IDs, fixed body-free audit metadata, complete
final-authority rollback with key/namespace reclamation, and cancellation after
write with audit rollback. The subsequent full API captures and their source
boundaries are documented below. The earlier service-only capture did not prove
these later Demo or PostgreSQL changes.

## Acceptance evidence and complete remaining scope

### Authorized HTTP policy administration

All routes belong to `/organizations/{organizationId}/retention-policies` and
require current active account, active Organization and Owner/Admin membership.
Collection GET accepts one nonempty UUID `after`; GET `/{policyId}` returns the
current revision; GET `/{policyId}/history` accepts one positive `beforeVersion`.
PATCH `/{policyId}` creates at version zero or changes the reviewed current
revision, with a nonempty UUID `Idempotency-Key`. The request has required `version`
and `policy`; policy requires recordClass, category, anchor, retentionDays (explicit
null is allowed), action and source, with optional jurisdiction/notes. Record class,
anchor and action are named camel-case enum values. Applicability remains fixed
for a retained identity. No DELETE or automatic disposition operation is exposed.

Input rejects duplicate/unknown/missing fields and numeric enum values and bounds
both chunked and fixed-length bodies to 32,768 bytes. Current admission precedes
body, key and cursor validation, then the owning command/read freshly rechecks
authority. Responses use `private, no-store`; expected-actor mismatch returns 401.
Stable envelopes distinguish validation (400), version/namespace/retry conflicts
(409), authorized missing policy (404), denied Organization (404 without protected
source facts), and unavailable private storage/source (503).

Fresh whole-solution compilation passed without warnings/errors. **17/17 focused
Linux cases** passed with **828 backend/test hashes matching**, including Demo HTTP
refresh/history/original retry, private headers, foreign/anonymous and expected-actor
denial, and strict malformed input. The unchanged maintained
[HTTP fixture](../../scripts/ci/test-retention-policy-http.sh) passed against a
fresh compiled Production API with canonical provisioned API/Worker roles, fresh
schema and installed capability assertions: persisted equality, first/later changes,
original and changed/stale retries, namespace conflicts, retained attribution,
history seeks, history/receipt/audit counters and four body-free metadata fields,
foreign/anonymous refusal, and suspended/deactivated denial with unchanged restored
revision. All **1,908 repository source hashes** matched. Owned fixtures and
credential files were removed. The fixture is now mandatory in build-once release
integration CI, but this local run used compiled mounts, not the immutable release
images. The completed current backend regression is documented below; exact-head
CI remains pending.

The owning Application service now supplies bounded policy-directory and retained
history pages. Both adapters read at most 51 sources and return 50 items with a
seek boundary: ascending policy UUID for the directory, descending retained
revision for history. PostgreSQL uses the existing tenant/policy primary keys;
Demo uses the same ordering and boundaries. History carries immutable actor label,
actor/membership identities, membership version, acting capacity and event ID;
it never joins today's profile or membership to rewrite the revision attribution.
Retry fingerprints and keys are excluded from these read models.

The service admits current Owner/Admin authority before cursor validation/private
IO and rechecks it before disclosure. It rejects excess rows, duplicate or foreign
directory identities, incomplete/noncontiguous revision pages, inconsistent current
source, changed applicability, nonmonotonic clocks and invalid historical
attribution. A missing policy returns a fixed not-found result; seeking below
revision one returns an authorized empty page. These are Application read models;
The current HTTP surface uses these seek boundaries and private headers. The
internal MUI administration route and mutation review are documented above.

Fresh whole-solution compilation passed with zero warnings/errors; **11/11 focused
Linux cases** passed with **826 backend/test hashes matching**. New maintained
cases cross the 50-item boundary with 52 policies and 53 retained revisions,
exclude another Organization's sources, check original attribution and exhausted
boundaries, and refuse duplicate/foreign/incomplete/substituted sources and
withdraw pages after authority loss during IO. A separate fresh local native
PostgreSQL harness executed the actual directory/history queries and seek
boundaries with retained attribution, alongside prior write/retry/concurrency/
rollback checks. Its **826 source hashes matched** and independent committed
graph/audit counters remained one header, three revisions, three receipts and
three fixed body-free audit events. Its prospective write grants were fixture-local;
this harness predated canonical provisioning and HTTP activation described above.
Owned fixtures and credentials were removed. Subsequent full API results are
documented below; queued release CI is not claimed as proof of those sources.

[RecordRetentionTests.cs](../../tests/StrataAI.Domain.Tests/RecordRetentionTests.cs)
passes **28/28 focused Linux cases**. The complete current Linux Domain suite
passes **971/971**, zero failures, skipped, aborted or timed-out cases. Both
captures match all **820 backend/test source hashes**. Fresh compilation passes
with zero warnings/errors; owned terminal test containers were removed.

The final disposable PostgreSQL check passed with **1,899 captured source files**
and zero mismatches. It exercised policy namespace and version refusal,
atomic source/receipt integrity, historical attribution, immutable history,
cross-tenant and unscoped disclosure refusal, and canonical API/Worker capability
checks. The unchanged full maintained migration runner also passed, including
repeat execution and its synthetic failure/serialization cases. These checks are
now required by the build-once integration workflow. Fresh whole-solution
compilation passed without warnings or errors. A compiled Production API returned
503 `runtime_database_schema_incompatible` after ledger 142 was removed and
recovered after it was restored. Owned fixtures were removed. These are local
schema/compiled-runtime checks; exact release-image CI remains pending.

This supports policy storage integrity, validation and disposition/hold decision primitives
for RECORDS-FR-001/002/003. It does not close any numbered acceptance criterion
or linked scenario. Required remaining work includes:

- Remaining typed producer identities, real record classification/confidentiality
  authorization and Production hold runtime activation, with Demo parity
  and complete policy selector acceptance.
- Owning transaction admission, explicit elevated authorization, review/concurrency,
  complete hold lookup and current lock ordering across every disposition path.
- Applied/released hold commands, policy selector/retirement rules and remaining Records MUI workflows,
  cross-session domain publication and authorized replay after membership changes.
- Independent governance preservation; controlled redaction and authorized original
  access; privacy deletion/anonymization versus retained governance/audit sources.
- Scoped exports, confidentiality, asynchronous expiring object-storage downloads;
  idempotent/recoverable disposition jobs; offboarding steps and verification.
- MUI workflows, disclosure withdrawal, degraded/empty/conflict states, keyboard,
  mobile and screen-reader acceptance; fixed-content telemetry and runbooks.
- All PRD-70 linked scenarios, cross-PRD consistency and actual build-once release
  image integration/security/CI acceptance. No new service or datastore is introduced.

The earlier API regression captures predate these two added Domain/test files;
they remain evidence for their captured API source, not proof of retention
runtime enforcement. PRD-27 stays open at 24% estimated work remaining.

The later `retcmd3-full` API capture has now completed **846/846 cases**, without
failed, skipped, aborted or timed-out cases. It captured 822 backend/test files;
six captured files changed by completion. It predates later adapter, pagination
and HTTP activation changes, so it is historical regression evidence only.
The separate `retdemo2-full` API capture completed **850/850 cases**, with zero
failed/skipped/aborted/timed-out cases. It captured 825 backend/test files; eight
captured files changed by completion. It predates pagination and HTTP activation
and is also historical regression evidence only.
The current backend `rethttp1-full` capture has completed **859/859 cases**,
with zero failed/skipped/aborted/timed-out cases. All **828 captured backend/test
hashes matched**. This is full current backend source regression evidence,
including adapter, pagination and HTTP activation; it does not establish release
images or frontend acceptance. Exact-head release CI remains queued and no release
acceptance is claimed.


The `retreviewcmd1-full` compiled-host API regression completed on 2026-10-11:
**876/876 cases passed**, with zero failed, skipped, timed-out, aborted or
unexecuted cases. All **854 captured source hashes matched**, including the new
first-classification owning review test. This is backend source regression
evidence; it does not establish unactivated classification HTTP/MUI workflows
or the current build-once release CI gate. PRD-70 remains open at 80% estimated
work remaining.

## Durable first-classification receipt graph

Migration 146 adds a nullable original command key to immutable classifications
and actor-bound `record_retention_source_receipts`. Historical classifications
retain NULL keys; no command identity or receipt is backfilled. A new key-bearing
classification requires the exact receipt at commit through a deferred foreign
key. The inverse receipt binding includes the source class/identity, original
actor/key and original safe-audit event. A receipt cannot attach to another
classification or supply a different source clock. Keys are unique within the
Organization/actor classification namespace, and each source has at most one
receipt. The fingerprint is 64 uppercase hexadecimal characters and expiry is
exactly 24 hours after the original source clock. Receipt updates/deletes and
source-key rewriting are refused.

Receipt RLS is forced and binds both current tenant and identity subject. The
migration grants no API/Worker capability. Application receipt lookup still must
perform current account, membership and producer/confidentiality authorization;
this database graph is not an authorization grant. The initial migration-only
step left owning retry commands, Production/Demo receipt adapters and HTTP/MUI
workflows unfinished; the command/adapter evidence below records subsequent work.

The mandatory restricted Records persistence invocation passed on 2026-10-11,
including all **146 migration entries individually rejected/restored for both
API and Worker hosts**, existing real producer/hold adapter contracts and the new
receipt graph contract. It verifies source/audit rollback when a receipt is
missing, changed actor/record/event/clock/expiry/fingerprint rejection, duplicate
receipt/key refusal, immutability, tenant/actor RLS and historical NULL keys.
All **1,955 captured hashes matched**. Fresh whole-solution compilation has zero
warnings/errors. An initial test cleanup attempt was rejected because it tried
to roll back an already completed failed commit; the final transaction-disposal
contract passed with all assertions retained.

The complete maintained migration-runner script also passed: clean/repeated
application, exact pre-existing source/audit comparison across the forward
upgrade, concurrent runner serialization and failed/unrecorded migration refusal.
All **381 captured hashes matched**. Its synthetic fixtures now follow migration
146 without numbering collisions. These are local database/source results;
current build-once immutable-image acceptance remains required. PRD-70 remains
open at **80% estimated work remaining**.

## Policy recovery after outer Organization withdrawal

Definitive internal Organization admission denial now clears saved private policy
submissions for that Organization, even when the outer boundary retires the policy
page before its own denial effect runs. Original submissions for other
Organizations remain available. Transport uncertainty preserves the original
retry intent, and independent Portal denial does not erase internal policy work.

An older release trace showed the desktop withdrawal scenario waiting 111 seconds
to focus a policy refresh button after the outer boundary had removed the page.
The fresh unchanged maintained replay reproduced the boundary ordering: three
cases passed, while the desktop scenario waited for the page-specific denial
message after the outer denial was already visible. The maintained scenario now
requires definitive outer denial and absence of the protected refresh control;
its private-display, saved-recovery removal, original-key refusal, retained
server result and no-horizontal-overflow assertions remain required.

The repaired scenario passed all **four desktop/phone cases** on 2026-10-11 in
99.3 seconds, with no skipped, unexpected or flaky cases. The actual restricted
API/Worker replay used verified accounts and preserved the keyboard confirmation,
WCAG checks, lost acknowledgment, reload, exact original retry and immutable
history assertions. All **1,364 native** and **1,957 hosted captured hashes
matched**. The separate checkout also passed **44 focused tests**, frontend and
browser type checks, lint, production web build and fresh full-solution compilation
with zero .NET warnings/errors. These are local source results; exact-head
build-once image acceptance remains required. PRD-70 remains open at **80%
estimated work remaining**.

## Owning first-classification command and receipt adapters

The backend-private `RetentionSourceClassificationService` owns the existing
Organization transaction. It admits the current account and active Owner/Admin,
requires the exact reviewed Card/Comment/Attachment tuple, and fingerprints that
tuple with the explicit human category and optional jurisdiction. It never
guesses classifications or rewrites an existing source with a new key.

Actor/key recovery reads a minimal original reference first, authorizes both the
original classification Board and current producer Board, then reads and
cross-checks the immutable receipt. Exact retries return the original event,
category, jurisdiction, context and source clock after a move; changed inputs
conflict, expired keys are refused at 24 hours, and denied access withholds private
receipt bodies. Admission and reference checks repeat before returning or commit.

Both real adapters capture classification, original receipt and safe audit in the
same owning transaction. PostgreSQL inserts the exact migration-146 graph inside
the existing savepoint before its late review fence. Demo retains actual producer
storage and restores both classification and receipt dictionaries with the work
audit on failure. Legacy private captures remain receipt-free; no historical
command identity is fabricated. These commands and adapters remain unregistered:
canonical API/Worker grants and HTTP/MUI confirmation/recovery are not activated.

Verification on 2026-10-11:

- All **35 focused Records cases** passed. The new real Demo command case covers
  all three producers, exact/mismatched input, stale review, new-key refusal,
  original recovery after moves, both original/current Board denial before body
  lookup, expiry, and complete classification/receipt/audit rollback.
- The final restricted PostgreSQL contract passed owning classifications and
  original recovery for all three producers, changed-input/new-key refusal,
  owning rollback and post-insert savepoint restoration with audit/receipt absence.
  It retained the receipt graph and all **146 ledger-entry readiness checks for
  both API and Worker**; all **1,959 captured hashes matched**.
- The complete compiled-host API regression passed **877/877 cases**, with zero
  failed, skipped, timed-out, aborted or unexecuted cases. All **857 captured
  backend/test source hashes matched**.
- Fresh whole-solution compilation passed with zero warnings/errors. Owned
  temporary hosts, databases, roles and credential files were removed; original
  PostgreSQL data was preserved.

These are local source/database results, not retained immutable-image acceptance.
HTTP/MUI activation, broad/other owning producers, legal holds, redaction,
exports, privacy actions, disposition/offboarding and complete release/PRD
acceptance remain unfinished. PRD-70 stays open at **80% estimated work remaining**.

## First-classification request boundary

`RetentionSourceEndpoints` defines separate source review and classification
handlers using explicit transport projections. The shipping host does not map
these routes yet: Production/Demo composition, canonical runtime capabilities and
the source-context review/recovery interface must be completed together.

The classification body requires every reviewed relationship, revision and
lifecycle field, an explicit nullable jurisdiction and human confirmation.
Duplicate/unknown properties, numeric enums, missing evidence, oversized bodies,
route/evidence substitutions and missing UUID retry keys are refused. The actor
comes from the authenticated session; an expected-actor mismatch requires sign-in.
Organization admission precedes request parsing. Responses prohibit caching and
the review projection contains no titles, comment bodies or attachment URLs.

An exact original submission does not obtain a fresh review before recovery:
the owning service checks both original and current Boards and returns the
original classification receipt. This preserves acknowledgment recovery after
a move without treating the original evidence as a retained permission grant.

Local verification on 2026-10-11: the whole solution compiled with zero warnings
and errors; **35/35 focused compiled-host Records cases passed**, with zero
failed/skipped/aborted/timed-out/unexecuted cases and **858 captured backend/test
hashes unchanged**. The real Demo classification regression invokes the request
handlers for all three source kinds, checks strict schema/confirmation refusals,
expected-actor refusal and Organization denial before body consumption, and
recovers exact original results after a Card move. These are handler/owning-store
checks, not proof of a mapped Production HTTP route or completed MUI workflow.
Public activation and full immutable-image acceptance remain unfinished.

## Classification client and retained original recovery

The source client validates exact review projections, retained source lifecycles,
Card/Comment/Attachment relationships, safe revisions and precise UTC source
timestamps. A confirmed classification intent privately owns immutable original
request bytes and its UUID retry key. Restoration validates the complete bounded
recovery record and requires byte-for-byte canonical identity; another source or
key cannot overwrite an unresolved original in the same actor/Organization scope.

Submission uses the authenticated actor fence and the normal same-origin CSRF
transport. It sends one bounded request without a fresh source review: current
and original Board authority remain the owning server command's responsibility.
Successful acknowledgment requires the exact original source, original Board,
revision, category, jurisdiction and actor, with valid event/UTC registration
evidence. A changed actor after delivery or a malformed/substituted response
does not acknowledge success or clear the original. Network failure and timeout
preserve the retained request for an explicit retry with identical bytes.

Confirmed sign-out/account withdrawal clears source recovery. Definitive internal
Organization denial clears only that Organization; transport uncertainty and
independent Portal denial preserve internal originals. Storage is bounded to 32
scopes and cannot evict a pending original to make room for another Organization.

Local verification on 2026-10-11: **63/63 focused source-client, SurfaceAdmission
and ProfilePage cases passed**, including lost acknowledgment recovery, exact
byte identity despite later source movement/drafts, altered receipt refusal,
actor changes before/after delivery, bounded timeout, corrupt/competing recovery,
and scoped withdrawal/sign-out preservation/cleanup. Frontend type checking,
lint and web compilation passed (the existing bundle-size advisory remains).
These results do not prove a completed source-context MUI control, mapped
Production/Demo workflow or retained immutable-image release acceptance.

## Source-context MUI review control

`RetentionSourceControl` now implements the review/confirmation/recovery state
machine for an actual source context. Its caller must provide the admitted actor,
Organization, owning Card/Board and current Card/source revisions; server review
evidence must match that context. The control displays source relationships,
revisions, retained lifecycles and creation evidence before explicit category,
optional jurisdiction and confirmation. Stale context prevents submission while
retaining the unsent category draft. A confirmed current source cannot start
another first-classification review.

Original recovery is generic until the owning command admits the original and
current source: pending intent from another source cannot expose its category or
jurisdiction through the current view. Persistence is verified before delivery;
even an unsuccessful/partial storage write preserves the unsent original key.
Malformed storage prevents new delivery. A definitive refusal preserves the
original for support instead of silently replacing its key. Uncertain access
hides private controls and retains work; definitive denial clears private state
and recovery. Epoch/cancellation fences prevent late success from acknowledging
a command after the caller withdraws admission. Rendered-state focus restoration
uses the existing owned-focus rules, preserving another user-selected control.

Local verification on 2026-10-11: **74/74 focused MUI control, classification
client, SurfaceAdmission and ProfilePage cases passed**, including 11 control
cases for confirmation, stale context, remount/move recovery, privacy, corrupt
storage, refusal, storage failure before delivery, definitive server denial,
late success after uncertainty, and accessible names/category focus. Frontend
types, lint and web build passed; the existing bundle-size advisory remains.
These are component/transport tests; actual browser keyboard/mobile/AA proof
is still required. The control is not yet composed into shipping Card/Comment/
Attachment views. Production/Demo provider composition, canonical capabilities,
mapped HTTP/native proof and immutable-image release acceptance remain open.

## Mapped classification API and provider activation

The following activation supersedes the earlier unmapped-boundary stage. The
authenticated Organization group now maps:

- `GET /organizations/{organizationId}/retention-sources/{kind}/{recordId}/review`
- `POST /organizations/{organizationId}/retention-sources/{kind}/{recordId}/classification`

Supported kinds are `card`, `cardComment` and `attachment`. Both modes resolve
the owning review/classification services. Demo shares one source store across
command/reference/current-producer interfaces and includes it in Organization
rollback; Production uses the PostgreSQL source, producer and reference adapters.
Legal-hold commands/readers are not activated by this composition.

Canonical runtime provisioning now grants the API **SELECT/INSERT only** on
`record_retention_sources` and `record_retention_source_receipts`. It grants no
source/receipt capability to the Worker and no API update/delete/column mutation
capability. Native fixtures revoke only their additional prospective mutation
grants, preserving canonical rights between contracts. Existing installations
must rerun `db/provision-runtime-roles.sql` after the ordered migrations. The
schema remains at migration 146; activation does not backfill classifications,
invent historical commands or widen Worker access.

Local verification on 2026-10-11:

- Fresh whole-solution build: zero warnings/errors.
- **37/37 focused compiled-host Records cases passed**, with zero failures,
  skips, timeouts, aborts or unexecuted cases; **860 captured backend/test hashes
  matched**. The actual mapped Demo HTTP test reviews and confirms all three
  producers, verifies cache/privacy/confirmation/actor/tenant boundaries, exact
  original retry and changed-input/new-key refusal, moves the owning Card and
  recovers identical original responses with exactly three audit facts.
- The actual Production host resolves the complete real classification provider
  graph using the established deliberately unreachable composition fixture;
  disconnected readiness remains unavailable rather than falling back to Demo.
  This is provider/host proof, not a database-backed Production HTTP flow.
- Native PostgreSQL contracts passed the canonical minimum-capability assertions,
  immutable receipt/source graph, owning classification and rollback, with both
  restricted API/Worker hosts passing all **146 migration readiness entries**;
  **1,967 captured hashes matched**. Owned temporary containers/credentials were
  removed; original PostgreSQL data was preserved.

Full API regression and retained immutable-image CI acceptance are still required.
The MUI control remains to be composed into actual Card/Comment/Attachment views,
then verified through maintained Production browser keyboard/mobile/AA flows.
Other RECORDS requirements remain unfinished; PRD-70 stays open.

## Classification entry points in actual source views

The source-view integration supersedes the standalone-control stage above.
Active Card details and archived Card details now expose the MUI classification
disclosure using the actual Card identity and revision. Admitted, undeleted
Comment rows and admitted Attachment metadata rows expose their own source
identity and revision while retaining the owning Card/Board context. Archived
classification does not enable operational editing or change a Card revision.

Opening the disclosure checks the current account, Organization Owner/Admin
authority and current Board view/administration authority before mounting the
source review. Identity invalidation and parent-context changes retire admission;
uncertain access hides private work and preserves the original command/draft.
Hiding and reopening a disclosure preserves its unsent draft. The server remains
authoritative for each review, confirmation and original-command retry.

Recovery cleanup distinguishes definitive Organization/account withdrawal from
denial on one source or Board. Source denial clears only its matching original;
confirmed Board withdrawal clears the owning Card family, preserving an original
for an unrelated Card. A Card boundary stays mounted when administration rights
disappear, allowing a bounded Organization authority check even if its disclosure
was never opened. An uncertain parent permission snapshot starts no withdrawal
probe and cannot erase the retained original.

Local verification on 2026-10-11: **90/90 focused component/client cases passed**,
covering all three real source-view consumers, strict original command delivery,
source-scoped cleanup, Organization role withdrawal before disclosure, Card-family
cleanup, and uncertain parent withdrawal both before and after admission. Frontend
types, changed-file lint and the production web build passed; the existing bundle
size advisory remains. These
are component/transport tests, not Production browser or WCAG acceptance. Actual
desktop/mobile keyboard/AA flows and immutable-image CI acceptance remain open,
alongside the other unfinished RECORDS requirements. PRD-70 remains open.

## Maintained Production source classification browser acceptance

`tests/browser/retention-source-classification.spec.ts` exercises the actual
Card, Comment and Attachment entry points at **1280px and 390px**. Its disposable
verified Owner creates the three sources through actual commands. Each reviewed
classification requires keyboard confirmation and a category; no jurisdiction
is inferred. The fixture replaces only the first already-committed reply with
503, reloads the source view, and recovers the exact original command. Both POST
bodies, retry keys and authoritative receipts must be identical. Receipts retain
the actual actor, source identity/revision and original Board, with three distinct
event identities. Classification leaves the operational Card revision unchanged.

The reviewed form passes the maintained WCAG 2.2 AA axe tag set and horizontal
overflow check at both widths; keyboard focus enters the category and returns to
original recovery after the uncertain reply. This is automated browser evidence,
not a claim of complete screen-reader or all-RECORDS accessibility acceptance.

Local Production verification on 2026-10-11: **2/2 maintained browser cases
passed**, using actual PostgreSQL, API and separate Worker with verified-account
admission. **1,377 browser-capture and 1,970 native-capture source hashes matched**.
Temporary API/Worker, PostgreSQL fixture, roles, databases and private credentials
were removed; the original local PostgreSQL data was preserved. A fresh complete
.NET solution build passed with zero warnings/errors before the capture.

The strict verified-account release-image phase explicitly runs this specification;
the complete browser suite also collects it in the existing four intact-file
shards. Local mandatory integration verification covers **124 registered steps**
across four groups/seven isolated executions; browser collection covers **374
cases in 143 files**, distributed **95/94/95/90** across the four shards.
Workflow/partition regressions passed **212/212** cases, including refusal of a
skipped, filtered, ignored or weakened verified-account classification phase.
Browser TypeScript and whitespace checks passed. Retained
immutable-image CI, source classification browser move/withdrawal cases, and the
unfinished hold/redaction/privacy/export/disposition/offboarding behavior remain
required. PRD-70 stays open.

The maintained specification now also includes actual cross-Board Card movement
at both widths. After an uncertain classification acknowledgment, the Owner moves
the Card through the real versioned command, waits for destination delivery, and
opens its current destination details. Recovery offers the original submission
without fresh source review. The recovered receipt and body remain byte-identical
to the first response/command, retaining the original Board, List and source
revision despite the new operational context. Current and original Board authority
remain server-enforced; this browser increment proves authorized recovery, not
every permission-loss ordering.

Local expanded Production verification on 2026-10-11: **4/4 maintained cases
passed**, with **1,377 browser-capture and 1,970 native-capture hashes matched**.
Fresh whole-solution build passed with zero warnings/errors; browser TypeScript
passed. Full collection accounts for **376 cases in 143 intact files**, distributed
**95/94/95/92** across four shards. Owned fixtures and private credentials were
removed, preserving original PostgreSQL. Browser withdrawal and retained
immutable-image acceptance remain open, alongside the other RECORDS requirements.

## Complete API regression after source-provider activation

The complete compiled-host API regression finished on 2026-10-11: **879/879
tests passed**, with zero failures, errors, timeouts, aborts, skips or unexecuted
cases. Both the actual mapped three-source confirmation/move/recovery case and
the complete real Production provider-graph case passed within this full run.
All **860 captured backend/test source hashes matched**. The capture ran against
`8decbf7d`; backend/API-test/database sources remain unchanged through `51b9ef50`,
whose subsequent changes compose the MUI views and maintain browser/CI coverage.
The owned terminal API container was removed, preserving original PostgreSQL.

This supersedes the earlier pending full-regression status. It does not establish
retained immutable-image release acceptance: exact-commit CI remains to be
verified. Other Records acceptance requirements remain open.
