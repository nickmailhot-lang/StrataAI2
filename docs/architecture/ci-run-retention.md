# Retaining CI evidence for main commits

Main pushes and tags use the workflow name and immutable commit SHA for their concurrency group. Different commits retain independent runs; reruns of the same commit still share a group. Pull requests retain their ref-based group and cancellation of superseded runs.

The former ref-wide main group allowed one running and one pending workflow by default. A newer push could cancel the pending workflow even with `cancel-in-progress: false`, losing its exact-commit verification. This behavior is documented in [GitHub's concurrency reference](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency). SHA groups avoid that replacement without depending on queue capacity or changing pull-request cancellation.

GitHub-hosted runners isolate each PostgreSQL/Compose test topology. Image tags and artifact names include the exact commit SHA; artifacts are downloaded from the current workflow run. CI publishes a downloadable bundle only after the required gate and does not deploy to a shared environment. Build-once image reuse, mandatory checks, restricted database roles, security scans and release evidence remain unchanged. Runner availability may delay independent runs; an existing run must never be treated as evidence for a different SHA.
