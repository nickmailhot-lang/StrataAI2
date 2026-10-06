# Account profile management (PRD-02)

The MUI profile screen loads the authenticated account from `GET /me` and saves
display name, avatar URL, locale and timezone through `PATCH /me`. Email and
account status remain read-only. The screen displays the returned server state,
preserves edits on failed saves, supports retry/discard, and redirects to sign-in
when the session expires. Failed sign-out leaves the user on their profile with
a retry message.

Profile updates require a nonempty display name of at most 120 characters, a
regional locale, and a timezone recognized by the runtime. Avatar URLs must use
HTTPS without embedded credentials and be at most 2,048 characters. An empty
avatar URL removes it; an omitted/null URL preserves it. The API stores the URL
without fetching it. Updates preserve identity and historical attribution and
emit the existing `USER_PROFILE_UPDATED` audit entry.

Verification: `ProfilePage.test.tsx` covers authoritative save responses,
network retry, server validation, discard, session expiration, and sign-out
failure. `scripts/ci/test-demo-auth.sh` covers persisted preferences, invalid
input rejection without mutation, avatar removal, and unauthenticated updates
(PRD-02-TC-01/03/04/06). CI runs the account lifecycle against the exact API image.

Profile writes require the positive `version` returned by `GET /me`. Missing or
invalid versions return HTTP 400 (`invalid_version`); stale versions return HTTP
409 (`version_conflict`). Both stores compare the version atomically while
writing, increment it only on success, and reject writes to deactivated accounts.
The UI preserves conflicted edits and offers an explicit discard-and-reload
action. Clients must reload and reconcile before resubmitting with a new version.

`test-profile-concurrency.sh` checks simultaneous saves (exactly one succeeds),
stale retries without overwrite, and recovery using the latest version. It runs
against both Demo and the PostgreSQL provider using the exact CI API image.
The CI-only Compose overlay enables disposable registration without verification
after checking secure production defaults. It is excluded from release bundles.
No schema change is needed: the existing user version column is used.

Password recovery screens are available at `/forgot-password` and
`/reset-password#token=...`. The request screen uses identical confirmation for
known/unknown accounts and never displays the Demo API's returned token. The
reset screen consumes a fragment token, immediately removes the fragment from
history, checks password confirmation, handles expired/single-use tokens, and
clears the token/passwords after success. Tokens in query strings are not accepted.
The API enforces password policy and revokes prior sessions on successful reset.
Component tests verify confirmation privacy, rate/network errors, token handling,
success and rejection; container browser tests cover the request and invalid-link
recovery. Demo API tests prove reset token reuse is rejected.

Production email delivery now uses the separate Worker, durable global identity
outbox and provider idempotency described in [identity-email.md](identity-email.md).
Verification/resend screens and a mobile keyboard test consume real queued fixture
links, including successful reset and old-session revocation. Deployment still
requires runtime keys, restricted database credentials and provider configuration.

Profile update delivery and reconnect recovery are implemented through the
account-bound SignalR and HTTP recovery contracts in [identity realtime
recovery](identity-realtime.md) and [profile recovery](profile-recovery.md).
The account browser fixture checks actual identity frames and two-client
recovery; the realtime guide records the tested historical revision. Those
results must not be treated as release evidence for a later commit.

PRD-02 remains open. Verify current account lifecycle, preference consumers,
accessibility and all remaining acceptance requirements against the current
release images before closure.

### Comment timezone display and remaining acceptance

AUTH-FR-010 applies to timestamp displays as well as Card date controls.
[Card comments](../../apps/web/src/features/kanban/CardCommentsControl.tsx) now
retain locale/timezone from the validated current account after the review read
and after mutation acknowledgment. Both reviewed and newly acknowledged comments
use the [shared date/time formatter](../../apps/web/src/features/auth/userDateTime.ts).
Formatting changes display only; immutable UTC timestamps and original command
keys, bodies and versions remain unchanged. Failed formatting displays an explicit
unavailable date rather than silently using the browser timezone. Account refusal
and cancellation retain the existing protected-state and late-response fences.

The [comment component fixtures](../../apps/web/src/features/kanban/CardCommentsControl.test.tsx)
verify Honolulu's previous calendar day and Tokyo's later time after preference
changes during lost-response recovery, with the original request and timestamp
unchanged. The desktop/phone [native comment fixture](../../tests/browser/card-comments.spec.ts)
registers real Honolulu preferences and checks both acknowledgment and reviewed
captions against the stored instant. Its execution remains pending in CI.
Notifications and activity history already use account preferences. Broader
preference-consumer and current-release acceptance remains required; these scoped
checks do not complete PRD-02.

`tests/browser/account.spec.ts` runs Chromium against the actual web/API release
images and PostgreSQL. It checks registration, sign-in through Nginx, profile
persistence, two-page stale-save recovery and logout/session revocation. Browser
traces and screenshots are retained on failure. Nginx and the Vite development
proxy forward the API's top-level routes as well as `/api`; `/app`, `/login` and
`/portal` remain SPA routes.

### Global search deadline display

Global search now stores the final validated account locale/timezone alongside
its admitted result page and formats deadline instants with the shared account
date/time formatter. Refresh replaces results and preferences together; refusal
or a new read removes both. Stored UTC deadlines and search criteria/cursors are
unchanged. Formatting failure displays an explicit unavailable date.

A component case requires Honolulu's previous day and Tokyo's later time after
a profile preference refresh, with the same UTC search result. Native desktop
and phone fixtures create an actual timed Card deadline and change the account
preference before refreshing search. Ten focused component cases, source
TypeScript and lint pass; native fixture execution remains pending CI. Search now carries canonical nullable `boardDateTimezone` from its authorized
Board read. Applying that optional policy in the browser remains the next step. This account-preference repair
does not establish full AUTH-FR-010/PRD-02 or PRD-16 acceptance.

The search document's `boardDateTimezone` comes from the canonical Board already
held by the authorized Work read, not from directory routing hints. No extra
connection, tenant context or database migration is introduced. A new API-host
case checks null policy, Honolulu, Tokyo and clearing; every fresh search must
reflect the admitted current policy while an outsider receives no private Card or
policy metadata. Compilation succeeds; native execution remains pending CI.
Browser policy precedence is still incomplete, so this contract increment does
not close the timezone acceptance gap.

The mandatory exact-image global-search fixture now requires the
`boardDateTimezone` field on every admitted result, checks initial null policy,
Honolulu, Tokyo and clearing through actual Board date-policy commands, and
requires private/no-store search responses. Its outsider checks require no private
Board/List names or policy strings. Existing 50+2 paging, cursor binding and
archived-parent checks remain required afterward. Bash syntax passes; native
execution is pending. The complete web run against unchanged browser source at
`b9f257e` completed successfully: 122 test files and 1,491 tests passed, with
process exit 0 (508.60 seconds). This verifies the account-preference display
increment across the web suite; backend/fixture coverage does not prove browser
Board-policy precedence or native release acceptance.

Search now validates the required nullable `boardDateTimezone` before admitting
each result. Invalid or missing policy rejects the page rather than using the
browser timezone. Display uses that Board policy when present and otherwise the
final admitted account timezone; account locale applies in both cases. A focused
component case checks Honolulu, a change to UTC, and clearing back to Tokyo with
the same stored UTC deadline. Parser cases cover valid/null policies and malformed
values. All 19 focused search tests pass. Desktop and phone native fixtures now
change and clear the actual Board policy, refresh results, and verify that the
stored deadline remains the same instant; their execution remains pending CI.
