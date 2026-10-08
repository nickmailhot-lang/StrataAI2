# Mandatory integration groups (ARCH-11)

The `container-integration` job runs four independent groups on separate GitHub-hosted runners. Each group downloads the current run's `strataai-images-<sha>` artifact, verifies its archive checksums, loads the same web/API/Worker images, and starts its own fresh PostgreSQL/pgvector topology. No group builds application images. Tests remain sequential inside each group, preserving database wait observations and runtime configuration transitions.

| Group | Mandatory scope |
| --- | --- |
| `commands` | Runtime refusal cases, identity/Organization/Board/Work command contracts, restricted persistence and Worker delivery, supported-capacity measurements, and operator metrics |
| `browser-foundation` | Demo lifecycle, attachment transport/publication, contiguous replay, actual large-Board native capacity, and database-wait authorization |
| `browser-notifications` | Identity mail integration, accessible keyboard verification/recovery, strictly verified watch/assignment/mention/reminder producers, and watch/permission ordering |
| `browser-full` | The complete browser suite, automatic metadata and invitation authority routing, and API/edge abuse-limit contracts |

Every group first verifies the real Production authentication defaults, then explicitly applies the isolated self-registration/unverified-account fixture. Strict notification tests subsequently require verified accounts in both actual hosts. Both mail-dependent browser groups generate their own ephemeral signing keys and start their own private test mail provider. The full suite sets up its mail overlay and automatic Worker routing explicitly rather than inheriting state from another group's tests. Production rate budgets, browser timeouts, native gestures, expected versions, and whole-snapshot assertions are unchanged.

The matrix uses `fail-fast: false` and does not ignore errors. A failure leaves other groups running to retain their evidence. The stable `required-ci` job still requires the aggregate `container-integration` result to be `success`; release bundling therefore waits for all four groups, plus all existing source/build/security gates. GitHub documents matrix execution and failure handling in its [job variation guide](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations).

Generic browser and container diagnostic artifacts include the suite name to avoid upload collisions. Capacity artifacts have one owning group, including Card-copy evidence from the command phase. Each group collects failure diagnostics and tears down its own topology even when a check fails. The image artifact, security evidence, and release bundle retain their existing identity and build-once flow.

[`scripts/ci/integration-suites.json`](../../scripts/ci/integration-suites.json) records ownership for every named integration step. Source quality runs the [coverage verifier](../../scripts/ci/verify-integration-suites.mjs) and [mutation regressions](../../tests/integration-suites.test.mjs). They reject missing checks/groups, unknown conditions, ignored failures, broken mail prerequisites, artifact collisions, downstream image builds, and weakened aggregate gates. Add new checks to both the workflow and ownership registry; changing a command still requires reviewing its actual coverage.

Local validation:

```sh
npm ci
node scripts/ci/verify-integration-suites.mjs
node --test tests/integration-suites.test.mjs
```

The restructuring preserves all 95 previous integration steps: the former combined Production-policy/profile phase becomes an explicit common prerequisite plus the command group's unchanged profile test. Local comparison verified every previous test command, environment, shell, and action input, allowing only the two generic diagnostic artifact suffixes. Nineteen coverage/mutation tests pass locally, and Actionlint validates the workflow syntax and expressions. This is structural evidence; the new matrix still needs its exact-commit CI execution. No runtime reduction or full acceptance completion is claimed until those results exist. The current native capacity failure also remains open pending retained evidence and repair.

Related: [main-run evidence retention](ci-run-retention.md), [embedded build identity](build-identity.md), [documentation index](../README.md).
