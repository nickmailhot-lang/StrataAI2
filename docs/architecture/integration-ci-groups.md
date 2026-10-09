# Mandatory integration groups (ARCH-11)

The `container-integration` job runs four logical groups on separate GitHub-hosted runners. The complete browser group now uses [four intact-file partitions](browser-shards.md), for seven mandatory isolated executions overall, capped at four concurrent runners. Each group downloads the current run's `strataai-images-<sha>` artifact, verifies its archive checksums, loads the same web/API/Worker images, and starts its own fresh PostgreSQL/pgvector topology. No group builds application images. Tests remain sequential inside each group, preserving database wait observations and runtime configuration transitions.

| Group | Mandatory scope |
| --- | --- |
| `commands` | Runtime refusal cases, identity/Organization/Board/Work command contracts, restricted persistence and Worker delivery, supported-capacity measurements, and operator metrics |
| `browser-foundation` | Demo lifecycle, attachment transport/publication, contiguous replay, actual large-Board native capacity, and database-wait authorization |
| `browser-notifications` | Identity mail integration, accessible keyboard verification/recovery, strict Organization departure/account continuity, strictly verified watch/assignment/mention/reminder producers, desktop/phone inbox recovery, and watch/permission ordering |
| `browser-full` | The complete browser suite, automatic metadata and invitation authority routing, and API/edge abuse-limit contracts |

Every group first verifies the real Production authentication defaults, then explicitly applies the isolated self-registration/unverified-account fixture. Strict notification tests subsequently require verified accounts in both actual hosts. Both mail-dependent browser groups generate their own ephemeral signing keys and start their own private test mail provider. The full suite sets up its mail overlay and automatic Worker routing explicitly rather than inheriting state from another group's tests. Production rate budgets, browser timeouts, native gestures, expected versions, and whole-snapshot assertions are unchanged.

The matrix uses `fail-fast: false` and does not ignore errors. A failure leaves other groups running to retain their evidence. The stable `required-ci` job still requires the aggregate `container-integration` result to be `success`; release bundling therefore waits for all four logical groups and every full-browser partition, plus all existing source/build/security gates. GitHub documents matrix execution and failure handling in its [job variation guide](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations).

The first exact-image notification-group run at `cef54e18` fails the identity-mail test's initial inherited-account predicate (line 141). The old sequential profile phase had created an account; a fresh notification-group database has none. The mail test now creates its own account through actual registration and verifies its persisted row before the unchanged known/missing disabled-mail assertions. This makes the fixture's data prerequisite explicit rather than borrowing another group's state. The full mail and strict browser checks remain mandatory; their repaired exact-image execution is still required.

The focused bootstrap proof starts with zero users in a fresh PostgreSQL 17/pgvector schema-114 database, establishing that the former predicate fails. A current compiled Production API uses the restricted database role and the same explicit optional-verification/disabled-mail fixture. Actual registration returns 201 and persists exactly one owned account; both missing and known password-recovery targets return 503 with `identity_delivery_unavailable`. The first harness attempt encounters PostgreSQL's temporary initialization server; its readiness now requires the final TCP server before migrations. The corrected invocation passes and removes its owned containers/network while preserving existing services/volumes. This proves the data prerequisite and unchanged disabled-mail behavior; it does not establish complete identity provider/browser acceptance.

All 16 integration evidence scopes now stage [canonical metadata and payload checksums](artifact-evidence-provenance.md) before upload, preserving existing measurement schemas and absent-evidence behavior.

Generic browser and container diagnostic artifacts include the suite name to avoid upload collisions. Capacity artifacts have one owning group, including Card-copy evidence from the command phase. Each group collects failure diagnostics and tears down its own topology even when a check fails. The image artifact, security evidence, and release bundle retain their existing identity and build-once flow.

[`scripts/ci/integration-suites.json`](../../scripts/ci/integration-suites.json) records ownership for every named integration step. Source quality runs the [coverage verifier](../../scripts/ci/verify-integration-suites.mjs) and [mutation regressions](../../tests/integration-suites.test.mjs). They reject missing checks/groups, unknown conditions, ignored failures, broken mail prerequisites, artifact collisions, downstream image builds, and weakened aggregate gates. Add new checks to both the workflow and ownership registry; changing a command still requires reviewing its actual coverage.

