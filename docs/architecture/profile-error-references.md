# Profile and account error references

## Accessible deactivation recovery

The exact `8a172a7c` web CI run failed the unchanged deactivation 503 reference/retry assertion: the retry control was present in a root still marked `aria-hidden`, leaving no accessible roles after the confirmation Dialog was removed. The recovery screen now publishes its error reference and retry controls after the prior Modal's unmount cleanup, and focuses the available retry action. It preserves the original deactivation intent, error wording/reference pairing, server/account boundaries and original assertions/deadlines. An added regression checks that the announced reference has an accessible, enabled and focused retry control while private profile fields remain absent.

The combined configuration/profile focused run passed 138 cases before the later intake-retry addition. Type checking and lint passed. This source evidence does not establish every browser recovery branch or exact-image CI acceptance; those gates remain required.

Profile initial reads, protected refreshes, saves, sign-out and deactivation
now pair existing fixed wording with the validated response reference using
the [public identifier contract](public-error-references.md). Each independent
error/refresh/deactivation channel holds its message and reference together.
The existing warning status for refresh remains; reference text wraps within
the alert without changing the original message's text element.

Command and refresh paths capture the received header before reading the body.
Malformed acknowledgments and response-body deadlines can retain that actual
response reference; network failures without a response get none. Invalid header
metadata and provider text remain hidden. Starting another command clears its
prior reference. Existing mutation epochs, canceled/late-result fences, expected
user headers, authorization withdrawal, draft/version/conflict checks and retry
key/body intent are preserved. Ownership refusals keep their original actionable
state; uncertain deactivation retains the original retry path.

## Executed source verification

Six new initial-read, refresh, save, logout and deactivation reference regressions
fail before implementation: **29 passed / 6 failed among 35 cases**. The first
complete authentication/API-boundary run after implementation records **314
passed / 1 failed among 315**. That new owner-refusal test finds the reference
but queries the underlying profile action while the MUI dialog is still closing.
It now waits for the same accessible, enabled action; its criterion is unchanged.
Earlier results are retained privately.

Additional malformed acknowledgment/header, authorization withdrawal and
retry/network cases produce the final **319/319 passing source cases**, zero
failed/pending cases. Original assertions remain. Typecheck, lint and current
private web build pass. Private source reports: `profile-error-references-20261010`.

## Browser and acceptance boundary

The complete original `account.spec.ts` and `identity-expected-account.spec.ts`
files pass together **20/20 on their only attempt**, with zero skipped, flaky,
unexpected cases or report errors, in 567,120.012 ms, against the profile frontend bundle, compiled
schema-133 API/Worker, actual restricted PostgreSQL and Nginx/CSP. This phase uses
the original optional-verification policy for these account cases; the separate
[strict identity-mail phase](identity-error-references.md) is not silently reused
as current profile evidence. Original assertions, deadlines, fixture pacing and
no-case-retry policy remain. Private report:
`profile-error-reference-browser-native-20261010`. Owned API/web/Worker containers,
fresh database and credential environments are independently absent after
terminal cleanup. These files retain their full original scope; their passing
result does not execute every newly added reference-display branch.

These source checks do not establish every profile/reference browser branch or
immutable-image CI acceptance. Invitation and other primary-screen consumers
still need review. PRD-02 remains open at **16% estimated work remaining**;
PRD-01 remains open at **34%** (planning estimates).
