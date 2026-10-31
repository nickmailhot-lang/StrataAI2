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

Use the shared docs/feature/full impact tier from `verify:local -- --plan`.
Do not narrow backend/shared/security/unknown changes by filename. Include all new
and untracked tests. Feature evidence keeps whole Board/permission/tenant baseline
and affected browser files; full risk requires full qualification. Automatic CI
reuse requires completed successful exact integration/base/policy/scope proof;
manual reruns and release qualification stay fresh. Record actual scope/duration
and prior failures separately; a reused leaf pass cannot establish full acceptance.
