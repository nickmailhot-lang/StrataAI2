# Release authentication policy configuration

The release Compose file previously omitted documented authentication policy
settings. Setting them in the deployment `.env` did not forward them to the
containers, so the API kept its mode defaults and Worker handlers kept their
verification default. This could leave intended initial account enrollment
disabled or ignore the operator's password/session/token policy.

The API now receives all five documented policy settings. Worker reminder and
invitation handlers also receive the verification requirement they consume.
Omitted production values retain self-registration disabled, verification
required, minimum password length 12, session duration 12 hours and token
duration 30 minutes. Compose uses unset-only defaults: explicitly empty values
are forwarded rather than silently replaced. Existing API startup validation
rejects supplied empty, malformed and out-of-range values without echoing them.
The environment example documents these settings. API/Worker image identities,
database roles, volumes, key rings and provider architecture are unchanged.

Before upgrading an existing deployment, review policy values already present
in its `.env`: previously ignored values now take effect. Omitted settings keep
the same production behavior.

Four regressions exercise actual Docker Compose resolution using disposable
environment files, with no containers, image builds or deployment mutation.
They check defaults, valid explicit values, empty values and malformed values
for the API and the Worker's verification flag. All four fail against the old
model's missing settings and pass after repair. The model output is captured
privately; tests return only policy fields, never resolved credentials. This
check is mandatory in source CI before image construction.

After a terminal successful fresh solution build, ten real compiled Production
API startups use the actual Compose-resolved policy values: empty and malformed
inputs for each of the five settings. All ten refuse startup at the intended
named setting; no malformed marker is disclosed. Each attempt is bounded at
15 seconds and uses a deliberately unavailable local database address. This
proves configuration refusal, not database or provider readiness. An earlier
run overlapped the build and is excluded from final evidence.

All 23 release-integrity checks, 213 CI coverage guards, two source-free startup
checks, workflow validation and final Compose checks pass. The supplied optional
attachment overlay already forwards managed-storage settings; it remains the
deployment path for attachments and an operator-provisioned scanner socket.
No cloud or scanner resources are created by this repair.

Current complete build-once CI and retained-image startup/promotion remain
required for the [operational MVP milestone](operational-mvp-milestone.md).
Managed verification delivery and initial verified-account enrollment require
operator-provisioned transport credentials and restricted mail roles; this
local refusal evidence does not establish that acceptance. Full
[PRD-02 acceptance](prd-02-acceptance.md) still governs closure. Estimated
work remaining stays **16%**; the issue remains open.

## Worker discovery settings

Release Compose also previously omitted `STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED` and `STRATAAI_ORGANIZATION_OPERATIONAL_LIFECYCLE_DISCOVERY_ENABLED`. Setting either to `false` in the operator's `.env` was ignored; the Production Worker continued using its default. Both settings now reach the Worker and appear in `.env.release.example` alongside the existing deletion, metadata, invitation-recipient and invitation-issuer discovery controls.

All six discovery settings preserve the Production default `true` when omitted and forward explicit values unchanged. Empty values now reach the Worker's existing strict Boolean validation, rather than becoming `true` through Compose interpolation. Invalid values refuse startup. Each flag controls its corresponding automatic loop; explicit general-job Organization scope remains independent. Review previously ignored values and empty settings before upgrading an existing deployment. The API receives no Worker discovery settings, and runtime handlers, database capabilities and discovery defaults are unchanged.

Four new real Compose regression cases fail before the fix and pass afterward: omitted defaults, explicit disabling, explicit empty and malformed values. The four original authentication-policy checks remain green. The same mandatory CI test file now executes all eight checks.

After a fresh successful solution build, 12 actual compiled Production Worker startups using the rendered Compose settings reject each discovery flag's empty and malformed values within 15 seconds. Every refusal names the relevant category; none discloses the malformed marker. These attempts use an unavailable local database and create no containers or provider resources. All 25 release-integrity checks, 213 CI coverage guards and two source-free startup checks pass. This is configuration wiring and startup-refusal evidence; exact retained-image CI, successful delivery and release promotion remain necessary for acceptance. The operational MVP milestone and the full roadmap remain open.
