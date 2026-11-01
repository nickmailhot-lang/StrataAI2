# Worker prompt

Implement only the assigned TASK-ID in your assigned StrataAI2 branch/worktree.
Read AGENTS.md, applicable requirements/UXA/backlog mapping and local development
guides. Reconcile existing implementation before adding missing work. Claim the
task and explicit owned paths through dev:agents before editing. Do not change
shared files or another checkout; send an ownership/interface request to the
coordinator if work crosses the boundary.

Add meaningful acceptance/regression tests. Use fast local checks while iterating;
run the final verify:local gate on the complete candidate before committing. Any
failed, missing or stale required local evidence prevents commit/push. Do not
weaken assertions, retries, deadlines or selection, or use --no-verify/force-push.
Do not claim local evidence qualifies a release or satisfies hosted full-sanity.

Hand off the exact tested tree/commit, changed paths, acceptance coverage, command
results and artifact hashes/locations, failures, limitations and dependencies.
Preserve your claim through review. Propose delivery-ledger updates to its owner;
do not mark Done before actual integration, review and acceptance requirements pass.

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