Local validation:

```sh
npm ci
node scripts/ci/verify-integration-suites.mjs
node --test tests/integration-suites.test.mjs
```

The restructuring preserves all 95 previous integration steps: the former combined Production-policy/profile phase becomes an explicit common prerequisite plus the command group's unchanged profile test. Local comparison verified every previous test command, environment, shell, and action input, allowing only the two generic diagnostic artifact suffixes. Nineteen coverage/mutation tests pass locally, and Actionlint validates the workflow syntax and expressions. This initial validation is structural evidence; the scoped hosted execution evidence below supplements it. The complete matrix still needs successful exact-commit execution. No runtime reduction or full acceptance completion is claimed until those results exist. The current native capacity failure also remains open pending retained evidence and repair.

The first `browser-foundation` exact-image group at `cef54e18818bb78e98ec39342095e2bd7a13bf9b` succeeds in [job 113546668078](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37843466326/job/113546668078), including Demo lifecycle, attachment pipeline, contiguous replay, rank allocation/native capacity, and write-scope lock waits. The desktop and phone capacity cases both pass, taking 23.6 and 26.0 seconds respectively; the capacity browser phase reports two passes in 1.7 minutes. The test also verifies preservation of all 100,000 complete archived records.

Independently downloaded capacity artifact `11580895631` has ZIP SHA-256 `aa7ce7377090c8a9d67dc192adf371ab4e2ab21a17323f90153fedb45a054abf`. It contains the two numerical diagnostic documents and their Playwright attachment copies. Schema-1 diagnostics record 328 desktop/335 phone samples with zero dropped samples. Desktop horizontal scrolling has 145 calls, maximum absolute requested delta 9.380165289256198; phone has 128 calls, maximum 8.24561403508772. Vertical scrolling has 136 desktop/157 phone calls, with maximum absolute requested delta 9.289099526066348 in both. These are observed arguments, not an imposed application rate limit. This run supplies exact-image positive evidence and retained diagnostics; it does not establish the cause or repair of the earlier phone overscroll failure, success of other matrix groups, or full PRD acceptance. No timeout, native gesture, assertion, or test retry is changed to obtain this result.

The same `cef54e18` run's `commands` group also succeeds in [job 113546668048](https://github.com/nickmailhot-lang/StrataAI2/actions/runs/37843466326/job/113546668048), completing its mandatory identity, Organization/Board/Work, Worker authority, capacity and operator checks. [Organization command evidence](prd-03-acceptance.md#hosted-exact-image-organization-command-execution) records the exact scope and limits. This does not override the run's known notification-group failure or establish the still-running full browser group.

Related: [main-run evidence retention](ci-run-retention.md), [embedded build identity](build-identity.md), [documentation index](../README.md).

