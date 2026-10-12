# Owner Portal screen verification

The [Portal shell](../../apps/web/src/portal/PortalShell.tsx) keeps the separate
Owner Portal admission boundary. Its document-browsing scaffold now says that
browsing is not available yet and disables the previously inert Browse button.
It exposes no internal ticket identifier and does not imply that PRD-80 document
publication/browsing has been implemented.

The existing [surface scenarios](../../tests/browser/surface-admission.spec.ts)
now run at **1280, 768, 390 and 320 CSS pixels**. Each retains the original server
Portal/Internal distinction, internal directory/member refusal, private/public
Board deep-link behavior, newly granted internal navigation, mid-session
membership withdrawal, surviving Portal admission and final logout withdrawal.
Added assertions require no horizontal page scrolling and the clear unavailable
browsing state. The original 90-second case deadline and zero retries remain.

All **4/4 cases pass**, each on its only attempt, with zero skips, flaky outcomes
or report errors, in 122,994.644 ms. This uses the compiled schema-133 Production
API, actual restricted PostgreSQL, the current web bundle and Nginx/CSP. Email
verification is optional, matching the existing release browser policy; this is
not strict-verification-policy evidence. Owned API/web containers, the separate
fixture database and credential file are independently absent. Private report:
`portal-surface-schema133-native-20261009`.

The web bundle, typecheck and lint pass. Complete browser collection proves all
**323 cases across 124 intact files** belong once to the four existing CI shards
(83/79/97/64 cases), preserving per-case deadlines, expected outcomes, sequential
file ownership and no retries. All nine shard-verifier tests pass. Collection
is not execution of those 323 cases.

Independent Chromium geometry observations also show no document/body/header
overflow at all four widths before the copy/button change. Those development
observations mock only Portal admission and establish layout, not server security.
No CSS overflow defect or corresponding style repair is claimed.

This is local compiled-source verification, not an immutable release image,
physical-device result, completed PRD-80, or full primary-screen/accessibility
matrix. Current build-once CI and all remaining PRD acceptance requirements
remain required. PRD-01 stays open at **34% estimated work remaining**, a planning
estimate rather than a passed-case percentage.
## Current Portal sign-out and watch-order verification (2026-10-12)

The complete unchanged surface-admission and watch-permission-order files first
finish **5/8 in 572.37 seconds**. Three Portal-width cases fail their former
post-logout surface-denial-panel expectation; all four watch lock-order roles
pass unchanged. Their older retained-image failures are not reproduced or claimed
repaired by this run. One old Portal expectation passes before the final session
redirect; that result does not prove the old panel is the final unauthenticated UI.

The fixture now observes the actual sign-in route after Portal logout/reload,
within the same original five-second expectation budget, and independently
requires Portal surface-access HTTP 401. All prior valid-session INTERNAL
membership denial, independent PORTAL-grant admission, withheld Council
navigation, private/public deep-link, four-width and document-withdrawal assertions
remain. No product authorization or navigation behavior is changed.

The same complete two-file invocation then passes **8/8 in 555.95 seconds**,
zero skips/unexpected/flaky outcomes or report errors. It includes all Portal
widths (1280/768/390/320) and MEMBER/ADMIN/ORGANIZATION_READER/PUBLIC_READER watch
ordering, actual database wait observations, private-state/watch fingerprints,
activity/notification rows, original receipt bytes and permission withdrawal.

All **2,074** hosted source/runtime and **1,409** native hashes match. Independent
inspection matches all four served web files; owned containers/database/credential
files are absent, with original PostgreSQL preserved. Fresh warning-free solution
build, browser type checks and diff validation pass. Current compiled applications,
Nginx/cached runtime support and the general CI optional-API/mandatory-Worker
verification policy remain explicit. This is local evidence; current immutable
release, complete Portal/identity policy and full AC/DoD still govern closure.
PRD-02 remains open at **16% estimated work remaining**.
