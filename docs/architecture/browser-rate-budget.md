# Release browser rate budget

CI preserves the production API authentication limit and Nginx sensitive-request limit. Browser contexts and API request contexts in a hosted job share the same real peer; changing accounts does not create additional capacity. Forwarding-header spoofing remains ineffective.

The retained trace for schema-021 commit `5d32730` shows registration 201 followed by sign-in 429. Commit `3eb338e` recreated the API and web processes from the already loaded release images before the browser suite. That removed earlier fixture consumption, but its release logs still show profile setup sign-in and recovery requests receiving 429 when the expanded scenarios ran in a rapid burst. These are assertion failures, not permission to increase limits or accept a throttled operation as success.

All browser specs now import the shared `releaseTest` fixture. The required release job enables `STRATAAI_E2E_RATE_PACING=1`, adding 25 seconds before each scenario. Existing scenarios issue fewer than 20 sensitive requests each, so this preserves API window capacity and replenishes the edge burst budget. The fixture has its own 35-second setup timeout, keeping the original per-test operation deadlines intact. A locally executed runtime probe confirmed that setup pacing completes even when the test body has a five-second deadline. Local tests default to no pacing.

The suite still uses one worker, zero test retries and strict status assertions. It neither catches unexpected 429 responses and retries them until success nor modifies production configuration. Required API and edge abuse-limit checks run separately after the browser suite, using spoofed forwarding headers to prove rejection, retry metadata and security headers. Exact web/API/Worker images continue to be built once and emitted only after every mandatory gate passes.

This is fixture isolation and pacing, not proof of application latency targets. New scenarios must stay within the documented sensitive-request budget or receive an explicitly reviewed scheduling adjustment without weakening the production controls.
