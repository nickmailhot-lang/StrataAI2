# Integration prompt

Review completed worker handoffs for StrataAI2. Use a dedicated integration
branch/worktree and preserve every source/evidence history. Confirm ownership,
base SHA, dependencies, changed paths and actual acceptance coverage. Integrate
one candidate at a time with a noncommitting merge; resolve conflicts deliberately
with the responsible owners. Never overwrite another checkout or force-push.

Run final verify:local against the current target base on the combined candidate.
A previously green worker branch cannot qualify the combined tree. Commit only
after the exact staged/working tree passes; push the verified feature branch for
independent required-ci, full qualification when selected, and review. Never push
main directly, bypass hooks/checks, or infer production promotion from local tests.

Update the canonical delivery ledger/report serially, retain failures and separate
implementation/local testing/CI/integration/product acceptance. Release claims only
after integration or explicit documented cancellation. Report remaining hosted,
platform and manual acceptance requirements honestly.

For GitHub connector/API publication, explicitly run the local verification hook
checks before writing; hooks do not run on API calls. Publish only the verified
tree and intended parent under a new branch or expected-head update, then fetch
and compare the resulting tree. Preserve the original local commit if metadata
differs. Connector access never waives local or hosted verification.

Use the current docs/feature/explicit-full profile from `verify:local -- --plan`.
The 2026-10-31 user-approved development gate selects complete affected units,
reviewed complete HTTP methods and focused compiled UI smoke; retain all new and
untracked source/tests. Unknown comparisons/consumers refuse mapping review.
Shared/CI changes do not automatically request full. Full original production,
role/tenant/Portal, provider and browser matrices remain nightly and mandatory for
an exact production candidate; `--full` requests complete local qualification.
Automatic reuse requires original completed successful exact source/base/policy,
profile/backend/method/file/smoke scope and timing proof. Manual requests stay
fresh. Report deferred suites, existing skips and every failure separately; a
feature pass never establishes full or product acceptance.
