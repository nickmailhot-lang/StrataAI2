# Profile and account error references

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
files are running together against the current profile frontend bundle, compiled
schema-133 API/Worker, actual restricted PostgreSQL and Nginx/CSP. This phase uses
the original optional-verification policy for these account cases; the separate
[strict identity-mail phase](identity-error-references.md) is not silently reused
as current profile evidence. Original assertions, deadlines, fixture pacing and
no-case-retry policy remain. Private report:
`profile-error-reference-browser-native-20261010`. The browser invocation is
nonterminal; no pass or cleanup is claimed yet.

These source checks do not establish every profile/reference browser branch or
immutable-image CI acceptance. Invitation and other primary-screen consumers
still need review. PRD-02 remains open at **16% estimated work remaining**;
PRD-01 remains open at **34%** (planning estimates).
