# Board creation during access recovery

Owner: PRD-04-VS-01. Diagnosis: PRD-04-VS-01-T-02. Repair: PRD-04-VS-01-T-03.

An authorized member opens Organization home, starts a private Board, enters a name and description, survives a temporary access recheck, explicitly submits, and opens the canonical Board. The original draft and unchanged uncertain submission key survive transport recovery. No mutation is automatically replayed.

Board/metadata stream reset, invalidation and disconnect previously called `setCreating(false)`. Lifecycle transport uncertainty used the same destructive form withdrawal as confirmed deletion. A delayed initial reset or disconnect therefore destroyed the user's form even when subsequent reads confirmed unchanged access. Controlled tests reproduce the first two paths against the original implementation; the lifecycle path has its own controlled case.

The repair keeps the original form in its own local MUI portal container. During uncertain admission the container, protected names and actions are hidden; submission also requires current discovery data. A bounded authenticated read must confirm the original actor and Organization before the form reappears. Restore the previously focused control only if focus has not moved elsewhere. Fresh 401/403/404, account change, deletion pending/completed and route changes retire creation. Existing server authorization remains authoritative.

This is a focused recovery increment. It does not certify the rest of PRD-04, PostgreSQL/RLS, immutable release delivery or production readiness. The original broad Board test retains its 30-second deadline, all keyboard assertions and zero retries. Failures, including later unrelated time-budget failures, remain evidence and are not waived.

UXA-01–08 apply: existing Organization entry, shared MUI controls/theme, private default, no new routes; keyboard focus and explicit retry; 320/390/768/1280px and 200% text at tablet; accessible dialog and no page overflow; canonical Board read/reload and return to the Organization directory. Automated checks and developer visual review are separate from final product/release acceptance.

Material assumption: an unsent draft may remain in memory while presentation is withdrawn for transport uncertainty. It confers no authority. Confirmed access refusal destroys it, even if later access returns. This matches the existing protected surface recovery policy.
