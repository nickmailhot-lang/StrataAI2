# Card description Markdown — PRD-09

[PRD-09](https://github.com/nickmailhot-lang/StrataAI2/issues/10) remains open at
**20% estimated work remaining** (planning estimate). This implements
DETAIL-FR-008/009 and verifies a focused AC-DETAIL-09-03 consumer path; it does not
certify the whole Card Detail ticket or its dependencies.

## User behavior and original source

Active Card details retain the original description in the MUI text editor and
show its formatted preview immediately below. Supported syntax includes headings,
bold, italic, links, ordered/unordered lists, inline/fenced code and blockquotes.
The editor has six scrollable rows, so long source text does not push every other
Card control below an expanding editor. No source is truncated. The preview
updates with the local draft; a remote revision does not replace a dirty draft,
and conflict recovery retains the existing version/confirmation rules.

Save persists the original Markdown string through the existing Card command,
receipt, revision and `CARD_UPDATED` source. Formatting does not rewrite storage
or create a redaction/retention record. Authorized archived details use the same
renderer, with the original retained description still available through the
existing authorized archive API. Empty descriptions show an explicit empty state.
Underlying Board context, Card routes, focus restoration and existing source
permissions remain authoritative. Another authenticated session receives the
saved description through the real Worker/live Board path without a reload.

## Rendering and security boundary

The pinned [react-markdown renderer](https://github.com/remarkjs/react-markdown#security)
parses to React nodes. Raw HTML processing is disabled; there is no HTML insertion
or executable-content plugin. A fixed element whitelist and explicit URL policy
permit HTTP(S), mailto and safe relative links, with `noopener noreferrer nofollow`
for opened links. Executable/unsupported schemes become inert text. Headings are
nested under Card detail, and native semantic markup is styled through a single
MUI theme container. Code remains literal text. Images render their textual
alternative without an image element or automatic external request.

The internal shell initializes only fixed public grammar and discards that tree
before producing any DOM. This caches no Card/profile/source values. The actual
description renderer receives only its current source string; changing source
replaces the old preview, and permission/session withdrawal removes the protected
page through its existing admission boundaries.

## Executed verification and retained failures

- The complete frontend invocation passes **2,536/2,536** tests with two local
  workers, original deadlines, no skipped/failed cases and all **2,004** captured
  tracked-source hashes matching. Earlier complete source results retained two
  failures: a lifecycle mock invoked before subscription and a cached-dialog
  timeout. Waiting for the actual mock subscription fixes the reproduced paging
  prerequisite; the dialog test passes unchanged. The full result precedes only
  the later read-only drag-state marker and capacity fixture change; all 25
  focused rendering/editor/drag cases pass after those changes.
- Both maintained desktop/mobile description cases pass with independently
  authenticated peer sessions, original persisted source/version, automatic
  peer delivery, exact archive source, zero image/embed requests, inert script
  and unsafe links, keyboard editing/focus, tagged Axe checks and session
  withdrawal. The independent peer session retains authorized access.
- The rich cached fixture has one List/Card and **466 source characters** at
  1280/390 pixels, warm assets and two observed initial Board reads. A single
  browser observation requires a visible, non-inert dialog, editable title and
  source, exact original source and rendered preview. The unchanged **<200 ms**
  target passes at **191.78/184.25 ms**; the complete two-case run takes 101.68 s.
  Earlier timing failures, ranging from about 204–306 ms, remain retained.
  Initial experiments mixed unsettled admission, background-page operation and
  serial automation observations into the measurement. CPU sampling additionally
  identified auto-sizing source-editor layout work. Neither the diagnostic nor
  any one adjustment establishes a sole root cause or performance repeatability.
- The original five normal cases, fixtures, assertions and timing limits remain
  unchanged and pass together in **156.72 s** with no retries/skips/flaky results:

| Scenario | Usable Board (<1500 ms) | Cached detail (<200 ms) | Feedback (<100 ms) | Mutation p95 (<500 ms) |
| --- | ---: | ---: | ---: | ---: |
| Desktop, 50 dated Cards | 945.59 | 170.48 | — | 87.32 |
| Desktop Kanban | 760.01 | 158.35 | 59.50 | 117.73 |
| Phone Kanban | 585.76 | 149.54 | 63.60 | 100.22 |
| Desktop List feedback | — | — | 39.60 | — |
| Phone List feedback | — | — | 41.10 | — |

All twenty mutation samples and nearest-rank p95 are independently checked.
Local compiled API/Worker run readonly through current Nginx/security headers,
restricted PostgreSQL 17/pgvector and all 146 migrations. Strict verified accounts
are used for the description cases; normal fixtures retain their original
optional-verification policy. Each successful browser capture matches **2,073
source/compiled hashes** and **four hosted web files**. Provenance explicitly
remains **unverified runtime**, not immutable release-image certification.
Owned fixtures/credentials are removed, preserving the original database.

## Supported-scale follow-up

The entire unchanged rank/concurrency fixture and all three 1280/768/390 capacity
cases pass with the description bundle in **177.23 s** for the browser phase.
The 200 Lists, over 5,000 active Cards and 100,000 archived Cards retain their
original scale, windowing, keyboard, focus, Back/Forward/Close, exact nested
scroll, responsive-width and Axe assertions. The final SQL count and complete
record fingerprint prove every archived Card unchanged after all three cases.

Two earlier runs timed out at the final pointer-transfer acknowledgment, on
different viewports. The second reproduced the problem on the idle machine.
The final fixture waits for the existing drag engine to commit its exact target
before pointer release; a read-only `data-card-drop-over` marker exposes that
state. No move, authorization or collision behavior changes. Original exact
write counts, revisions, persisted placement and all 150-second deadlines remain.
All 2,073 captured hashes match and owned cleanup completes. The earlier failures
remain separate; this does not prove a sole product root cause or physical-device
acceptance.

Browser types, lint, web build, fresh full solution build, all **213** integration/
shard guard regressions and the intact **388-case/144-file** collection pass.
New description cases are mandatory in the strict retained-image Board group
and in the complete four-shard browser collection. Omission of that dedicated
invocation fails its registry regression. Dependency review adds 79 locked
packages, changes no existing package version and reports zero audit findings
under the required npm 11.19.0 toolchain.

Current complete immutable-image CI, the complete role/lifecycle/recovery and
accessibility matrices, documented telemetry, performance repeatability and all
original dependencies/Definition of Done still govern closure. No acceptance
requirement is waived by these local results.
