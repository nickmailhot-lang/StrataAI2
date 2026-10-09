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
