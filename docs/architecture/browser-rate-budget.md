# Release browser rate budget

CI preserves the production API authentication limit and Nginx sensitive-request limit. Browser contexts and API request contexts in a hosted job share the same real peer; changing accounts does not create additional capacity. Forwarding-header spoofing remains ineffective.

The retained trace for schema-021 commit `5d32730` shows registration 201 followed by sign-in 429. Commit `3eb338e` recreated the API and web processes from the already loaded release images before the browser suite. That removed earlier fixture consumption, but its release logs still show profile setup sign-in and recovery requests receiving 429 when the expanded scenarios ran in a rapid burst. These are assertion failures, not permission to increase limits or accept a throttled operation as success.

All browser specs now import the shared `releaseTest` fixture. The required release job enables `STRATAAI_E2E_RATE_PACING=1`, adding 25 seconds before each scenario. Existing scenarios issue fewer than 20 sensitive requests each, so this preserves API window capacity and replenishes the edge burst budget. The fixture has its own 35-second setup timeout, keeping the original per-test operation deadlines intact. A locally executed runtime probe confirmed that setup pacing completes even when the test body has a five-second deadline. Local tests default to no pacing.

The suite still uses one worker, zero test retries and strict status assertions. It neither catches unexpected 429 responses and retries them until success nor modifies production configuration. Required API and edge abuse-limit checks run separately after the browser suite, using spoofed forwarding headers to prove rejection, retry metadata and security headers. Exact web/API/Worker images continue to be built once and emitted only after every mandatory gate passes.

This is fixture isolation and pacing, not proof of application latency targets. New scenarios must stay within the documented sensitive-request budget or receive an explicitly reviewed scheduling adjustment without weakening the production controls.

## Shared authentication route checks

The mandatory API/edge abuse fixture now probes registration, password recovery,
password reset, verification resend/completion and logout after exhausting
sign-in capacity. Changing routes and spoofing forwarding headers must not obtain
a fresh authentication budget. Each route sends a concurrent pair; all twelve
requests start before response assertions. Direct API pairs must both return
429. Each edge pair must include a 429: Nginx replenishes capacity continuously
at 60 requests/minute, whereas the API uses a fixed one-minute window. Empty
credentials and invalid tokens prevent fixture account creation or token use.
The fixture verifies every denial's stable code, positive Retry-After, absence
of cookies/private token fields, and edge security headers. Its existing
invitation exhaustion, independent unmapped-edge refusal and health checks remain.

Both complete final invocations passed locally on 2026-10-07 using the frozen
compiled Production API at `79753c03`, real PostgreSQL, the frozen production web
bundle and current Nginx configuration in cached runtime images. API/invitation
limits remain at 60; Nginx retains 60r/m and burst 20. Nginx configuration, Bash
syntax and diff checks pass. The original sequential edge extension failed when
assertion time allowed capacity to replenish; this was a fixture assumption,
not evidence of a product bypass. The final burst checks respect that capacity
without increasing limits or changing application behavior. Disposable API/web
containers were removed, preserving the original three services. Current
retained-image CI execution and full PRD acceptance remain outstanding.
