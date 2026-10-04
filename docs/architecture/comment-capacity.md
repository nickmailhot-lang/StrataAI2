# Comment read capacity (PRD-15, pending execution)

The mandatory release capacity step now reuses the existing actual supported
parent fixture: 200 Lists, 5,000 active Cards and 100,000 archived Cards. After
the Checklist and activity checks it adds 100,000 synthetic comments to the
selected active Card, with tied creation timestamps and the normal immutable
ownership/revision guard enabled. The oldest comment is redacted through the
valid guarded revision transition. This is scale setup, not evidence of 100,000
audited commands or an API deletion; real author mutation/redaction acceptance
uses the separate comment command and native browser scenarios.

Actual reads run through the restricted release API and Nginx. The fixture
requires two 50-row pages containing 100 distinct identities, correct tenant,
Card and author scope, and no-store headers. A versioned cursor constructed from
the second-oldest persisted source checks the final one-row redacted tombstone
with no body and no continuation. This does not claim that all intermediate
2,000 pages were enumerated. The Card version comes from the admitted API page.

Twenty first-page HTTP samples are retained as milliseconds and nearest-rank
p95. A before/after digest covers the full selected Card and all comment rows;
event/audit/job/receipt counts must also remain unchanged. The scale reads must
not mutate state or publish effects.

After that read-only boundary, twenty actual authenticated comment creates run
serially through the exact API/Nginx images, with independent retry keys and
the expected current Card revision. Every reply must confirm the correct
scope/author, new stable comment identity and exactly one Card revision advance.
Database checks require exactly twenty new comments, comment events, audits,
body-free revision snapshots and receipts, plus twenty Card revision advances.
This distinguishes real authoritative commands from the synthetic scale setup.

The original first request is replayed after all twenty changes. It must recover
the same acknowledgment and leave the complete comment/Card digest and
publication counts unchanged. The pre-mutation page cursor must now return the
stable version-conflict response without items. The retained verification flags
therefore cover exact recovery and stale-pagination reconciliation as well as
bounded read correctness.

The PRD server mutation acknowledgment target is a mandatory nearest-rank
**p95 below 500 ms** over the twenty complete HTTP responses. The documented
condition is one serial authenticated client, no intentional network latency
and no concurrent user commands, in the existing release CI topology. Reports
include numeric mutation samples/p95 and that fixed condition, without request
keys, bodies, identities or receipt payloads. A failure of the timing budget,
scope/version admission, atomic effects or recovery fails the release step and
does not emit a successful artifact. This is not a concurrent load-test promise.

The independent `comment-capacity-<revision>` artifact contains fixed fixture
sizes, page lengths/verification flags, numeric timings and the immutable build
revision only. Comment bodies, identities, authors, profiles, captions, SQL and
cursor tokens stay in temporary fixture files and do not enter retained evidence.
Existing Checklist/activity artifacts remain separate. A failure produces no
successful comment artifact and fails the mandatory release capacity step.

Local shell syntax and diff checks verify fixture construction. Linux execution
against the immutable release topology remains required before claiming either
read capacity or the mutation budget passed. These measurements do not establish
browser rendering, cached Card opening or movement feedback. Native mobile/keyboard/two-client,
permission/lifecycle/retention and complete PRD-15 acceptance remain separate.
# Verified release evidence

Run [37190533527](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37190533527)
passed the mandatory capacity step on immutable release images at revision
`4ab794d5dfd354f81b5037609f80e4d54cb4a65a`. Retained artifact
`comment-capacity-4ab794d5dfd354f81b5037609f80e4d54cb4a65a` (ID 11299217162)
was downloaded and inspected: 200 Lists, 5,000 active Cards, 100,000 archived
Cards and 100,000 seeded comments; 50/50/1 scoped pages, distinct seek results,
final redaction, no-store and unchanged read state. Twenty serial actual HTTP
comment commands passed atomic publication, exact retry with unchanged state,
and stale-cursor refusal. First-page p95 was **34.345 ms** and command p95 was
**57.554 ms**, below the gated 500 ms mutation budget under the artifact's fixed
single-client/no-intentional-latency condition. These are API timings, not browser
or concurrent-user measurements. The whole run's native/runtime gate was still
in progress when this evidence was recorded.
