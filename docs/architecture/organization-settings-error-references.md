# Organization settings error references

The [settings screen](../../apps/web/src/features/organizations/OrganizationSettingsPage.tsx)
now pairs its fixed user-facing error with the validated reference from the
actual failed HTTP response. It uses the shared
[public identifier contract](public-error-references.md), also used by Work
request errors. Arbitrary Problem text and malformed header values stay out of
the display; local validation/network failures receive no invented server ID.

Message and reference share one state value. Existing wording remains in its
own text element, so adding a reference does not replace the operation's
meaning or require weakening exact-message checks. Starting a new action or
retiring its state clears the prior reference. Account replacement, scope
replacement and cancellation retain their existing withdrawal gates.

Read failures, validation/conflict refusals and uncertain saves use their own
response references after the same original account admission checks. If the
final account-check HTTP request fails, its reference belongs to that failure;
an unadmitted save response reference is not disclosed. A successful protected
background refresh preserves the original uncertain-save reference and retry
intent because it cannot recover that command's acknowledgment. Request keys,
body/version checks, final actor checks and telemetry labels are unchanged.

## Verification

Three new reference-display cases fail before implementation (read refusal,
400 save refusal and uncertain 503 save). The initial test run has 33 passes
and four failures among 37 cases; the fourth failure belongs to a newly added
account-replacement fixture using an invalid profile ID. That fixture is
corrected to a valid replacement UUID so it reaches the actual account-change
branch, preserving the original withdrawal assertion. Original test criteria
are not weakened.

The repaired request-boundary/settings/telemetry group passes 59/59 cases.
Adding successful background-refresh retention and final actor-check reference
coverage yields **61/61 passing cases**, with zero failures/pending cases.
Typecheck, lint and the current private web build pass. Private source reports:
`organization-settings-references-20261009`.

The first complete desktop/phone settings, realtime and telemetry phase
finishes with **4/6 passing cases**. The settings-file desktop case fails its
original 30-second delivered-event/protected-read condition; its fixture has no
scoped Worker, unlike the companion realtime file. The phone case fails its
initial settings-navigation assertion after an unchecked focus/keyboard action.
The failed report remains retained privately:
`organization-settings-reference-browser-native-20261009`.

The settings fixture now explicitly starts the existing scoped Worker for its
Organization and restores it in `finally`. Initial keyboard navigation uses
the existing admission helper, which checks enabled/current focus before one
activation. Assertions, command counts, deadlines, event/read condition and
no-case-retry policy remain unchanged. This changes runtime/focus preconditions;
it does not replace the observed delivery requirement.

The corrected complete six-case phase passes **6/6 on their only attempt**, with
zero skipped, flaky, unexpected cases or report errors, on the same current web bundle,
compiled schema-133 Production API, actual restricted PostgreSQL and separate
Worker behind Nginx/CSP. The same optional-email-verification policy as the
release browser phase applies. Private report:
`organization-settings-reference-browser-fixtures-native-20261009`.
Owned API/web/Worker containers, fresh database and credential environments are
independently absent after terminal cleanup. Current browser collection also
retains all **323 cases in 124 intact files**, partitioned 83/79/97/64 across four
shards with no changed case deadlines or retries.

This is settings-specific error/reference work. Other primary-screen consumers,
client-only failure correlation, strict verification policy, current immutable
images and full PRD/NFR acceptance remain required. PRD-01 remains open at
**34% estimated work remaining**; PRD-03 remains open at **8%** (planning estimates).
