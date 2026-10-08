# Notification and watching acceptance map — PRD-17

[PRD-17](https://github.com/nickmailhot-lang/StrataAI2/issues/18) remains open at
**21% estimated work remaining** (planning estimate). Earlier estimates below
record their evidence scope. This maps all twelve
functional requirements to implementation contracts and required completion
evidence. A feature document or isolated passing scenario does not close a row.
The adopted MUI/API/separate Worker/PostgreSQL architecture is unchanged.

## Functional requirements

| Requirement | Implementation and evidence source | Required completion evidence |
| --- | --- | --- |
| NOTIFY-FR-001 notification center | [Bounded MUI inbox](notification-inbox.md), account admission, paging, empty/error states and read recovery | Current immutable-image native desktop/phone execution, keyboard/accessibility and account/membership withdrawal |
| NOTIFY-FR-002 recipient, actor, type, link and clocks | Persisted notifications and current authorized links; [assignment source](assignment-notifications.md) and strict browser parsing | Exact stored attribution/clock comparison across assignment, mention, watched activity and due-reminder producers; moved/deleted parent admission |
| NOTIFY-FR-003 single and bulk read | Recipient-bound read commands, original-key recovery and immutable first-read timestamp; [inbox contract](notification-inbox.md) | Full restricted API/current-image read, rollback, overlapping command, paging and lost-response/browser cases |
| NOTIFY-FR-004 CARD/LIST/BOARD watch/unwatch | [Personal watch contracts](watch-subscriptions.md), retained subscription identity, version and original receipt | Current-image native watch lifecycle, permission/account withdrawal, retry, movement and parent transitions |
| NOTIFY-FR-005 watched Card changes | [Configured activity matrix](watch-activity-notifications.md), originating command transaction and post-mutation revision | All configured producers, current eligibility, source/journal/job atomicity and native producer-to-inbox delivery |
| NOTIFY-FR-006 current List and newly created Cards | Same producer selects current List watchers and includes Card creation | Actual creation/movement boundary cases, concurrent watch/access transitions and exact historical attribution |
| NOTIFY-FR-007 Board-wide Card activity | Same producer selects current Board watchers and deduplicates overlapping scopes | Full activity matrix and current-image 500-recipient correctness/latency fixture; scale Worker delivery remains distinct |
| NOTIFY-FR-008 relationship at triggering event | Post-mutation current List/Board attribution; direct Card watch follows identity | Watched/unwatched source/destination movement matrix, real concurrent admission and native reconciliation |
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
matched to the current revision and policy; local browser policy permits
unverified fixture accounts and does not certify strict verified-email admission.

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
