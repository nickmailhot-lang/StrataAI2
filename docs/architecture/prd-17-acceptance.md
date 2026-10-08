# Notification and watching acceptance map — PRD-17

[PRD-17](https://github.com/nickmailhot-lang/StrataAI2/issues/18) remains open at
**15% estimated work remaining** (planning estimate). Earlier estimates below
record their evidence scope. This maps all twelve
functional requirements to implementation contracts and required completion
evidence. A feature document or isolated passing scenario does not close a row.
The adopted MUI/API/separate Worker/PostgreSQL architecture is unchanged.

## Functional requirements

| Requirement | Implementation and evidence source | Required completion evidence |
| --- | --- | --- |
| NOTIFY-FR-001 notification center | [Bounded MUI inbox](notification-inbox.md), account admission, paging, empty/error states and read recovery | Current immutable-image native desktop/phone execution, keyboard/accessibility and account/membership withdrawal |
| NOTIFY-FR-002 recipient, actor, type, link and clocks | Persisted notifications and current authorized links; [assignment source](assignment-notifications.md) and strict browser parsing | Local exact stored attribution/clocks and complete private envelopes across all producer families are executed below; current immutable images, complete role/interaction and moved/deleted parent admission remain required |
| NOTIFY-FR-003 single and bulk read | Recipient-bound read commands, original-key recovery and immutable first-read timestamp; [inbox contract](notification-inbox.md) | Full restricted API/current-image read, rollback, overlapping command, paging and lost-response/browser cases |
| NOTIFY-FR-004 CARD/LIST/BOARD watch/unwatch | [Personal watch contracts](watch-subscriptions.md), retained subscription identity, version and original receipt | Current-image native watch lifecycle, permission/account withdrawal, retry, movement and parent transitions |
| NOTIFY-FR-005 watched Card changes | [Configured activity matrix](watch-activity-notifications.md), originating command transaction and post-mutation revision | All configured producers, current eligibility, source/journal/job atomicity and native producer-to-inbox delivery |
| NOTIFY-FR-006 current List and newly created Cards | Same producer selects current List watchers and includes Card creation | Native current-List creation/movement boundaries executed below; current-image, concurrent watch/access transitions and exact historical attribution remain |
| NOTIFY-FR-007 Board-wide Card activity | Same producer selects current Board watchers and deduplicates overlapping scopes | Full activity matrix and current-image 500-recipient correctness/latency fixture; scale Worker delivery remains distinct |
| NOTIFY-FR-008 relationship at triggering event | Post-mutation current List/Board attribution; direct Card watch follows identity | Local native List/cross-Board and actual Card/List/Board unwatch/activity lock-order matrices executed below; current-image, complete role/visibility and concurrent permission/movement admission remain |
| NOTIFY-FR-009 mentions and assignments | [Assignment precedence](watch-activity-notifications.md), [assignment persistence](assignment-notifications.md), [mentions](comments-mentions-activity.md) | Both producer pipelines, actor suppression, deduplication, visibility withdrawal and current-image native delivery |
| NOTIFY-FR-010 due reminders | [Personal due reminders and typed Worker delivery](card-dates.md) | Real scheduled fire, narrow Worker capability, exactly-once notification/journal effect, cancellation/reclaim/access withdrawal and native recovery; saving a future reminder alone does not prove fire |
| NOTIFY-FR-011 actor self-suppression | Originating activity excludes actor; assignment/watch precedence and due-reminder policy are documented separately | Full configured producer matrix, including intentional personal reminder semantics, without applying suppression indiscriminately |
| NOTIFY-FR-012 idempotent creation | Event-recipient uniqueness, original command receipts and atomic private journal; [recipient realtime](notification-realtime.md) | Duplicate/concurrent source delivery, rollback/reclaim, exact identity/timestamps/sequences and two-client recovery on current images |

## Acceptance criteria and definition of done

- **AC-NOTIFY-17-01:** prove actual authenticated persisted producer state,
  bounded inbox disclosure and authoritative MUI rendering together.
- **AC-NOTIFY-17-02:** invalid/unauthorized read changes must return stable errors
  without protected disclosure or changes to notification/journal/receipt state.
- **AC-NOTIFY-17-03:** two admitted clients recover the original canonical
  creation/read transition without manual reload or duplicate creation. Observe
  actual recipient-private transport as well as fresh authorized HTTP content.

The complete thirteen linked scenarios remain required: happy path, empty state,
invalid input, unauthorized access, mid-session withdrawal, timeout/retry,
idempotency, concurrent update, reconnect, parent lifecycle, keyboard, mobile and
documented large-data performance. Source/API/PostgreSQL/native evidence must be
matched to the current revision and policy. Earlier local scenarios permitted
unverified fixture accounts; the strict-policy executions below separately
prove verified-email admission for their declared watch matrices.

Closure also requires the full unit/integration/end-to-end gates, migration/role
review, event consumption, documented private telemetry, useful error/empty/loading
states, accessibility and no known P0/P1 defects. Email/push/mobile delivery is
outside this PRD. Required immutable images must be built once and the same images
tested and emitted; local compiled assemblies cannot substitute for that gate.

## Existing lifecycle and capacity evidence

The [21-case direct/moved deletion matrix](lifecycle-acceptance.md#moved-card-deletion-surfaces)
proves local HTTP search/inbox/sync/read-receipt admission, including surviving
moved work. The [six terminal retirement cases](lifecycle-acceptance.md#accepted-deletion-after-account-deactivation)
prove Organization content withdrawal after actual Worker completion, with
immutable existing audit rows after logout/account deactivation. Neither matrix
establishes the complete watch/mention/reminder producer contract or browser
transport acceptance.

The [500-recipient producer benchmark](watch-activity-notifications.md#full-http-fan-out-after-schema-111)
passed twenty actual local HTTP edits at p95 477.964 ms with exact 10,000
notification/journal effects and unchanged replay effects. This is one serial
producer under documented conditions, not 500 browser recipients or Worker
delivery at that scale. Consumer capacity and overlapping read commands have
their own mandatory fixture. Current immutable-image execution still governs
release acceptance.

## Executed native watch, inbox and reminder recovery

All five unchanged scenarios across `watch-notifications.spec.ts`,
`watch-subscriptions.spec.ts`, `notification-center.spec.ts` and
`card-reminders.spec.ts` passed in one local invocation on 2026-10-08.
The web bundle was built from current source at `ba543cc9`. Nginx served it with
production security headers against the current compiled Production API and a
separate compiled Worker, using restricted roles in a cloned PostgreSQL
17/pgvector database upgraded through schema 112. No fixture fabricated
notifications, watch state, reminder state or Worker readiness.

The desktop/390px inbox observed actual recipient-private SignalR creation/read
envelopes, canonical links, exact event counts, shared read state and decimal
cursor recovery after an interrupted connection. It recovered a committed read
with its identical original key/body, performed selected bulk reads, recovered
cross-session timezone changes without changing persisted notification clocks,
and withdrew articles after Board membership removal. Both views passed Axe.

Overlapping Board/List/Card watches produced exactly one typed notification from
a peer's actual MUI edit. Self-actions produced no extra notification; removing
all watches suppressed later activity and the issuer inbox stayed empty.
Desktop/phone personal-watch changes recovered across clients; a deliberately
lost successful Card watch acknowledgment recovered with the original key/body.
The direct Card subscription retained its identity/version across movement and
became unavailable after destination List archival. Keyboard and focus assertions
remained unchanged.

Both desktop/phone personal-reminder cases recovered a committed save after a
lost response, retained the original request key/body, updated the other client
without reload and recovered cancellation to the exact canonical version and
generation. Accessibility and focus checks passed. These reminders have a future
2040 due date: this browser invocation proves schedule/recovery/cancellation,
not actual due firing or NOTIFY-FR-010 delivery.

Web and full browser typechecks and the production web build passed. The
invocation-owned API/web/Worker containers and cloned database were removed;
original services and volumes remained intact. Rate pacing and diagnostic
traces make this a correctness run, not a latency/capacity benchmark.

The browser fixture uses the same unverified-account policy as CI's browser
phase; strict verified-email behavior remains separately required. Assemblies
were mounted read-only in cached framework containers, so this is local compiled
Production evidence rather than current immutable release-image acceptance.
The five scenarios remain in the mandatory full CI browser suite. Full producer,
reminder-fire, concurrent/large-data and retained-image gates still prevent
closure; estimated PRD-17 work remaining stays **22%**.

## Actual Worker due firing and native inbox recovery

The complete unchanged `scripts/ci/test-card-dates.sh` passed against the current
compiled Production API, separate compiled Worker, restricted PostgreSQL
17/pgvector schema 112 and Nginx on 2026-10-08. This includes canonical UTC/DST
dates; unauthorized/invalid commands; full publication rollback; original-key
recovery/no-op; completion/reopen/clear; Card/List/Board reminder suspension and
renewal; all 76 synthetic personal choices; cancelled-choice preservation;
Board date-policy rollback/replay without rewritten dates/generations; and
Organization deletion-request rollback/suspension. Synthetic choices establish
bounded-page independence, not actual user enrollment at scale.

A real personal `AT_DUE` request persists a future job; no fixture advances its
clock, lease or firing state. The separate Worker fires it within the existing
60-second deadline, persists FIRED version 2/generation 1, one intentional
self-recipient `REMINDER_FIRED` notification and one ready canonical source.
Original scheduling acknowledgment recovery creates no additional job or
notification and does not rewind canonical Card/reminder state. The requesting
recipient is fixture-verified; the Worker retains verified-email enforcement.
Existing role/capability and lease failure contracts remain separate requirements.

Two new mandatory native cases in `card-reminders.spec.ts` pass at 1280px and
390px. Each sets a real due time sixty seconds ahead, chooses `At the due time`
through keyboard MUI controls and observes automatic delivery in the open Card
reminder panel and a second native inbox. Exactly one canonical Card link and
intentional self notification appear. The original schedule key/body returns its
identical original acknowledgment after firing. Mark-read yields exactly one
creation and one read journal event with distinct identities. Tagged Axe and
horizontal overflow checks pass on both views. These cases establish native
recovery without manual reload; they do not independently inspect reminder
WebSocket frames or prove concurrent/sustained load.

The complete HTTP script matches staged source apart from local URL/UUID/scratch
adapters. Both native cases passed unchanged in one invocation with a fresh
current MUI bundle and read-only compiled API/Worker assemblies. Their fixtures
verify through the real endpoint when a test token is available; otherwise only
the disposable account is activated by controlled SQL. Email-provider delivery
is not claimed. API fixture policy permits unverified accounts, while the actual
Worker and reminder recipient in these firing cases enforce verification.

All invocation-owned containers and cloned databases were removed and original
services/volumes preserved. Browser typechecking and shell/diff checks pass.
These compiled-runtime results strengthen NOTIFY-FR-010/012 and PRD-12 lifecycle
acceptance. Full current build-once image, producer/concurrency/large-data and
complete Definition of Done gates still govern closure. Estimated PRD-17 work
remaining is **21%**, and PRD-12 **24%** (planning estimates); both stay open.

## Mention producer recovery

The [six-case selected/group mention invocation](prd-15-acceptance.md#executed-selected-and-group-mention-recovery)
passes actual MUI author commands, consent/quota/role and original-key recovery,
with real private HTTP recipient inboxes, deduplication and grant withdrawal.
It strengthens NOTIFY-FR-009/011/012 producer evidence. These cases do not
independently execute a recipient MUI mention center or inspect mention
WebSocket frames; full current-image and remaining integrated acceptance remain.
Estimated PRD-17 work remaining stays **21%** (planning estimate).

## Executed native selected-mention consumer

The [recipient inbox and private live cases](prd-15-acceptance.md#executed-recipient-mention-inbox-and-private-live-delivery)
pass at desktop and phone widths. They extend the previous mention-producer
record with actual recipient MUI delivery, canonical Card navigation, keyboard
read acknowledgment, exactly one private creation/read event, and native
withdrawal after Board membership removal. Stale selected handles disclose
nothing; original command retry keeps one notification and the author sees no
self notification. These strengthen NOTIFY-FR-009/011/012.

This is compiled-runtime evidence with unverified fixture accounts, not current
retained-image identity or strict verification proof. Group-recipient native
coverage, remaining producer/concurrency/capacity and complete Definition of
Done requirements remain. Estimated PRD-17 work remaining stays **21%**.

## Executed native confirmed-group consumer

Both [confirmed-group recipient cases](prd-15-acceptance.md#executed-confirmed-group-recipient-inbox-and-live-delivery)
pass at desktop and phone widths. They independently account for the initial
real assignment, observe three deduplicated group mentions and private creation
events, verify canonical native Card links and withdraw all inbox disclosure
after Board membership removal. Unconfirmed text, the fourth rate-limited group
and the actor's own Card group add no notification. Recipient tagged Axe and
overflow checks pass. This supersedes the earlier group-recipient HTTP-only
limitation and strengthens NOTIFY-FR-009/011/012 alongside selected mentions.

These are compiled-runtime results with unverified fixture accounts. Current
retained-image, strict-policy, remaining producer/concurrency/capacity and full
Definition of Done requirements remain. Estimated PRD-17 work remaining stays
**21%** (planning estimate); the issue remains open.

## Executed two-inbox reminder private transport

Both actual Worker due-firing cases in `card-reminders.spec.ts` pass together
at 1280px and 390px on 2026-10-08 (2 passed, exit 0). Two native inbox pages
receive their empty recipient-private SignalR snapshots before scheduling.
A real near-future AT_DUE request is then saved through keyboard MUI controls;
no fixture advances its due clock, lease or firing state. The separate Worker
retains verified-email enforcement and the disposable recipient is verified.
The Card panel recovers FIRED version 2/generation 1 and both inboxes show one
intentional self-recipient reminder with its canonical Card link.

Both actual live streams contain exactly one NOTIFICATION_CREATED followed by
one NOTIFICATION_READ. Every observed snapshot/event matches the Organization
and recipient; Notification event metadata is empty. The two streams' event
IDs exactly match the persisted private journal's two distinct identities.
Keyboard mark-read in one inbox updates both to zero unread without manual
reload. Original scheduling key/body recovers its unchanged acknowledgment
after firing without extra notification/events. Tagged Axe and overflow checks
pass on the Card page and both inboxes at each viewport.

The first invocation was stopped after its retained live trace showed a wait
for the missing AT_DUE menu option: scheduling never started and no reminder
notification appeared. Its invocation-owned browser processes were stopped;
the outer fixture removed its containers/database. It is failed diagnostic
evidence, not acceptance. The fixture now activates the author foreground,
uses existing admitted-focus single keyboard activation, checks option visibility
with a bounded assertion and applies the same admission to save/read controls.
It does not repeat commands or change the due clock, delivery deadline or
product authorization. The subsequent complete invocation passes both cases.

The current MUI production bundle, read-only compiled Production API/separate
Worker, restricted PostgreSQL 17/pgvector schema 112 and Nginx establish local
compiled evidence. API fixture policy permits unverified accounts while these
recipients and the Worker enforce verification; provider delivery is not claimed.
All owned containers/database are removed and original services/data preserved.
Browser typechecking, relative-link and diff checks pass. These mandatory full-CI
cases supersede the earlier lack of independent reminder WebSocket-frame proof
and strengthen NOTIFY-FR-010/012 and AC-NOTIFY-17-03. Current immutable-image,
concurrent/capacity, remaining producer and complete Definition of Done acceptance
still govern closure. Estimated PRD-17 work remaining stays **21%**; PRD-12 stays
**24%** (planning estimates); both remain open.

## Executed native assignment consumer and receipt recovery

Both [native assignment desktop/phone cases](../card-assignment.md#executed-native-assignment-producer-and-private-inbox)
pass actual keyboard author commands and original-key/body receipt recovery,
recipient MUI/private live delivery, typed actor/recipient/Card attribution and
canonical links. Unassignment retains the first notification; reassignment
produces a distinct second identity; assigning the actor adds no notification.
Observed private creation identities equal the persisted journal, and Board
membership removal withdraws native/HTTP inbox disclosure without extra events.
Author and recipient tagged Axe/overflow checks pass.

This strengthens NOTIFY-FR-009/011/012. Compiled Production runtime with optional
fixture verification does not establish current retained-image identity, strict
verification or complete role/concurrency/capacity/producer/Definition of Done
acceptance. Estimated PRD-17 work remaining stays **21%**; PRD-11 stays **35%**
(planning estimates); neither issue is closed.

## Executed native current-List notification boundaries

The complete `watch-notifications.spec.ts` case passes on 2026-10-08
(1 passed, 53.5 seconds, exit 0), with both 1280px desktop and 390px phone
recipient inboxes. All existing overlapping Board/List/Card deduplication,
author suppression, keyboard watch/unwatch, peer MUI edit and notification/read
assertions remain. After all original subscriptions are removed, the recipient
uses actual MUI Board-canvas controls to subscribe only to the original List.

A peer creates a Card in that watched List and another in an unwatched List.
The original Card moves out of the watched List and receives a further edit
outside it. Exactly one new CARD_CREATED notification identifies the watched
creation and issuer; unwatched creation, move-out and outside edit create none.
Moving the original Card back into the watched List adds exactly one CARD_MOVED
notification with its canonical Card link and issuer. Both open native inboxes
recover the exact two-then-three total articles and creation/move captions
without manual reload. The issuer inbox stays empty. Tagged Axe checks pass on
both native views. These assertions establish the List relationship at each
actual triggering event rather than retaining its former List relationship.

The first invocation timed out in its existing three-minute budget before the
new movement assertions completed. Its retained live trace showed a wait during
earlier watching activation; it is not acceptance. The fixture now brings each
keyboard target's page to the foreground, uses the existing enabled-focus
admission helper and bounds its single Enter action to five seconds. The new
List controls open from the Board canvas rather than underneath Card detail.
There is no command retry, longer overall deadline or authorization/effect-count
relaxation. The subsequent complete invocation passes the existing and new
assertions together.

The current MUI production bundle, read-only compiled Production API/separate
scoped Worker, restricted PostgreSQL 17/pgvector schema 112 and Nginx establish
local compiled evidence. Fixture accounts use the optional-verification browser
policy. All owned containers/database were removed, preserving original services
and data. Browser typechecking and documentation-link/diff checks pass. This
strengthens NOTIFY-FR-006/008 and native reconciliation; it does not independently
inspect these movement notification WebSocket frames, prove cross-Board or
concurrent transitions, strict policy, capacity or current retained-image/full
Definition of Done acceptance. Estimated PRD-17 work remaining is now **20%**
(planning estimate); the issue remains open.

## Executed private live watched-List identities

The complete native watch case passes again on 2026-10-08 (1 passed,
54.6 seconds, exit 0), now with actual desktop and phone SignalR frame
observation. Both inboxes receive an empty private snapshot before the first
peer edit. Creation/read delivery is observed before self-suppression/unwatch
checks. Desktop later navigates to the Board to resubscribe only the List and
returns; the fixture waits for its newly admitted private snapshot before
triggering the watched creation/movement commands. Phone stays subscribed.

Every observed snapshot/event matches the Organization and recipient;
Notification event metadata is empty. Both consumers' canonical event IDs match
the complete persisted journal in order: creation, read, watched creation,
move-in creation. The continuously subscribed phone receives exactly those
four frames. Desktop history is deduplicated by event identity across its
explicit navigation/recovery; repeated observed IDs must retain their type.
The original exact inbox totals, actor/type/link attribution, unwatched creation,
move-out/outside-edit suppression, self-suppression and Axe checks still pass.

The first added-frame invocation passed native inbox/journal assertions but
missed one desktop creation frame after returning to the inbox. HTTP recovery
can establish current rows before its private feed is admitted. Waiting for the
new actual snapshot before the triggering command yields the subsequent full
pass without relaxing event identities/counts or repeating mutations. Failed
trace evidence remains retained and is not acceptance.

The compiled-runtime topology, optional fixture verification and current-image
limitations match the preceding List-boundary record. All owned containers and
cloned database were removed; original services/data remain. Browser typechecking,
documentation links and staged diff checks pass. This supersedes the prior lack
of independent watched-List notification frame proof and strengthens
NOTIFY-FR-006/008/012 and AC-NOTIFY-17-03. Current immutable images, strict policy,
cross-Board/concurrent transitions, capacity, remaining producers and full
Definition of Done still govern closure. Estimated PRD-17 work remaining stays
**20%** (planning estimate); the issue remains open.

## Executed native cross-Board watch relationships

The new mandatory `watch-cross-board-notifications.spec.ts` passes on
2026-10-08 (1 passed, 58.3 seconds, exit 0). Actual account registration,
invitation acceptance and explicit peer grants establish two private Boards.
The Organization-owner recipient changes watches through keyboard MUI controls;
a different admitted member issues real HTTP Card edits/cross-Board moves.
Desktop and 390px phone inboxes stay open with actual private live subscriptions.

Source Board and List watches produce no notification when the Card moves into
an unwatched destination Board. Subscribing to the destination Board produces
one notification for the next peer edit. Moving back into overlapping watched
source Board/List scopes yields one move notification. A direct Card watch is
then created, source Board/List watches removed, and the Card moved to the
watched destination Board: direct Card/destination Board overlap yields one
notification. The direct subscription retains its original ID, watching state
and version 1 after crossing Boards. Removing the destination Board watch leaves
the direct Card watch active; the next edit delivers one notification.

Both native inboxes receive exactly four creation frames with Organization and
recipient admission, Notification entity type and empty metadata. Their event
IDs equal all four persisted private journal identities. Stored notifications
contain two move and two edit types, the peer actor and intended recipient;
all four returned/native Card links use the final destination Board. The peer
inbox remains empty. Tagged Axe and horizontal overflow checks pass on both
recipient views. These finite scenarios establish current triggering-Board
relationships, direct Card identity and overlap deduplication, not scale or
concurrent/complete role/visibility admission.

The first invocation failed the source List watch activation without sending a
List write. A second fixture incorrectly required initial automatic dialog focus
on Check; that initial read does not promise this focus. The final fixture
explicitly issues a read-only Check, waits for owned focus/current workspace
admission, sends one mutation and requires its real successful response and
post-command focus. Both failed invocations remain retained as diagnostics.
The full third invocation passes without retrying commands, relaxing recipient
counts/permissions or extending the three-minute case deadline.

Topology: current MUI production bundle, read-only compiled Production API and
separate scoped Worker, restricted PostgreSQL 17/pgvector schema 112 and Nginx.
Fixture verification policy is optional, matching the CI browser phase. All
owned containers/database were removed and original services/data preserved.
Browser typechecking and documentation links/staged diff checks pass. This
strengthens NOTIFY-FR-005/007/008/012 and AC-NOTIFY-17-03. Current immutable-image,
strict-policy, complete role/visibility and concurrent/capacity/producer/Definition
of Done acceptance remain. Estimated PRD-17 work remaining is now **19%**
(planning estimate); the issue remains open.

## Executed ordinary-member cross-Board withdrawal

Both complete cross-Board watch cases pass together on 2026-10-08 (2 passed,
exit 0). The existing Organization-owner case is retained. A second case uses
an independently registered Organization administrator, real recipient/issuer
invitations and explicit MEMBER grants on both private Boards. The ordinary
recipient performs the same native keyboard watch changes; peer HTTP edits and
moves produce the same exact four desktop/phone notifications/private creation
frames and persisted journal identities. Direct Card subscription ID/version
survive movement, overlapping Board/List/Card relationships deduplicate, actor
self-delivery stays suppressed, and final Card links recover the destination.
Both views pass tagged Axe and overflow checks.

Afterward the administrator actually removes the Member's destination Board
grant. The recipient's direct Card watch read returns 404, HTTP inbox is empty,
and both open native inboxes withdraw all four articles. A still-admitted peer
then performs another real edit and the separate Worker completes readiness;
no recipient article/event is added. A read-only administrative fixture query
compares the complete persisted recipient notification and private journal rows
before withdrawal and after the later edit. Their fingerprints and exact counts
of four notifications/four events remain unchanged. This verifies retained
history and producer eligibility separately from hidden inbox disclosure.
No accounts, grants, watches, moves or notifications are injected in SQL.

The complete two-case invocation uses the current MUI production bundle,
read-only compiled Production API/separate scoped Worker, restricted PostgreSQL
17/pgvector schema 112 and Nginx with optional fixture email verification.
All owned containers/database are removed and original services/data preserved.
Browser typechecking and documentation links/staged diff checks pass. The normal
full CI browser suite includes both cases. This extends the earlier Owner-only
native boundary and strengthens NOTIFY-FR-004/008/012 and mid-session withdrawal.
It does not prove every role/visibility, strict-policy or concurrent transition,
capacity, current immutable-image or complete Definition of Done acceptance.
Estimated PRD-17 work remaining is now **18%** (planning estimate); the issue
remains open.

## Executed strict verified-account cross-Board watches

The same Owner and ordinary-Member cross-Board matrix passes together under
strict email verification: **two cases, 2.0 minutes**, with no failed cases.
Every fresh account first receives HTTP 403 `email_verification_required` on
login, then succeeds after verification. Where the Production response keeps
the token private, the isolated CI fixture activates only the newly registered,
UUID-validated account. This is account admission setup, not email-provider
delivery evidence; the separate identity-mail phase tests actual delivery.

The actual API and separate scoped Worker both report verified-email policy
`true`. All existing keyboard watch/unwatch, current source/destination
relationships, direct Card subscription identity, overlapping watch deduplication,
actor suppression, four canonical notification/private-event identities across
desktop and phone, moved links, ordinary-Member grant withdrawal and retained
history assertions pass unchanged. Tagged Axe and overflow checks pass.
Execution uses the current compiled Production services and MUI bundle against
restricted PostgreSQL 17/pgvector schema 112. Owned containers and the disposable
database are removed; original running services and stored data are preserved.

The build-once CI now runs this matrix before restoring optional verification
for the full browser suite. It explicitly checks API and Worker policy `true`
and reuses the loaded release images, including scoped Worker recreation.
The identity-test overlay makes the Worker's existing strict default explicit;
production defaults and architecture are unchanged. Browser typechecking passes.
Current retained-image execution/full CI, remaining role/visibility and producer
matrices, concurrent transitions, capacity and complete Definition of Done
remain unproven. PRD-17 stays open at **17% estimated work remaining**.

## Executed role and visibility reader watch matrix

The corrected complete cross-Board invocation passes **five cases in 5.2 minutes**:
Organization Owner, explicit Board ADMIN and MEMBER on Private Boards, and
Organization members without Board grants viewing Organization or Public Boards.
All use strict verified-email admission and the separate scoped Production
Worker. ADMIN/MEMBER grants are made through real HTTP commands and their
returned roles asserted. Read-only viewing follows visibility rules; no new
Viewer role is introduced. Anonymous public visitors are outside this personal
watch fixture because watching requires active Organization membership.

Each visibility reader's actual Board access allows viewing and denies editing
and administration. A real Card edit returns neutral 404 `card_not_found`, and
the canonical Board snapshot proves its title/version unchanged. Those readers
then perform the same native keyboard Board/List/Card watch and unwatch matrix.
Peer actions produce four exact notifications and private creation identities
on desktop and phone, with overlap deduplication, actor suppression, direct Card
subscription continuity and current destination links. Tagged Axe/overflow pass.

For ADMIN/MEMBER the Owner removes the destination Board grant. For visibility
readers the Owner changes the destination to Private at its current version.
Direct Card watch reads return 404, authorized HTTP inboxes become empty and
both already-open native inboxes withdraw every article. A subsequent admitted
peer edit adds no recipient notification/event. Full stored recipient
notification/journal fingerprints and four/four counts remain unchanged.

An initial fixture attempted an unsupported Viewer grant; a subsequent reader
fixture attempted an unsupported direct Card read. These failed invocations
are not acceptance evidence. The final fixture follows canonical ADMIN/MEMBER
roles and reads the Board snapshot, and passes all five cases together without
relaxed deadlines, authorization rules, counts or historical comparisons.
Browser typechecking passes. Owned containers/database are removed and original
services/data preserved. Current compiled Production execution does not prove
current retained-image/full CI, every role/visibility combination, concurrent
transitions, full configured producer matrix or capacity. Both strict CI and
optional full-suite phases include these five cases. PRD-17 remains open at
**16% estimated work remaining**.

## Executed native watch re-admission and retained history

The complete five-case cross-Board invocation passes again with **five cases in
6.1 minutes**, no failed cases. Owner remains the control case. Board ADMIN,
MEMBER and Organization/Public visibility readers now actually regain access
after the previously proved withdrawal and skipped peer activity. The Owner
re-grants the same canonical Board role, or restores the destination's original
visibility at its current version; no retained rows are seeded or reconstructed.

Each restored recipient reads the original direct Card subscription ID, version,
creation/update clocks and `watching=true` at the current destination. All four
original HTTP notification rows return identical, and the full
stored notification/private-journal fingerprints and four/four counts remain
unchanged by re-admission. Both already-open native inboxes recover four articles
without reload. Keyboard opening/checking/closing the Card watching dialog shows
the retained watching state, returns Check focus, and leaves stored history
unchanged. No additional watch mutation is needed to recover retained intent.

A subsequent admitted peer edit then creates exactly one new `CARD_UPDATED`
notification, with the actual actor/recipient/Card and canonical destination
link. Both inboxes show five articles. Original four notification rows and four
canonical journal envelopes remain identical. The new canonical event identifies
the new notification and reaches each actual recipient-private transport exactly
once; each observed event-identity set equals the five stored journal identities.
Tagged Axe/overflow checks pass again and the actor inbox stays empty. The edit
performed while access was unavailable remains unnotified.

This executes current compiled Production API/separate scoped Worker, strict
verified-email admission, current MUI bundle and restricted PostgreSQL 17/pgvector
schema 112. Browser typechecking passes. Owned containers/database are removed;
original services and data remain. Both mandatory strict CI and optional full
browser phases run the extended cases. Current retained-image/full CI, complete
role/visibility and producer matrices, concurrent transitions, capacity and full
Definition of Done still govern closure. PRD-17 remains open at **16% estimated
work remaining** (planning estimate).

## Executed all thirteen configured watch producers

`watch-activity-matrix.spec.ts` passes its complete final case in **1.0 minute**
under strict verified-email admission. A real Organization Owner enables Board
watching through keyboard MUI controls. A real invited Member with a current
Private Board grant also watches that Board and issues thirteen actual HTTP
commands. No event, watch, Card or notification is inserted as test data in SQL.
Disposable account activation follows the separately documented strict-admission
fixture and is not provider-delivery evidence.

The exact configured set is `CARD_CREATED`, `CARD_COPIED`, `CARD_UPDATED`,
`CARD_MOVED`, `CARD_MEMBER_ADDED`, `CARD_MEMBER_REMOVED`, `LABEL_ADDED`,
`LABEL_REMOVED`, `CARD_DATE_CHANGED`, `CARD_DUE_COMPLETED`, `CARD_DUE_REOPENED`,
`CARD_ARCHIVED` and `CARD_RESTORED`. Thirteen stored recipient rows include each
type once. Actor/recipient/Card/Board and unread state match the authorized inbox.
Creation clocks compare against complete stored rows at their full precision,
normalizing only equivalent UTC notation and trailing fractional zeros. A read-only
source-event join verifies all thirteen exact source actors, entities, Boards,
revisions, types and clocks. The Member assigns/unassigns themselves, so the other
watcher receives member activity while the actor remains excluded; zero persisted
actor notifications and an empty actor inbox prove actual matching-watch suppression.

Both native desktop and 390px inboxes show thirteen distinct fixed captions,
twelve canonical links to the original Card and one to the copied Card. During
original Card archive, only the copy remains visible in HTTP/native inboxes and
the current-admission journal. Each actual private stream reaches cursor 12 while
withholding the archive event. Restore reveals all retained notifications and
the thirteen-event canonical journal, while each stream has exactly the twelve
identities visible when delivered. This distinguishes retained publication from
permitted disclosure. Tagged Axe and overflow checks pass on both clients.

All thirteen original commands are replayed after restoration with their exact
keys and bodies. Each returns its original response text; complete stored Card,
notification and private-journal fingerprints and the authorized inbox remain
unchanged. Initial setup failed because its modal workspace locator omitted hidden
regions; the final fixture follows existing admission checks without relaxed
deadlines, roles, counts or retries. Browser typechecking passes.

Execution uses current compiled Production API, a separate scoped Worker, current
MUI bundle and restricted PostgreSQL 17/pgvector schema 112. Owned containers and
database are removed; original services/data preserved. The build-once strict CI
phase runs this case alongside cross-Board watches, and the optional full suite
also includes it. Current retained-image/full CI, complete role/visibility and
producer interaction matrices, concurrent publication/rollback, capacity and the
complete Definition of Done remain required. PRD-17 stays open at **15% estimated
work remaining** (planning estimate).

## Executed complete private activity envelopes

The complete thirteen-producer case passes again in **1.0 minute**, now retaining
the entire actual desktop/phone private event envelopes. Each creation payload
has exactly the twelve approved keys: event/type, actor/recipient, Organization/
Board, Notification entity/type, version, decimal sequence, creation clock and
empty metadata. Extra payload fields are rejected by the test. Actual actor and
Board match the admitted issuer/scope, version is one and sequence is a decimal
string. UTC comparison preserves every significant fractional digit.

A read-only query retrieves all thirteen complete persisted private journal rows.
Every canonical HTTP envelope matches stored event/type, actor/recipient,
tenant/Board, notification ID, version, sequence and metadata. Sequences are
exactly 1–13; each envelope's creation clock equals both its persisted journal
clock and the corresponding notification clock. The twelve envelopes actually
disclosed to each connected client match the complete authorized HTTP envelopes,
normalizing only equivalent UTC spelling/trailing fractional zeros. The retained
archive event remains withheld at cursor 12 until fresh authorized recovery after
restore, preserving the earlier current-parent admission behavior.

All existing producer counts, captions, links, matching actor-watch suppression,
original-key replies, unchanged Card/notification/journal fingerprints and tagged
Axe/overflow checks pass unchanged under strict verification. Browser typechecking
passes. Owned runtime/database are removed and original services/data preserved.
This strengthens NOTIFY-FR-002/012 and AC-NOTIFY-17-03 for configured watched
activity. Current retained-image/full CI, exact attribution/clock proof for the
other producer families, complete role/visibility/interaction/concurrent rollback
and capacity acceptance remain required. PRD-17 stays open at **15% estimated
work remaining** (planning estimate).

## Executed complete activity bulk-read recovery

The thirteen-producer native case additionally passes a real bulk read in
**1.2 minutes** under strict verified-account policy. Keyboard selection on the
390px phone selects all thirteen actual unread notifications. The server commits
the read before the fixture replaces its successful response with a 503. The
desktop observes zero unread while the phone retains a recoverable command.
Native retry submits the exact original IDs, body and idempotency key and returns
the identical successful acknowledgment; focus returns to Refresh notifications.

Both clients retain thirteen articles, show zero unread and have no remaining
individual read buttons. All thirteen notifications share one persisted first
read clock. Read-only queries compare every complete HTTP read envelope against
its stored private journal row and notification read timestamp at full significant
UTC precision. Read actors are the recipient, versions are two, sequences are
14–26 and metadata is empty. The original thirteen creation envelopes remain
identical. Each actual private stream contains exactly thirteen matching complete
read transitions after its twelve admitted creation envelopes; the archived
creation event remains withheld during the earlier denial.

A further fresh-key read of the already-read selection returns the identical
acknowledgment and leaves full Card/notification/journal fingerprints and the
26-event canonical journal unchanged. Tagged Axe and overflow checks pass on
desktop and phone, and browser typechecking passes. Owned containers/database
are removed; original running services and stored data remain intact. This
strengthens NOTIFY-FR-002/003/012, AC-NOTIFY-17-03 and PRD-17-TC-06/07/08/11/12.
Current retained-image/full CI, other producer attribution, complete role and
interaction matrices, concurrent publication/rollback and capacity acceptance
remain required. PRD-17 remains open at **15% estimated work remaining** (planning
estimate).

## Executed complete attribution across notification producer families

The final assignment, selected-mention and actual due-fire cases pass at both
1280px and 390px (six passes). The repaired group-mention cases then pass together
in **1.8 minutes**. Together these eight cases use the final read-only persistence
oracle in `persistedNotificationDelivery.ts`. An initial full eight-case invocation
passed in 8.1 minutes; after explicit stored-sequence comparison was added, the
next invocation passed six but failed two group cases before their persistence
checks (retry focus and later draft entry). The group fixture now foregrounds the
author and establishes current workspace admission and enabled keyboard focus
before its single Review/Add keypresses. Automatic retry focus, original request
keys/bodies, deadlines, counts, permissions and quotas remain asserted unchanged.

Each admitted inbox row matches complete persisted recipient/actor, historical
Board, stable Card, notification type, canonical link and creation/first-read
clocks at full significant UTC precision. Every notification joins its actual
Work source by tenant/event, actor, Board, source type and creation clock. Card
sources additionally match Card identity/revision; fired reminders instead match
the actual typed Reminder identity, Card and fired revision. No notification,
source event, timestamp or journal record is fabricated by the oracle.

Complete canonical creation/read envelopes match all persisted journal fields,
including event identity/type, actor/recipient, tenant/Board, Notification entity,
version, decimal sequence, timestamp and empty metadata. Exact twelve-key guards
reject extra private payload fields. Actual private streams equal the complete
canonical envelopes, normalizing only equivalent UTC spelling/trailing zeros.
Group streams compare the three newly delivered mentions; the assignment created
before subscription is independently verified in the inbox/source/journal. Both
reminder clients compare the same creation/read pair. Existing native recovery,
self-suppression, canonical links, withdrawal, tagged Axe and overflow checks pass.

Execution uses current compiled Production API, separate Worker, current MUI,
restricted PostgreSQL 17/pgvector schema 112 and Nginx. API fixture verification
is optional; the reminder Worker requires verification and its fresh reminder
accounts are verified. This does not establish strict policy for the other
families or email-provider delivery. Owned fixtures are removed and original
services/data preserved. Browser typechecking passes. Alongside the thirteen
configured-watch-producer proof, this supersedes the local exact-attribution gap
for NOTIFY-FR-002/009/010/012. Current retained-image/full CI, complete role and
interaction matrices, concurrent publication/rollback, capacity and the entire
Definition of Done remain required. PRD-17 stays open at **15% estimated work
remaining** (planning estimate).

## Executed strict verified-account notification producer families

All eight assignment, selected-mention, confirmed-group and actual reminder-fire
cases pass together in **8.2 minutes**, at 1280px and 390px. Both running API and
scoped Worker explicitly require verified email. The shared account fixture first
requires each fresh account's real login to return 403
`email_verification_required`, then verifies the account and requires login 200.
It uses the actual verification endpoint when a token is available; otherwise a
CI-only canonical-UUID guard permits activation of only the freshly registered
disposable account. This fixture does not prove email-provider delivery; that
contract retains its own earlier required CI phase.

Every original producer, lost-response key/body, recovery focus, quota and role,
self-suppression, current withdrawal, canonical-link, tagged Axe/overflow and
complete persisted source/notification/private-envelope assertion passes unchanged.
The same final persistence oracle compares exact journal sequences and full
significant creation/first-read clocks. Real reminders fire through the separate
Worker; both private clients compare complete creation/read pairs. No test
fabricates notification, source, journal or reminder-fire effects.

The required build-once CI now runs these eight cases in a dedicated strict phase
after strict watch delivery and before the optional-verification profile fixtures.
The step checks the actual API/Worker settings and reuses the same loaded immutable
images. Earlier optional-policy runs remain historical evidence; this invocation
supersedes their local verified-account admission gap for these producer families.
Browser typechecking passes. Local execution still uses current compiled Production
services/MUI and restricted PostgreSQL 17/pgvector schema 112. Owned fixtures are
removed and original services/data preserved. Current retained-image/full CI,
complete role/interaction/concurrent rollback and capacity matrices, provider
acceptance and the full Definition of Done remain independently required. PRD-17
stays open at **15% estimated work remaining** (planning estimate).

## Executed actual watch and activity command ordering

`watch-trigger-order.spec.ts` passes all six ordering scenarios in one
**1.1-minute** strict verified-account invocation: CARD, LIST and BOARD personal
watches, each with unwatch first and activity first. Fresh Owner/Member accounts
use actual registration, refused pre-verification login, post-verification login,
Organization invitation/acceptance and explicit private Board membership. Actual
HTTP commands create the List/Card and each watch; no subscriptions, notification
rows or source events are fabricated.

A separate PostgreSQL transaction holds only the real Board row lock. The first
actual API command is observed waiting, then the second is observed waiting;
`pg_blocking_pids` independently confirms the queued peer blocker before release.
This establishes command order without timing assumptions. When unwatch precedes
the Card edit, the actual source version creates zero recipient notifications.
When activity precedes unwatch, that exact source version creates one retained
notification. Across all three scopes, six real edits advance the Card to version
seven and produce exactly three notifications. Watch identity/creation clock
survive re-enablement, and final watch versions are four with watching false.

Every original edit and unwatch key/body is replayed after its pair. Replies
match original full response texts, and full recorded Card/watch/notification/
private journal/counter/Work-event/audit/job/receipt fingerprints remain unchanged.
Final authorized inbox and canonical journal match complete stored notifications,
real Card source actor/revision/type/clocks and exact private creation envelopes.
This is HTTP/PostgreSQL concurrency proof; it adds no native keyboard/mobile,
private transport, rollback, permission-loss or scale acceptance claim.

The required build-once CI adds a dedicated strict observed-wait phase before
optional profile fixtures; the full browser suite also collects the case. Browser
TypeScript and script syntax checks pass. Local execution uses current compiled
Production API, current MUI/Nginx and restricted PostgreSQL 17/pgvector schema 112;
no Worker is needed for this originating-transaction proof. Owned runtime/database
are removed and original services/data retained. This strengthens
NOTIFY-FR-004/005/006/007/008/012 and PRD-17-TC-07/08. Current retained-image/full CI,
complete role/visibility/concurrent permission and movement matrices, rollback,
capacity and the entire Definition of Done remain required. PRD-17 stays open at
**15% estimated work remaining** (planning estimate).
