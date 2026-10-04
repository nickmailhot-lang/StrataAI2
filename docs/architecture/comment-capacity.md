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

The independent `comment-capacity-<revision>` artifact contains fixed fixture
sizes, page lengths/verification flags, numeric timings and the immutable build
revision only. Comment bodies, identities, authors, profiles, captions, SQL and
cursor tokens stay in temporary fixture files and do not enter retained evidence.
Existing Checklist/activity artifacts remain separate. A failure produces no
successful comment artifact and fails the mandatory release capacity step.

Local shell syntax and diff checks verify fixture construction. Linux execution
against the immutable release topology remains required. These measured read
timings do not establish browser rendering, cached Card opening, movement
feedback or server mutation budgets. Native mobile/keyboard/two-client,
permission/lifecycle/retention and complete PRD-15 acceptance remain separate.
