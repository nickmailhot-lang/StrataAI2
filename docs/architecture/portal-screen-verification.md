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
