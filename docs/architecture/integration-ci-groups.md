# Mandatory integration groups (ARCH-11)

The `container-integration` job runs four logical groups on separate GitHub-hosted runners. The complete browser group now uses [four intact-file partitions](browser-shards.md), for seven mandatory isolated executions overall, capped at four concurrent runners. Each group downloads the current run's `strataai-images-<sha>` artifact, verifies its archive checksums, loads the same web/API/Worker images, and starts its own fresh PostgreSQL/pgvector topology. No group builds application images. Tests remain sequential inside each group, preserving database wait observations and runtime configuration transitions.

| Group | Mandatory scope |
| --- | --- |
| `commands` | Runtime refusal cases, identity/Organization/Board/Work command contracts, restricted persistence and Worker delivery, supported-capacity measurements, and operator metrics |
| `browser-foundation` | Demo lifecycle, attachment transport/publication, contiguous replay, actual large-Board native capacity, and database-wait authorization |
| `browser-notifications` | Identity mail integration, accessible keyboard verification/recovery, strictly verified watch/assignment/mention/reminder producers, desktop/phone inbox recovery, and watch/permission ordering |
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