The mandatory enabled attachment pipeline now also deletes its actually published
source using canonical archive acknowledgment revisions. All three source
delivery routes and current-version restore must refuse admission; both original
and copied Board images must retain identical sanitized PNGs. Original selection
receipt recovery must survive the source tombstone while the Board is still
authorized. The existing complete two-viewport browser invocation and subsequent
Board archive/public/Private/session/retirement assertions remain. See the
[owned native equivalent and its provider/CI limits](attachment-object-storage.md#executed-enabled-pipeline-with-published-source-deletion).

The [complete default restricted PostgreSQL contract invocation](attachment-acceptance.md#complete-default-restricted-postgresql-contract-execution)
also finishes locally without narrowing arguments: zero exit, 67 source-fixed
completion messages, executed final-attempt scan recovery and the complete large
Organization deletion workload. Its fixture/HTTP/Worker-process/provider/current
immutable limits and independently checked cleanup are recorded separately.

Both enabled attachment pipeline viewports now also execute the actual published
source as a Card cover: PUBLIC refusal/consent, original receipt recovery, native
PNG rendering, anonymous sanitized bytes/private metadata denial and removal.
The [complete expanded native equivalent](attachment-object-storage.md#real-upload-through-worker-card-cover-browser-workflow)
passes 2/2 in 87.1 seconds and all subsequent HTTP checks. Its unchanged 150-second
case deadlines, complete invocation, original Board-background assertions and
current immutable/provider limits remain; canonical post-browser Card revision
is exactly 8. The subsequent private lifecycle-cover selection advances to 9,
source archive clears that cover in revision 10, and source deletion retains its
withdrawal in revision 11. The [selected-cover lifecycle extension](attachment-object-storage.md#actual-selected-cover-source-archive-and-deletion)
passes together with both viewports and the complete native HTTP pipeline; all
owned fixture resources are independently absent.

The same complete two-viewport invocation now also checks [live peer cover removal](attachment-object-storage.md#live-peer-cover-removal-and-temporary-refresh-recovery):
real Worker/SignalR sequence advancement, withdrawal of an already-rendered PNG,
canonical null cover and zero main-frame navigation in a second actual session
of the same account. The final native run passes 2/2 in 104.8 seconds and all
subsequent HTTP assertions. This does not establish distinct-account permissions,
source-archive live withdrawal or current immutable release success.

The [distinct-member extension](attachment-object-storage.md#distinct-board-member-live-cover-removal)
now creates and admits an actual different Board MEMBER through invitation and
membership APIs. Both complete native cases pass in 103.3 seconds, plus every
subsequent HTTP check and resource cleanup. The release shell supplies separate
credentials and the browser requires distinct actual profile IDs. Linux exact-image
execution and the remaining permission/reconnect/lifecycle matrix are pending.

## Strict notification consumer coverage

The assignment/mention/reminder phase now also selects the complete recipient
inbox recovery scenario. Its five explicit files and five grep alternatives
preserve the original eight producer cases and add one consumer case. The
existing API and Worker verified-email checks, rate pacing, sequential execution,
original scenario deadlines and all 110 registered steps remain. The complete
four-shard browser phase remains unfiltered.

The workflow verifier requires this exact producer/consumer command and both
host-policy checks. Four new negative mutations remove the inbox file, filter
out its case, disable strict account verification or omit the Worker policy
check. The previous verifier accepts these omissions (52 pass, four fail in the
expanded guard suite); the strengthened verifier rejects all four (56/56 pass).
Workflow syntax validation also passes. See the
[PRD-17 acceptance map](prd-17-acceptance.md#strict-notification-center-and-durable-read-recovery)
for executed native scope and remaining release requirements.

The strict watch phase also includes the full Board/List/Card personal-control
scenario alongside both existing producer files. Both strict account fixture
flags and API/Worker policy checks are required by the workflow verifier. Three
new omission mutations establish fail-before/pass-after guard coverage (59/59
checks pass after repair). See the
[native watch-control scope](prd-17-acceptance.md#strict-personal-watch-controls-and-native-stored-clocks).

## Strict Organization departure prerequisite

After identity mail and accessible verification/recovery, the notification group
now runs both complete Organization departure files with the strict account
fixture flag and normal rate pacing. The phase asserts verified-email
enforcement in the actual API and Worker before selecting all six native cases,
then precedes the existing watch and assignment/mention/reminder phases. No
existing check is removed, and the unfiltered full-browser phase retains the
optional-verification execution of those same files.

The registry now owns 111 named integration steps (all 110 previous steps plus
this phase), across the unchanged seven isolated matrix executions. The verifier
requires both departure files, the verification flag, both host-policy checks and
phase ordering. Three negative mutations remove account-replacement cases,
disable verification or omit the Worker policy check. They fail with the old
guard (59 pass / three fail) and pass after strengthening (62/62). Browser
TypeScript, workflow syntax and all 320 cases in four intact-file shards pass.
See the [Organization acceptance map](prd-03-acceptance.md#strict-verified-account-departure-and-continuity)
for native execution evidence and remaining release requirements.

## Strict Board management prerequisite

The notification group now runs all eight Board-management/consumer files under
strict fixture verification after Organization departure and before the existing
watch/notification producers. Its eighteen cases cover persisted workflow,
metadata, copy, lifecycle, archived-account recovery, personal stars, activity
history and the separately bounded stored-background client contract. Both
actual hosts must report verified-email enforcement. The phase retains normal
rate pacing, all original case deadlines and retry policy, and selects every
case in those files without filtering. Stored-background publication/PNG replies
remain simulated in that client file; real storage/Worker contracts retain their
independent mandatory gates.

The registry contains 112 named steps, preserving all previous 111 and the
unchanged seven isolated matrix executions. Three new omission checks remove
copy cases, disable fixture verification or omit Worker policy verification:
the previous guard accepts them (62 pass / three fail), while the strengthened
suite passes all 65 checks. Browser TypeScript, workflow syntax and the unfiltered
320-case/124-file complete four-shard coverage pass. See the
[PRD-04 scope and pending execution](prd-04-acceptance.md#strict-verified-account-board-management-phase).

The strict Board prerequisite is further extended to all five genuine directory
realtime cases in `organization-board-live.spec.ts`: nine full files / 23 cases.
The previous eighteen remain intact. Its new omission mutation rejects dropping
the genuine directory file; all 66 guard tests pass. Real source frames, Worker
identity/reconnect and private audience withdrawal are independent of simulated
background client replies. Native verification is pending; see the
[strict directory evidence](prd-04-acceptance.md#strict-genuine-board-directory-realtime-coverage).

The strict Board prerequisite additionally retains all five native visibility,
member-consent and two-administrator live cases: twelve complete files / 28 cases.
API/Worker verified-email policy checks and normal rate pacing remain required.
Three negative guards reject omitting any permission file; all 69 guard tests
pass. Named steps and matrix executions stay at 112 and seven. Native permission
execution remains pending; see the
[PRD-05 evidence](prd-05-acceptance.md#strict-native-visibility-and-member-administration).

## Private browser fixture producers remain local to each job

The matrix split left three checks owned only by `commands` even though they
also prepare four private fixture exports required by the unfiltered native
suite. `test-invitation-registration.sh` creates invitation signup and Board
invitation link fixtures, `test-invitation-discovery.sh` creates Organization
invitation link fixtures, and `test-board-admin-continuity.sh` creates the real
53-row Board member directory fixture. Their `RUNNER_TEMP` files and
`GITHUB_ENV` exports, and their corresponding database rows, cannot supply
another isolated job. Required consumers remain in the full suite; no skip or
optional fallback replaces this dependency.

All three existing exact-image checks now retain `commands` ownership and also
run inside every `browser-full` job, after the initial auth fixture and before
identity mail preparation and full browser execution. Each job creates its own
files and matching database state; private fixture contents are not transferred
or added to diagnostic artifacts. Existing script bodies, shell/environment,
registration policy overrides, restoration and command acceptance checks remain
unchanged. Source/runtime API/Worker images are still loaded from the same
exact-SHA archives without rebuilding.

Six regression mutations omit each producer from browser ownership or move it
after the consumers. The previous verifier accepts them (69 pass / six fail);
the repaired verifier passes all 75 tests. Workflow syntax and the 112-step
registry/seven-execution topology pass. These guards prove structural dependency
coverage; execution of the repaired full-browser jobs remains pending current
immutable CI. Earlier successful command-only execution of these scripts does
not establish the repaired browser matrix or complete release success.

The completed older full-browser shard `113595872331` in run `37854464485`
confirms the fixture gap at runtime: both Board invitation-link and both Board
member-directory cases fail at their required environment predicates with
`undefined`. Its six failures also include metadata editor admission timeout
and a missing Card-label confirmation, which are separate investigation items;
the fixture ownership correction does not claim to fix them. The current local
28-case phase additionally records a phone Card activity opening failure and
continues collecting the remaining cases. No combined pass is claimed.

The older shard's browser diagnostic artifact `11588154757` was downloaded and
its SHA-256 verified as
`2c05b86f69731f26b67443a3f40a2df7b7bf98aa440b4f20ea84f6cc6bbe878c`.
The label trace contains no root Board-label DELETE request following the
confirmation gesture; an initially matched successful DELETE is a Card-label
association removal. A later snapshot contains the prior confirmation, which
does not establish successful Board-label deletion. Its activation failure
remains under investigation; no speculative notice-clearing change is made.
The current combined phase also records a separate phone member-consent failure
and continues collecting the remaining cases. The
[activity-opening fixture increment](prd-15-acceptance.md#activity-opening-admission-in-the-combined-strict-phase)
has fresh native execution pending. No combined-phase pass is certified.


## Completed combined phase and member entry-focus regression

The previously running complete local strict Board phase has completed:
26 passes / two failures in 21.7 minutes, with no skips or flaky cases. Phone
activity opening and phone member consent failed. Its owned fixtures were
removed. A fresh activity-only invocation passes both complete viewport cases
in 4.2 minutes; this does not certify a combined pass or exact intermittent
cause. See [activity evidence](prd-15-acceptance.md#complete-activity-opening-verification).

A real-MUI member dialog regression proves delayed entry unconditionally steals
chosen Confirm focus. The ownership repair passes all 18 member/focus component
checks; unchanged native member scenarios are running against fresh web assets.
See [member evidence](prd-05-acceptance.md#preserve-chosen-member-confirmation-during-dialog-entry).
Current immutable/full matrix acceptance remains pending.


## Complete member entry-focus native verification

Both unchanged strict native member scenarios pass at desktop and phone widths
against the member entry-focus repair, with zero skips, retries or flaky cases.
They retain reviewed-version conflict, actual lost PATCH/DELETE replies, one
removal, warning recovery, authority and Organization-membership invariants,
keyboard focus and responsive assertions. Current compiled Production API and
separate Worker, freshly built web assets and restricted schema-114 PostgreSQL
17/pgvector were used. Owned containers and database were removed.

This scoped result does not establish the prior intermittent phone failure's
exact cause or a complete combined/full immutable pass. Current CI remains
queued; complete acceptance still governs closure. Estimated PRD-05 work
remaining stays **15%**.


## Strict label workflow admission and mandatory coverage

Both desktop/phone label baselines fail login with 403 under verified-email
enforcement. The existing shared account fixture now supplies strict admission
without altering production policy or the ordinary unfiltered path. The strict
Board step adds the complete `card-labels.spec.ts`: thirteen files / 30 cases,
retaining all previous 28 cases. The preceding 28-case invocation is still
running and cannot certify the expanded phase.

The new omission guard fails before verifier repair (75 pass / one fail); all
76 guards pass afterward. The 112-step/seven-execution immutable architecture
and four complete unfiltered shards remain. Native execution of both label
cases is in progress. See [PRD-10 evidence](../board-label-api.md#strict-verified-account-native-label-workflow).
The older missing root label DELETE is not explained by the strict account
fixture, since that invocation had already completed login.


## Strict label execution retains a phone failure

The complete strict label invocation finishes with one desktop pass and one
phone failure in 2.0 minutes, with no skips or flaky cases. Both now complete
verified-account admission. Phone fails the original Clear-filter keyword
assertion: expected empty, actual `absent`. The trace records enabled/focused
Clear admission and one Enter, but no actual `change=clear` request; the three
Apply acknowledgments remain. This is a dispatch/admission investigation, not
proof of successful Clear losing its result. Owned containers/database were
removed. The strict fixture correction is validated at setup scope only;
complete native acceptance is still failing. Estimated PRD-10 work remaining
stays **35%**.

The still-running combined 28-case phase also reproduces the phone peer-history
50-row assertion failure after explicit opening focus admission. Protected
activity reads return real 200 responses; one peer read takes about 4.9 seconds.
Exact recovery/parent-generation timing remains under investigation. The earlier
scoped two-case pass does not establish resolution. Estimated PRD-15 work
remaining stays **36%**; current immutable/full CI remains required.


## Complete visibility entry-focus native verification

Both unchanged strict visibility scenarios pass at desktop and phone widths,
with zero skips, retries or flaky cases. Public read-only enforcement, reviewed
visibility consent/conflict, lost-response recovery, live updates, current
permission checks, keyboard focus and responsive assertions remain. They use
fresh visibility-focus web assets, current compiled Production API/separate
Worker and restricted schema-114 PostgreSQL17/pgvector. Owned containers/database
were removed. This scoped pass does not certify the combined phase, which
predates this product change and has a phone activity failure, or current
immutable/full CI. Estimated PRD-05 work remaining stays **15%**.


## Completed member-focus combined phase retains lifecycle failures

The complete 28-case member-focus invocation finishes with 25 passes / three
failures in 21.5 minutes, no skips/flaky cases. Phone peer activity fails the
original 50-row assertion; both lifecycle cases fail at source line 97 on restore request count (expected two / actual one). This
run predates the visibility-focus repair and the activity-read optimization.
All owned containers/database were removed. This is failing combined evidence,
not a certificate for the expanded mandatory 30-case strict phase.

The isolated page-level keyboard comparison passes Clear at both widths but
fails later label-management controls on both cases (zero passes overall).
Its unchanged assertions/deadlines remain; private diagnostic fixtures were
removed. The repository keyboard helper is unchanged. Current immutable/full
CI and exact causes remain required before closure.


## Complete original-key lifecycle recovery verification

Both complete strict desktop/phone lifecycle scenarios pass in 1.9 minutes,
with zero skips/retries/flaky cases. They now explicitly require the original
restore acknowledgment before checking the empty directory/focus and exactly
two identical key/body requests. Genuine two-client Worker updates, current
actor/role admission, child lifecycle preservation, keyboard/Axe and original
deadlines remain. Fresh original-recovery web, rebuilt Production API/separate
Worker and restricted schema-114 PostgreSQL17/pgvector were used. Owned
containers/database were removed. This scoped pass does not certify the
combined 30-case or current immutable/full CI; remaining full acceptance applies.
Estimated PRD-04 work remaining stays **16%**, PRD-18 **16%**.


## Complete strict label-management admission verification

Both full label viewport workflows pass together in 2.0 minutes, with zero
skips/retries/flaky cases, using existing admission for opening/selection/reload
and unchanged shared keyboard activation. Original lost-response key/body
recovery, filter persistence/counts, saved order/version, explicit deletion
consent, actual Card association removal and focus/mobile assertions remain.
Runtime: current compiled Production API/separate Worker, frozen original-key
lifecycle web and restricted schema-114 PostgreSQL17/pgvector. Owned containers
and database were removed.

This is scoped local acceptance evidence, not proof of the older immutable
activation failure's exact cause or a complete release. A fresh mandatory
13-file/30-case combined strict phase is running against those same frozen
assets and rebuilt hosts. Complete API and current immutable/full CI remain
pending. Estimated PRD-10 work remaining stays **35%**.


## Strict verified-account two-client label/filter recovery

The complete `label-filter-live.spec.ts` initially fails under Production
verified-email enforcement: its immediate login expects 200 but receives 403.
It now uses the existing verified-account fixture, which first observes the
pending-account denial and activates only its newly registered disposable user.
Production policy and the ordinary optional fixture path remain unchanged;
this activation does not certify email-provider delivery.

The complete scenario passes in 89.4 seconds with zero skips, retries or flaky
cases. Two independent browser contexts share one real account, with desktop
and phone viewports. Genuine Worker delivery and an upstream WebSocket exercise
label assignment/removal/rename, forced disconnect and reconnect, due completion,
assignee changes, persisted filters and filtered canvas, exactly four deliberate
filter-change writes, and archive withdrawal. Original assertions and the
180-second deadline remain. Runtime: current compiled Production API/separate
Worker, frozen original-key recovery web and restricted schema-114
PostgreSQL17/pgvector. Owned containers and database were removed.

Mandatory strict Board coverage now includes this whole file: fourteen files /
31 cases. A negative guard rejects its omission; the integration registry suite
passes all 77 tests, browser TypeScript passes, and the registry/shard checks
retain 112 registered steps across seven isolated executions and all 320 cases
in 124 intact files across four full shards. Workflow syntax validation passes.
The already-running 30-case invocation predates this addition and cannot certify
the expanded 31-case phase. Current immutable/full CI and complete acceptance
remain required. Estimated PRD-10 work remaining: **35%**; PRD-16: **18%**.


## Complete strict member-removal keyboard admission

The current combined strict invocation's phone member workflow fails while
waiting for its uncertain-removal warning. The retained network trace contains
no member DELETE request; this does not establish a lost-response notice defect.
Removal opening now uses the existing enabled/focused admission helper before
one page-level Enter. The fixture observes the specifically named removal
consent dialog, then admits its own Confirm control before one further Enter.
Only focus checks may repeat; the removal command is never retried. The shared
keyboard helper, product behavior, server permission checks, original versions,
conflict/lost-response assertions and deadlines remain unchanged.

Both complete desktop/phone member workflows pass in 93.2 seconds with zero
skips, retries or flaky cases, against current compiled Production API/separate
Worker, frozen original-key recovery web and restricted schema-114
PostgreSQL17/pgvector. They retain genuine Worker updates, competing-version
conflict, lost role/removal responses, exactly two intercepted role requests and
one actual removal, current read recovery without assumed acknowledgment,
recipient access withdrawal, and unchanged Organization membership. Browser
TypeScript passes and owned containers/database were removed. This is scoped
native evidence; the original intermittent cause and combined/current immutable
acceptance remain unproven. Estimated PRD-05 work remaining stays **15%**.


## Activity paging keyboard admission investigation

The current combined desktop history case misses its original historical actor
on the older Board page. Its retained network trace contains no older Board
activity request. A fresh complete two-case invocation that admitted only this
last action fails earlier in both viewports: Card Older remains at 50 rows
instead of 17, and the desktop trace contains no older Card request. That
invocation has zero passes, two failures, no skips/flaky cases, lasts 141.4
seconds, and removes its owned containers/database. It does not prove a server
paging or actor-label defect, nor resolution of the combined failure.

Card Older/Newer and Board Older now use the existing enabled/focused admission
checks before one page-level Enter, matching the member-removal fixture's
single-key delivery. Existing assertions that paging focus is restored remain;
only focus admission may repeat. Page navigation, commands, 50/17-row checks,
actor immutability, genuine two-account Worker/reconnect, lifecycle/withdrawal,
Axe and the 180-second deadlines remain unchanged. The shared keyboard helper
and product authorization/reset generations remain unchanged. Browser
TypeScript passes. A fresh complete two-case invocation is running against
current compiled Production API/separate Worker, frozen original-key recovery
web and restricted schema-114 PostgreSQL17/pgvector. Its outcome, exact causes,
complete API, PostgreSQL races and current immutable/full CI remain pending.
Estimated PRD-15 work remaining stays **36%**.


## Complete single-key activity paging verification

Both complete strict activity-history desktop/phone workflows pass in 175.1
seconds, with zero skips, retries or flaky cases. The original 180-second
per-case deadline, restored paging focus, 50/17-row pages, immutable actor/UTC,
genuine two-account Worker delivery/reconnect, preferences, permission/session
withdrawal, archive/delete and Axe remain. Paging admission precedes one
page-level Enter; only focus checks can repeat. Current compiled Production
API/separate Worker, frozen original-key recovery web and restricted schema-114
PostgreSQL17/pgvector were used. This scoped result does not establish exact
intermittent cause, combined/current immutable success or the PostgreSQL
post-wait/session race acceptance. Estimated PRD-15 remaining: **36%**.


## Combined strict Board result and single-key label Clear verification

The complete 13-file/30-case strict invocation finishes with 27 passes and
three failures in 23.4 minutes, zero skips/flaky cases, and removes its owned
containers/database. Desktop history misses its original actor on Board Older;
phone member recovery misses its uncertain-removal warning; phone labels retain
the old keyword after Clear. Traces show no corresponding older Board request,
member DELETE, or Clear command. This failing combined run predates the newer
paging/member admission changes and expanded 14-file/31-case requirement.

Label Clear now uses the existing enabled/focused admission followed by one
page-level Enter, preserving exactly four filter writes and the original
`change=clear` assertion. Shared keyboard helper, product behavior, other label
commands and deadlines remain unchanged. Both complete strict label workflows
pass in 112.2 seconds with zero skips/retries/flaky cases. Current compiled
Production API/separate Worker, frozen original-key recovery web and restricted
schema-114 PostgreSQL17/pgvector were used; owned fixtures were removed. Original
lost-response key/body recovery, persistence/order/version, explicit root-label
delete consent, actual Card association removal, focus and phone assertions
remain. Exact intermittent cause and current immutable/full acceptance remain
unproven. Estimated PRD-10 remaining: **35%**.


## Complete API source-suite result

The full source API suite completes successfully in 43 minutes 15 seconds:
691 total, 673 passed, zero failed, 18 skipped. All skips are the Linux-only
controlled-download role/visibility matrix; this Windows result cannot certify
those cases. It tests the current bounded activity-reader product, compiled
before the later additional private-reference assertions; those added
assertions separately pass in their fresh 11-case source invocation. This
result and the complete two-case activity keyboard pass do not certify current
immutable images, Linux-only cases or PostgreSQL post-wait/session races.
Estimated PRD-15 remaining: **36%**.


## Native PostgreSQL activity post-wait and key-recreation proof

An isolated equivalent of `scripts/ci/test-activity-feeds.sh` passes against the
current compiled Production API and a separate restricted schema-114
PostgreSQL17/pgvector database. Three real accounts first receive the required
pending-email login denial; only these disposable users are activated. The
fixture uses the release script's curl cookie transport and non-idempotent
login, while mutation requests retain their own identities. Login-replay rows
intentionally restrict hard session deletion and were not bypassed or removed.

The proof checks the complete tied 50/16-row seek (66 distinct events), source
and current parent projection, viewer/target-bound cursors, outsider denial
without items, no-store headers, immutable historical actor after profile
rename, and byte-identical tail recovery after deleting/recreating the API
container with the same owned protected key volume. Historical source birth
is synthetic, matching the release fixture; this is not a Card move command.

Actual observed PostgreSQL waits then cover both source and current Board
FOR UPDATE gates. Membership removal is committed while each read waits;
each resumed HTTP read returns 404/activity_not_found without items. Restoring
the member permits a fresh read. A users FOR UPDATE gate holds the issuing
read before its session SHARE lock; deleting that disposable issuing session
returns 401 without items after release. A source Board gate holds another
issuing read while its real session naturally expires: both unexpired and
expired states are observed using the database clock before gate release.
The resumed read returns 401/session_unavailable without items; a new login
recovers 200. No permission/session boundary was relaxed.

All assertions pass and all owned API/web containers, database and key volume
are removed. This is actual native PostgreSQL/HTTP security evidence using
compiled hosts, not execution of the unchanged mandatory Linux script or
current immutable release images. The complete expanded strict Board phase
and current immutable/Linux/full acceptance remain required. Estimated PRD-15
work remaining: **35%**.


## Current explicit Linux private-staging source evidence

The [source platform audit](api-host-testing.md#explicit-linux-private-staging-coverage)
replaces six silent non-Linux returns with explicit xUnit skips and executes
those six cases plus the eighteen-row download matrix on Linux. All 24 pass in
66.8 seconds with no errors/failures/skips/not-run; Windows correctly reports
24 skips. The fresh locked Release build has zero warnings/errors and the owned
Linux test container is removed. Original staging/byte/session/HTTP assertions
remain; synthetic publication/storage, PostgreSQL/external-provider and current
immutable/full acceptance boundaries still apply. Earlier full Windows totals
included six early returns and cannot prove those checks executed.


## Complete fourteen-file strict Board invocation

The complete mandatory 14-file/31-case strict Board phase passes together in
21.7 minutes, with zero failures/skips/retries/flaky cases. It uses the workflow's
entire explicit file list without test-name filtering: Board canvas/metadata,
copy, lifecycle, archive-account withdrawal, stars, activity history, background
client behavior, Organization/Board live recovery, visibility, members, live
administration, labels and two-client label/filter recovery. Original assertions,
command counts/identities, focus, keyboard/mobile/Axe checks and deadlines remain.

Runtime: current compiled Production API/separate Worker, frozen original-key
recovery web and restricted schema-114 PostgreSQL17/pgvector, with verified-email
policy enforced. Current single-key paging, member removal and label Clear
fixtures all execute in this same complete invocation. Owned containers and
database are removed. Background-client publication/PNG fixtures are simulated;
this run does not establish actual external storage/scanner or full provider
image publication. The label/filter live case shares one account across two
browser contexts; cases with distinct accounts retain their own actual sessions.

This replaces the prior failing 30-case combined evidence for the current local
strict scenario set. It is compiled local runtime evidence, not current immutable
release/full PRD certification. Current source-platform metadata changes are
source tests only and do not change the tested product/runtime. All remaining
functional, provider, capacity, privacy and current full CI requirements still
apply before issue closure.
