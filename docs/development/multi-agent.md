# Coordinating agents on one repository

One integration owner allocates bounded tasks. Each implementation agent uses a
unique feature branch and linked Git worktree. Worktrees isolate working files;
path claims and reviewed integration handle logical conflicts. These are local
collaboration guardrails, not access control against another process with the same
OS account.

```sh
npm run dev:agents -- init --owner integrator
# In agent A's isolated worktree:
npm run dev:agents -- claim --owner agent-a --id TASK-A --paths apps/web/src/features/exampleA
# In agent B's separate worktree:
npm run dev:agents -- claim --owner agent-b --id TASK-B --paths apps/web/src/features/exampleB
npm run dev:agents -- status
```

The registry records owner, task ID, branch/worktree, base SHA, explicit paths,
dependencies, unique runtime project and append-only action history. Claims use an
exclusive lock plus atomic replacement; duplicate IDs, overlapping parent/child
paths (case-folded and Unicode-normalized for Mac/Windows safety), reused active worktrees/branches and worker claims on shared infrastructure
are refused. The existing integration owner cannot be silently replaced. Shared
shell/theme/component files belong to that owner even when their path is not
listed in the CLI's conservative built-in protected prefixes.

Do not edit outside claimed paths. Request a coordinator handoff first. Reserve
package manifests/locks, schema migration numbering, API/event contracts, app
shell/navigation/theme, CI policy and shared delivery records for one owner.
Workers submit proposed ledger changes in handoff evidence; the owner updates the
canonical ledger/report serially. Do not force-reset, stash, clean, delete or edit
another checkout. Never force-push shared branches.

After local verification, create a JSON handoff outside the source tree containing
arrays `acceptance`, `evidence`, `limitations`, and `dependencies`. Evidence must
identify the tested tree/commit, exact commands/results and artifact locations;
include changed paths and known failures in the acceptance/limitations notes.

```sh
npm run dev:agents -- handoff --owner agent-a --id TASK-A --handoff /absolute/path/handoff.json
# Integration owner only, after integration or documented cancellation:
npm run dev:agents -- release --owner integrator --id TASK-A --reason 'Integrated and retested in integration worktree'
```

A handoff retains the claim through review. Only the integration owner releases
it, recording a reason. Dependencies do not automatically mean a task is blocked.
Record actual blocked conditions, owner and prior resume state in the delivery
ledger. No registry action changes product acceptance or merges branches.

The integration owner reviews handoffs and combines one candidate at a time in a
separate integration worktree. Prepare merges with `git merge --no-commit --no-ff`
so the combined tree can be tested before creating the merge commit. Resolve
conflicts with their owners; run the final local gate on the combined tree, then
commit/push a feature branch for independent required-ci and review. Update the
base and rerun when upstream changes affect the candidate. Do not infer combined
success from two independently passing branches.

Runtime checks reserve the shared fixed-port fixture slot; unique project names,
volumes and tags prevent cross-cleanup. Do not start a conflicting manual stack
while that slot is held. Source test processes are isolated per worktree, but
coordinate heavy builds to avoid exhausting this Mac's memory.

Use the [coordinator](../prompts/coordinator.md), [worker](../prompts/worker.md) and
[integration](../prompts/integrator.md) prompts. Automated tests exercise two real
linked worktrees, independent claims, overlap refusal and reviewed handoff history;
they do not claim two autonomous agents have delivered separate product features.
