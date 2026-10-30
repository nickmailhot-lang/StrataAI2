## Change

Problem, resulting behavior, owning PRD/ARCH and linked UX PRDs. Preserve approved MVP scope and canonical feature ownership.

## UX integration

- Discovery → role/capability → primary task → return route:
- Applicable UXA-01–08 and backlog-map row:
- Shared shell/theme/components reused; migration flag/rollback if disruptive:
- Existing contracts, tenant/Portal and recovery boundaries preserved:

## Evidence at this revision

| Required gate | Command / scope | Passed / failed / blocked / not run / not applicable (reason) |
| --- | --- | --- |
| TypeScript (web/browser) | | |
| Lint | | |
| Unit | | |
| Relevant integration | | |
| Affected Playwright | | |
| Role-based access | | |
| Responsive layout / zoom | | |
| Accessibility / visual review | | |
| Existing regression / immutable release gates | | |

Record failure cause, impact, owner, resolution and rerun evidence. Pending required checks keep the PR in draft; do not bypass failures.

Update the single `docs/ux/delivery-report.md`: functional implementation, UX integration, automated tests, dependencies/blockers and MVP acceptance remain separate. Link product/UX acceptance reviewer/date when applicable.

## Local verification and coordination

- Task ID, owner, branch/worktree, owned paths and integration owner:
- Base SHA and exact tested tree; resulting commit SHA:
- Final `verify:local` receipt, command/log hashes and measured duration:
- Combined-tree integration test evidence (when applicable):
- Full CI selection/reasons and checks still requiring hosted qualification:

Local feature verification, required-ci, integration, product acceptance and release
qualification are separate. Retain failed results and do not publish private
runtime environment/provider data. Commit and push only after the exact candidate
passes the applicable final local gate; never bypass verification hooks.
