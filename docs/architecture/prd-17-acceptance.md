# Notification and watching acceptance map — PRD-17

[PRD-17](https://github.com/nickmailhot-lang/StrataAI2/issues/18) remains open at
**22% estimated work remaining** (planning estimate). This maps all twelve
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
